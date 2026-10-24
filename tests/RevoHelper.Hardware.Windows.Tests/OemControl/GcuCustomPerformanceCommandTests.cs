using RevoHelper.Hardware.Windows.OemControl;

namespace RevoHelper.Hardware.Windows.Tests.OemControl;

public sealed class GcuCustomPerformanceCommandTests
{
    [Theory]
    [InlineData(true, "{\"Action\":\"SET_OPERATING_MODE_DETAIL\",\"CpuTccOffsetSwitch\":\"1\"}")]
    [InlineData(false, "{\"Action\":\"SET_OPERATING_MODE_DETAIL\",\"CpuTccOffsetSwitch\":\"0\"}")]
    public void CpuTemperatureWallSwitch_UsesOfficialPayload(bool enabled, string expected) =>
        Assert.Equal(expected, GcuCustomPerformanceCommand.CpuTemperatureWallSwitch(enabled));

    [Fact]
    public void CpuTemperatureWall_UsesOfficialTemperatureField() =>
        Assert.Equal("{\"Action\":\"SET_OPERATING_MODE_DETAIL\",\"CpuTccOffset\":95}",
            GcuCustomPerformanceCommand.CpuTemperatureWall(95));

    [Theory]
    [InlineData(true, "CPUPerformanceAndOverClockMenuSwitch_ON")]
    [InlineData(false, "CPUPerformanceAndOverClockMenuSwitch_OFF")]
    public void CpuAdvancedMenu_UsesOfficialDirectionalField(bool enabled, string field) =>
        Assert.Contains($"\"{field}\":\"{(enabled ? 1 : 0)}\"", GcuCustomPerformanceCommand.CpuAdvancedMenu(enabled));

    [Fact]
    public void GpuCommands_UseOfficialDetailFields()
    {
        Assert.Contains("\"OverClockingSwitch\":\"1\"", GcuCustomPerformanceCommand.OverclockingSwitch(true));
        Assert.Contains("\"GpuConfigurableTGPSwitch\":\"1\"", GcuCustomPerformanceCommand.GpuConfigurableTgpSwitch(true));
        Assert.Contains("\"GpuConfigurableTGPTarget\":\"150\"", GcuCustomPerformanceCommand.GpuConfigurableTgp(150));
        Assert.Contains("\"GpuDynamicBoostSwitch\":\"1\"", GcuCustomPerformanceCommand.GpuDynamicBoostSwitch(true));
        Assert.Contains("\"GpuDynamicBoost\":\"25\"", GcuCustomPerformanceCommand.GpuDynamicBoost(25));
        Assert.Contains("\"GpuCoreClockOffsetOC\":\"100\"", GcuCustomPerformanceCommand.GpuCoreOffset(100));
        Assert.Contains("\"GpuMemoryClockOffsetOC\":\"500\"", GcuCustomPerformanceCommand.GpuMemoryOffset(500));
    }
}
