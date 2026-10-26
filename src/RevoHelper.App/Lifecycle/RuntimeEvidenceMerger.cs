using RevoHelper.Core.Control;
using RevoHelper.Core.Hardware;

namespace RevoHelper.App.Lifecycle;

public static class RuntimeEvidenceMerger
{
    public static OemRuntimeEvidence Merge(OemRuntimeEvidence current, OemRuntimeEvidence update)
    {
        var modes = new HashSet<PlatformMode>(current.SupportedPlatformModes.Concat(update.SupportedPlatformModes));
        var merged = new OemRuntimeEvidence(modes,
            current.HasCpuPowerLimits || update.HasCpuPowerLimits,
            current.HasFanCurves || update.HasFanCurves,
            current.HasGpuOutputModes || update.HasGpuOutputModes,
            current.HasLighting || update.HasLighting,
            current.HasLiquidCooling || update.HasLiquidCooling);
        // Preserve object identity for no-op updates, so periodic CPU/status
        // messages do not continuously rebuild controls or interrupt editors.
        return modes.SetEquals(current.SupportedPlatformModes) &&
            (merged with { SupportedPlatformModes = current.SupportedPlatformModes }) == current ? current : merged;
    }
}
