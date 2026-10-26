using System.Diagnostics;

namespace RevoHelper.App.Lifecycle;

public sealed record ProcessIdentity(int Id, string Name);

public sealed class ApplicationInstanceGuard : IDisposable
{
    private readonly string mutexName;
    private readonly int currentProcessId;
    private readonly Func<IReadOnlyList<ProcessIdentity>> processSnapshot;
    private Mutex? mutex;

    public ApplicationInstanceGuard()
        : this(
            "Local\\RevoHelper.SingleInstance",
            Environment.ProcessId,
            SnapshotProcesses)
    {
    }

    public ApplicationInstanceGuard(
        string mutexName,
        int currentProcessId,
        Func<IReadOnlyList<ProcessIdentity>> processSnapshot)
    {
        this.mutexName = mutexName;
        this.currentProcessId = currentProcessId;
        this.processSnapshot = processSnapshot;
    }

    public bool TryAcquire()
    {
        if (mutex is not null) return true;
        // Elect one current instance before consulting legacy process names. Two
        // concurrent launches must not reject each other merely for both existing.
        mutex = new Mutex(initiallyOwned: true, mutexName, out var createdNew);
        if (!createdNew)
        {
            mutex.Dispose();
            mutex = null;
            return false;
        }
        try
        {
            // Current releases share this mutex. Historical preview executables
            // predate it, so retain their explicit compatibility protection only.
            if (processSnapshot().Any(process => process.Id != currentProcessId &&
                    process.Name.StartsWith("RevoHelper-Preview", StringComparison.OrdinalIgnoreCase)))
            {
                Dispose();
                return false;
            }
            return true;
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        if (mutex is null) return;
        try
        {
            mutex.ReleaseMutex();
        }
        catch (ApplicationException)
        {
        }
        mutex.Dispose();
        mutex = null;
    }

    private static IReadOnlyList<ProcessIdentity> SnapshotProcesses()
    {
        var result = new List<ProcessIdentity>();
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                try { result.Add(new(process.Id, process.ProcessName)); }
                catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
                { /* A process can exit while the legacy preview check is in progress. */ }
            }
        }
        return result;
    }
}
