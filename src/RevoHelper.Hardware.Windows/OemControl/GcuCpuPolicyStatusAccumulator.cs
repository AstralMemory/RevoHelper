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
        if (update.Extreme == 1 && update.Silent is null && previous?.Silent.HasValue == true)
        {
            silent = 0;
        }
        else if (update.Silent == 1 && update.Extreme is null && previous?.Extreme.HasValue == true)
        {
            extreme = 0;
        }

        var powerDisplay = !update.HasCpuPowerLimitSnapshot && previous is not null &&
                           !ChangedActiveProfile(previous, update)
            ? previous
            : update;
        var changedMode = previous?.OperatingMode.HasValue == true && update.OperatingMode.HasValue &&
                          update.OperatingMode != previous.OperatingMode;
        return update with
        {
            Pl1Watts = powerDisplay.Pl1Watts,
            Pl2Watts = powerDisplay.Pl2Watts,
            Pl1MinimumWatts = powerDisplay.Pl1MinimumWatts,
            Pl1MaximumWatts = powerDisplay.Pl1MaximumWatts,
            Pl2MinimumWatts = powerDisplay.Pl2MinimumWatts,
            Pl2MaximumWatts = powerDisplay.Pl2MaximumWatts,
            // Retained values are for display only, never a fresh readback.
            HasCpuPowerLimitSnapshot = update.HasCpuPowerLimitSnapshot,
            OperatingMode = update.OperatingMode ?? previous?.OperatingMode,
            GamingProfileIndex = update.GamingProfileIndex ??
                (changedMode && update.OperatingMode == 1 ? null : previous?.GamingProfileIndex),
            OfficeProfileIndex = update.OfficeProfileIndex ??
                (changedMode && update.OperatingMode == 0 ? null : previous?.OfficeProfileIndex),
            TurboProfileIndex = update.TurboProfileIndex ??
                (changedMode && update.OperatingMode == 2 ? null : previous?.TurboProfileIndex),
            CustomProfileIndex = update.CustomProfileIndex ??
                (changedMode && update.OperatingMode == 3 ? null : previous?.CustomProfileIndex),
            Extreme = extreme,
            Silent = silent,
            BatteryChargingLimitPercent = update.BatteryChargingLimitPercent ?? previous?.BatteryChargingLimitPercent,
            FanBoostEnable = update.FanBoostEnable ?? previous?.FanBoostEnable
        };
    }

    private static bool ChangedActiveProfile(GcuCpuPolicyStatus previous, GcuCpuPolicyStatus update)
    {
        if (update.OperatingMode.HasValue && update.OperatingMode != previous.OperatingMode) return true;
        return (update.OperatingMode ?? previous.OperatingMode) switch
        {
            0 => update.OfficeProfileIndex.HasValue && update.OfficeProfileIndex != previous.OfficeProfileIndex,
            1 => update.GamingProfileIndex.HasValue && update.GamingProfileIndex != previous.GamingProfileIndex,
            2 => update.TurboProfileIndex.HasValue && update.TurboProfileIndex != previous.TurboProfileIndex,
            3 => update.CustomProfileIndex.HasValue && update.CustomProfileIndex != previous.CustomProfileIndex,
            _ => true
        };
    }
}
