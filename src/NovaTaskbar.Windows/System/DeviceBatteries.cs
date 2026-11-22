using Microsoft.Extensions.Logging;
using NovaTaskbar.Core.Widgets;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Devices.Enumeration;

namespace NovaTaskbar.Windows.SystemInfo;

/// <summary>
/// Bateria de periféricos Bluetooth conectados — as mesmas fontes que as Configurações do Windows usam:
/// (1) propriedade de bateria do dispositivo (fones e periféricos clássicos, Windows 10 2004+) e
/// (2) serviço GATT de Bateria (0x180F) de aparelhos BLE (mouse, teclado, controles). Somente leitura, local.
/// </summary>
public sealed class DeviceBatteries(ILogger log)
{
    private const string BatteryKey = "{104EA319-6EE2-4701-BD47-8DDBF425BBE5} 2"; // DEVPKEY_Bluetooth_Battery
    private const string ContainerKey = "System.Devices.ContainerId";
    private const string ConnectedKey = "System.Devices.Aep.IsConnected";
    private const string AepContainerKey = "System.Devices.Aep.ContainerId";
    private const string CodMajorKey = "System.Devices.Aep.Bluetooth.Cod.Major";
    private const string CodMinorKey = "System.Devices.Aep.Bluetooth.Cod.Minor";
    private const string AppearanceKey = "System.Devices.Aep.Bluetooth.Le.Appearance";
    private static readonly TimeSpan GattTimeout = TimeSpan.FromSeconds(4);

    private sealed record Endpoint(string Name, int? Major, int? Minor, int? Appearance);

    public async Task<IReadOnlyList<PeripheralBattery>> ReadAsync()
    {
        var result = new List<PeripheralBattery>();
        Dictionary<Guid, Endpoint> connected;
        try { connected = await ConnectedEndpointsAsync(); }
        catch (Exception ex) { log.LogWarning(ex, "Periféricos: falha ao listar conectados"); return []; }
        if (connected.Count == 0) return [];

        try { await ReadPnpBatteryAsync(connected, result); }
        catch (Exception ex) { log.LogWarning(ex, "Periféricos: falha na propriedade de bateria"); }
        try { await ReadGattBatteryAsync(connected, result); }
        catch (Exception ex) { log.LogWarning(ex, "Periféricos: falha no serviço GATT de bateria"); }
        return Peripherals.Normalize(result);
    }

    /// <summary>Aparelhos Bluetooth (clássico e LE) conectados agora, por ContainerId.</summary>
    private static async Task<Dictionary<Guid, Endpoint>> ConnectedEndpointsAsync()
    {
        var map = new Dictionary<Guid, Endpoint>();
        string[] props = [ConnectedKey, AepContainerKey, CodMajorKey, CodMinorKey, AppearanceKey];
        foreach (var selector in new[] { BluetoothDevice.GetDeviceSelectorFromPairingState(true), BluetoothLEDevice.GetDeviceSelectorFromPairingState(true) })
        {
            var devices = await DeviceInformation.FindAllAsync(selector, props, DeviceInformationKind.AssociationEndpoint);
            foreach (var d in devices)
            {
                if (d.Properties.TryGetValue(ConnectedKey, out var c) && c is true && d.Properties.TryGetValue(AepContainerKey, out var id) && id is Guid g)
                    map[g] = new Endpoint(d.Name, AsInt(d, CodMajorKey), AsInt(d, CodMinorKey), AsInt(d, AppearanceKey));
            }
        }
        return map;
    }

    private static async Task ReadPnpBatteryAsync(Dictionary<Guid, Endpoint> connected, List<PeripheralBattery> result)
    {
        var nodes = await DeviceInformation.FindAllAsync("", [BatteryKey, ContainerKey], DeviceInformationKind.Device);
        foreach (var n in nodes)
        {
            if (!n.Properties.TryGetValue(BatteryKey, out var b) || b is null) continue;
            if (!n.Properties.TryGetValue(ContainerKey, out var cid) || cid is not Guid g || !connected.TryGetValue(g, out var ep)) continue;
            int pct = Convert.ToInt32(b);
            result.Add(new PeripheralBattery(ep.Name, pct, Peripherals.Classify(ep.Name, ep.Major, ep.Minor, ep.Appearance)));
        }
    }

    private async Task ReadGattBatteryAsync(Dictionary<Guid, Endpoint> connected, List<PeripheralBattery> result)
    {
        var services = await DeviceInformation.FindAllAsync(GattDeviceService.GetDeviceSelectorFromUuid(GattServiceUuids.Battery), [ContainerKey]);
        foreach (var s in services)
        {
            if (!s.Properties.TryGetValue(ContainerKey, out var cid) || cid is not Guid g || !connected.TryGetValue(g, out var ep)) continue;
            try
            {
                using var service = await GattDeviceService.FromIdAsync(s.Id).AsTask().WaitAsync(GattTimeout);
                if (service is null) continue;
                var chars = await service.GetCharacteristicsForUuidAsync(GattCharacteristicUuids.BatteryLevel, BluetoothCacheMode.Uncached).AsTask().WaitAsync(GattTimeout);
                if (chars.Status != GattCommunicationStatus.Success || chars.Characteristics.Count == 0) continue;
                var read = await chars.Characteristics[0].ReadValueAsync(BluetoothCacheMode.Uncached).AsTask().WaitAsync(GattTimeout);
                if (read.Status != GattCommunicationStatus.Success || read.Value.Length < 1) continue;
                using var reader = global::Windows.Storage.Streams.DataReader.FromBuffer(read.Value);
                result.Add(new PeripheralBattery(ep.Name, reader.ReadByte(), Peripherals.Classify(ep.Name, ep.Major, ep.Minor, ep.Appearance)));
            }
            catch (Exception ex) when (ex is TimeoutException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException)
            {
                log.LogDebug("Periféricos: GATT indisponível para {Name}: {Error}", ep.Name, ex.Message);
            }
        }
    }

    private static int? AsInt(DeviceInformation d, string key) =>
        d.Properties.TryGetValue(key, out var v) && v is not null ? Convert.ToInt32(v) : null;
}
