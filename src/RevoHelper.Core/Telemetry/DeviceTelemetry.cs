using RevoHelper.Core.Dashboard;

namespace RevoHelper.Core.Telemetry;

/// <param name="Vendor">Who made the device, from the sensor library's own type; used to find its row.</param>
/// <param name="Asleep">A discrete GPU that Windows reports powered down; it has no live temperature or clock.</param>
/// <param name="ZoneTemperatureC">
/// The hottest ACPI thermal zone, which the laptop's firmware reports for its CPU. Used only when
/// neither the sensors nor the OEM service give a CPU temperature.
/// </param>
/// <param name="InstanceId">
/// The GPU's Plug and Play instance ID (PCI\VEN_…) when known; it ties the sensor library's
/// device to the adapter Windows lists, whatever either of them calls it.
/// </param>
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
    double? MaximumCoreUsagePercent = null,
    HardwareVendor Vendor = HardwareVendor.Unknown,
    bool Asleep = false,
    double? ZoneTemperatureC = null,
    string? InstanceId = null);
