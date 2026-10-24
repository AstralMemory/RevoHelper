namespace RevoHelper.Core.Hardware;

public static class CapabilityReportFactory
{
    public static CapabilityReport Create(MachineIdentity identity) =>
        new(
            identity,
            MachineSupportClassifier.Classify(identity),
            ReadOnly: true,
            EnabledWriteCapabilities: Array.Empty<string>());
}
