using RevoHelper.Core.Dashboard;
using RevoHelper.Core.Telemetry;

namespace RevoHelper.Hardware.Windows.Telemetry;

/// <summary>
/// Fills what the sensor library could not read with what Windows itself reports. The library's
/// value always wins; Windows only covers a gap. This runs whatever the library returned, even
/// nothing at all, so the CPU row never depends on one library recognising one processor.
/// </summary>
public static class ProcessorTelemetryFallback
{
    public static bool HasClock(DeviceTelemetry cpu) =>
        cpu.FrequencyMhz is not null || cpu.PerformanceCoreFrequencyMhz is not null ||
        cpu.EfficiencyCoreFrequencyMhz is not null;

    /// <summary>Whether this tick needs Windows' counters at all; on a fully read machine they are not touched.</summary>
    public static bool NeedsWindowsCounters(IReadOnlyList<DeviceTelemetry> devices) =>
        devices.FirstOrDefault(device => device.Kind is DeviceKind.Cpu) is not { } cpu ||
        cpu.UsagePercent is null || !HasClock(cpu) || cpu.PowerWatts is null || cpu.TemperatureC is null ||
        devices.Any(IsIntelIntegratedWithoutPower);

    /// <param name="processorName">Used only when the library returned no CPU at all.</param>
    /// <param name="wmi">The older WMI counters, asked only for what PDH could not give.</param>
    public static IReadOnlyList<DeviceTelemetry> Fill(
        IReadOnlyList<DeviceTelemetry> devices,
        WindowsCounterReading windows,
        string processorName,
        Func<ProcessorPerformance?>? wmi = null)
    {
        var result = devices.ToList();
        var index = result.FindIndex(device => device.Kind is DeviceKind.Cpu);
        var cpu = index >= 0 ? result[index] : new DeviceTelemetry(DeviceKind.Cpu, processorName, null, null, null, null);

        var usage = cpu.UsagePercent ?? windows.CpuUsagePercent;
        var frequency = HasClock(cpu) ? cpu.FrequencyMhz : windows.CpuFrequencyMhz;
        if (usage is null || (!HasClock(cpu) && frequency is null))
        {
            ProcessorPerformance? older = null;
            try { older = wmi?.Invoke(); }
            catch (Exception) { /* A broken WMI provider leaves the gap as it was. */ }
            usage ??= older?.UsagePercent;
            if (!HasClock(cpu)) frequency ??= older?.FrequencyMhz;
        }

        cpu = cpu with
        {
            UsagePercent = usage,
            FrequencyMhz = frequency,
            PowerWatts = cpu.PowerWatts ?? windows.CpuPackagePowerWatts,
            // Kept apart from TemperatureC: the OEM service's CPU temperature still comes first.
            ZoneTemperatureC = cpu.TemperatureC is null ? windows.ZoneTemperatureC : null
        };
        if (index >= 0) result[index] = cpu;
        else result.Insert(0, cpu);

        // Intel's integrated graphics has its own plane in the processor's energy meter.
        if (windows.GraphicsPowerWatts is { } graphics)
        {
            for (var gpu = 0; gpu < result.Count; gpu++)
            {
                if (IsIntelIntegratedWithoutPower(result[gpu]))
                    result[gpu] = result[gpu] with { PowerWatts = graphics };
            }
        }

        return result;
    }

    private static bool IsIntelIntegratedWithoutPower(DeviceTelemetry device) =>
        device is { Kind: DeviceKind.IntegratedGpu, Vendor: HardwareVendor.Intel, PowerWatts: null, Asleep: false };
}
