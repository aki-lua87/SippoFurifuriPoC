using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Windows.Threading;
using TailEmg.App.Mvvm;
using TailEmg.Core.Ble;
using TailEmg.Core.Input;
using TailEmg.Core.Osc;
using TailEmg.Core.Processing;
using TailEmg.Windows;

namespace TailEmg.App;

public enum InputSourceKind
{
    Fake,
    MyoWareBle,
}

public sealed class MainViewModel : ObservableObject
{
    private const int MaxLogLines = 200;
    private const int MaxQueuedNotifications = 500;
    private static readonly TimeSpan SafeRampWait = TimeSpan.FromMilliseconds(300);

    private readonly FakeEmgInput _fake = new();
    private readonly MyoWareBleInput _ble = new();
    private readonly BleScanner _scanner = new(BleNotifyProfile.MyoWare);
    private readonly ProcessingSettings _processing = new();
    private readonly OscSettings _osc = new();
    private readonly EmgPipeline _pipeline;
    private readonly OscPublisher _publisher;
    private readonly DispatcherTimer _uiTimer;
    private readonly ConcurrentQueue<BleNotification> _notifications = new();
    private readonly List<string> _logLines = new();
    private int _tick;
    private long _notificationCount;
    private long _rateBaseCount;
    private readonly System.Diagnostics.Stopwatch _rateClock = System.Diagnostics.Stopwatch.StartNew();
    private string _notificationRateText = "-";

    private InputSourceKind _inputSource = InputSourceKind.Fake;
    private bool _isRunning;
    private string _status = "Stopped";
    private double _raw;
    private double _normalized;
    private double _filtered;
    private double _oscOutput;
    private string _oscError = "";
    private string _lastSampleText = "-";

    private BleDeviceItem? _selectedDevice;
    private bool _isScanning;
    private string _bleStateText = nameof(BleConnectionState.Disconnected);
    private string _bleDeviceName = "-";
    private string _bleError = "";
    private string _gattReportText = "Select a device and press GATT Discover.";
    private string _notificationLog = "";
    private bool _pauseLog;
    private bool _discovering;

