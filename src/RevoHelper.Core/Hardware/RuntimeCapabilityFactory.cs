using RevoHelper.Core.Discovery;
using RevoHelper.Core.Control;

namespace RevoHelper.Core.Hardware;

public static class RuntimeCapabilityFactory
{
    public static RuntimeCapabilitySnapshot Create(
        MachineIdentity identity,
        OemControlInventory inventory,
        OemRuntimeEvidence evidence,
        IReadOnlyList<CpuBackendCapability> cpuBackends)
    {
        // SMBIOS labels and component-file inventory describe discovery, not
        // whether a live feature response is valid. Assess each feature from
        // its own runtime evidence instead of imposing an installation gate.
        var features = Enum.GetValues<HardwareFeature>().ToDictionary(
            feature => feature,
            feature => Capability(feature, evidence, cpuBackends));
        var modes = features[HardwareFeature.OemPlatformModes].Availability == CapabilityAvailability.Available
            ? evidence.SupportedPlatformModes
            : new HashSet<PlatformMode>();

        return new(identity, features, modes, cpuBackends);
    }

    private static HardwareFeatureCapability Capability(
        HardwareFeature feature,
        OemRuntimeEvidence evidence,
        IReadOnlyList<CpuBackendCapability> cpuBackends)
    {
        if (feature is HardwareFeature.WindowsProcessorPolicy or HardwareFeature.IntelRapl or HardwareFeature.AmdSmu)
        {
            var backend = feature switch
            {
                HardwareFeature.WindowsProcessorPolicy => CpuControlBackend.WindowsPowerPolicy,
                HardwareFeature.IntelRapl => CpuControlBackend.IntelRapl,
                _ => CpuControlBackend.AmdSmu
            };
            var result = cpuBackends.FirstOrDefault(item => item.Backend == backend);
            return result is null
                ? new(feature, CapabilityAvailability.Unavailable, "Backend does not apply to this CPU.")
                : new(feature, result.Availability, result.Reason);
        }

        var confirmed = feature switch
        {
            HardwareFeature.OemPlatformModes => evidence.SupportedPlatformModes.Count > 0,
            HardwareFeature.OemCpuPowerLimits => evidence.HasCpuPowerLimits,
            HardwareFeature.OemFanCurves => evidence.HasFanCurves,
            HardwareFeature.GpuOutputModes => evidence.HasGpuOutputModes,
            HardwareFeature.Lighting => evidence.HasLighting,
            HardwareFeature.LiquidCooling => evidence.HasLiquidCooling,
            _ => false
        };

        return confirmed
            ? new(feature, CapabilityAvailability.Available, "Runtime evidence confirmed.")
            : new(feature, CapabilityAvailability.Unverified, "No positive runtime evidence has been observed yet.")
                { Diagnostic = CapabilityDiagnostic.AwaitingEvidence };
    }
}
