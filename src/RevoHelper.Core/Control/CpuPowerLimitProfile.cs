namespace RevoHelper.Core.Control;

public sealed record CpuPowerLimitProfile(
    string Key,
    string SourceName,
    string DisplayName,
    int Pl1Watts,
    bool Pl1Enabled,
    int Pl2Watts,
    bool Pl2Enabled,
    bool Activated);
