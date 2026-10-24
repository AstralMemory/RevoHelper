using RevoHelper.Core.Control;
using RevoHelper.Hardware.Windows.OemControl;

namespace RevoHelper.Hardware.Windows.Tests.OemControl;

public sealed class GcuPlatformModeCommandTests
{
    [Theory]
    [InlineData(PlatformMode.Office, 0, "{\"Action\":\"OPERATING_OFFICE_MODE\",\"ProfileIndex\":0}")]
    [InlineData(PlatformMode.Balanced, 0, "{\"Action\":\"OPERATING_GAMING_MODE\",\"ProfileIndex\":0}")]
    [InlineData(PlatformMode.TurboQuiet, 0, "{\"Action\":\"OPERATING_TURBO_MODE\",\"ProfileIndex\":0}")]
    [InlineData(PlatformMode.TurboHigh, 0, "{\"Action\":\"OPERATING_TURBO_MODE\",\"ProfileIndex\":0}")]
    [InlineData(PlatformMode.Custom, 2, "{\"Action\":\"OPERATING_CUSTOM_MODE\",\"ProfileIndex\":1}")]
    public void SelectMode_UsesOfficialActionAndProfileIndex(
        PlatformMode mode,
        int profileIndex,
        string expected) =>
        Assert.Equal(expected, GcuPlatformModeCommand.SelectMode(new(mode, profileIndex)));

    [Fact]
    public void SelectTurboStrategy_UsesOfficialSilentAndExtremeActions()
    {
        Assert.Equal(
            "{\"Action\":\"SET_CPU_CORE_OFFSET_SILENT\",\"SILENT\":1}",
            GcuPlatformModeCommand.SelectTurboStrategy(PlatformMode.TurboQuiet));
        Assert.Equal(
            "{\"Action\":\"SET_CPU_CORE_OFFSET_EXTREME\",\"EXTREME\":1}",
            GcuPlatformModeCommand.SelectTurboStrategy(PlatformMode.TurboHigh));
    }

    [Theory]
    [InlineData(PlatformMode.Office, "{\"IsNormalRun\":0}")]
    [InlineData(PlatformMode.Balanced, "{\"IsNormalRun\":1}")]
    [InlineData(PlatformMode.TurboQuiet, "{\"IsNormalRun\":2}")]
    [InlineData(PlatformMode.TurboHigh, "{\"IsNormalRun\":2}")]
    [InlineData(PlatformMode.Custom, "{\"IsCustomRun\":true}")]
    public void CompanionMode_UsesOfficialLiquidCoolingPayload(PlatformMode mode, string expected) =>
        Assert.Equal(expected, GcuPlatformModeCommand.CompanionMode(mode));

    [Fact]
    public void MapStatus_ConvertsFirmwareCustomIndexToUserFacingProfileNumber()
    {
        var status = new GcuCpuPolicyStatus(
            35, 36, 10, 210, 10, 210,
            OperatingMode: 3,
            OfficeProfileIndex: 0,
            GamingProfileIndex: 0,
            TurboProfileIndex: 0,
            CustomProfileIndex: 2,
            Silent: 0,
            Extreme: 0);

        Assert.Equal(
            new PlatformModeSelection(PlatformMode.Custom, 3),
            GcuPlatformModeStatusMapper.Map(status));
    }
}
