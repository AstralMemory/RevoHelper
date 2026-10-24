using RevoHelper.Core.Control;
using RevoHelper.Hardware.Windows.OemControl;

namespace RevoHelper.Hardware.Windows.Tests.OemControl;

public sealed class GcuRuntimeCapabilityMapperTests
{
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
