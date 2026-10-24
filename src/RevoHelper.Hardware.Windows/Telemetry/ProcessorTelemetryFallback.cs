namespace RevoHelper.Hardware.Windows.Telemetry;

public static class ProcessorTelemetryFallback
{
    public static SelectedTelemetry ReadIfNeeded(SelectedTelemetry selected, IProcessorPerformanceReader reader)
    {
        if (selected.UsagePercent is not null) return selected;
        try { return Merge(selected, reader.Read()); }
        catch (Exception) { return selected; }
    }

    public static SelectedTelemetry Merge(
        SelectedTelemetry selected,
        ProcessorPerformance fallback) => selected with
        {
            UsagePercent = selected.UsagePercent ?? fallback.UsagePercent
        };
}
