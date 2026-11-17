using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Devices.Enumeration;
using Windows.Storage.Streams;

namespace OpenTaskbar.Services;

public enum WirelessKind { Headset, Mouse, Keyboard, Controller, Phone, Other }

public sealed record WirelessDevice(string Name, WirelessKind Kind, int? Battery);

/// <summary>
/// Connected Bluetooth devices (classic and LE) with their battery level when Windows or the device reports it.
/// Classic devices (most headsets) expose it as the PnP property DEVPKEY_Bluetooth_Battery; LE devices through the
/// GATT Battery Service, read with a short timeout like João's SysVitals does. Nothing is invented: no value, no %.
/// </summary>
public static class WirelessDevices
{
    private const string BatteryProperty = "{104EA319-6EE2-4701-BD47-8DDBF425BBE5} 2";
    private static readonly Guid BatteryService = new("0000180f-0000-1000-8000-00805f9b34fb");
    private static readonly Guid BatteryLevel = new("00002a19-0000-1000-8000-00805f9b34fb");

    // Bluetooth association endpoints (paired devices) that are connected right now: classic and LE.
    private const string ClassicProtocol = "{e0cbf06c-cd8b-4647-bb8a-263b43f0f974}";
    private const string LowEnergyProtocol = "{bb7bb05e-5972-42b5-94fc-76eaa7084d49}";
    private const string ConnectedProperty = "System.Devices.Aep.IsConnected";

