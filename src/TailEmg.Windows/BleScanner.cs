using System.Collections.Concurrent;
using TailEmg.Core.Ble;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Advertisement;

namespace TailEmg.Windows;

/// <summary>
/// Passive-to-active advertisement scan. Devices are merged per address (name often arrives in the
/// scan response, service UUIDs in the advertisement). Consumers poll <see cref="Snapshot"/>.
/// </summary>
public sealed class BleScanner
{
    private readonly BleNotifyProfile _profile;
    private readonly ConcurrentDictionary<ulong, BleDeviceInfo> _devices = new();
    private BluetoothLEAdvertisementWatcher? _watcher;

    public BleScanner(BleNotifyProfile profile) => _profile = profile;

    public string? LastError { get; private set; }

    public bool IsScanning => _watcher is { Status: BluetoothLEAdvertisementWatcherStatus.Started };

    public void Start()
    {
        Stop();
        _devices.Clear();
        LastError = null;

        try
        {
            var watcher = new BluetoothLEAdvertisementWatcher { ScanningMode = BluetoothLEScanningMode.Active };
            watcher.Received += OnReceived;
            watcher.Stopped += OnStopped;
            _watcher = watcher;
            watcher.Start();
        }
        catch (Exception ex)
        {
            _watcher = null;
            LastError = $"Scan failed: {ex.Message}";
        }
    }

    public void Stop()
    {
        var watcher = _watcher;
        _watcher = null;
        if (watcher is null)
        {
            return;
        }

        watcher.Received -= OnReceived;
        watcher.Stopped -= OnStopped;
        try
        {
            watcher.Stop();
        }
        catch (Exception)
        {
            // Already stopped / radio gone: nothing to do.
        }
    }

    public IReadOnlyList<BleDeviceInfo> Snapshot() => _devices.Values.ToArray();

    private void OnStopped(BluetoothLEAdvertisementWatcher sender, BluetoothLEAdvertisementWatcherStoppedEventArgs args)
    {
        if (args.Error != BluetoothError.Success)
        {
            LastError = $"Scan stopped: {args.Error}";
        }
    }

    private void OnReceived(BluetoothLEAdvertisementWatcher sender, BluetoothLEAdvertisementReceivedEventArgs args)
    {
        try
        {
            var name = args.Advertisement.LocalName;
            var services = args.Advertisement.ServiceUuids.ToArray();
            int? rssi = args.RawSignalStrengthInDBm == 127 ? null : args.RawSignalStrengthInDBm; // 127 = unavailable
            var hasService = services.Contains(_profile.ServiceUuid);

            _devices.AddOrUpdate(
                args.BluetoothAddress,
                address => new BleDeviceInfo(address, NullIfEmpty(name), rssi, hasService, services, DateTimeOffset.Now),
                (address, old) => new BleDeviceInfo(
                    address,
                    NullIfEmpty(name) ?? old.Name,
                    rssi ?? old.Rssi,
                    old.HasMyoWareService || hasService,
                    old.AdvertisedServices.Union(services).ToArray(),
                    DateTimeOffset.Now));
        }
        catch (Exception)
        {
            // A malformed advertisement must never take the scanner down.
        }
    }

    private static string? NullIfEmpty(string? s) => string.IsNullOrWhiteSpace(s) ? null : s;
}
