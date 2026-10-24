using RevoHelper.Core.Dashboard;
using RevoHelper.Core.Telemetry;

namespace RevoHelper.Core.Tests.Telemetry;

public sealed class TelemetryFormatterTests
{
    [Fact]
    public void Format_UsesCompactUnits()
    {
        var telemetry = new DeviceTelemetry(
            DeviceKind.Cpu,
            "Core Ultra 9 275HX",
            UsagePercent: 32.4,
            TemperatureC: 62.1,
            PowerWatts: 38.2,
            FrequencyMhz: 3800.4);

        var display = TelemetryFormatter.Format(telemetry);

        Assert.Equal("32%", display.Usage);
        Assert.Equal("62°C", display.Temperature);
        Assert.Equal("38 W", display.Power);
        Assert.Equal("3800 MHz", display.Frequency);
    }

    [Fact]
    public void Format_UsesNAForMissingOrNonFiniteValues()
    {
        var telemetry = new DeviceTelemetry(
            DeviceKind.DiscreteGpu,
            "GeForce RTX 5090 Laptop GPU",
            UsagePercent: null,
            TemperatureC: double.NaN,
            PowerWatts: double.PositiveInfinity,
            FrequencyMhz: null);

        var display = TelemetryFormatter.Format(telemetry);

        Assert.Equal("N/A", display.Usage);
        Assert.Equal("N/A", display.Temperature);
        Assert.Equal("N/A", display.Power);
        Assert.Equal("N/A", display.Frequency);
    }

    [Fact]
    public void Format_CpuHybridClocks_AreShownSeparatelyOnTwoCompactLines()
    {
        var telemetry = new DeviceTelemetry(
            DeviceKind.Cpu,
            "Core Ultra 9 275HX",
            UsagePercent: 19,
            TemperatureC: 61,
            PowerWatts: 40,
            FrequencyMhz: null,
            PerformanceCoreFrequencyMhz: 5187,
            EfficiencyCoreFrequencyMhz: 1995);

        var display = TelemetryFormatter.Format(telemetry);

        Assert.Equal("P 5187 MHz\nE 1995 MHz", display.Frequency);
    }
}
