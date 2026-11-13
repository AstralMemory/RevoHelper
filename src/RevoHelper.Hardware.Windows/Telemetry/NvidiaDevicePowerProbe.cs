using System.Runtime.InteropServices;

namespace RevoHelper.Hardware.Windows.Telemetry;

/// <summary>
/// Asks Windows (not the NVIDIA driver) whether the NVIDIA display adapter is powered.
/// Reading this property never wakes the device.
/// </summary>
public sealed class NvidiaDevicePowerProbe
{
    private static readonly Guid DisplayClass = new("4d36e968-e325-11ce-bfc1-08002be10318");
    private static readonly Guid DeviceFormat = new("a45c254e-df1c-4efd-8020-67d146a850e0");
    private string[]? deviceIds;

    /// <summary>true = D0, false = D1–D3, null = no NVIDIA adapter or the state cannot be read.</summary>
    public bool? ReadPowered()
    {
        try
        {
            deviceIds ??= FindAdapters();
            bool? result = null;
            foreach (var id in deviceIds)
            {
                if (CM_Locate_DevNodeW(out var node, id, 0) != 0) continue;
                var data = ReadProperty(node, 32, 56);
                if (data is null || data.Length < 8) continue;
                var state = BitConverter.ToInt32(data, 4);
                if (state == 1) return true;           // any powered adapter means "awake"
                if (state is >= 2 and <= 4) result = false;
            }
            return result;
        }
        catch (Exception exception) when (exception is DllNotFoundException or EntryPointNotFoundException or ArgumentException)
        {
            return null;
        }
    }

    private static string[] FindAdapters()
    {
        const uint filterEnumerator = 0x1, filterPresent = 0x100;
        if (CM_Get_Device_ID_List_SizeW(out var length, "PCI", filterEnumerator | filterPresent) != 0 || length < 2)
            return [];
        var buffer = new char[length];
        if (CM_Get_Device_ID_ListW("PCI", buffer, length, filterEnumerator | filterPresent) != 0) return [];
        return new string(buffer).Split('\0', StringSplitOptions.RemoveEmptyEntries)
            .Where(id => id.StartsWith(@"PCI\VEN_10DE", StringComparison.OrdinalIgnoreCase))
            .Where(id => CM_Locate_DevNodeW(out var node, id, 0) == 0
                && ReadProperty(node, 10, 16) is { Length: 16 } guid && new Guid(guid) == DisplayClass)
            .ToArray();
    }

    private static byte[]? ReadProperty(uint node, uint propertyId, int capacity)
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
    private static extern uint CM_Locate_DevNodeW(out uint node, string deviceId, uint flags);

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
    private static extern uint CM_Get_DevNode_PropertyW(uint node, in DevPropKey key, out uint type,
        [Out] byte[] buffer, ref uint size, uint flags);
}
