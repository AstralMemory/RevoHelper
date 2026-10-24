using System.Text.Json;
using RevoHelper.Core.Control;

namespace RevoHelper.Hardware.Windows.OemControl;

public static class GcuGpuOutputModeStatusParser
{
    public static GcuGpuOutputModeStatus? Parse(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            using var document = JsonDocument.Parse(json);
            var status = FindString(document.RootElement, "DiscreteGpuDirectConnectionSwitch_Status");
            var mode = status switch
            {
                "DGPU_DIRECT_CONNECT_TOGGLE_OFF" => GpuOutputMode.Hybrid,
                "DGPU_DIRECT_CONNECT_TOGGLE_ON" => GpuOutputMode.Discrete,
                "DGPU_DIRECT_CONNECT_TOGGLE_IGPU" => GpuOutputMode.Integrated,
                _ => (GpuOutputMode?)null
            };
            if (mode is null) return null;

            return new(
                mode.Value,
                ParseFlag(FindString(document.RootElement, "DiscreteGpuDirectConnectionSwitch_Support")),
                ParseFlag(FindString(document.RootElement, "IGpuOnlyConnectionSwitch_Support")));
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool? ParseFlag(string? value) => value switch
    {
        "1" or "true" or "True" or "ON" => true,
        "0" or "false" or "False" or "OFF" => false,
        _ => null
    };

    private static string? FindString(JsonElement element, string name)
    {
        if (element.ValueKind is JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
                    return property.Value.ValueKind is JsonValueKind.String
                        ? property.Value.GetString()
                        : property.Value.ToString();
                var nested = FindString(property.Value, name);
                if (nested is not null) return nested;
            }
        }
        else if (element.ValueKind is JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                var nested = FindString(item, name);
                if (nested is not null) return nested;
            }
        }
        return null;
    }
}
