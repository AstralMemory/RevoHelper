using System.Globalization;
using RevoHelper.Hardware.Windows.Discovery;

namespace RevoHelper.Hardware.Windows.Telemetry;

public interface IProcessorPerformanceReader
{
    ProcessorPerformance Read();
}

public sealed record ProcessorPerformance(
    double? UsagePercent,
    double? FrequencyMhz);

public sealed class WindowsProcessorPerformanceReader(IWmiQuery query) : IProcessorPerformanceReader
{
    private const string ClassName = "Win32_PerfFormattedData_Counters_ProcessorInformation";

    public ProcessorPerformance Read()
    {
        var total = query.ReadAll(ClassName).FirstOrDefault(row =>
            row.TryGetValue("Name", out var name) && name == "_Total");
        return total is null
            ? new(null, null)
            : new(
                Parse(total, "PercentProcessorUtility"),
                Parse(total, "ProcessorFrequency"));
    }

    private static double? Parse(IReadOnlyDictionary<string, string> row, string key) =>
        row.TryGetValue(key, out var text) &&
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) &&
        double.IsFinite(value) &&
        value >= 0
            ? value
            : null;
}
