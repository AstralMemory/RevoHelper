using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using RevoHelper.Core.Monitoring;

namespace RevoHelper.Hardware.Windows.Monitoring;

public sealed class WindowsProcessTargetProvider : IProcessTargetProvider
{
    private static readonly HashSet<string> ShellProcesses = new(StringComparer.OrdinalIgnoreCase)
    {
        "explorer", "dwm", "ShellExperienceHost", "StartMenuExperienceHost", "SearchHost", "LockApp"
    };

    public ProcessTarget? GetForegroundTarget()
    {
        var window = GetForegroundWindow();
        if (window == IntPtr.Zero) return null;
        GetWindowThreadProcessId(window, out var processId);
        // Foreground monitoring needs process identity, not document/chat titles.
        var target = ReadTarget((int)processId, string.Empty);
        return target is not null && !ShellProcesses.Contains(target.ProcessName) ? target : null;
    }

    public bool IsAlive(ProcessTarget target)
    {
        try
        {
            using var process = Process.GetProcessById(target.ProcessId);
            return !process.HasExited && new DateTimeOffset(process.StartTime.ToUniversalTime()) == target.StartedAtUtc;
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }

    public IReadOnlyList<ProcessTarget> GetWindowTargets()
    {
        var result = new Dictionary<int, ProcessTarget>();
        EnumWindows((window, _) =>
        {
            if (!IsWindowVisible(window)) return true;
            GetWindowThreadProcessId(window, out var processId);
            if (processId == 0 || processId == Environment.ProcessId || result.ContainsKey((int)processId)) return true;
            var title = ReadTitle(window);
            if (string.IsNullOrWhiteSpace(title)) return true;
            var target = ReadTarget((int)processId, title);
            if (target is not null && !ShellProcesses.Contains(target.ProcessName)) result.Add(target.ProcessId, target);
            return true;
        }, IntPtr.Zero);
        return result.Values.OrderBy(target => target.DisplayName, StringComparer.CurrentCultureIgnoreCase).ToArray();
    }

    private static ProcessTarget? ReadTarget(int processId, string title)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            return process.HasExited ? null : new ProcessTarget(
                process.Id,
                new DateTimeOffset(process.StartTime.ToUniversalTime()),
                process.ProcessName,
                string.IsNullOrWhiteSpace(title) ? process.ProcessName : title);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return null;
        }
    }

    private static string ReadTitle(IntPtr window)
    {
        var buffer = new StringBuilder(256);
        GetWindowText(window, buffer, buffer.Capacity);
        return buffer.ToString();
    }

    private delegate bool EnumWindowsCallback(IntPtr window, IntPtr parameter);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool EnumWindows(EnumWindowsCallback callback, IntPtr parameter);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr window, StringBuilder text, int maximumCount);
}
