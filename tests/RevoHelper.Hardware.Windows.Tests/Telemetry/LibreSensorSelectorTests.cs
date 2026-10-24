using RevoHelper.Hardware.Windows.Telemetry;
using LibreHardwareMonitor.Hardware;

namespace RevoHelper.Hardware.Windows.Tests.Telemetry;

public sealed class LibreSensorSelectorTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Selection_DoesNotAllocateCopiesOfTheSensorList(bool cpu)
    {
        var sensors = Enumerable.Range(0, 64)
            .Select(index => new SensorDescriptor($"Sensor {index}", SensorValueKind.Clock, 1000))
            .ToArray();
        Func<SelectedTelemetry> select = cpu
            ? () => LibreSensorSelector.SelectCpu(sensors)
            : () => LibreSensorSelector.SelectGpu(sensors);
        for (var index = 0; index < 100; index++) select();

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var index = 0; index < 1000; index++) select();
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.True(allocated < 512_000, $"Selection allocated {allocated} bytes for 1000 samples.");
    }

    [Fact]
    public void SelectCpu_PreservesPriorityAndFirstValidDuplicateWithoutMutatingInput()
    {
        SensorDescriptor[] sensors =
        [
            new("Core Average", SensorValueKind.Temperature, 40),
            new("CPU Package", SensorValueKind.Temperature, double.NaN),
            new("cpu package", SensorValueKind.Temperature, 60),
            new("CPU Package", SensorValueKind.Temperature, 70),
            new("CPU Core #1", SensorValueKind.Clock, 3000),
            new("CPU Core #0", SensorValueKind.Clock, -1),
            new("CPU Core #0", SensorValueKind.Clock, 4000),
            new("P-Core #1", SensorValueKind.Clock, double.PositiveInfinity),
            new("CPU Total", SensorValueKind.Load, null),
            new("CPU Total", SensorValueKind.Load, 0)
        ];
        var original = sensors.ToArray();

        var reading = LibreSensorSelector.SelectCpu(sensors);

        Assert.Equal(60, reading.TemperatureC);
        Assert.Equal(4000, reading.FrequencyMhz);
        Assert.Equal(0, reading.UsagePercent);
        Assert.Null(reading.PerformanceCoreFrequencyMhz);
        Assert.Equal(original, sensors);
    }

    [Fact]
    public void SelectGpu_PreservesPriorityIncludingZeroPowerBeforeLowerPriorityFallback()
    {
        var reading = LibreSensorSelector.SelectGpu([
            new("D3D 3D", SensorValueKind.Load, 90),
            new("GPU Core", SensorValueKind.Load, double.NaN),
            new("gpu core", SensorValueKind.Load, 0),
            new("GPU Board Power", SensorValueKind.Power, 100),
            new("GPU Package", SensorValueKind.Power, 50),
            new("GPU Power", SensorValueKind.Power, 0)]);

        Assert.Equal(0, reading.UsagePercent);
        Assert.Null(reading.PowerWatts);
    }

    [Fact]
    public void Selection_EnumeratesLiveSourceOnlyOncePerSampleAndReadsFreshValues()
    {
        var enumerations = 0;
        var load = 12d;
        IEnumerable<SensorDescriptor> ReadSensors()
        {
            enumerations++;
            yield return new("CPU Total", SensorValueKind.Load, load);
            yield return new("GPU Core", SensorValueKind.Load, load);
        }

        Assert.Equal(12, LibreSensorSelector.SelectCpu(ReadSensors()).UsagePercent);
        Assert.Equal(1, enumerations);
        load = 34;
        Assert.Equal(34, LibreSensorSelector.SelectGpu(ReadSensors()).UsagePercent);
        Assert.Equal(2, enumerations);
    }

    [Theory]
    [InlineData(SensorType.Load, SensorValueKind.Load)]
    [InlineData(SensorType.Temperature, SensorValueKind.Temperature)]
    [InlineData(SensorType.Power, SensorValueKind.Power)]
    [InlineData(SensorType.Clock, SensorValueKind.Clock)]
    [InlineData(SensorType.SmallData, SensorValueKind.Memory)]
    public void DescriptorFactory_MapsSupportedLibreSensorTypes(
        SensorType sensorType,
        SensorValueKind expected)
    {
        var descriptor = LibreSensorDescriptorFactory.Create("Sensor", sensorType, 12.5f);

        Assert.NotNull(descriptor);
        Assert.Equal(expected, descriptor.Kind);
        Assert.Equal(12.5, descriptor.Value);
    }

    [Fact]
    public void DescriptorFactory_IgnoresUnsupportedSensorTypes()
    {
        Assert.Null(LibreSensorDescriptorFactory.Create("Voltage", SensorType.Voltage, 1.2f));
    }

    [Fact]
    public void SelectCpu_UsesPackageMetricsAndHighestValidPerformanceAndEfficiencyClocks()
    {
        SensorDescriptor[] sensors =
        [
            new("CPU Core #1", SensorValueKind.Load, 10),
            new("CPU Total", SensorValueKind.Load, 32),
            new("CPU Core #1", SensorValueKind.Temperature, 68),
            new("CPU Package", SensorValueKind.Temperature, 62),
            new("CPU Package", SensorValueKind.Power, 38),
            new("P-Core #1", SensorValueKind.Clock, 4000),
            new("P-Core #2", SensorValueKind.Clock, 5153),
            new("P-Core #3", SensorValueKind.Clock, null),
            new("E-Core #1", SensorValueKind.Clock, 1982),
            new("E-Core #2", SensorValueKind.Clock, 1800)
        ];

        var reading = LibreSensorSelector.SelectCpu(sensors);

        Assert.Equal(32, reading.UsagePercent);
        Assert.Equal(62, reading.TemperatureC);
        Assert.Equal(38, reading.PowerWatts);
        Assert.Null(reading.FrequencyMhz);
        Assert.Equal(5153, reading.PerformanceCoreFrequencyMhz);
        Assert.Equal(1982, reading.EfficiencyCoreFrequencyMhz);
    }

    [Fact]
    public void SelectCpu_WhenHybridTopologyIsUnavailable_FallsBackToCoreZero()
    {
        SensorDescriptor[] sensors =
        [
            new("CPU Core #0", SensorValueKind.Clock, 4277),
            new("CPU Core #1", SensorValueKind.Clock, 3192)
        ];

        var reading = LibreSensorSelector.SelectCpu(sensors);

        Assert.Equal(4277, reading.FrequencyMhz);
        Assert.Null(reading.PerformanceCoreFrequencyMhz);
        Assert.Null(reading.EfficiencyCoreFrequencyMhz);
    }

    [Fact]
    public void SelectCpu_NonHybridTopology_UsesHighestValidNumberedCoreClock()
    {
        var reading = LibreSensorSelector.SelectCpu([
            new("CPU Core #0", SensorValueKind.Clock, 800),
            new("CPU Core #1", SensorValueKind.Clock, 2200),
            new("CPU Core #7", SensorValueKind.Clock, 5150),
            new("cpu core #11", SensorValueKind.Clock, 5300),
            new("CPU Core #3", SensorValueKind.Clock, double.NaN),
            new("CPU Core #4", SensorValueKind.Clock, double.PositiveInfinity),
            new("CPU Core #5", SensorValueKind.Clock, -1),
            new("Bus Speed", SensorValueKind.Clock, 9000),
            new("CPU Core #12 Effective", SensorValueKind.Clock, 9500),
            new("CPU Core #13", SensorValueKind.Temperature, 10000)]);

        Assert.Equal(5300, reading.FrequencyMhz);
        Assert.Null(reading.PerformanceCoreFrequencyMhz);
        Assert.Null(reading.EfficiencyCoreFrequencyMhz);
    }

    [Fact]
    public void SelectCpu_NonHybridWithoutCoreZeroOrOne_StillFindsAvailableCore()
    {
        var reading = LibreSensorSelector.SelectCpu([
            new("CPU Core #5", SensorValueKind.Clock, 4900),
            new("CPU Core #6", SensorValueKind.Clock, 4800),
            new("CPU Core #", SensorValueKind.Clock, 9000)]);

        Assert.Equal(4900, reading.FrequencyMhz);
    }

    [Fact]
    public void SelectGpu_UsesCoreMetricsAndReturnsNullForUnavailablePower()
    {
        SensorDescriptor[] sensors =
        [
            new("GPU Core", SensorValueKind.Load, 12),
            new("GPU Core", SensorValueKind.Temperature, 45),
            new("GPU Memory", SensorValueKind.Clock, 1750),
            new("GPU Core", SensorValueKind.Clock, 697)
        ];

        var reading = LibreSensorSelector.SelectGpu(sensors);

        Assert.Equal(12, reading.UsagePercent);
        Assert.Equal(45, reading.TemperatureC);
        Assert.Null(reading.PowerWatts);
        Assert.Equal(697, reading.FrequencyMhz);
    }

    [Fact]
    public void SelectGpu_UsesDedicatedMemoryTotalAndIgnoresSharedMemoryCapacity()
    {
        SensorDescriptor[] sensors =
        [
            new("GPU Memory Total", SensorValueKind.Memory, 24463),
            new("D3D Shared Memory Total", SensorValueKind.Memory, 37044)
        ];

        var reading = LibreSensorSelector.SelectGpu(sensors);

        Assert.Equal(24463, reading.DedicatedMemoryMegabytes);
    }

    [Fact]
    public void SelectGpu_FallsBackToD3D3DLoadAndRejectsZeroPower()
    {
        SensorDescriptor[] sensors =
        [
            new("D3D 3D", SensorValueKind.Load, 29.8),
            new("GPU Power", SensorValueKind.Power, 0),
            new("GPU Core", SensorValueKind.Clock, 650)
        ];

        var reading = LibreSensorSelector.SelectGpu(sensors);

        Assert.Equal(29.8, reading.UsagePercent);
        Assert.Null(reading.PowerWatts);
        Assert.Equal(650, reading.FrequencyMhz);
    }

    [Fact]
    public void SelectCpu_RejectsZeroPackagePower()
    {
        var reading = LibreSensorSelector.SelectCpu(
            [new("CPU Package", SensorValueKind.Power, 0)]);

        Assert.Null(reading.PowerWatts);
    }

    [Fact]
    public void SelectCpu_DoesNotGuessFromUnrelatedSensors()
    {
        SensorDescriptor[] sensors =
        [
            new("Memory", SensorValueKind.Load, 50),
            new("VRM", SensorValueKind.Temperature, 70),
            new("Bus Speed", SensorValueKind.Clock, 100)
        ];

        var reading = LibreSensorSelector.SelectCpu(sensors);

        Assert.Null(reading.UsagePercent);
        Assert.Null(reading.TemperatureC);
        Assert.Null(reading.PowerWatts);
        Assert.Null(reading.FrequencyMhz);
    }
}
