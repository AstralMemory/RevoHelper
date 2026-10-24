using System.IO;

namespace RevoHelper.App.Lifecycle;

/// <summary>Bounded local lifecycle diagnostics; no MQTT payloads or credentials.</summary>
public static class RuntimeDiagnosticLog
{
    private static readonly object Sync = new();
    private static string? previous;
    private static DateTimeOffset previousAt;

    public static void Write(string area, string message)
    {
        try
        {
            lock (Sync)
            {
                var text = $"{area}: {message.Replace('\r', ' ').Replace('\n', ' ')}";
                if (text.Length > 800) text = text[..800];
                var now = DateTimeOffset.Now;
                if (text == previous && now - previousAt < TimeSpan.FromSeconds(30)) return;
                previous = text;
                previousAt = now;
                var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "RevoHelper", "diagnostics");
                Directory.CreateDirectory(directory);
                var path = Path.Combine(directory, "runtime.log");
                if (File.Exists(path) && new FileInfo(path).Length > 256 * 1024)
                    File.Move(path, path + ".previous", overwrite: true);
                File.AppendAllText(path, $"{now:O} {text}{Environment.NewLine}");
            }
        }
        catch (Exception) { /* Diagnostics must never block startup or hardware control. */ }
    }
}
