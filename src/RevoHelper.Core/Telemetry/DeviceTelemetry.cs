using RevoHelper.Core.Dashboard;

namespace RevoHelper.Core.Telemetry;

public sealed record DeviceTelemetry(
    DeviceKind Kind,
    string Model,
    double? UsagePercent,
    double? TemperatureC,
    double? PowerWatts,
    double? FrequencyMhz,
    double? PerformanceCoreFrequencyMhz = null,
    double? EfficiencyCoreFrequencyMhz = null,
    double? DedicatedMemoryMegabytes = null,
    double? MaximumCoreUsagePercent = null);
