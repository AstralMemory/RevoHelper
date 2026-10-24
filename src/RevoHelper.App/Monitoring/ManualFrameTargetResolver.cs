using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using RevoHelper.App.Settings;
using RevoHelper.Core.Monitoring;

namespace RevoHelper.App.Monitoring;

public interface IManualFrameTargetResolver
{
    ProcessTarget? FindRunning(string executablePath);
}

public sealed record ManualExecutableProcess(int ProcessId, DateTimeOffset StartedAtUtc, string ExecutablePath);

public interface IManualExecutableProcessProbe
{
    IReadOnlyList<ManualExecutableProcess> FindByExecutableName(string processName);
}

/// <summary>Only inspects instances of the explicitly selected EXE, never enumerates window titles.</summary>
public sealed class WindowsManualFrameTargetResolver : IManualFrameTargetResolver
{
    private readonly IManualExecutableProcessProbe probe;

    public WindowsManualFrameTargetResolver(IManualExecutableProcessProbe? probe = null)
    {
        this.probe = probe ?? new WindowsExecutableProcessProbe();
    }

    public ProcessTarget? FindRunning(string executablePath)
    {
        var requestedPath = MonitorProgramStore.NormalizeExecutablePath(executablePath);
        if (requestedPath is null) return null;
        var name = Path.GetFileNameWithoutExtension(requestedPath);
        ManualExecutableProcess? newest = null;
        foreach (var candidate in probe.FindByExecutableName(name))
        {
            if (candidate.ProcessId <= 0 || candidate.StartedAtUtc == default ||
                MonitorProgramStore.NormalizeExecutablePath(candidate.ExecutablePath) is not { } candidatePath ||
                !string.Equals(candidatePath, requestedPath, StringComparison.OrdinalIgnoreCase)) continue;
            if (newest is null || candidate.StartedAtUtc > newest.StartedAtUtc ||
                (candidate.StartedAtUtc == newest.StartedAtUtc && candidate.ProcessId > newest.ProcessId)) newest = candidate;
        }
        return newest is null ? null : new ProcessTarget(newest.ProcessId, newest.StartedAtUtc.ToUniversalTime(), name, Path.GetFileName(requestedPath));
    }

    private sealed class WindowsExecutableProcessProbe : IManualExecutableProcessProbe
    {
        public IReadOnlyList<ManualExecutableProcess> FindByExecutableName(string processName)
        {
            // Inspect matching instances of the validated, user-added EXE only;
            // no unrelated window titles are acquired or stored.
            var processes = Process.GetProcessesByName(processName);
            var result = new List<ManualExecutableProcess>(processes.Length);
            foreach (var process in processes)
            {
                using (process)
                {
                    try
                    {
                        if (process.HasExited || process.MainModule?.FileName is not { } path) continue;
                        result.Add(new(process.Id, new DateTimeOffset(process.StartTime.ToUniversalTime()), path));
                    }
                    catch (Exception exception) when (exception is Win32Exception or InvalidOperationException or NotSupportedException)
                    {
                        // A protected or exited instance is unavailable, not a reason to follow a different app.
                    }
                }
            }
            return result;
        }
    }
}
