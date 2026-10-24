using System.Text.Json;

namespace RevoHelper.Hardware.Windows.OemControl;

public sealed record GpuPowerBudgetRecord(int TargetTgpWatts, int EnabledBoostWatts,
    int RawTgpWatts, int RawBoostWatts, bool BoostEnabled);

public interface IGpuPowerBudgetStore
{
    GpuPowerBudgetRecord? Read(int profile);
    void Write(int profile, GpuPowerBudgetRecord record);
}

public sealed class JsonGpuPowerBudgetStore(string path) : IGpuPowerBudgetStore
{
    private readonly Dictionary<int, GpuPowerBudgetRecord> records = Load(path);
    private readonly object sync = new();
    public GpuPowerBudgetRecord? Read(int profile) { lock (sync) return records.GetValueOrDefault(profile); }

    public void Write(int profile, GpuPowerBudgetRecord record)
    {
        lock (sync)
        {
            if (records.GetValueOrDefault(profile) == record) return;
            var updated = new Dictionary<int, GpuPowerBudgetRecord>(records) { [profile] = record };
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var temporary = path + ".tmp";
            try
            {
                File.WriteAllText(temporary, JsonSerializer.Serialize(updated));
                File.Move(temporary, path, true);
                records[profile] = record;
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
    }

    private static Dictionary<int, GpuPowerBudgetRecord> Load(string path)
    {
        try
        {
            if (!File.Exists(path)) return [];
            return (JsonSerializer.Deserialize<Dictionary<int, GpuPowerBudgetRecord>>(File.ReadAllText(path)) ?? [])
                .Where(pair => pair.Key is >= 1 and <= 5 && pair.Value is
                    { TargetTgpWatts: >= 25 and <= 250, RawTgpWatts: >= 25 and <= 250,
                      EnabledBoostWatts: >= 0 and <= 50, RawBoostWatts: >= 0 and <= 50 })
                .ToDictionary(pair => pair.Key, pair => pair.Value);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        { return []; }
    }
}
