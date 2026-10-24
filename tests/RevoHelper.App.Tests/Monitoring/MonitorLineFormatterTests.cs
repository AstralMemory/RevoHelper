using RevoHelper.App.Monitoring;
using RevoHelper.Core.Dashboard;
using RevoHelper.Core.Telemetry;

namespace RevoHelper.App.Tests.Monitoring;

public sealed class MonitorLineFormatterTests
{
    [Fact]
    public void SharedOemTemperature_IsOnlyFallbackForMissingCpuSensor()
    {
        var sample = new HardwareTelemetrySnapshot(DateTimeOffset.UtcNow,
            [new(DeviceKind.Cpu, "CPU", 12, null, 20, 4000)]);
        var missing = string.Concat(MonitorLineFormatter.Format(sample, null, 61).Select(run => run.Text));
        Assert.Contains("61 °C", missing);
        var direct = sample with { Devices = [sample.Devices[0] with { TemperatureC = 55 }] };
        var preferred = string.Concat(MonitorLineFormatter.Format(direct, null, 61).Select(run => run.Text));
        Assert.Contains("55 °C", preferred);
        Assert.DoesNotContain("61 °C", preferred);
    }

    [Fact]
    public void HybridLayout_PreservesScreenshotOrderAndColors_WithoutIntegratedGpuSecondRow()
    {
        var snapshot = new HardwareTelemetrySnapshot(DateTimeOffset.UtcNow,
        [
            new(DeviceKind.IntegratedGpu, "Integrated", 99, 80, 20, 900),
            new(DeviceKind.Cpu, "Hybrid CPU", 34, 63, 23, null, 3292, 1995),
            new(DeviceKind.DiscreteGpu, "Discrete", 24, 57, 73, 1957)
        ]);

        var runs = MonitorLineFormatter.Format(snapshot, 72);

        Assert.Equal(new[]
        {
            "FPS 72", "    CPU:  ", "63 °C   34 %   23 W",
            "  P-Core  ", "3292 MHz", "  E-Core  ", "1995 MHz",
            "    GPU:  ", "57 °C   24 %   73 W", "   1957 MHz"
        }, runs.Select(run => run.Text));
        Assert.Equal(new[]
        {
            "#38FF98", "#FFFFFF", "#57ACFF", "#FFFFFF", "#00CED1", "#FFFFFF", "#00CED1",
            "#FFFFFF", "#FFB13C", "#FF7800"
        }, runs.Select(run => run.Color));
        Assert.DoesNotContain("900 MHz", string.Concat(runs.Select(run => run.Text)));
        Assert.Single(runs, run => run.Text.Contains("GPU:", StringComparison.Ordinal));
    }

    [Fact]
    public void NonHybridCpu_ShowsSingleFrequencyWithoutInventingPerformanceEfficiencyGroups()
    {
        var snapshot = new HardwareTelemetrySnapshot(DateTimeOffset.UtcNow,
            [new(DeviceKind.Cpu, "Ryzen 7", 30, 50, 25, 5100)]);

        var text = string.Concat(MonitorLineFormatter.Format(snapshot, null).Select(run => run.Text));

        Assert.StartsWith("FPS —", text);
        Assert.Contains("5100 MHz", text);
        Assert.DoesNotContain("P-Core", text);
        Assert.DoesNotContain("E-Core", text);
    }

    [Fact]
    public void UnavailableTelemetry_UsesDashes_NotOldOrFabricatedNumbers()
    {
        var text = string.Concat(MonitorLineFormatter.Format(null, null).Select(run => run.Text));

        Assert.Equal("FPS —    CPU:  —   —   —   —    GPU:  —   —   —   —", text);
        Assert.DoesNotContain("0 MHz", text);
    }

    [Fact]
    public void InvalidAndNonFiniteNumbers_AreNotShownAsTelemetry()
    {
        var snapshot = new HardwareTelemetrySnapshot(DateTimeOffset.UtcNow,
        [
            new(DeviceKind.Cpu, "CPU", -1, double.NaN, double.PositiveInfinity, double.NegativeInfinity),
            new(DeviceKind.DiscreteGpu, "GPU", double.PositiveInfinity, -1, double.NaN, -10)
        ]);

        var text = string.Concat(MonitorLineFormatter.Format(snapshot, double.NaN).Select(run => run.Text));

        Assert.DoesNotContain("NaN", text);
        Assert.DoesNotContain("Infinity", text);
        Assert.DoesNotContain("-1", text);
        Assert.StartsWith("FPS —", text);
    }

    [Fact]
    public void Values_RoundToWholeUnits_Consistently()
    {
        var snapshot = new HardwareTelemetrySnapshot(DateTimeOffset.UtcNow,
            [new(DeviceKind.Cpu, "CPU", 34.5, 62.5, 22.5, 3291.5)]);

        var text = string.Concat(MonitorLineFormatter.Format(snapshot, 71.5).Select(run => run.Text));

        Assert.StartsWith("FPS 72", text);
        Assert.Contains("63 °C   35 %   23 W", text);
        Assert.Contains("3292 MHz", text);
    }
}
