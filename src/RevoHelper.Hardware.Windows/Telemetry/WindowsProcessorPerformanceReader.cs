using System.Globalization;
using RevoHelper.Hardware.Windows.Discovery;

namespace RevoHelper.Hardware.Windows.Telemetry;

public interface IProcessorPerformanceReader
{
    ProcessorPerformance Read();
}

/// <param name="FrequencyMhz">
/// The live average clock, as Task Manager shows it: base clock times "% Processor Performance".
/// Null when either counter is missing; the base clock alone is never reported as a live clock.
/// </param>
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
        if (total is null) return new(null, null);
        var baseClock = Parse(total, "ProcessorFrequency");
        var performance = Parse(total, "PercentProcessorPerformance");
        return new(
            Parse(total, "PercentProcessorUtility"),
            baseClock is > 0 && performance is > 0 ? Math.Round(baseClock.Value * performance.Value / 100) : null);
    }

    private static double? Parse(IReadOnlyDictionary<string, string> row, string key) =>
        row.TryGetValue(key, out var text) &&
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) &&
        double.IsFinite(value) &&
        value >= 0
            ? value
            : null;
}
