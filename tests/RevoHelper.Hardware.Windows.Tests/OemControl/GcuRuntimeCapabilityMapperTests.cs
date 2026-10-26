using RevoHelper.Core.Control;
using RevoHelper.Hardware.Windows.OemControl;

namespace RevoHelper.Hardware.Windows.Tests.OemControl;

public sealed class GcuRuntimeCapabilityMapperTests
{
    [Theory]
    [InlineData(0, PlatformMode.Office)]
    [InlineData(1, PlatformMode.Balanced)]
    [InlineData(2, PlatformMode.TurboQuiet)]
    [InlineData(3, PlatformMode.Custom)]
    public void Map_ModeOnlyStatus_ConfirmsOnlyTheObservedMode(int mode, PlatformMode expected)
    {
        var status = GcuCpuPolicyStatusParser.Parse($"{{\"OperatingMode\":{mode}}}")!;

        var evidence = GcuRuntimeCapabilityMapper.Map(status);

        Assert.Equal(expected, Assert.Single(evidence.SupportedPlatformModes));
        Assert.False(evidence.HasCpuPowerLimits);
    }

    [Fact]
    public void Map_UnknownModeOnly_DoesNotInventSupportedTargets()
    {
        var status = GcuCpuPolicyStatusParser.Parse("""{"OperatingMode":99}""")!;

        Assert.Empty(GcuRuntimeCapabilityMapper.Map(status).SupportedPlatformModes);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData(0, null)]
    [InlineData(null, 0)]
    [InlineData(255, 0)]
    [InlineData(0, -1)]
    public void Map_TurboWithoutBothStrategyFields_ExposesOnlyBasicTurbo(int? extreme, int? silent)
    {
        var status = new GcuCpuPolicyStatus(45, 65, 5, 210, 5, 210,
            OperatingMode: 0, OfficeProfileIndex: 0, TurboProfileIndex: 0,
            Extreme: extreme, Silent: silent);

        var modes = GcuRuntimeCapabilityMapper.Map(status).SupportedPlatformModes;

        Assert.Contains(PlatformMode.TurboQuiet, modes);
        Assert.DoesNotContain(PlatformMode.TurboHigh, modes);
    }

    [Fact]
    public void Map_CompleteTurboStrategyEvidence_ExposesBothStrategies()
    {
        var status = new GcuCpuPolicyStatus(45, 65, 5, 210, 5, 210,
            OperatingMode: 2, TurboProfileIndex: 0, Extreme: 0, Silent: 1);

        Assert.Contains(PlatformMode.TurboHigh, GcuRuntimeCapabilityMapper.Map(status).SupportedPlatformModes);
    }

    [Fact]
    public void Map_UsesOnlyModeFieldsActuallyReturnedByGcu()
    {
        var status = new GcuCpuPolicyStatus(
            45, 65, 5, 210, 5, 210,
            OperatingMode: 1,
            GamingProfileIndex: 0,
            OfficeProfileIndex: 0,
            TurboProfileIndex: null,
            CustomProfileIndex: 1);

        var evidence = GcuRuntimeCapabilityMapper.Map(status);

        Assert.Contains(PlatformMode.Office, evidence.SupportedPlatformModes);
        Assert.Contains(PlatformMode.Balanced, evidence.SupportedPlatformModes);
        Assert.Contains(PlatformMode.Custom, evidence.SupportedPlatformModes);
        Assert.DoesNotContain(PlatformMode.TurboQuiet, evidence.SupportedPlatformModes);
        Assert.True(evidence.HasCpuPowerLimits);
    }
}
