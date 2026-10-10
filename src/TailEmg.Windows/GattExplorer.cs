using TailEmg.Core.Ble;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.GenericAttributeProfile;

namespace TailEmg.Windows;

/// <summary>Connects briefly and lists every GATT service / characteristic the device really exposes.</summary>
public static class GattExplorer
{
    public static async Task<GattDiscoveryResult> DiscoverAsync(ulong address, CancellationToken ct)
    {
        string? name = null;
        try
        {
            using var device = await BluetoothLEDevice.FromBluetoothAddressAsync(address).AsTask(ct);
            if (device is null)
            {
                return new GattDiscoveryResult(address, null, "Device not found (is it powered on and in range?)", Array.Empty<GattServiceInfo>());
            }

            name = device.Name;
            var servicesResult = await device.GetGattServicesAsync(BluetoothCacheMode.Uncached).AsTask(ct);
            if (servicesResult.Status != GattCommunicationStatus.Success)
            {
                return new GattDiscoveryResult(address, name, $"GetGattServices: {servicesResult.Status}", Array.Empty<GattServiceInfo>());
            }

            var services = new List<GattServiceInfo>();
            foreach (var service in servicesResult.Services)
            {
                try
                {
                    var chars = await service.GetCharacteristicsAsync(BluetoothCacheMode.Uncached).AsTask(ct);
                    if (chars.Status != GattCommunicationStatus.Success)
                    {
                        services.Add(new GattServiceInfo(service.Uuid, Array.Empty<GattCharacteristicInfo>(), chars.Status.ToString()));
                        continue;
                    }

                    services.Add(new GattServiceInfo(
                        service.Uuid,
                        chars.Characteristics.Select(c => new GattCharacteristicInfo(c.Uuid, c.CharacteristicProperties.ToString())).ToArray()));
                }
                finally
                {
                    service.Dispose();
                }
            }

            return new GattDiscoveryResult(address, name, null, services);
        }
        catch (OperationCanceledException)
        {
            return new GattDiscoveryResult(address, name, "Timed out / cancelled", Array.Empty<GattServiceInfo>());
        }
        catch (Exception ex)
        {
            return new GattDiscoveryResult(address, name, ex.Message, Array.Empty<GattServiceInfo>());
        }
    }
}
