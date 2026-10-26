using RevoHelper.Core.Control;
using RevoHelper.Hardware.Windows.OemControl;

namespace RevoHelper.Hardware.Windows.Tests.OemControl;

public sealed class GcuCpuPolicyStatusAccumulatorTests
{
    [Fact]
    public void Merge_EnteringCustomWithoutItsIndex_DoesNotReuseAnInactiveProfileIndex()
    {
        var previous = Status(operatingMode: 0) with { CustomProfileIndex = 0 };
        var update = GcuCpuPolicyStatusParser.Parse("""{"OperatingMode":3}""")!;

        var merged = GcuCpuPolicyStatusAccumulator.Merge(previous, update);

        Assert.Null(merged.CustomProfileIndex);
        Assert.Null(GcuPlatformModeStatusMapper.Map(merged));
    }

    [Fact]
    public void Merge_ProfileBeforeFirstMode_KeepsSplitMessageEvidence()
    {
        var profile = GcuCpuPolicyStatusParser.Parse("""{"CustomProfileIndex":1}""")!;
        var mode = GcuCpuPolicyStatusParser.Parse("""{"OperatingMode":3}""")!;

        var merged = GcuCpuPolicyStatusAccumulator.Merge(profile, mode);

        Assert.Equal(new PlatformModeSelection(PlatformMode.Custom, 2), GcuPlatformModeStatusMapper.Map(merged));
    }

    [Fact]
    public void Merge_ModeOnlySameProfile_PreservesDisplayValuesButNotFreshPowerEvidence()
    {
        var full = Status(operatingMode: 3) with { CustomProfileIndex = 0 };
        var partial = GcuCpuPolicyStatusParser.Parse("""{"OperatingMode":3,"CustomProfileIndex":0}""")!;

        var merged = GcuCpuPolicyStatusAccumulator.Merge(full, partial);

        Assert.Equal(full.Pl1Watts, merged.Pl1Watts);
        Assert.False(merged.HasCpuPowerLimitSnapshot);
        Assert.False(GcuRuntimeCapabilityMapper.Map(merged).HasCpuPowerLimits);
    }

    [Theory]
    [InlineData(3, 1)]
    [InlineData(1, 0)]
    public void Merge_ModeOrActiveProfileChanges_DoesNotReuseOldPowerValues(int operatingMode, int profileIndex)
    {
        var full = Status(operatingMode: 3) with { CustomProfileIndex = 0 };
        var partial = new GcuCpuPolicyStatus(0, 0, 0, 0, 0, 0,
            OperatingMode: operatingMode, CustomProfileIndex: profileIndex)
        {
            HasCpuPowerLimitSnapshot = false
        };

        var merged = GcuCpuPolicyStatusAccumulator.Merge(full, partial);

        Assert.Equal(0, merged.Pl1Watts);
        Assert.Equal(0, merged.Pl2MaximumWatts);
        Assert.False(merged.HasCpuPowerLimitSnapshot);
    }

    [Fact]
    public void Merge_SingleStrategyWithoutPriorOppositeField_DoesNotInventDualStrategySupport()
    {
        var partial = GcuCpuPolicyStatusParser.Parse("""{"Extreme":1}""")!;

        var merged = GcuCpuPolicyStatusAccumulator.Merge(null, partial);

        Assert.Equal(1, merged.Extreme);
        Assert.Null(merged.Silent);
        Assert.False(merged.SupportsTurboStrategies);
    }

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
