using System.ComponentModel;
using System.Diagnostics;

namespace RevoHelper.App.Lifecycle;

/// <summary>A process of the official background service, told apart by its ID and start time.</summary>
public sealed record OfficialServiceProcess(int Id, DateTime StartedUtc);

/// <summary>
/// The official background service's process in this session (GCUService), which opens the
/// official tray. RevoHelper's channel to the official service can drop and connect again while
/// the same process keeps running, so a reconnect alone is no reason to look for the tray: only a
/// new process, or one that has appeared since the last look, opens the tray again by itself.
/// Looked at only when RevoHelper starts and when that channel connects; never on a timer.
/// </summary>
public sealed class OfficialServiceInstance(Func<OfficialServiceProcess?> find)
{
    private readonly object sync = new();
    private OfficialServiceProcess? last;
    private bool looked;

    public OfficialServiceInstance() : this(FindInThisSession) { }

    /// <summary>True when the last look found no service process in this session.</summary>
    public bool Missing { get { lock (sync) return looked && last is null; } }

    /// <summary>
    /// Looks again and records what it found. True when the service now runs as a process other
    /// than at the last look, including one that has appeared since; the first look only records.
    /// </summary>
    public bool LookAgain()
    {
        var now = find();
        lock (sync)
        {
            var changed = looked && now is not null && now != last;
            last = now;
            looked = true;
            return changed;
        }
    }

    private static OfficialServiceProcess? FindInThisSession()
    {
        OfficialServiceProcess? found = null;
        int session;
        using (var self = Process.GetCurrentProcess()) session = self.SessionId;
        foreach (var process in Process.GetProcessesByName("GCUService"))
        {
            using (process)
            {
                try
                {
                    if (found is null && process.SessionId == session) found = new(process.Id, StartedUtc(process));
                }
                catch (InvalidOperationException) { /* It has exited. */ }
            }
        }
        return found;
    }

    // Without the start time (access refused), the process ID alone tells processes apart.
    private static DateTime StartedUtc(Process process)
    {
        try { return process.StartTime.ToUniversalTime(); }
        catch (Exception exception) when (exception is Win32Exception or NotSupportedException) { return DateTime.MinValue; }
    }
}
