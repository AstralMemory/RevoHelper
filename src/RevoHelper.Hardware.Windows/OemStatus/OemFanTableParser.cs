using System.Text.Json;
using RevoHelper.Core.Fans;

namespace RevoHelper.Hardware.Windows.OemStatus;

public static class OemFanTableParser
{
    public static FanCurveTable? Parse(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            var cpu = ReadPoints(root, "CPU");
            var gpu = ReadPoints(root, "GPU");
            if (cpu.Count == 0 && gpu.Count == 0)
            {
                return null;
            }

            return new(
                ReadString(root, "Name") ?? string.Empty,
                ReadBool(root, "FanControlRespective"),
                cpu,
                gpu);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static IReadOnlyList<FanCurvePoint> ReadPoints(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var array) || array.ValueKind is not JsonValueKind.Array)
        {
            return Array.Empty<FanCurvePoint>();
        }

        var points = new List<FanCurvePoint>();
        foreach (var item in array.EnumerateArray())
        {
            if (!TryReadInt(item, "ID", out var id) ||
                !TryReadInt(item, "UpT", out var up) ||
                !TryReadInt(item, "DownT", out var down) ||
                !TryReadInt(item, "Duty", out var duty))
            {
                continue;
            }

            points.Add(new(id, up, down, duty));
        }

        return points;
    }

    private static bool TryReadInt(JsonElement root, string name, out int result)
    {
        result = default;
        if (!root.TryGetProperty(name, out var value))
        {
            return false;
        }

        return value.ValueKind switch
        {
            JsonValueKind.Number => value.TryGetInt32(out result),
            JsonValueKind.String => int.TryParse(value.GetString(), out result),
            _ => false
        };
    }

    private static string? ReadString(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind is JsonValueKind.String
            ? value.GetString()
            : null;

    private static bool ReadBool(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind is JsonValueKind.True;
}
