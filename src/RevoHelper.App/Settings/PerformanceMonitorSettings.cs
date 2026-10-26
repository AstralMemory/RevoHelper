using System.IO;
using System.Text.Json;

namespace RevoHelper.App.Settings;

public enum MonitorPositionPreset
{
    Custom,
    TopLeft,
    TopCenter,
    TopRight,
    BottomLeft,
    BottomRight,
    BottomCenter
}

public sealed record PerformanceMonitorSettings(
    bool Enabled = false,
    bool AlwaysOnTop = true,
    int SizePreset = 1,
    double BackgroundOpacity = 0.50,
    double? Left = null,
    double? Top = null,
    string[]? ExcludedProcesses = null,
    int SchemaVersion = 2,
    MonitorPositionPreset PositionPreset = MonitorPositionPreset.TopCenter,
    bool IsPositionLocked = false,
    bool IsAutomatic = true,
    string? SelectedProgramId = null)
{
    public PerformanceMonitorSettings Normalize() => this with
    {
        SchemaVersion = 2,
        SizePreset = Math.Clamp(SizePreset, 0, 4),
        PositionPreset = Enum.IsDefined(PositionPreset) ? PositionPreset : MonitorPositionPreset.TopCenter,
        BackgroundOpacity = double.IsFinite(BackgroundOpacity) ? Math.Clamp(BackgroundOpacity, 0, 1) : 0.5,
        Left = Left is double x && double.IsFinite(x) ? x : null,
        Top = Top is double y && double.IsFinite(y) ? y : null,
        SelectedProgramId = !string.IsNullOrWhiteSpace(SelectedProgramId) && SelectedProgramId.Length <= 64 &&
            !SelectedProgramId.Any(char.IsControl) ? SelectedProgramId : null,
        ExcludedProcesses = (ExcludedProcesses ?? []).Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Take(64).ToArray()
    };
}

public sealed class PerformanceMonitorSettingsStore(string path)
{
    public PerformanceMonitorSettings Load()
    {
        try
        {
            if (!File.Exists(path)) return new PerformanceMonitorSettings().Normalize();
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return new PerformanceMonitorSettings().Normalize();
            var settings = root.Deserialize<PerformanceMonitorSettings>() ?? new();
            if (!root.TryGetProperty(nameof(PerformanceMonitorSettings.SchemaVersion), out var version) ||
                version.GetInt32() < 2)
            {
                // Snapshot 1 used 12/15/18 DIP at indices 0/1/2. Preserve that
                // visible size rather than treating those indices as new fonts.
                settings = settings with
                {
                    SizePreset = Math.Clamp(settings.SizePreset, 0, 2) + 1,
                    PositionPreset = settings.Left is double x && double.IsFinite(x) &&
                        settings.Top is double y && double.IsFinite(y)
                        ? MonitorPositionPreset.Custom : MonitorPositionPreset.TopCenter
                };
            }
            return settings.Normalize();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
        { return new PerformanceMonitorSettings().Normalize(); }
    }
    public void Save(PerformanceMonitorSettings settings)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(settings.Normalize(), new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temporary, path, overwrite: true);
    }
}
