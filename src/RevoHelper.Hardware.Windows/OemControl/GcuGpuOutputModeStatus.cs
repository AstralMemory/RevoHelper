using RevoHelper.Core.Control;

namespace RevoHelper.Hardware.Windows.OemControl;

public sealed record GcuGpuOutputModeStatus(
    GpuOutputMode Mode,
    bool? SupportsDirectSwitch,
    bool? SupportsIntegrated);
