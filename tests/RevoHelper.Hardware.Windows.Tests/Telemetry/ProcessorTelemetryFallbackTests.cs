using RevoHelper.Core.Dashboard;
using RevoHelper.Core.Telemetry;
using RevoHelper.Hardware.Windows.Telemetry;

namespace RevoHelper.Hardware.Windows.Tests.Telemetry;

public sealed class ProcessorTelemetryFallbackTests
{
    private static readonly WindowsCounterReading Windows = new(18, 2250, 35.1, 0.4, 66.1);

    [Theory]
    [InlineData(0)]
    [InlineData(37)]
    public void ACompleteSensorReadingDoesNotTouchWindowsCounters(double usage)
    {
        IReadOnlyList<DeviceTelemetry> devices = [Cpu(usage, 60, 24, 4277)];
        Assert.False(ProcessorTelemetryFallback.NeedsWindowsCounters(devices));
    }

    [Fact]
    public void MissingUsageAndClockAreBothFilledFromWindows()
    {
        var cpu = Fill([Cpu(null, 60, 24, null)]).Single();

        Assert.Equal(18, cpu.UsagePercent);
        Assert.Equal(2250, cpu.FrequencyMhz);
        Assert.Equal(60, cpu.TemperatureC);
        Assert.Equal(24, cpu.PowerWatts);
    }

    // Without the PawnIO driver the sensors report usage but no clock (seen on an AMD 7840H).
    [Fact]
    public void AMissingClockIsFilledEvenWhenUsageIsPresent()
    {
        var cpu = Fill([Cpu(13, null, null, null)]).Single();

        Assert.Equal(13, cpu.UsagePercent);
        Assert.Equal(2250, cpu.FrequencyMhz);
        Assert.Equal(35.1, cpu.PowerWatts);
        Assert.Equal(66.1, cpu.ZoneTemperatureC);
        Assert.Null(cpu.TemperatureC);
    }

    [Fact]
    public void PerformanceAndEfficiencyClocksCountAsAClock()
    {
        var hybrid = Cpu(12, 60, 24, null) with { PerformanceCoreFrequencyMhz = 5100, EfficiencyCoreFrequencyMhz = 4500 };
        Assert.False(ProcessorTelemetryFallback.NeedsWindowsCounters([hybrid]));
        var cpu = Fill([hybrid with { PowerWatts = null }]).Single();
        Assert.Null(cpu.FrequencyMhz);
        Assert.Equal(5100, cpu.PerformanceCoreFrequencyMhz);
    }

    [Fact]
    public void TheSensorLibraryAlwaysWinsOverWindows()
    {
        var cpu = Fill([Cpu(12, 61, 24, 4277)]).Single();

        Assert.Equal(12, cpu.UsagePercent);
        Assert.Equal(4277, cpu.FrequencyMhz);
        Assert.Equal(24, cpu.PowerWatts);
        Assert.Null(cpu.ZoneTemperatureC);
    }

    [Fact]
    public void WithNoCpuFromTheLibraryTheRowStillGetsWindowsReadings()
    {
        IReadOnlyList<DeviceTelemetry> none = [];
        Assert.True(ProcessorTelemetryFallback.NeedsWindowsCounters(none));

        var cpu = ProcessorTelemetryFallback.Fill(none, Windows, "Intel(R) Core(TM) Ultra 9 275HX").Single();

        Assert.Equal(DeviceKind.Cpu, cpu.Kind);
        Assert.Equal("Intel(R) Core(TM) Ultra 9 275HX", cpu.Model);
        Assert.Equal(18, cpu.UsagePercent);
        Assert.Equal(2250, cpu.FrequencyMhz);
        Assert.Equal(35.1, cpu.PowerWatts);
        Assert.Equal(66.1, cpu.ZoneTemperatureC);
    }

    [Fact]
    public void WmiIsAskedOnlyForWhatPdhCouldNotGive()
    {
        var calls = 0;
        ProcessorPerformance Wmi() { calls++; return new(22, 3100); }

        var complete = ProcessorTelemetryFallback.Fill([Cpu(null, 60, 24, null)], Windows, "CPU", Wmi).Single();
        Assert.Equal(0, calls);
        Assert.Equal(2250, complete.FrequencyMhz);

        var noPdh = ProcessorTelemetryFallback.Fill([Cpu(null, 60, 24, null)], WindowsCounterReading.Empty, "CPU", Wmi).Single();
        Assert.Equal(1, calls);
        Assert.Equal(22, noPdh.UsagePercent);
        Assert.Equal(3100, noPdh.FrequencyMhz);
    }

    [Fact]
    public void ABrokenWmiProviderLeavesTheGapAndKeepsTheOtherReadings()
    {
        var cpu = ProcessorTelemetryFallback.Fill([Cpu(null, 60, 24, 4277)], WindowsCounterReading.Empty, "CPU",
            () => throw new InvalidOperationException("WMI down")).Single();

        Assert.Null(cpu.UsagePercent);
        Assert.Equal(4277, cpu.FrequencyMhz);
        Assert.Equal(60, cpu.TemperatureC);
    }

    [Fact]
    public void IntelIntegratedGraphicsPowerComesFromTheProcessorsEnergyMeter()
    {
        var intel = new DeviceTelemetry(DeviceKind.IntegratedGpu, "Intel(R) Graphics", 3, null, null, null, Vendor: HardwareVendor.Intel);
        var amd = new DeviceTelemetry(DeviceKind.IntegratedGpu, "AMD Radeon(TM) 780M", 3, null, null, null, Vendor: HardwareVendor.Amd);
        var sleeping = new DeviceTelemetry(DeviceKind.DiscreteGpu, "RTX 5090", 0, null, 0, null, Vendor: HardwareVendor.Nvidia, Asleep: true);
        IReadOnlyList<DeviceTelemetry> devices = [Cpu(10, 60, 24, 4000), intel, amd, sleeping];
        Assert.True(ProcessorTelemetryFallback.NeedsWindowsCounters(devices));

        var filled = ProcessorTelemetryFallback.Fill(devices, Windows, "CPU");

        Assert.Equal(0.4, filled[1].PowerWatts);
        Assert.Null(filled[2].PowerWatts);
        Assert.Equal(0, filled[3].PowerWatts);
    }

    private static IReadOnlyList<DeviceTelemetry> Fill(IReadOnlyList<DeviceTelemetry> devices) =>
        ProcessorTelemetryFallback.Fill(devices, Windows, "CPU", () => throw new InvalidOperationException("WMI must not be asked."));

    private static DeviceTelemetry Cpu(double? usage, double? temperature, double? power, double? frequency) =>
        new(DeviceKind.Cpu, "CPU", usage, temperature, power, frequency);
}
