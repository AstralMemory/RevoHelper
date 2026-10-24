using RevoHelper.Hardware.Windows.Telemetry;

namespace RevoHelper.Hardware.Windows.Tests.Telemetry;

public sealed class ProcessorTelemetryFallbackTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(37)]
    public void CompleteSensorReadingDoesNotCallWmi(double usage)
    {
        var reader = new RecordingReader();
        var selected = new SelectedTelemetry(usage, 60, 24, 4277);
        var result = ProcessorTelemetryFallback.ReadIfNeeded(selected, reader);
        Assert.Same(selected, result);
        Assert.Equal(0, reader.Calls);
    }

    [Fact]
    public void MissingUsageStillReadsFallbackWithoutInventingActualClock()
    {
        var reader = new RecordingReader();
        var result = ProcessorTelemetryFallback.ReadIfNeeded(new(null, 60, 24, null), reader);
        Assert.Equal(1, reader.Calls);
        Assert.Equal(18, result.UsagePercent);
        Assert.Null(result.FrequencyMhz);
        Assert.Equal(60, result.TemperatureC);
    }

    [Fact]
    public void UnavailableFallbackPreservesOtherSensors()
    {
        var selected = new SelectedTelemetry(null, 60, 24, 4277);
        var result = ProcessorTelemetryFallback.ReadIfNeeded(selected, new RecordingReader { Fail = true });
        Assert.Same(selected, result);
        Assert.Null(result.UsagePercent);
    }

    private sealed class RecordingReader : IProcessorPerformanceReader
    {
        public int Calls;
        public bool Fail;
        public ProcessorPerformance Read()
        {
            Calls++;
            if (Fail) throw new InvalidOperationException("Unavailable");
            return new(18, 2250);
        }
    }

    [Fact]
    public void Merge_FillsUsageButRejectsAggregateWindowsFrequencyAsActualClock()
    {
        var selected = new SelectedTelemetry(null, null, null, null);

        var merged = ProcessorTelemetryFallback.Merge(
            selected,
            new ProcessorPerformance(18, 2250));

        Assert.Equal(18, merged.UsagePercent);
        Assert.Null(merged.FrequencyMhz);
    }

    [Fact]
    public void Merge_PreservesRealPerCoreClockWhenSensorProvidesOne()
    {
        var selected = new SelectedTelemetry(12, 60, 24, 4277);

        var merged = ProcessorTelemetryFallback.Merge(
            selected,
            new ProcessorPerformance(18, 2250));

        Assert.Equal(12, merged.UsagePercent);
        Assert.Equal(4277, merged.FrequencyMhz);
    }
}
