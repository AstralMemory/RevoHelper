namespace RevoHelper.Core.Hardware;

public sealed record MachineSupportAssessment(
    MachineSupportStatus Status,
    string DeviceKey,
    string Reason);
