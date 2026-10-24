using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RevoHelper.App.Settings;

public sealed record MonitorProgramEntry(
    string Id,
    string ExecutablePath,
    string DisplayName,
    DateTimeOffset AddedAtUtc,
    DateTimeOffset? LastSelectedAtUtc = null)
{
    [JsonIgnore]
    public string AddedAtText => AddedAtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.CurrentCulture);
    [JsonIgnore]
    public string LastSelectedAtText => LastSelectedAtUtc?.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.CurrentCulture) ?? "尚未选择";
}

/// <summary>Stores only programs deliberately added by the user. It never discovers programs or launches EXEs.</summary>
public sealed class MonitorProgramStore
{
    private const int MaximumPrograms = 64;
    private const long MaximumStoreBytes = 1024 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly object sync = new();
    private readonly string path;

    public MonitorProgramStore(string path)
    {
        this.path = Path.GetFullPath(path);
    }

    public IReadOnlyList<MonitorProgramEntry> Load()
    {
        lock (sync) return LoadCore();
    }

    public MonitorProgramEntry Add(string executablePath, DateTimeOffset now)
    {
        var normalizedPath = NormalizeExecutablePath(executablePath)
            ?? throw new ArgumentException("请选择完整的 EXE 程序路径。", nameof(executablePath));
        lock (sync)
        {
            var entries = LoadCore();
            var existing = entries.FirstOrDefault(entry => string.Equals(entry.ExecutablePath, normalizedPath, StringComparison.OrdinalIgnoreCase));
            if (existing is not null) return existing;
            if (entries.Count >= MaximumPrograms) throw new InvalidOperationException("最多可保存 64 个程序，请先移除不需要的记录。");

            var entry = new MonitorProgramEntry(Guid.NewGuid().ToString("N"), normalizedPath,
                Path.GetFileName(normalizedPath), now.ToUniversalTime());
            entries.Add(entry);
            SaveCore(entries);
            return entry;
        }
    }

    public MonitorProgramEntry? MarkSelected(string id, DateTimeOffset now)
    {
        lock (sync)
        {
            var entries = LoadCore();
            var index = entries.FindIndex(entry => string.Equals(entry.Id, id, StringComparison.Ordinal));
            if (index < 0) return null;
            var selected = entries[index] with { LastSelectedAtUtc = now.ToUniversalTime() };
            entries[index] = selected;
            SaveCore(entries);
            return selected;
        }
    }

    public void Remove(string id)
    {
        lock (sync)
        {
            var entries = LoadCore();
            if (entries.RemoveAll(entry => string.Equals(entry.Id, id, StringComparison.Ordinal)) == 0) return;
            SaveCore(entries);
        }
    }

    internal static string? NormalizeExecutablePath(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var trimmed = value.Trim();
        if (trimmed.Length > 32767 || trimmed.Any(character => char.IsControl(character) || character is '"' or '<' or '>' or '|' or '*' or '?'))
            return null;
        try
        {
            if (!Path.IsPathFullyQualified(trimmed)) return null;
            var fullPath = Path.GetFullPath(trimmed);
            // Reject alternate data streams and device paths; this list contains normal EXE files only.
            if (fullPath.StartsWith(@"\\.\", StringComparison.Ordinal) || fullPath.AsSpan(2).Contains(':') ||
                !string.Equals(Path.GetExtension(fullPath), ".exe", StringComparison.OrdinalIgnoreCase)) return null;
            var fileName = Path.GetFileName(fullPath);
            return fileName.Length is > 4 and <= 255 ? fullPath : null;
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }

    private List<MonitorProgramEntry> LoadCore()
    {
        try
        {
            if (!File.Exists(path) || new FileInfo(path).Length > MaximumStoreBytes) return [];
            var loaded = JsonSerializer.Deserialize<List<MonitorProgramEntry?>>(File.ReadAllText(path));
            if (loaded is null) return [];
            var result = new List<MonitorProgramEntry>(Math.Min(loaded.Count, MaximumPrograms));
            var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var entry in loaded)
            {
                if (result.Count >= MaximumPrograms) break;
                if (entry is null || string.IsNullOrWhiteSpace(entry.Id) || entry.Id.Length > 64 ||
                    entry.Id.Any(char.IsControl) || entry.AddedAtUtc == default ||
                    NormalizeExecutablePath(entry.ExecutablePath) is not { } fullPath ||
                    paths.Contains(fullPath) || ids.Contains(entry.Id)) continue;
                paths.Add(fullPath);
                ids.Add(entry.Id);
                result.Add(entry with
                {
                    ExecutablePath = fullPath,
                    // Never acquire a document/window title as a display label.
                    DisplayName = Path.GetFileName(fullPath),
                    AddedAtUtc = entry.AddedAtUtc.ToUniversalTime(),
                    LastSelectedAtUtc = entry.LastSelectedAtUtc?.ToUniversalTime()
                });
            }
            return result;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
        {
            // Merely opening the settings must not overwrite a malformed/inaccessible file.
            return [];
        }
    }

    private void SaveCore(IReadOnlyList<MonitorProgramEntry> entries)
    {
        var json = JsonSerializer.SerializeToUtf8Bytes(entries, JsonOptions);
        if (json.LongLength > MaximumStoreBytes) throw new InvalidOperationException("保存的程序路径过长，请减少记录后重试。");
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.Write(json);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }
}
