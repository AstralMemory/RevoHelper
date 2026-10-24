namespace RevoHelper.Hardware.Windows.OemControl;

/// <summary>
/// GCU emits both full Fan/Status snapshots and partial updates after individual
/// commands. Preserve the last known optional fields so a strategy-only response
/// cannot erase the current platform mode.
/// </summary>
public static class GcuCpuPolicyStatusAccumulator
{
    public static GcuCpuPolicyStatus Merge(
        GcuCpuPolicyStatus? previous,
        GcuCpuPolicyStatus update)
    {
        ArgumentNullException.ThrowIfNull(update);

        var extreme = update.Extreme ?? previous?.Extreme;
        var silent = update.Silent ?? previous?.Silent;
        if (update.Extreme == 1 && update.Silent is null)
        {
            silent = 0;
        }
        else if (update.Silent == 1 && update.Extreme is null)
        {
            extreme = 0;
        }

        return update with
        {
            OperatingMode = update.OperatingMode ?? previous?.OperatingMode,
            GamingProfileIndex = update.GamingProfileIndex ?? previous?.GamingProfileIndex,
            OfficeProfileIndex = update.OfficeProfileIndex ?? previous?.OfficeProfileIndex,
            TurboProfileIndex = update.TurboProfileIndex ?? previous?.TurboProfileIndex,
            CustomProfileIndex = update.CustomProfileIndex ?? previous?.CustomProfileIndex,
            Extreme = extreme,
            Silent = silent,
            BatteryChargingLimitPercent = update.BatteryChargingLimitPercent ?? previous?.BatteryChargingLimitPercent,
            FanBoostEnable = update.FanBoostEnable ?? previous?.FanBoostEnable
        };
    }
}
