using System.IO;
using System.Text;
using System.Text.Json;

namespace RevoHelper.App.Settings;

public sealed class JsonApplicationSettingsStore(string path) : IApplicationSettingsStore
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };
    private readonly object gate = new();
    private string BackupPath => path + ".bak";

    public ApplicationSettings Load()
    {
        lock (gate)
        {
            if (TryRead(path) is { } current) return current;
            // The file exists but cannot be used (a power cut can leave it empty or full of zero
            // bytes). Fall back to the last copy that loaded, instead of silently starting from
            // defaults and then overwriting everything the user had set.
            if (TryRead(BackupPath) is { } previous)
            {
                KeepUnreadableCopy();
                return previous;
            }
            return new();
        }
    }

    public void Save(ApplicationSettings settings)
    {
        lock (gate)
        {
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(settings, Options));
            var temporaryPath = $"{path}.tmp";
            using (var stream = new FileStream(temporaryPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                stream.Write(bytes);
                stream.Flush(true); // On disk before it replaces the good file.
            }
            // Keep the file that is about to be replaced, but only if it is itself readable.
            if (TryRead(path) is not null)
            {
                try { File.Copy(path, BackupPath, overwrite: true); }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
            }
            File.Move(temporaryPath, path, overwrite: true);
        }
    }

    private static ApplicationSettings? TryRead(string file)
    {
        try
        {
            if (!File.Exists(file)) return null;
            var text = File.ReadAllText(file);
            return string.IsNullOrWhiteSpace(text.Trim('\0')) ? null : JsonSerializer.Deserialize<ApplicationSettings>(text, Options);
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return null;
        }
    }

    private void KeepUnreadableCopy()
    {
        try
        {
            if (File.Exists(path)) File.Copy(path, path + ".unreadable", overwrite: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
    }
}
