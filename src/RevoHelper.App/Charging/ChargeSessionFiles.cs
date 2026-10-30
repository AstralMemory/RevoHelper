using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using RevoHelper.Hardware.Windows.BatteryCharge;

namespace RevoHelper.App.Charging;

public sealed record ChargeLease(string SessionId, int LimitPercent, int OwnerId, long OwnerStartTicks,
    int GuardianId, long GuardianStartTicks, int WorkerId, long WorkerStartTicks, ChargeEcSnapshot Original);

public sealed record ChargeWireStatus(string Phase, string Message, int LimitPercent,
    bool Enabled = false, bool RecoveryPending = false,
    ChargeEcSnapshot? Snapshot = null, ChargeRecoveryResult? Recovery = null);

public sealed class ChargeSessionFiles
{
    public static string Root => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RevoHelper", "ExperimentalCharge");
    public string SessionId { get; }
    public string DirectoryPath { get; }
    public string StopName => @"Local\RevoHelper.ExperimentalCharge.Stop." + SessionId;
    public string StartName => @"Local\RevoHelper.ExperimentalCharge.Start." + SessionId;
    public string LeasePath => Path.Combine(DirectoryPath, "lease.json");
    public string StatusPath => Path.Combine(DirectoryPath, "status.json");
    private string JournalPath => Path.Combine(DirectoryPath, "journal.jsonl");

    public ChargeSessionFiles(string sessionId)
    {
        if (!Guid.TryParseExact(sessionId, "N", out var id) || id.ToString("N") != sessionId)
            throw new ArgumentException("Invalid experimental session identifier.", nameof(sessionId));
        SessionId = sessionId;
        DirectoryPath = Path.Combine(Root, sessionId);
    }
    public void Initialize()
    {
        Directory.CreateDirectory(Root);
        RejectReparse(Root);
        Directory.CreateDirectory(DirectoryPath);
        RejectReparse(DirectoryPath);
    }
    public void SaveLease(ChargeLease lease) => WriteAtomic(LeasePath, lease);
    public ChargeLease ReadLease()
    {
        var lease = JsonSerializer.Deserialize<ChargeLease>(File.ReadAllText(LeasePath)) ?? throw new InvalidDataException("Missing recovery lease.");
        if (lease.SessionId != SessionId || lease.LimitPercent is not (60 or 80)) throw new InvalidDataException("Recovery lease differs from this session.");
        ChargeSessionEngine.ValidateInitial(lease.Original);
        return lease;
    }
    public void SaveStatus(ChargeWireStatus status) => WriteAtomic(StatusPath, status);
    public ChargeWireStatus? ReadStatus()
    {
        try { return JsonSerializer.Deserialize<ChargeWireStatus>(File.ReadAllText(StatusPath)); }
        catch (IOException) { return null; }
        catch (JsonException) { return null; }
    }
    public void Journal(string phase, ushort? address = null, int? from = null, int? to = null)
    {
        RejectReparse(DirectoryPath);
        var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { SessionId, At = DateTimeOffset.UtcNow, Phase = phase, Address = address, From = from, To = to }) + "\n");
        using var stream = new FileStream(JournalPath, FileMode.Append, FileAccess.Write, FileShare.Read);
        stream.Write(bytes); stream.Flush(true);
    }
    public bool HasIntent(string phase)
    {
        if (!File.Exists(JournalPath)) throw new FileNotFoundException("Recovery intent journal is missing.", JournalPath);
        foreach (var line in File.ReadLines(JournalPath))
        {
            try
            {
                using var document = JsonDocument.Parse(line);
                if (document.RootElement.GetProperty("SessionId").GetString() == SessionId
                    && document.RootElement.GetProperty("Phase").GetString() == phase) return true;
            }
            catch (JsonException) { }
        }
        return false;
    }
    private void WriteAtomic<T>(string path, T value)
    {
        RejectReparse(DirectoryPath);
        var temporary = Path.Combine(DirectoryPath, Guid.NewGuid().ToString("N") + ".tmp");
        using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        { JsonSerializer.Serialize(stream, value); stream.Flush(true); }
        File.Move(temporary, path, true);
    }
    private static void RejectReparse(string path)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new IOException("Experimental session path must not be a junction or symbolic link.");
    }
    public static bool IsProcess(int id, long startTicks)
    {
        try { using var process = Process.GetProcessById(id); return !process.HasExited && process.StartTime.ToUniversalTime().Ticks == startTicks; }
        catch (ArgumentException) { return false; }
        catch (InvalidOperationException) { return false; }
        catch (System.ComponentModel.Win32Exception) { return false; }
    }
    public static Process Launch(string mode, string session, int limit, int owner, long ticks)
    {
        var info = new ProcessStartInfo(Environment.ProcessPath ?? throw new InvalidOperationException("Executable path unavailable."))
        { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden };
        foreach (var value in new[] { mode, session, limit.ToString(System.Globalization.CultureInfo.InvariantCulture), owner.ToString(System.Globalization.CultureInfo.InvariantCulture), ticks.ToString(System.Globalization.CultureInfo.InvariantCulture) })
            info.ArgumentList.Add(value);
        return Process.Start(info) ?? throw new InvalidOperationException("Cannot start independent charge monitor.");
    }
}
