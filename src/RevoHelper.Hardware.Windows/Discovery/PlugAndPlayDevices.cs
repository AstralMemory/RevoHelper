using System.Runtime.InteropServices;

namespace RevoHelper.Hardware.Windows.Discovery;

/// <summary>PCI bus position of a device, as the bus driver reports it.</summary>
public sealed record PciLocation(uint Bus, uint Device, uint Function);

/// <summary>
/// Reads Plug and Play device properties straight from the configuration manager: no WMI, no
/// driver, and reading never wakes a sleeping device.
/// </summary>
public static class PlugAndPlayDevices
{
    private static readonly Guid DisplayClass = new("4d36e968-e325-11ce-bfc1-08002be10318");
    private static readonly Guid DeviceFormat = new("a45c254e-df1c-4efd-8020-67d146a850e0");
    // GUID_DISPLAY_DEVICE_ARRIVAL: the interface a WDDM adapter is opened by.
    private static readonly Guid DisplayDeviceInterface = new("1ca05180-a699-450a-9a0c-de4fbe3ddd89");
    private const uint DeviceDescription = 2, ClassGuid = 10, FriendlyName = 14, BusNumber = 23, Address = 30, PowerData = 32;

    /// <summary>
    /// true = working (D0), false = powered down (D1–D3), null = unknown. This is the Plug and Play
    /// manager's own record, so reading it never wakes the device.
    /// </summary>
    public static bool? ReadPowered(string instanceId)
    {
        try
        {
            if (CM_Locate_DevNodeW(out var node, instanceId, 0) != 0) return null;
            // CM_POWER_DATA: its size, then the most recent DEVICE_POWER_STATE (1 = D0 … 4 = D3).
            var data = ReadBytes(node, PowerData, 56);
            if (data is null || data.Length < 8) return null;
            var state = BitConverter.ToInt32(data, 4);
            return state == 1 ? true : state is >= 2 and <= 4 ? false : null;
        }
        catch (Exception exception) when (exception is DllNotFoundException or EntryPointNotFoundException or ArgumentException)
        {
            return null;
        }
    }

    /// <summary>The device path the graphics kernel opens this adapter by; null when it has no WDDM driver.</summary>
    public static string? DisplayInterfacePath(string instanceId)
    {
        try
        {
            var guid = DisplayDeviceInterface;
            if (CM_Get_Device_Interface_List_SizeW(out var length, ref guid, instanceId, 0) != 0 || length < 2) return null;
            var buffer = new char[length];
            if (CM_Get_Device_Interface_ListW(ref guid, instanceId, buffer, length, 0) != 0) return null;
            return new string(buffer).Split('\0', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        }
        catch (Exception exception) when (exception is DllNotFoundException or EntryPointNotFoundException or ArgumentException)
        {
            return null;
        }
    }

    /// <summary>
    /// A device path (\\?\PCI#VEN_10DE&amp;…#4&amp;…#{guid}) becomes its instance ID (PCI\VEN_10DE&amp;…\4&amp;…);
    /// an instance ID comes back as it is.
    /// </summary>
    public static string NormalizeInstanceId(string value)
    {
        var text = value.Trim();
        if (!text.StartsWith(@"\\?\", StringComparison.Ordinal)) return text;
        text = text[4..];
        var guid = text.LastIndexOf("#{", StringComparison.Ordinal);
        if (guid >= 0) text = text[..guid];
        return text.Replace('#', '\\');
    }

    public static PciLocation? ReadPciLocation(string instanceId)
    {
        try
        {
            if (!instanceId.StartsWith("PCI\\", StringComparison.OrdinalIgnoreCase) ||
                CM_Locate_DevNodeW(out var node, instanceId, 0) != 0) return null;
            if (ReadUInt32(node, BusNumber) is not { } bus || ReadUInt32(node, Address) is not { } address) return null;
            // For PCI the address is (device << 16) | function.
            return new(bus, address >> 16, address & 0xFFFF);
        }
        catch (Exception exception) when (exception is DllNotFoundException or EntryPointNotFoundException or ArgumentException)
        {
            return null;
        }
    }

    /// <summary>Present PCI display adapters with their driver's description; the list WMI would give.</summary>
    public static IReadOnlyList<(string InstanceId, string Name)> PresentDisplayAdapters()
    {
        try
        {
            const uint filterEnumerator = 0x1, filterPresent = 0x100;
            if (CM_Get_Device_ID_List_SizeW(out var length, "PCI", filterEnumerator | filterPresent) != 0 || length < 2) return [];
            var buffer = new char[length];
            if (CM_Get_Device_ID_ListW("PCI", buffer, length, filterEnumerator | filterPresent) != 0) return [];
            var adapters = new List<(string, string)>();
            foreach (var id in new string(buffer).Split('\0', StringSplitOptions.RemoveEmptyEntries))
            {
                if (CM_Locate_DevNodeW(out var node, id, 0) != 0) continue;
                if (ReadBytes(node, ClassGuid, 16) is not { Length: 16 } guid || new Guid(guid) != DisplayClass) continue;
                var name = ReadString(node, FriendlyName) ?? ReadString(node, DeviceDescription);
                if (!string.IsNullOrWhiteSpace(name)) adapters.Add((id, name.Trim()));
            }
            return adapters;
        }
        catch (Exception exception) when (exception is DllNotFoundException or EntryPointNotFoundException or ArgumentException)
        {
            return [];
        }
    }

    private static uint? ReadUInt32(uint node, uint propertyId) =>
        ReadBytes(node, propertyId, 4) is { Length: 4 } data ? BitConverter.ToUInt32(data) : null;

    private static string? ReadString(uint node, uint propertyId)
    {
        var data = ReadBytes(node, propertyId, 1024);
        if (data is null || data.Length < 2) return null;
        var text = System.Text.Encoding.Unicode.GetString(data);
        var end = text.IndexOf('\0');
        return end >= 0 ? text[..end] : text;
    }

    private static byte[]? ReadBytes(uint node, uint propertyId, int capacity)
    {
        var key = new DevPropKey { FormatId = DeviceFormat, PropertyId = propertyId };
        var buffer = new byte[capacity];
        var size = (uint)capacity;
        return CM_Get_DevNode_PropertyW(node, in key, out _, buffer, ref size, 0) == 0
            ? buffer.AsSpan(0, (int)Math.Min(size, (uint)capacity)).ToArray()
            : null;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DevPropKey { public Guid FormatId; public uint PropertyId; }

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
    private static extern uint CM_Get_Device_ID_List_SizeW(out uint length, string? filter, uint flags);

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
    private static extern uint CM_Get_Device_ID_ListW(string? filter, [Out] char[] buffer, uint length, uint flags);

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
    private static extern uint CM_Get_Device_Interface_List_SizeW(out uint length, ref Guid interfaceClass, string? deviceId, uint flags);

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
    private static extern uint CM_Get_Device_Interface_ListW(ref Guid interfaceClass, string? deviceId, [Out] char[] buffer,
        uint length, uint flags);

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
    private static extern uint CM_Locate_DevNodeW(out uint node, string deviceId, uint flags);

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
    private static extern uint CM_Get_DevNode_PropertyW(uint node, in DevPropKey key, out uint type,
        [Out] byte[] buffer, ref uint size, uint flags);
}
