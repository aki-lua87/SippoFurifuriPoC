using TailEmg.Core.Ble;
using TailEmg.Core.Input;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Foundation;
using Windows.Storage.Streams;

namespace TailEmg.Windows;

/// <summary>The device is reachable but does not expose the expected GATT layout; retrying will not help.</summary>
public sealed class GattProfileMismatchException(string message) : Exception(message);

/// <summary>
/// IEmgInput backed by a BLE notify characteristic (MyoWare factory firmware by default).
/// Owns connect → subscribe → watch for loss → backoff reconnect. StopAsync is an explicit
/// disconnect and never reconnects. Events fire on thread-pool threads; subscribers must not block.
/// </summary>
public sealed class MyoWareBleInput : IEmgInput
{
    public const int DefaultChannel = 0;

    private static readonly TimeSpan[] Backoff =
    {
        TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5),
    };

    private readonly BleNotifyProfile _profile;
    private CancellationTokenSource? _cts;
    private Task? _loop;
    private int _state = (int)BleConnectionState.Disconnected;

    public MyoWareBleInput(BleNotifyProfile? profile = null) => _profile = profile ?? BleNotifyProfile.MyoWare;

    public event EventHandler<EmgSample>? SampleReceived;
    public event EventHandler<BleNotification>? NotificationReceived;
    public event EventHandler<BleConnectionState>? StateChanged;

    /// <summary>Bluetooth address of the device to connect to. Set before <see cref="StartAsync"/>.</summary>
    public ulong TargetAddress { get; set; }

    public string? LastError { get; private set; }

    public BleConnectionState State => (BleConnectionState)Volatile.Read(ref _state);

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (_loop is { IsCompleted: false })
        {
            return Task.CompletedTask;
        }

        if (TargetAddress == 0)
        {
            LastError = "No device selected";
            SetState(BleConnectionState.Error);
            return Task.CompletedTask;
        }

        LastError = null;
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var ct = _cts.Token;
        _loop = Task.Run(() => RunAsync(ct));
        return Task.CompletedTask;
    }

    public async Task StopAsync()
    {
        var cts = _cts;
        var loop = _loop;
        _cts = null;
        _loop = null;
        if (cts is not null)
        {
            cts.Cancel();
            if (loop is not null)
            {
                try
                {
                    await loop.ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                }
            }

            cts.Dispose();
        }

        SetState(BleConnectionState.Disconnected);
    }

    private async Task RunAsync(CancellationToken ct)
    {
        var attempt = 0;
        while (!ct.IsCancellationRequested)
        {
            SetState(attempt == 0 ? BleConnectionState.Connecting : BleConnectionState.Reconnecting);

            Link? link = null;
            try
            {
                link = await ConnectAsync(ct).ConfigureAwait(false);
                attempt = 0;
                LastError = null;
                SetState(BleConnectionState.Connected);

                await link.Lost.Task.WaitAsync(ct).ConfigureAwait(false);
                LastError = "Connection lost";
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (GattProfileMismatchException ex)
            {
                LastError = $"{ex.Message} Use GATT Discover to see what the device exposes.";
                SetState(BleConnectionState.Error);
                return;
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
            }
            finally
            {
                if (link is not null)
                {
                    await link.CloseAsync().ConfigureAwait(false);
                }
            }

            if (ct.IsCancellationRequested)
            {
                break;
            }

            SetState(BleConnectionState.Reconnecting);
            var delay = Backoff[Math.Min(attempt, Backoff.Length - 1)];
            attempt++;
            try
            {
                await Task.Delay(delay, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task<Link> ConnectAsync(CancellationToken ct)
    {
        var device = await BluetoothLEDevice.FromBluetoothAddressAsync(TargetAddress).AsTask(ct).ConfigureAwait(false);
        if (device is null)
        {
            throw new InvalidOperationException("Device not found (powered on and in range?)");
        }

        var link = new Link(device, OnValueChanged);
        try
        {
            // Ask Windows to keep the link up between notifications.
            link.Session = await GattSession.FromDeviceIdAsync(device.BluetoothDeviceId).AsTask(ct).ConfigureAwait(false);
            link.Session.MaintainConnection = true;

            var services = await device.GetGattServicesForUuidAsync(_profile.ServiceUuid, BluetoothCacheMode.Uncached)
                .AsTask(ct).ConfigureAwait(false);
            if (services.Status != GattCommunicationStatus.Success)
            {
                throw new InvalidOperationException($"GATT service query failed: {services.Status}");
            }

            if (services.Services.Count == 0)
            {
                throw new GattProfileMismatchException($"Service {_profile.ServiceUuid} not found.");
            }

            link.Service = services.Services[0];
            var chars = await link.Service.GetCharacteristicsForUuidAsync(_profile.CharacteristicUuid, BluetoothCacheMode.Uncached)
                .AsTask(ct).ConfigureAwait(false);
            if (chars.Status != GattCommunicationStatus.Success)
            {
                throw new InvalidOperationException($"GATT characteristic query failed: {chars.Status}");
            }

            if (chars.Characteristics.Count == 0)
            {
                throw new GattProfileMismatchException($"Characteristic {_profile.CharacteristicUuid} not found.");
            }

            var characteristic = chars.Characteristics[0];
            var props = characteristic.CharacteristicProperties;
            GattClientCharacteristicConfigurationDescriptorValue mode;
            if (props.HasFlag(GattCharacteristicProperties.Notify))
            {
                mode = GattClientCharacteristicConfigurationDescriptorValue.Notify;
            }
            else if (props.HasFlag(GattCharacteristicProperties.Indicate))
            {
                mode = GattClientCharacteristicConfigurationDescriptorValue.Indicate;
            }
            else
            {
                throw new GattProfileMismatchException($"Characteristic does not support Notify/Indicate ({props}).");
            }

            // Keep a strong reference: if the characteristic is collected, notifications silently stop.
            link.Characteristic = characteristic;
            characteristic.ValueChanged += link.ValueChangedHandler;

            var status = await characteristic.WriteClientCharacteristicConfigurationDescriptorAsync(mode).AsTask(ct).ConfigureAwait(false);
            if (status != GattCommunicationStatus.Success)
            {
                throw new InvalidOperationException($"Subscribe failed: {status}");
            }

            device.ConnectionStatusChanged += link.OnConnectionStatusChanged;
            link.StatusHooked = true;
            if (device.ConnectionStatus == BluetoothConnectionStatus.Disconnected)
            {
                link.Lost.TrySetResult();
            }

            return link;
        }
        catch
        {
            await link.CloseAsync().ConfigureAwait(false);
            throw;
        }
    }

    private void OnValueChanged(GattCharacteristic sender, GattValueChangedEventArgs args)
    {
        try
        {
            var timestamp = DateTimeOffset.Now;
            var buffer = args.CharacteristicValue;
            var bytes = new byte[buffer.Length];
            using (var reader = DataReader.FromBuffer(buffer))
            {
                reader.ReadBytes(bytes);
            }

            var ok = BlePayloadParser.TryParse(bytes, out var text, out var value);
            NotificationReceived?.Invoke(this, new BleNotification(timestamp, bytes, text, ok ? value : null));
            if (ok)
            {
                SampleReceived?.Invoke(this, new EmgSample(timestamp, DefaultChannel, value));
            }
        }
        catch (Exception)
        {
            // Bad payload or a faulty subscriber must not break the notification stream.
        }
    }

    private void SetState(BleConnectionState state)
    {
        if (Interlocked.Exchange(ref _state, (int)state) != (int)state)
        {
            StateChanged?.Invoke(this, state);
        }
    }

    /// <summary>All WinRT objects for one connection, released together.</summary>
    private sealed class Link
    {
        private readonly BluetoothLEDevice _device;

        public Link(BluetoothLEDevice device, TypedEventHandler<GattCharacteristic, GattValueChangedEventArgs> valueChanged)
        {
            _device = device;
            ValueChangedHandler = valueChanged;
        }

        public TypedEventHandler<GattCharacteristic, GattValueChangedEventArgs> ValueChangedHandler { get; }
        public TaskCompletionSource Lost { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public GattSession? Session { get; set; }
        public GattDeviceService? Service { get; set; }
        public GattCharacteristic? Characteristic { get; set; }
        public bool StatusHooked { get; set; }

        public void OnConnectionStatusChanged(BluetoothLEDevice sender, object args)
        {
            if (sender.ConnectionStatus == BluetoothConnectionStatus.Disconnected)
            {
                Lost.TrySetResult();
            }
        }

        public async Task CloseAsync()
        {
            try
            {
                if (StatusHooked)
                {
                    _device.ConnectionStatusChanged -= OnConnectionStatusChanged;
                }

                if (Characteristic is not null)
                {
                    Characteristic.ValueChanged -= ValueChangedHandler;
                    try
                    {
                        // Best effort: fails harmlessly when the link is already gone.
                        await Characteristic.WriteClientCharacteristicConfigurationDescriptorAsync(
                            GattClientCharacteristicConfigurationDescriptorValue.None);
                    }
                    catch (Exception)
                    {
                    }
                }

                Service?.Dispose();
                Session?.Dispose();
                _device.Dispose();
            }
            catch (Exception)
            {
            }
        }
    }
}
