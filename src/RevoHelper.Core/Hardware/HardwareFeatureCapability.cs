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

public enum CapabilityDiagnostic
{
    None,
    IdentityUnconfirmed,
    OtherManufacturer,
    ControlChainIncomplete,
    AwaitingEvidence
}

public sealed record HardwareFeatureCapability(
    HardwareFeature Feature,
    CapabilityAvailability Availability,
    string Reason)
{
    public CapabilityDiagnostic Diagnostic { get; init; }
}
