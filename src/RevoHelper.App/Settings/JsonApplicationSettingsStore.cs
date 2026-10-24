using System.IO;
using System.Text.Json;

namespace RevoHelper.App.Settings;

public sealed class JsonApplicationSettingsStore(string path) : IApplicationSettingsStore
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public ApplicationSettings Load()
    {
        if (!File.Exists(path))
        {
            return new();
        }

        try
        {
            return JsonSerializer.Deserialize<ApplicationSettings>(File.ReadAllText(path), Options)
                ?? new();
        }
        catch (JsonException)
        {
            return new();
        }
        catch (IOException)
        {
            return new();
        }
    }

    public void Save(ApplicationSettings settings)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var temporaryPath = $"{path}.tmp";
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(settings, Options));
        File.Move(temporaryPath, path, overwrite: true);
    }
}
