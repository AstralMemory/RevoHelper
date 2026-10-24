using RevoHelper.Core.Control;

namespace RevoHelper.Hardware.Windows.OemControl;

public static class GcuGpuOutputModeCommand
{
    public const string GetStatus = "{\"Action\":\"GETSTATUS\"}";

    public static string Select(GpuOutputMode mode) => mode switch
    {
        GpuOutputMode.Hybrid => "{\"Action\":\"DGPU_DIRECT_CONNECT_TOGGLE_OFF\"}",
        GpuOutputMode.Discrete => "{\"Action\":\"DGPU_DIRECT_CONNECT_TOGGLE_ON\"}",
        GpuOutputMode.Integrated => "{\"Action\":\"DGPU_DIRECT_CONNECT_TOGGLE_IGPU\"}",
        _ => throw new ArgumentOutOfRangeException(nameof(mode))
    };
}
