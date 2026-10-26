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
        var isMechrevo = identity.Manufacturer.Trim()
            .Equals("MECHREVO", StringComparison.OrdinalIgnoreCase);
        var oemBase = OemBaseAvailability(identity, isMechrevo, inventory);
        var features = Enum.GetValues<HardwareFeature>().ToDictionary(
            feature => feature,
            feature => Capability(feature, oemBase, evidence, cpuBackends));
        var modes = features[HardwareFeature.OemPlatformModes].Availability == CapabilityAvailability.Available
            ? evidence.SupportedPlatformModes
            : new HashSet<PlatformMode>();

        return new(identity, features, modes, cpuBackends);
    }

    private static (CapabilityAvailability Availability, string Reason, CapabilityDiagnostic Diagnostic) OemBaseAvailability(
        MachineIdentity identity,
        bool isMechrevo,
        OemControlInventory inventory)
    {
        if (string.IsNullOrWhiteSpace(identity.Manufacturer) ||
            identity.Manufacturer.Equals("UNKNOWN", StringComparison.OrdinalIgnoreCase))
        {
            return (CapabilityAvailability.Unverified, "Machine manufacturer could not be identified.",
                CapabilityDiagnostic.IdentityUnconfirmed);
        }
        if (!isMechrevo)
        {
            return (CapabilityAvailability.Unavailable,
                "OEM writes are scoped to explicitly identified MECHREVO systems.", CapabilityDiagnostic.OtherManufacturer);
        }

        if (!inventory.HasCompletePlatformModeChain)
        {
            return (CapabilityAvailability.Unavailable,
                "The required GCU/ACPI control chain is incomplete.", CapabilityDiagnostic.ControlChainIncomplete);
        }

        // Each feature needs its own evidence. An absent mode list must not
        // discard a positive fan/status response from the same verified chain.
        return (CapabilityAvailability.Available, "GCU control chain confirmed.", CapabilityDiagnostic.None);
    }

    private static HardwareFeatureCapability Capability(
        HardwareFeature feature,
        (CapabilityAvailability Availability, string Reason, CapabilityDiagnostic Diagnostic) oemBase,
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

        if (oemBase.Availability != CapabilityAvailability.Available)
        {
            return new(feature, oemBase.Availability, oemBase.Reason) { Diagnostic = oemBase.Diagnostic };
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
