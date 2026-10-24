using RevoHelper.Hardware.Windows.OemControl;

namespace RevoHelper.Hardware.Windows.Tests.OemControl;

public sealed class GcuCustomPerformanceStatusParserTests
{
    [Fact]
    public void Parse_MapsLiveFanStatusInsteadOfFallingBackToOldProfile()
    {
        const string status = """
            {"OperatingMode":"3","OverClockingSwitch":"1","TjMax":"105",
             "CPU_TccOffsetSwitch":"1","CPU_TccOffset":"10","CPU_PL4":"145",
             "CPU_PerformanceAndOverClockMenuSwitch":"1",
             "GPU_ConfigurableTGPSwitch":"1","GPU_ConfigurableTGPTarget":"150",
             "GPU_DynamicBoostSwitch":"1","GPU_DynamicBoost":"25",
             "GPU_CoreClockOffsetOC":"107","GPU_MemoryClockOffsetOC":"960"}
            """;
        var state = GcuCustomPerformanceStatusParser.Parse(status);
        Assert.NotNull(state);
        Assert.Equal(95, state.CpuTemperatureWallC);
        Assert.Equal(145, state.CpuBurstPowerLimitWatts);
        Assert.True(state.CpuAdvancedMenuEnabled);
        Assert.Equal(107, state.GpuCoreClockOffsetMhz);
        Assert.Equal(960, state.GpuMemoryClockOffsetMhz);
        Assert.Equal(150, state.GpuConfigurableTgpWatts);
        Assert.Equal(25, state.GpuDynamicBoostWatts);
        Assert.True(state.OverclockingEnabled);
        Assert.True(state.GpuDynamicBoostEnabled);
        Assert.True(GcuCustomPerformanceStatusParser.ParseGpuConfigurableTgpEnabled(status));
        Assert.Equal(107, GcuCustomPerformanceStatusParser.ParseGpu(status)?.GpuCoreClockOffsetMhz);
    }

    [Fact]
    public void Parse_MapsOfficialCustomProfileShape()
    {
        var state = GcuCustomPerformanceStatusParser.Parse("""
            {
              "OverClockingEnabled":1,
              "CPU":{"TccOffsetSwitch":1,"TccOffset":10,"PL4":145,"PerformanceAndOverClockMenuSwitch":1},
              "GPU":{"ConfigurableTGPSwitch":1,"ConfigurableTGPTarget":150,"DynamicBoostSwitch":1,"DynamicBoost":25,
                     "CoreClockOffsetOC":100,"MemoryClockOffsetOC":500}
            }
            """);

        Assert.NotNull(state);
        Assert.True(state.OverclockingEnabled);
        Assert.True(state.CpuTemperatureWallEnabled);
        Assert.Equal(95, state.CpuTemperatureWallC);
        Assert.Equal(145, state.CpuBurstPowerLimitWatts);
        Assert.True(state.CpuAdvancedMenuEnabled);
        Assert.Equal(150, state.GpuConfigurableTgpWatts);
        Assert.True(state.GpuDynamicBoostEnabled);
        Assert.Equal(25, state.GpuDynamicBoostWatts);
        Assert.Equal(100, state.GpuCoreClockOffsetMhz);
        Assert.Equal(500, state.GpuMemoryClockOffsetMhz);
        Assert.True(GcuCustomPerformanceStatusParser.ParseGpuConfigurableTgpEnabled("""
            {"GPU":{"ConfigurableTGPSwitch":1,"ConfigurableTGPTarget":150}}
            """));
    }
}
