namespace RevoHelper.Core.Hardware;

public enum CpuControlBackend
{
    WindowsPowerPolicy,
    IntelRapl,
    AmdSmu
}

public sealed record CpuBackendCapability(
    CpuControlBackend Backend,
    CapabilityAvailability Availability,
    string Reason);
