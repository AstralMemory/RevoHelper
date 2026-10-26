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
    int? FanBoostEnable = null)
{
    // True only when this response carried all six CPU power values. Display
    // values retained from earlier updates must never become a fresh write baseline.
    public bool HasCpuPowerLimitSnapshot { get; init; } = true;

    public bool SupportsTurboStrategies => Extreme is 0 or 1 && Silent is 0 or 1;
}
