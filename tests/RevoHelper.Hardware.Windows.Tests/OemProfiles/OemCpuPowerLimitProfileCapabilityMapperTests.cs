using RevoHelper.Core.Control;
using RevoHelper.Core.Hardware;
using RevoHelper.Hardware.Windows.OemProfiles;

namespace RevoHelper.Hardware.Windows.Tests.OemProfiles;

public sealed class OemCpuPowerLimitProfileCapabilityMapperTests
{
    [Fact]
    public void Map_TurboProfile_ExposesOnlyBasicTurboWithoutClaimingStrategyOrPowerWrites()
    {
        var evidence = OemCpuPowerLimitProfileCapabilityMapper.Map([
            new CpuPowerLimitProfile("turbo:1", "Mode3_Profile1", "Turbo", 210, true, 210, true, true)
        ]);

        Assert.Contains(PlatformMode.TurboQuiet, evidence.SupportedPlatformModes);
        Assert.DoesNotContain(PlatformMode.TurboHigh, evidence.SupportedPlatformModes);
        Assert.False(evidence.HasCpuPowerLimits);
    }
}
