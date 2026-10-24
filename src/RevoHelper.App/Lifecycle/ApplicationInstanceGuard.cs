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
        if (processSnapshot().Any(process =>
                process.Id != currentProcessId &&
                IsRevoHelperApplication(process.Name)))
        {
            return false;
        }

        mutex = new Mutex(initiallyOwned: true, mutexName, out var createdNew);
        if (!createdNew)
        {
            mutex.Dispose();
            mutex = null;
        }
        return createdNew;
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

    private static IReadOnlyList<ProcessIdentity> SnapshotProcesses() =>
        Process.GetProcesses()
            .Select(process => new ProcessIdentity(process.Id, process.ProcessName))
            .ToArray();

    private static bool IsRevoHelperApplication(string processName) =>
        processName.Equals("RevoHelper", StringComparison.OrdinalIgnoreCase) ||
        processName.StartsWith("RevoHelper-Preview", StringComparison.OrdinalIgnoreCase);
}
