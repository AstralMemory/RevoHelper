using RevoHelper.Core.Control;

namespace RevoHelper.Core.Hardware;

public sealed record RuntimeCapabilitySnapshot(
    MachineIdentity Identity,
    IReadOnlyDictionary<HardwareFeature, HardwareFeatureCapability> Features,
    IReadOnlySet<PlatformMode> SupportedPlatformModes,
    IReadOnlyList<CpuBackendCapability> CpuBackends)
{
    public HardwareFeatureCapability this[HardwareFeature feature] => Features[feature];

    public bool Supports(HardwareFeature feature) =>
        Features.TryGetValue(feature, out var capability) &&
        capability.Availability == CapabilityAvailability.Available;
}