    public static async Task<IReadOnlyList<WirelessDevice>> ReadAsync(CancellationToken cancellation)
    {
        var devices = new Dictionary<string, WirelessDevice>(StringComparer.OrdinalIgnoreCase);
        foreach (var (protocol, lowEnergy) in new[] { (ClassicProtocol, false), (LowEnergyProtocol, true) })
        {
            IReadOnlyList<DeviceInformation> found;
            try
            {
                var query = $"System.Devices.Aep.ProtocolId:=\"{protocol}\" AND {ConnectedProperty}:=System.StructuredQueryType.Boolean#True";
                found = await DeviceInformation.FindAllAsync(query, [BatteryProperty, ConnectedProperty], DeviceInformationKind.AssociationEndpoint);
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or System.Runtime.InteropServices.COMException or ArgumentException)
            {
                AppLog.Warning($"Bluetooth query failed: {ex.Message}");
                continue;
            }

            foreach (var info in found.Take(12))
            {
                cancellation.ThrowIfCancellationRequested();
                if (string.IsNullOrWhiteSpace(info.Name)) continue;
                // One device that refuses access must not hide the others.
                try
                {
                    var battery = ReadBatteryProperty(info);
                    var kind = Classify(info.Name);
                    if (!lowEnergy)
                    {
                        using var device = await BluetoothDevice.FromIdAsync(info.Id);
                        if (device is not null) kind = Classify(info.Name, (int)device.ClassOfDevice.MajorClass, (int)device.ClassOfDevice.MinorClass);
                    }
                    else
                    {
                        battery ??= await ReadGattBatteryAsync(info.Id);
                        using var device = await BluetoothLEDevice.FromIdAsync(info.Id);
                        if (kind == WirelessKind.Other && device is not null) kind = Classify(info.Name, appearanceCategory: device.Appearance.Category);
                    }

                    // The same device often appears as classic and LE; keep what each one knows.
                    if (devices.TryGetValue(info.Name, out var existing))
                        devices[info.Name] = existing with { Battery = existing.Battery ?? battery, Kind = existing.Kind != WirelessKind.Other ? existing.Kind : kind };
                    else
                        devices[info.Name] = new WirelessDevice(info.Name, kind, battery);
                }
                catch (Exception ex) when (ex is UnauthorizedAccessException or System.Runtime.InteropServices.COMException
                                               or InvalidOperationException or ArgumentException)
                {
                    devices.TryAdd(info.Name, new WirelessDevice(info.Name, Classify(info.Name), ReadBatteryProperty(info)));
                }
            }
        }

        return devices.Values.OrderBy(device => device.Kind).ThenBy(device => device.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }
    /// <summary>
    /// Device type from the Bluetooth class of device (major 4 = audio/video, 5 = peripheral with minor bits for
    /// keyboard/pointing/gamepad, 2 = phone), the LE appearance category, or finally the name.
    /// </summary>
    public static WirelessKind Classify(string? name, int? majorClass = null, int? minorClass = null, ushort? appearanceCategory = null)
    {
        switch (majorClass)
        {
            case 4: return WirelessKind.Headset;
            case 2: return WirelessKind.Phone;
            case 5 when minorClass is { } minor:
                // Peripheral minor: bits 4-5 keyboard(1)/pointing(2)/combo(3); low bits 1-2 joystick/gamepad.
                if ((minor & 0x0F) is 1 or 2) return WirelessKind.Controller;
                return ((minor >> 4) & 0x3) switch { 1 => WirelessKind.Keyboard, 2 => WirelessKind.Mouse, 3 => WirelessKind.Keyboard, _ => FromName(name) };
        }

        return appearanceCategory switch
        {
            0x0F when FromName(name) is var byName && byName != WirelessKind.Other => byName, // generic HID
            0x01 => WirelessKind.Phone,
            0x29 or 0x0941 => WirelessKind.Headset,
            _ => FromName(name)
        };
    }

    private static WirelessKind FromName(string? name)
    {
        var text = name?.ToLowerInvariant() ?? string.Empty;
        if (new[] { "headset", "headphone", "buds", "airpods", "fone", "earbud", "wh-", "wf-", "jbl", "audio", "speaker", "caixa", "soundcore", "h2015", "qcy", "edifier" }.Any(text.Contains)) return WirelessKind.Headset;
        if (new[] { "mouse", "mx master", "mx anywhere", "trackball" }.Any(text.Contains)) return WirelessKind.Mouse;
        if (new[] { "keyboard", "teclado", "keys" }.Any(text.Contains)) return WirelessKind.Keyboard;
        if (new[] { "controller", "gamepad", "xbox", "dualsense", "dualshock", "joy-con", "controle" }.Any(text.Contains)) return WirelessKind.Controller;
        if (new[] { "phone", "iphone", "galaxy", "pixel", "redmi", "moto", "celular" }.Any(text.Contains)) return WirelessKind.Phone;
        return WirelessKind.Other;
    }

    private static int? ReadBatteryProperty(DeviceInformation info) =>
        info.Properties.TryGetValue(BatteryProperty, out var value) && value is byte level and <= 100 ? level : null;

    private static async Task<int?> ReadGattBatteryAsync(string deviceId)
    {
        BluetoothLEDevice? device = null;
        try
        {
            var open = BluetoothLEDevice.FromIdAsync(deviceId).AsTask();
            if (await Task.WhenAny(open, Task.Delay(3000)) != open) return null;
            device = await open;
            if (device is null) return null;
            var services = await device.GetGattServicesForUuidAsync(BatteryService, BluetoothCacheMode.Cached);
            if (services.Status != GattCommunicationStatus.Success || services.Services.Count == 0) return null;
            using var service = services.Services[0];
            var characteristics = await service.GetCharacteristicsForUuidAsync(BatteryLevel, BluetoothCacheMode.Cached);
            if (characteristics.Status != GattCommunicationStatus.Success || characteristics.Characteristics.Count == 0) return null;
            var read = await characteristics.Characteristics[0].ReadValueAsync(BluetoothCacheMode.Cached);
            if (read.Status != GattCommunicationStatus.Success) return null;
            var level = DataReader.FromBuffer(read.Value).ReadByte();
            return level <= 100 ? level : null;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.IO.IOException or InvalidOperationException
                                       or ArgumentException or System.Runtime.InteropServices.COMException)
        {
            return null;
        }
        finally
        {
            device?.Dispose();
        }
    }
}
