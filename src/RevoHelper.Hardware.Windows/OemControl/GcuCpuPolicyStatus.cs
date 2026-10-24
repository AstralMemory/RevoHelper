namespace RevoHelper.Hardware.Windows.OemControl;

public sealed record GcuCpuPolicyStatus(
    int Pl1Watts,
    int Pl2Watts,
    int Pl1MinimumWatts,
    int Pl1MaximumWatts,
    int Pl2MinimumWatts,
    int Pl2MaximumWatts,
    int? OperatingMode = null,
    int? GamingProfileIndex = null,
    int? OfficeProfileIndex = null,
    int? TurboProfileIndex = null,
    int? CustomProfileIndex = null,
    int? Extreme = null,
    int? Silent = null,
    int? BatteryChargingLimitPercent = null,
    int? FanBoostEnable = null);
