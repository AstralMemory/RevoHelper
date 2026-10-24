namespace RevoHelper.Core.Dashboard;

public sealed record DeviceStatus(
    DeviceKind Kind,
    string Label,
    string Model,
    string Details,
    string Usage,
    string Temperature,
    string Power,
    string Frequency);
