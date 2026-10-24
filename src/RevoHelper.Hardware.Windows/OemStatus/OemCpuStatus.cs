using System.Globalization;
using System.Text.Json;

namespace RevoHelper.Hardware.Windows.OemStatus;

public sealed record OemCpuStatus(
    double? TemperatureC,
    double? UsagePercent,
    double? FrequencyMhz);

public static class OemCpuStatusParser
{
    public static OemCpuStatus? Parse(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            var temperature = ReadDouble(root, "CpuTemperature");
            if (temperature is <= 0)
            {
                temperature = null;
            }

            var usage = ReadDouble(root, "CpuUsage");
            var frequency = ReadDouble(root, "CpuFrequency");
            return temperature is null && usage is null && frequency is null
                ? null
                : new(temperature, usage, frequency);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static double? ReadDouble(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var value))
        {
            return null;
        }

        if (value.ValueKind is JsonValueKind.Number && value.TryGetDouble(out var number))
        {
            return double.IsFinite(number) ? number : null;
        }

        return value.ValueKind is JsonValueKind.String &&
               double.TryParse(value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out number) &&
               double.IsFinite(number)
            ? number
            : null;
    }
}
