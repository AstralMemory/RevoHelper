using RevoHelper.Hardware.Windows.Discovery;
using RevoHelper.Hardware.Windows.Telemetry;

namespace RevoHelper.Hardware.Windows.Tests.Telemetry;

public sealed class WindowsProcessorPerformanceReaderTests
{
    [Fact]
    public void Read_SelectsTotalRowAndReportsTheLiveAverageClock()
    {
        var query = new StubQuery(
        [
            Row(("Name", "0,0"), ("PercentProcessorUtility", "50"), ("ProcessorFrequency", "4200"), ("PercentProcessorPerformance", "120")),
            Row(("Name", "_Total"), ("PercentProcessorUtility", "18"), ("ProcessorFrequency", "2150"), ("PercentProcessorPerformance", "180"))
        ]);

        var reading = new WindowsProcessorPerformanceReader(query).Read();

        Assert.Equal(18, reading.UsagePercent);
        Assert.Equal(3870, reading.FrequencyMhz);       // 2150 base x 180 %
    }

    [Fact]
    public void Read_NeverReportsTheBaseClockAloneAsALiveClock()
    {
        var query = new StubQuery([Row(("Name", "_Total"), ("PercentProcessorUtility", "18"), ("ProcessorFrequency", "2150"))]);
        Assert.Null(new WindowsProcessorPerformanceReader(query).Read().FrequencyMhz);
    }

    [Fact]
    public void Read_ReturnsMissingValuesWhenTotalRowIsUnavailable()
    {
        var reading = new WindowsProcessorPerformanceReader(new StubQuery([])).Read();

        Assert.Null(reading.UsagePercent);
        Assert.Null(reading.FrequencyMhz);
    }

    private static IReadOnlyDictionary<string, string> Row(params (string Key, string Value)[] values) =>
        values.ToDictionary(value => value.Key, value => value.Value, StringComparer.OrdinalIgnoreCase);

    private sealed class StubQuery(IReadOnlyList<IReadOnlyDictionary<string, string>> rows) : IWmiQuery
    {
        public IReadOnlyDictionary<string, string> ReadFirst(string className) => rows.First();

        public IReadOnlyList<IReadOnlyDictionary<string, string>> ReadAll(string className) => rows;
    }
}