    public MainViewModel()
    {
        _pipeline = new EmgPipeline(_processing);
        // One entry point for every input: the pipeline only knows IEmgInput samples.
        _fake.SampleReceived += _pipeline.OnSample;
        _ble.SampleReceived += _pipeline.OnSample;
        _ble.NotificationReceived += OnBleNotification;
        _ble.StateChanged += OnBleStateChanged;

        _publisher = new OscPublisher(_osc, () => _pipeline.GetFiltered(FakeEmgInput.DefaultChannel));

        StartCommand = new RelayCommand(StartAsync, () => !IsRunning && IsFakeSource);
        StopCommand = new RelayCommand(StopAsync, () => IsRunning && IsFakeSource);
        ScanCommand = new RelayCommand(ToggleScan, () => IsBleSource);
        ConnectCommand = new RelayCommand(StartAsync, () => !IsRunning && IsBleSource && SelectedDevice is not null);
        DisconnectCommand = new RelayCommand(StopAsync, () => IsRunning && IsBleSource);
        DiscoverCommand = new RelayCommand(DiscoverAsync, () => SelectedDevice is not null && !_discovering);

        _uiTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(33) };
        _uiTimer.Tick += (_, _) => OnUiTick();
        _uiTimer.Start();
    }

    public RelayCommand StartCommand { get; }
    public RelayCommand StopCommand { get; }
    public RelayCommand ScanCommand { get; }
    public RelayCommand ConnectCommand { get; }
    public RelayCommand DisconnectCommand { get; }
    public RelayCommand DiscoverCommand { get; }

    public IReadOnlyList<InputSourceKind> InputSources { get; } = Enum.GetValues<InputSourceKind>();
    public IReadOnlyList<FakeEmgMode> InputModes { get; } = Enum.GetValues<FakeEmgMode>();
    public IReadOnlyList<ResponseCurveType> Curves { get; } = Enum.GetValues<ResponseCurveType>();

    // --- Input source ---
    public InputSourceKind InputSource
    {
        get => _inputSource;
        set
        {
            if (IsRunning)
            {
                OnPropertyChanged(); // snap the combo box back
                return;
            }

            if (Set(ref _inputSource, value))
            {
                OnPropertyChanged(nameof(IsFakeSource));
                OnPropertyChanged(nameof(IsBleSource));
                RaiseCommands();
            }
        }
    }

    public bool IsFakeSource => _inputSource == InputSourceKind.Fake;
    public bool IsBleSource => _inputSource == InputSourceKind.MyoWareBle;
    public bool CanChangeSource => !IsRunning;

    // --- Input (Fake) ---
    public FakeEmgMode InputMode
    {
        get => _fake.Mode;
        set { _fake.Mode = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsManual)); }
    }

    public bool IsManual => _fake.Mode == FakeEmgMode.Manual;

    public double ManualValue
    {
        get => _fake.ManualValue;
        set { _fake.ManualValue = value; OnPropertyChanged(); }
    }

    public double SinePeriodSeconds
    {
        get => _fake.SinePeriodSeconds;
        set { _fake.SinePeriodSeconds = value; OnPropertyChanged(); }
    }

    // --- Input (BLE) ---
    public ObservableCollection<BleDeviceItem> Devices { get; } = new();

    public BleDeviceItem? SelectedDevice
    {
        get => _selectedDevice;
        set
        {
            if (Set(ref _selectedDevice, value))
            {
                RaiseCommands();
            }
        }
    }

    public string ScanButtonText => _isScanning ? "Stop Scan" : "Scan";
    public string BleStateText { get => _bleStateText; private set => Set(ref _bleStateText, value); }
    public string BleDeviceName { get => _bleDeviceName; private set => Set(ref _bleDeviceName, value); }
    public string BleError { get => _bleError; private set => Set(ref _bleError, value); }
    public string NotificationRateText { get => _notificationRateText; private set => Set(ref _notificationRateText, value); }
    public string GattReportText { get => _gattReportText; private set => Set(ref _gattReportText, value); }
    public string NotificationLog { get => _notificationLog; private set => Set(ref _notificationLog, value); }
    public bool PauseLog { get => _pauseLog; set => Set(ref _pauseLog, value); }

    // --- Calibration (manual entry for now) ---
    public double Rest
    {
        get => _processing.Rest;
        set { _processing.Rest = value; OnPropertyChanged(); OnPropertyChanged(nameof(CalibrationWarning)); }
    }

    public double Max
    {
        get => _processing.Max;
        set { _processing.Max = value; OnPropertyChanged(); OnPropertyChanged(nameof(CalibrationWarning)); }
    }

    public string CalibrationWarning => _processing.Max <= _processing.Rest ? "MAX must be greater than REST" : "";

    // --- Processing ---
    public double Deadzone
    {
        get => _processing.Deadzone;
        set { _processing.Deadzone = Math.Clamp(value, 0, 0.99); OnPropertyChanged(); }
    }

    public ResponseCurveType Curve
    {
        get => _processing.Curve;
        set { _processing.Curve = value; OnPropertyChanged(); }
    }

    public double Gamma
    {
        get => _processing.Gamma;
        set { _processing.Gamma = Math.Max(0.01, value); OnPropertyChanged(); }
    }

    public double Smoothing
    {
        get => _processing.SmoothingAlpha;
        set { _processing.SmoothingAlpha = Math.Clamp(value, 0, 1); OnPropertyChanged(); }
    }

    // --- OSC ---
    public string OscHost { get => _osc.Host; set { _osc.Host = value.Trim(); OnPropertyChanged(); } }
    public int OscPort { get => _osc.Port; set { _osc.Port = Math.Clamp(value, 1, 65535); OnPropertyChanged(); } }
    public string OscAddress { get => _osc.Address; set { _osc.Address = value.Trim(); OnPropertyChanged(); } }
    public double OscRateHz { get => _osc.RateHz; set { _osc.RateHz = Math.Clamp(value, 1, 240); OnPropertyChanged(); } }
    public bool OscEnabled { get => _osc.Enabled; set { _osc.Enabled = value; OnPropertyChanged(); } }

    // --- Live readouts ---
    public bool IsRunning
    {
        get => _isRunning;
        private set
        {
            if (Set(ref _isRunning, value))
            {
                OnPropertyChanged(nameof(CanChangeSource));
                RaiseCommands();
            }
        }
    }

    public string Status { get => _status; private set => Set(ref _status, value); }
    public double Raw { get => _raw; private set => Set(ref _raw, value); }
    public double Normalized { get => _normalized; private set => Set(ref _normalized, value); }
    public double Filtered { get => _filtered; private set => Set(ref _filtered, value); }
    public double OscOutput { get => _oscOutput; private set => Set(ref _oscOutput, value); }
    public string OscError { get => _oscError; private set => Set(ref _oscError, value); }
    public string LastSampleText { get => _lastSampleText; private set => Set(ref _lastSampleText, value); }

    private IEmgInput ActiveInput => IsBleSource ? _ble : _fake;

    private async Task StartAsync()
    {
        if (IsRunning)
        {
            return;
        }

        if (IsBleSource)
        {
            if (SelectedDevice is null)
            {
                return;
            }

            _ble.TargetAddress = SelectedDevice.Address;
            BleDeviceName = SelectedDevice.Name ?? SelectedDevice.Display.Trim();
            await StopScanForConnectAsync();
        }

        IsRunning = true;
        _pipeline.Reset();

        // BLE: stay at 0 until the link is actually up (OnBleStateChanged releases it).
        _publisher.SafeState = IsBleSource;
        _publisher.Start();
        await ActiveInput.StartAsync(CancellationToken.None);
        Status = IsBleSource ? "Running (MyoWare BLE)" : "Running (Fake input)";
    }

    private async Task StopAsync()
    {
        if (!IsRunning)
        {
            return;
        }

        Status = "Stopping...";
        await ActiveInput.StopAsync();

        // Input is gone: ramp OSC to 0 rather than snapping, then StopAsync sends a final 0.0.
        _publisher.SafeState = true;
        await Task.Delay(SafeRampWait);
        await _publisher.StopAsync();

        IsRunning = false;
        Status = "Stopped";
    }

    private void ToggleScan()
    {
        if (_scanner.IsScanning)
        {
            _scanner.Stop();
        }
        else
        {
            if (!IsRunning)
            {
                Devices.Clear();
            }

            _scanner.Start();
        }

        BleError = _scanner.LastError ?? "";
        SyncScanState();
    }

    /// <summary>
    /// An active scan running during a connection attempt makes some Windows BLE adapters
    /// fail with "Unreachable", so stop it first. The device list is kept.
    /// </summary>
    private async Task StopScanForConnectAsync()
    {
        if (!_scanner.IsScanning)
        {
            return;
        }

        _scanner.Stop();
        SyncScanState();
        await Task.Delay(300);
    }

    private async Task DiscoverAsync()
    {
        var device = SelectedDevice;
        if (device is null)
        {
            return;
        }

        _discovering = true;
        DiscoverCommand.RaiseCanExecuteChanged();
        await StopScanForConnectAsync();
        GattReportText = $"Connecting to {device.Display.Trim()} ...";
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            var result = await GattExplorer.DiscoverAsync(device.Address, cts.Token);
            GattReportText = GattReport.Format(result, BleNotifyProfile.MyoWare);
        }
        finally
        {
            _discovering = false;
            DiscoverCommand.RaiseCanExecuteChanged();
        }
    }

    /// <summary>Called from window Closing so the avatar tail is released.</summary>
    public async Task ShutdownAsync()
    {
        _uiTimer.Stop();
        _scanner.Stop();
        await StopAsync();
    }

    // Runs on a thread-pool thread: touch only thread-safe things here.
    private void OnBleNotification(object? sender, BleNotification n)
    {
        Interlocked.Increment(ref _notificationCount);
        _notifications.Enqueue(n);
        while (_notifications.Count > MaxQueuedNotifications && _notifications.TryDequeue(out _))
        {
        }
    }

    private void OnBleStateChanged(object? sender, BleConnectionState state)
    {
        // Link down (lost / reconnecting / error) → ramp OSC to 0; link up → follow the signal again.
        _publisher.SafeState = state != BleConnectionState.Connected;
    }

    private void OnUiTick()
    {
        _tick++;
        var latest = _pipeline.GetLatest(FakeEmgInput.DefaultChannel);
        if (latest is not null)
        {
            Raw = latest.Raw;
            Normalized = latest.Normalized;
            Filtered = latest.Filtered;
            LastSampleText = latest.Timestamp.ToLocalTime().ToString("HH:mm:ss.fff");
        }

        OscOutput = _publisher.LastOutput;
        OscError = _publisher.LastError ?? "";

        var state = _ble.State;
        BleStateText = state == BleConnectionState.Disconnected && _scanner.IsScanning
            ? nameof(BleConnectionState.Scanning)
            : state.ToString();
        if (_ble.LastError is not null || _scanner.LastError is not null)
        {
            BleError = _ble.LastError ?? _scanner.LastError ?? "";
        }
        else if (state == BleConnectionState.Connected)
        {
            BleError = "";
        }

        if (_tick % 3 == 0)
        {
            DrainNotifications();
        }

        UpdateNotificationRate();

        if (_tick % 6 == 0)
        {
            SyncDevices();
            SyncScanState();
        }
    }

    /// <summary>Notifications per second, averaged over ~1 s windows (BLE arrives in bursts).</summary>
    private void UpdateNotificationRate()
    {
        var elapsed = _rateClock.Elapsed.TotalSeconds;
        if (elapsed < 1.0)
        {
            return;
        }

        var count = Interlocked.Read(ref _notificationCount);
        NotificationRateText = $"{(count - _rateBaseCount) / elapsed:0} Hz";
        _rateBaseCount = count;
        _rateClock.Restart();
    }

    private void SyncScanState()
    {
        if (_isScanning != _scanner.IsScanning)
        {
            _isScanning = _scanner.IsScanning;
            OnPropertyChanged(nameof(ScanButtonText));
        }
    }

    private void SyncDevices()
    {
        if (!_scanner.IsScanning && Devices.Count > 0 && !IsRunning)
        {
            return; // stopped: leave the list as is so it can be inspected / connected
        }

        var snapshot = _scanner.Snapshot();
        foreach (var info in snapshot)
        {
            var item = Devices.FirstOrDefault(d => d.Address == info.Address);
            if (item is null)
            {
                item = new BleDeviceItem(info);
                if (item.LooksLikeMyoWare)
                {
                    Devices.Insert(0, item); // candidates float to the top
                }
                else
                {
                    Devices.Add(item);
                }
            }
            else
            {
                item.Update(info);
            }
        }
    }

    private void DrainNotifications()
    {
        if (PauseLog)
        {
            return;
        }

        var added = false;
        while (_notifications.TryDequeue(out var n))
        {
            _logLines.Add(n.ToLogLine());
            added = true;
        }

        if (!added)
        {
            return;
        }

        if (_logLines.Count > MaxLogLines)
        {
            _logLines.RemoveRange(0, _logLines.Count - MaxLogLines);
        }

        // Newest first, so no auto-scroll is needed.
        NotificationLog = string.Join(Environment.NewLine, Enumerable.Reverse(_logLines));
    }

    private void RaiseCommands()
    {
        StartCommand.RaiseCanExecuteChanged();
        StopCommand.RaiseCanExecuteChanged();
        ScanCommand.RaiseCanExecuteChanged();
        ConnectCommand.RaiseCanExecuteChanged();
        DisconnectCommand.RaiseCanExecuteChanged();
        DiscoverCommand.RaiseCanExecuteChanged();
    }
}
