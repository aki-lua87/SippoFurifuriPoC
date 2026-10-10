namespace TailEmg.Core.Ble;

public enum BleConnectionState
{
    Disconnected,
    Scanning,
    Connecting,
    Connected,
    Reconnecting,
    Error,
}

/// <summary>
/// A GATT notify source: which service/characteristic to subscribe to. UUIDs live here and nowhere else.
/// A future profile (e.g. Nordic UART) is just another instance plus its own payload decoder.
/// </summary>
public sealed record BleNotifyProfile(string Name, Guid ServiceUuid, Guid CharacteristicUuid)
{
    /// <summary>Factory MyoWare Wireless Shield peripheral firmware (ASCII decimal string, Read + Notify).</summary>
    public static readonly BleNotifyProfile MyoWare = new(
        "MyoWare",
        Guid.Parse("EC3AF789-2154-49F4-A9FC-BC6C88E9E930"),
        Guid.Parse("F3A56EDF-8F1E-4533-93BF-5601B2E91308"));

    /// <summary>Only a display hint; never used to decide whether a device is supported.</summary>
    public const string MyoWareNameHint = "MyoWareSensor1";
}

public sealed record BleDeviceInfo(
    ulong Address,
    string? Name,
    int? Rssi,
    bool HasMyoWareService,
    IReadOnlyList<Guid> AdvertisedServices,
    DateTimeOffset LastSeen)
{
    public string AddressText => BleAddress.Format(Address);
}

public static class BleAddress
{
    /// <summary>48-bit Bluetooth address as AA:BB:CC:DD:EE:FF.</summary>
    public static string Format(ulong address)
        => string.Join(':', Enumerable.Range(0, 6).Select(i => ((address >> (8 * (5 - i))) & 0xFF).ToString("X2")));
}

/// <summary>One GATT notification, kept for diagnostics (hex / text / parsed value).</summary>
public sealed record BleNotification(DateTimeOffset Timestamp, byte[] Bytes, string Text, double? Value)
{
    public string ToLogLine()
    {
        var parsed = Value is { } v ? v.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) : "(parse failed)";
        return $"{Timestamp.ToLocalTime():HH:mm:ss.fff}  len={Bytes.Length}  {BlePayloadParser.ToHex(Bytes)}  \"{BlePayloadParser.ToPrintable(Text)}\"  -> {parsed}";
    }
}

public sealed record GattCharacteristicInfo(Guid Uuid, string Properties);

public sealed record GattServiceInfo(Guid Uuid, IReadOnlyList<GattCharacteristicInfo> Characteristics, string? Error = null);

public sealed record GattDiscoveryResult(
    ulong Address,
    string? DeviceName,
    string? Error,
    IReadOnlyList<GattServiceInfo> Services);
