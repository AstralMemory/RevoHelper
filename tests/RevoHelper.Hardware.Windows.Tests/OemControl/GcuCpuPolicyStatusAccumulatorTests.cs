using RevoHelper.Core.Control;
using RevoHelper.Hardware.Windows.OemControl;

namespace RevoHelper.Hardware.Windows.Tests.OemControl;

public sealed class GcuCpuPolicyStatusAccumulatorTests
{
    [Fact]
    public void Merge_TurboStrategyPartialStatus_PreservesTheRecognizablePlatformMode()
    {
        var full = Status(
            operatingMode: 2,
            turboProfileIndex: 0,
            extreme: 0,
            silent: 1);
        var strategyOnly = Status(extreme: 1, silent: 0);

        var merged = GcuCpuPolicyStatusAccumulator.Merge(full, strategyOnly);

        Assert.Equal(2, merged.OperatingMode);
        Assert.Equal(0, merged.TurboProfileIndex);
        Assert.Equal(1, merged.Extreme);
        Assert.Equal(0, merged.Silent);
        Assert.Equal(new PlatformModeSelection(PlatformMode.TurboHigh), GcuPlatformModeStatusMapper.Map(merged));
    }

    [Fact]
    public void Merge_SingleTurboFlag_ClearsTheMutuallyExclusivePreviousFlag()
    {
        var quiet = Status(operatingMode: 2, turboProfileIndex: 0, extreme: 0, silent: 1);

        var merged = GcuCpuPolicyStatusAccumulator.Merge(quiet, Status(extreme: 1));

        Assert.Equal(1, merged.Extreme);
        Assert.Equal(0, merged.Silent);
    }

    [Fact]
    public void Merge_PartialFanStatus_PreservesBatteryChargingLimit()
    {
        var full = Status() with { BatteryChargingLimitPercent = 80 };

        var merged = GcuCpuPolicyStatusAccumulator.Merge(full, Status());

        Assert.Equal(80, merged.BatteryChargingLimitPercent);
    }

    private static GcuCpuPolicyStatus Status(
        int? operatingMode = null,
        int? turboProfileIndex = null,
        int? extreme = null,
        int? silent = null) => new(
            Pl1Watts: 150,
            Pl2Watts: 150,
            Pl1MinimumWatts: 5,
            Pl1MaximumWatts: 250,
            Pl2MinimumWatts: 5,
            Pl2MaximumWatts: 250,
            OperatingMode: operatingMode,
            TurboProfileIndex: turboProfileIndex,
            Extreme: extreme,
            Silent: silent);
}
