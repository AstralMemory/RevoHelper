using System.Globalization;
using RevoHelper.Core.Dashboard;
using RevoHelper.Core.Telemetry;

namespace RevoHelper.App.Monitoring;

public sealed record MonitorTextRun(string Text, string Color);

public static class MonitorLineFormatter
{
    private static string Number(double? value, string unit) =>
        value is double number && double.IsFinite(number) && number >= 0
            ? Math.Round(number, MidpointRounding.AwayFromZero).ToString("0", CultureInfo.InvariantCulture) + unit : "—";

    public static IReadOnlyList<MonitorTextRun> Format(HardwareTelemetrySnapshot? snapshot, double? fps, double? fallbackCpuTemperature = null)
    {
        var cpu = snapshot?.Devices.FirstOrDefault(device => device.Kind == DeviceKind.Cpu);
        var gpu = snapshot?.Devices.FirstOrDefault(device => device.Kind == DeviceKind.DiscreteGpu)
            ?? snapshot?.Devices.FirstOrDefault(device => device.Kind == DeviceKind.IntegratedGpu);
        var runs = new List<MonitorTextRun>
        {
            new("FPS " + Number(fps, ""), "#38FF98"), new("    CPU:  ", "#FFFFFF"),
            new(Number(cpu?.TemperatureC ?? fallbackCpuTemperature, " °C") + "   " + Number(cpu?.UsagePercent, " %") + "   " + Number(cpu?.PowerWatts, " W"), "#57ACFF")
        };
        if (cpu?.PerformanceCoreFrequencyMhz is > 0 || cpu?.EfficiencyCoreFrequencyMhz is > 0)
        {
            runs.Add(new("  P-Core  ", "#FFFFFF"));
            runs.Add(new(Number(cpu.PerformanceCoreFrequencyMhz, " MHz"), "#00CED1"));
            runs.Add(new("  E-Core  ", "#FFFFFF"));
            runs.Add(new(Number(cpu.EfficiencyCoreFrequencyMhz, " MHz"), "#00CED1"));
        }
        else runs.Add(new("   " + Number(cpu?.FrequencyMhz, " MHz"), "#00CED1"));
        runs.Add(new("    GPU:  ", "#FFFFFF"));
        runs.Add(new(Number(gpu?.TemperatureC, " °C") + "   " + Number(gpu?.UsagePercent, " %") + "   " + Number(gpu?.PowerWatts, " W"), "#FFB13C"));
        runs.Add(new("   " + Number(gpu?.FrequencyMhz, " MHz"), "#FF7800"));
        return runs;
    }
}
