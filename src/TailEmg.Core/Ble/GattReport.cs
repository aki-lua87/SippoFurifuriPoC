using System.Text;

namespace TailEmg.Core.Ble;

/// <summary>Renders what a device actually exposes, and whether the expected profile is there.</summary>
public static class GattReport
{
    public static string Format(GattDiscoveryResult result, BleNotifyProfile expected)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Device : {result.DeviceName ?? "(no name)"}  {BleAddress.Format(result.Address)}");

        if (result.Error is not null)
        {
            sb.AppendLine($"ERROR  : {result.Error}");
        }

        sb.AppendLine($"Services: {result.Services.Count}");

        foreach (var service in result.Services)
        {
            var mark = service.Uuid == expected.ServiceUuid ? "   <== expected service" : "";
            sb.AppendLine($"  [S] {service.Uuid}{mark}");
            if (service.Error is not null)
            {
                sb.AppendLine($"      (characteristics unavailable: {service.Error})");
            }

            foreach (var ch in service.Characteristics)
            {
                var chMark = service.Uuid == expected.ServiceUuid && ch.Uuid == expected.CharacteristicUuid
                    ? "   <== expected characteristic"
                    : "";
                sb.AppendLine($"      [C] {ch.Uuid}  ({ch.Properties}){chMark}");
            }
        }

        var svc = result.Services.FirstOrDefault(s => s.Uuid == expected.ServiceUuid);
        var chr = svc?.Characteristics.FirstOrDefault(c => c.Uuid == expected.CharacteristicUuid);
        sb.AppendLine();
        sb.AppendLine($"Expected {expected.Name} service        {expected.ServiceUuid}: {(svc is null ? "NOT FOUND" : "found")}");
        sb.AppendLine($"Expected {expected.Name} characteristic {expected.CharacteristicUuid}: {(chr is null ? "NOT FOUND" : $"found ({chr.Properties})")}");
        return sb.ToString();
    }
}
