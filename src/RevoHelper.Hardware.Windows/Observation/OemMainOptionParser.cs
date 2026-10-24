using System.Text.Json;

namespace RevoHelper.Hardware.Windows.Observation;

public sealed record OemMainOptionState(
    int? OperatingMode,
    IReadOnlyDictionary<string, int?> Selections);

public static class OemMainOptionParser
{
    private static readonly string[] SelectionNames =
    [
        "GamingProfileIndex",
        "OfficeProfileIndex",
        "TurboProfileIndex",
        "CustomProfileIndex",
        "FanBoostEnable",
        "WhisperModeSwitch",
        "WhisperModeSetting"
    ];

    public static OemMainOptionState Parse(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            var selections = SelectionNames.ToDictionary(
                name => name,
                name => ReadInt(root, name),
                StringComparer.Ordinal);

            return new(ReadInt(root, "OperatingMode"), selections);
        }
        catch (JsonException)
        {
            return new(null, new Dictionary<string, int?>());
        }
    }

    private static int? ReadInt(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.TryGetInt32(out var parsed)
            ? parsed
            : null;
}
