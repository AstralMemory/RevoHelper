using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace RevoHelper.App.Lifecycle;

public sealed class WindowsOfficialFrontendProcesses(IOfficialFrontendWindowsApi windows) : IOfficialFrontendProcesses
{
    public WindowsOfficialFrontendProcesses() : this(new OfficialFrontendWindowsApi()) { }

    public OfficialFrontendSnapshot Find()
    {
        var targets = new List<OfficialFrontendProcess>();
        var unknown = false;
        var legacyRunning = false;
        var session = windows.SessionOf(Environment.ProcessId);
        foreach (var (id, name, sessionId) in windows.Snapshot())
        {
            if (sessionId != session) continue;
            if (NameIs(name, "ControlCenterU") || NameIs(name, "GamingCenterU"))
            {
                legacyRunning = true;
                continue;
            }
            if (!IsFrontendName(name)) continue;
            try
            {
                using var handle = windows.Open(id, forExit: false);
                if (handle is not null && windows.SessionOf(id) == session && MatchesInstalledImage(handle, name))
                    targets.Add(new(id, name));
            }
            catch (Win32Exception) { unknown = true; }
        }
        return new(targets, unknown, legacyRunning);
    }

    public OfficialFrontendCloseOutcome Close(OfficialFrontendProcess process, Func<bool> mayClose)
    {
        if (!IsFrontendName(process.Name)) return OfficialFrontendCloseOutcome.Gone;
        try
        {
            // Hold one kernel object from validation through termination and the exit wait.
            using var handle = windows.Open(process.Id, forExit: true);
            if (handle is null || windows.SessionOf(process.Id) != windows.SessionOf(Environment.ProcessId) ||
                !MatchesInstalledImage(handle, process.Name))
                return OfficialFrontendCloseOutcome.Gone;
            return mayClose() ? windows.TerminateAndWait(handle) : OfficialFrontendCloseOutcome.Skipped;
        }
        catch (Win32Exception) { return OfficialFrontendCloseOutcome.Failed; }
    }

    private static bool NameIs(string? value, string expected) =>
        string.Equals(value, expected, StringComparison.OrdinalIgnoreCase);

    private static bool IsFrontendName(string name) => NameIs(name, "CCUWinUI") || NameIs(name, "SystrayComponent");

    private bool MatchesInstalledImage(SafeProcessHandle handle, string name)
    {
        var image = windows.ImagePath(handle);
        if (!NameIs(Path.GetFileName(image), name + ".exe")) return false;
        var root = Path.GetDirectoryName(image);
        if (root is null) return false;
        if (NameIs(name, "SystrayComponent"))
        {
            if (!NameIs(Path.GetFileName(root), "Win32")) return false;
            root = Path.GetDirectoryName(root);
            if (root is null) return false;
        }
        var fullName = Path.GetFileName(root);
        // The registered CCU.WinUI_wrbgcf7aesyd8 family identifies the official publisher.
        if (!fullName.StartsWith("CCU.WinUI_", StringComparison.OrdinalIgnoreCase) ||
            !fullName.EndsWith("_wrbgcf7aesyd8", StringComparison.OrdinalIgnoreCase)) return false;
        // The tray can be launched by the service without a package identity of its own.
        var installed = windows.InstalledPackagePath(fullName);
        return installed is not null && NameIs(root, Path.TrimEndingDirectorySeparator(installed));
    }
}

public interface IOfficialFrontendWindowsApi
{
    IEnumerable<(int Id, string Name, int SessionId)> Snapshot();
    int SessionOf(int id);
    SafeProcessHandle? Open(int id, bool forExit);
    string ImagePath(SafeProcessHandle handle);
    string? InstalledPackagePath(string fullName);
    OfficialFrontendCloseOutcome TerminateAndWait(SafeProcessHandle handle);
}

internal sealed class OfficialFrontendWindowsApi : IOfficialFrontendWindowsApi
{
    private const uint QueryLimitedInformation = 0x1000, Terminate = 0x0001, Synchronize = 0x00100000;
    private const int InsufficientBuffer = 122;

    public IEnumerable<(int Id, string Name, int SessionId)> Snapshot()
    {
        foreach (var process in Process.GetProcesses())
        {
            using (process) yield return (process.Id, process.ProcessName, process.SessionId);
        }
    }

    public int SessionOf(int id) => ProcessIdToSessionId(id, out var session)
        ? session : throw new Win32Exception(Marshal.GetLastPInvokeError());

    public SafeProcessHandle? Open(int id, bool forExit)
    {
        var handle = OpenProcess(QueryLimitedInformation | (forExit ? Terminate | Synchronize : 0), false, id);
        if (!handle.IsInvalid) return handle;
        var error = Marshal.GetLastPInvokeError();
        handle.Dispose();
        if (error == 87) return null; // ERROR_INVALID_PARAMETER: this PID no longer exists.
        throw new Win32Exception(error);
    }

    public string ImagePath(SafeProcessHandle handle)
    {
        var buffer = new char[512];
        var length = buffer.Length;
        if (QueryFullProcessImageName(handle, 0, buffer, ref length)) return new string(buffer, 0, length);
        var error = Marshal.GetLastPInvokeError();
        if (error != InsufficientBuffer) throw new Win32Exception(error);
        buffer = new char[32768];
        length = buffer.Length;
        return QueryFullProcessImageName(handle, 0, buffer, ref length)
            ? new string(buffer, 0, length) : throw new Win32Exception(Marshal.GetLastPInvokeError());
    }

    public string? InstalledPackagePath(string fullName)
    {
        uint length = 0;
        var error = GetPackagePathByFullName(fullName, ref length, null);
        if (error == InsufficientBuffer)
        {
            var buffer = new char[length];
            error = GetPackagePathByFullName(fullName, ref length, buffer);
            if (error == 0) return new string(buffer, 0, checked((int)length) - 1);
        }
        // A copied or unpacked folder can have the shape of a package without a registered package.
        if (error is 2 or 3 or 87 or 123 or 1168 or 15601 or 15700) return null;
        throw new Win32Exception(error);
    }

    public OfficialFrontendCloseOutcome TerminateAndWait(SafeProcessHandle handle)
    {
        if (!TerminateProcess(handle, 1))
            return WaitForSingleObject(handle, 0) == 0 ? OfficialFrontendCloseOutcome.Gone : OfficialFrontendCloseOutcome.Failed;
        return WaitForSingleObject(handle, 3000) == 0 ? OfficialFrontendCloseOutcome.Closed : OfficialFrontendCloseOutcome.Failed;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool ProcessIdToSessionId(int processId, out int sessionId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern SafeProcessHandle OpenProcess(uint access, bool inheritHandle, int processId);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool QueryFullProcessImageName(SafeProcessHandle process, int flags, [Out] char[] name, ref int size);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int GetPackagePathByFullName(string packageFullName, ref uint pathLength, [Out] char[]? path);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool TerminateProcess(SafeProcessHandle process, uint exitCode);

    [DllImport("kernel32.dll")]
    private static extern uint WaitForSingleObject(SafeProcessHandle handle, uint milliseconds);
}
