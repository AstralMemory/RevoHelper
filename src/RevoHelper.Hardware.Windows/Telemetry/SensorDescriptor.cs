namespace RevoHelper.Hardware.Windows.Telemetry;

public enum SensorValueKind
{
    Load,
    Temperature,
    Power,
    Clock,
    Memory
}

public sealed record SensorDescriptor(
    string Name,
    SensorValueKind Kind,
    double? Value);

public sealed record SelectedTelemetry(
    double? UsagePercent,
    double? TemperatureC,
    double? PowerWatts,
    double? FrequencyMhz,
    double? PerformanceCoreFrequencyMhz = null,
    double? EfficiencyCoreFrequencyMhz = null,
    double? DedicatedMemoryMegabytes = null,
    double? MaximumCoreUsagePercent = null);
