using TailEmg.Core.Ble;

namespace TailEmg.Core.Tests;

public class GattReportTests
{
    [Fact]
    public void ListsActualServices_AndMarksExpectedOnes()
    {
        var p = BleNotifyProfile.MyoWare;
        var result = new GattDiscoveryResult(0xAABBCCDDEEFF, "MyoWareSensor1", null, new[]
        {
            new GattServiceInfo(Guid.Parse("00001800-0000-1000-8000-00805f9b34fb"), Array.Empty<GattCharacteristicInfo>()),
            new GattServiceInfo(p.ServiceUuid, new[] { new GattCharacteristicInfo(p.CharacteristicUuid, "Read, Notify") }),
        });

        var text = GattReport.Format(result, p);

        Assert.Contains("AA:BB:CC:DD:EE:FF", text);
        Assert.Contains("00001800-0000-1000-8000-00805f9b34fb", text);
        Assert.Contains("<== expected service", text);
        Assert.Contains("<== expected characteristic", text);
        Assert.Contains("Read, Notify", text);
        Assert.DoesNotContain("NOT FOUND", text);
    }

    [Fact]
    public void MissingExpectedService_StillShowsWhatExists()
    {
        var other = Guid.Parse("6E400001-B5A3-F393-E0A9-E50E24DCCA9E");
        var result = new GattDiscoveryResult(1, null, null, new[]
        {
            new GattServiceInfo(other, new[] { new GattCharacteristicInfo(Guid.NewGuid(), "Notify") }),
        });

        var text = GattReport.Format(result, BleNotifyProfile.MyoWare);

        Assert.Contains(other.ToString(), text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("NOT FOUND", text);
    }

    [Fact]
    public void Error_IsReported()
    {
        var text = GattReport.Format(new GattDiscoveryResult(1, null, "Unreachable", Array.Empty<GattServiceInfo>()), BleNotifyProfile.MyoWare);
        Assert.Contains("Unreachable", text);
        Assert.Contains("NOT FOUND", text);
    }

    [Fact]
    public void Profile_UsesDocumentedUuids()
    {
        Assert.Equal(Guid.Parse("ec3af789-2154-49f4-a9fc-bc6c88e9e930"), BleNotifyProfile.MyoWare.ServiceUuid);
        Assert.Equal(Guid.Parse("f3a56edf-8f1e-4533-93bf-5601b2e91308"), BleNotifyProfile.MyoWare.CharacteristicUuid);
    }
}
