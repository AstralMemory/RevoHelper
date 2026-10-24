using RevoHelper.Core.Control;

namespace RevoHelper.Core.Hardware;

public sealed record OemRuntimeEvidence(
    IReadOnlySet<PlatformMode> SupportedPlatformModes,
    bool HasCpuPowerLimits,
    bool HasFanCurves,
    bool HasGpuOutputModes,
    bool HasLighting,
    bool HasLiquidCooling)
{
    public static OemRuntimeEvidence None { get; } = new(
        new HashSet<PlatformMode>(), false, false, false, false, false);
}
