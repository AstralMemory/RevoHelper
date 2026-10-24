namespace RevoHelper.Core.Control;

public enum PlatformMode
{
    Office,
    Balanced,
    TurboQuiet,
    TurboHigh,
    Custom
}

public sealed record PlatformModeSelection(PlatformMode Mode, int ProfileIndex = 0);

public enum ControlModule
{
    PlatformMode,
    CpuPolicy,
    GpuPolicy,
    FanCurve,
    Lighting,
    LiquidCooling
}
