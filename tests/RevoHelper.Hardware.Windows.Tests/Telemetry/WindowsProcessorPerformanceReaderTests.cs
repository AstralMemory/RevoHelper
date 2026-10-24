using RevoHelper.Hardware.Windows.Discovery;
using RevoHelper.Hardware.Windows.Telemetry;

namespace RevoHelper.Hardware.Windows.Tests.Telemetry;

public sealed class WindowsProcessorPerformanceReaderTests
{
    [Fact]
    public void Read_SelectsTotalProcessorUtilityAndFrequency()
    {
        var query = new StubQuery(
        [
            Row(("Name", "0,0"), ("PercentProcessorUtility", "50"), ("ProcessorFrequency", "4200")),
            Row(("Name", "_Total"), ("PercentProcessorUtility", "18"), ("ProcessorFrequency", "2150"))
        ]);

        var reading = new WindowsProcessorPerformanceReader(query).Read();

        Assert.Equal(18, reading.UsagePercent);
        Assert.Equal(2150, reading.FrequencyMhz);
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
