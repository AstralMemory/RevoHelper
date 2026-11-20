using System.Globalization;

namespace RevoHelper.Core.Telemetry;

public static class TelemetryFormatter
{
    private const string NotAvailable = "N/A";

    /// <summary>A powered-down discrete GPU: nothing is wrong, it simply has no live temperature or clock.</summary>
    public const string Asleep = "休眠";

    public static FormattedTelemetry Format(DeviceTelemetry telemetry) =>
        new(
            FormatValue(telemetry.UsagePercent, "%"),
            telemetry.Asleep && telemetry.TemperatureC is null ? Asleep : FormatValue(telemetry.TemperatureC, "°C"),
            FormatValue(telemetry.PowerWatts, " W", includeSpace: false),
            telemetry.Asleep && telemetry.FrequencyMhz is null ? Asleep : FormatFrequency(telemetry));

    private static string FormatFrequency(DeviceTelemetry telemetry)
    {
        if (telemetry.Kind is not Dashboard.DeviceKind.Cpu ||
            telemetry.PerformanceCoreFrequencyMhz is null && telemetry.EfficiencyCoreFrequencyMhz is null)
        {
            return FormatValue(telemetry.FrequencyMhz, " MHz", includeSpace: false);
        }

        var lines = new List<string>(2);
        if (FormatValue(telemetry.PerformanceCoreFrequencyMhz, " MHz", includeSpace: false) is { } performance &&
            performance != NotAvailable)
        {
            lines.Add($"P {performance}");
        }

        if (FormatValue(telemetry.EfficiencyCoreFrequencyMhz, " MHz", includeSpace: false) is { } efficiency &&
            efficiency != NotAvailable)
        {
            lines.Add($"E {efficiency}");
        }

        return lines.Count == 0 ? NotAvailable : string.Join('\n', lines);
    }

    private static string FormatValue(double? value, string suffix, bool includeSpace = false)
    {
        if (value is not double number || !double.IsFinite(number) || number < 0)
        {
            return NotAvailable;
        }

        var rounded = Math.Round(number, MidpointRounding.AwayFromZero)
            .ToString("0", CultureInfo.InvariantCulture);
        return includeSpace ? $"{rounded} {suffix}" : $"{rounded}{suffix}";
    }
}

public sealed record FormattedTelemetry(
    string Usage,
    string Temperature,
    string Power,
    string Frequency);
