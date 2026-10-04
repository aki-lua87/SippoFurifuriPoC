using System.Windows.Threading;
using TailEmg.App.Mvvm;
using TailEmg.Core.Input;
using TailEmg.Core.Osc;
using TailEmg.Core.Processing;

namespace TailEmg.App;

public sealed class MainViewModel : ObservableObject
{
    private static readonly TimeSpan SafeRampWait = TimeSpan.FromMilliseconds(300);

    private readonly FakeEmgInput _fake = new();
    private readonly ProcessingSettings _processing = new();
    private readonly OscSettings _osc = new();
    private readonly EmgPipeline _pipeline;
    private readonly OscPublisher _publisher;
    private readonly DispatcherTimer _uiTimer;
    private bool _handlerAttached;

    private bool _isRunning;
    private string _status = "Stopped";
    private double _raw;
    private double _normalized;
    private double _filtered;
    private double _oscOutput;
    private string _oscError = "";

    public MainViewModel()
    {
        _pipeline = new EmgPipeline(_processing);
        _publisher = new OscPublisher(_osc, () => _pipeline.GetFiltered(FakeEmgInput.DefaultChannel));

        StartCommand = new RelayCommand(StartAsync, () => !IsRunning);
        StopCommand = new RelayCommand(StopAsync, () => IsRunning);

        _uiTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(33) };
        _uiTimer.Tick += (_, _) => RefreshOutputs();
        _uiTimer.Start();
    }

    public RelayCommand StartCommand { get; }
    public RelayCommand StopCommand { get; }

    public IReadOnlyList<FakeEmgMode> InputModes { get; } = Enum.GetValues<FakeEmgMode>();
    public IReadOnlyList<ResponseCurveType> Curves { get; } = Enum.GetValues<ResponseCurveType>();

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
                StartCommand.RaiseCanExecuteChanged();
                StopCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string Status { get => _status; private set => Set(ref _status, value); }
    public double Raw { get => _raw; private set => Set(ref _raw, value); }
    public double Normalized { get => _normalized; private set => Set(ref _normalized, value); }
    public double Filtered { get => _filtered; private set => Set(ref _filtered, value); }
    public double OscOutput { get => _oscOutput; private set => Set(ref _oscOutput, value); }
    public string OscError { get => _oscError; private set => Set(ref _oscError, value); }

    private async Task StartAsync()
    {
        if (IsRunning)
        {
            return;
        }

        IsRunning = true;
        _pipeline.Reset();
        if (!_handlerAttached)
        {
            _fake.SampleReceived += _pipeline.OnSample;
            _handlerAttached = true;
        }

        _publisher.SafeState = false;
        _publisher.Start();
        await _fake.StartAsync(CancellationToken.None);
        Status = "Running (Fake input)";
    }

    private async Task StopAsync()
    {
        if (!IsRunning)
        {
            return;
        }

        Status = "Stopping...";
        await _fake.StopAsync();

        // Input is gone: ramp OSC to 0 rather than snapping, then StopAsync sends a final 0.0.
        _publisher.SafeState = true;
        await Task.Delay(SafeRampWait);
        await _publisher.StopAsync();

        IsRunning = false;
        Status = "Stopped";
    }

    /// <summary>Called from window Closing so the avatar tail is released.</summary>
    public async Task ShutdownAsync()
    {
        _uiTimer.Stop();
        await StopAsync();
    }

    private void RefreshOutputs()
    {
        var latest = _pipeline.GetLatest(FakeEmgInput.DefaultChannel);
        if (latest is not null)
        {
            Raw = latest.Raw;
            Normalized = latest.Normalized;
            Filtered = latest.Filtered;
        }

        OscOutput = _publisher.LastOutput;
        OscError = _publisher.LastError ?? "";
    }
}
