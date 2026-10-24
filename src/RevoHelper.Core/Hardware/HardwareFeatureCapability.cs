namespace RevoHelper.Core.Hardware;

public enum HardwareFeature
{
    OemPlatformModes,
    OemCpuPowerLimits,
    OemFanCurves,
    GpuOutputModes,
    Lighting,
    LiquidCooling,
    WindowsProcessorPolicy,
    IntelRapl,
    AmdSmu
}

public sealed record HardwareFeatureCapability(
    HardwareFeature Feature,
    CapabilityAvailability Availability,
    string Reason);
