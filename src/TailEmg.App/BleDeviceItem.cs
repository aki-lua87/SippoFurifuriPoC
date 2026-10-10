using TailEmg.App.Mvvm;
using TailEmg.Core.Ble;

namespace TailEmg.App;

/// <summary>List row for a scanned BLE device; updated in place so the selection survives refreshes.</summary>
public sealed class BleDeviceItem : ObservableObject
{
    private string _display = "";

    public BleDeviceItem(BleDeviceInfo info)
    {
        Address = info.Address;
        Update(info);
    }

    public ulong Address { get; }

    public string? Name { get; private set; }

    public bool IsCandidate { get; private set; }

    public string Display { get => _display; private set => Set(ref _display, value); }

    public void Update(BleDeviceInfo info)
    {
        Name = info.Name;
        // The service UUID decides; the name is only shown as a hint.
        IsCandidate = info.HasMyoWareService;
        var nameHint = info.Name is not null && info.Name.Contains("MyoWare", StringComparison.OrdinalIgnoreCase);
        var tag = info.HasMyoWareService ? "[MyoWare svc]" : nameHint ? "[name only]  " : "             ";
        var rssi = info.Rssi is { } r ? $"{r,4} dBm" : "   ? dBm";
        Display = $"{tag} {info.Name ?? "(no name)",-18} {info.AddressText}  {rssi}";
    }

    public bool LooksLikeMyoWare => IsCandidate || (Name?.Contains("MyoWare", StringComparison.OrdinalIgnoreCase) ?? false);
}
