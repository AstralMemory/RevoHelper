using Microsoft.Win32;
using RevoHelper.Core.Control;

namespace RevoHelper.Hardware.Windows.OemControl;

public sealed class WindowsGpuOutputModeCapabilitySource : IGpuOutputModeCapabilitySource
{
    private const string ItemSupportPath = @"SOFTWARE\OEM\GamingCenter2\ItemSupport";

    public GpuOutputModeCapabilities Read()
    {
        using var key = Registry.LocalMachine.OpenSubKey(ItemSupportPath, writable: false);
        var direct = ReadFlag(key, "DGpuDirectConnectionSupport");
        var integrated = ReadFlag(key, "iGPUModeOnlySupport");
        return new(direct, direct, integrated);
    }

    private static bool ReadFlag(RegistryKey? key, string name) => key?.GetValue(name) switch
    {
        int value => value == 1,
        long value => value == 1,
        string value => value is "1" or "true" or "True",
        _ => false
    };
}
