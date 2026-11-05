using Microsoft.Win32;
using RevoHelper.Core.Control;

namespace RevoHelper.Hardware.Windows.OemControl;

public sealed class WindowsGpuOutputModeCapabilitySource : IGpuOutputModeCapabilitySource
{
    private const string ItemSupportPath = @"SOFTWARE\OEM\GamingCenter2\ItemSupport";

    public GpuOutputMachineSupport Read()
    {
        using var key = Registry.LocalMachine.OpenSubKey(ItemSupportPath, writable: false);
        return new(
            ParseFlag(key?.GetValue("DGpuDirectConnectionSupport")),
            ParseFlag(key?.GetValue("iGPUModeOnlySupport")));
    }

    public static bool? ParseFlag(object? value) => value switch
    {
        int number => number == 1,
        long number => number == 1,
        string text when text is "1" or "true" or "True" => true,
        string text when text is "0" or "false" or "False" => false,
        _ => null
    };
}
