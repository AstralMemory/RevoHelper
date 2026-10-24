namespace RevoHelper.Core.Hardware;

public sealed record CapabilityReport(
    MachineIdentity Identity,
    MachineSupportAssessment Support,
    bool ReadOnly,
    IReadOnlyList<string> EnabledWriteCapabilities);
