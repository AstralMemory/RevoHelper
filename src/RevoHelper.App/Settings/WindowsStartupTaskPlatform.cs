using System.Security.Principal;
using Microsoft.Win32;

namespace RevoHelper.App.Settings;

public sealed class WindowsStartupTaskPlatform : IStartupTaskPlatform
{
    public const string TaskName = "RevoHelper Startup";
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "RevoHelper";
    private static readonly string[] StartupApprovedKeyPaths =
    [
        @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run",
        @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run32"
    ];

    public string CurrentUserSid
    {
        get
        {
            using var identity = WindowsIdentity.GetCurrent();
            return identity.User?.Value ?? throw new InvalidOperationException("无法读取当前 Windows 用户 SID。");
        }
    }

    public string? ReadTaskXml() => ScheduledTaskFolder.ReadTaskXml(TaskName);

    public void RegisterTask(string xml)
    {
        RequireAdministrator();
        WithFolder<object?>(folder =>
        {
            object? registered = null;
            try
            {
                registered = folder.RegisterTask(TaskName, xml, 6, CurrentUserSid, null, 3, null);
                return null;
            }
            finally
            {
                Release(registered);
            }
        });
    }

    // TASK_UPDATE only (TASK_CREATE_OR_UPDATE is 6): a task removed in the meantime stays removed.
    public void UpdateTask(string xml)
    {
        RequireAdministrator();
        WithFolder<object?>(folder =>
        {
            object? registered = null;
            try
            {
                registered = folder.RegisterTask(TaskName, xml, 4, CurrentUserSid, null, 3, null);
                return null;
            }
            finally
            {
                Release(registered);
            }
        });
    }

    public void RemoveTask()
    {
        RequireAdministrator();
        ScheduledTaskFolder.DeleteTask(TaskName);
    }

    public string? ReadLegacyRunCommand()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
        return key?.GetValue(ValueName) as string;
    }

    public bool IsLegacyRunDisabled()
    {
        foreach (var path in StartupApprovedKeyPaths)
        {
            using var key = Registry.CurrentUser.OpenSubKey(path, writable: false);
            if (StartupApproval.IsDisabled(key?.GetValue(ValueName))) return true;
        }
        return false;
    }

    public void RemoveLegacyRun()
    {
        using (var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true))
            key?.DeleteValue(ValueName, throwOnMissingValue: false);
        foreach (var path in StartupApprovedKeyPaths)
        {
            using var key = Registry.CurrentUser.OpenSubKey(path, writable: true);
            key?.DeleteValue(ValueName, throwOnMissingValue: false);
        }
    }

    public LegacyStartupState CaptureLegacyState() => new(
        ReadValue(RunKeyPath), ReadValue(StartupApprovedKeyPaths[0]), ReadValue(StartupApprovedKeyPaths[1]));

    public void RestoreLegacyState(LegacyStartupState state)
    {
        if (!state.CanRestoreFrom(CaptureLegacyState()))
            throw new InvalidOperationException("旧启动项已被其他操作修改，未覆盖。");
        WriteValue(RunKeyPath, state.Run);
        WriteValue(StartupApprovedKeyPaths[0], state.ApprovalRun);
        WriteValue(StartupApprovedKeyPaths[1], state.ApprovalRun32);
        if (!state.Matches(CaptureLegacyState()))
            throw new InvalidOperationException("旧启动项恢复后回读不一致。");
    }

    private static StartupRegistryValue? ReadValue(string path)
    {
        using var key = Registry.CurrentUser.OpenSubKey(path, writable: false);
        var value = key?.GetValue(ValueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
        return value is null ? null : new(key!.GetValueKind(ValueName), value);
    }

    private static void WriteValue(string path, StartupRegistryValue? value)
    {
        if (value is null)
        {
            using var existing = Registry.CurrentUser.OpenSubKey(path, writable: true);
            existing?.DeleteValue(ValueName, throwOnMissingValue: false);
            return;
        }
        using var key = Registry.CurrentUser.CreateSubKey(path, writable: true);
        key.SetValue(ValueName, value.Value, value.Kind);
    }

    private static void RequireAdministrator()
    {
        using var identity = WindowsIdentity.GetCurrent();
        if (!new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator))
            throw new UnauthorizedAccessException("请以管理员身份运行 RevoHelper 后重试。");
    }

    private static T WithFolder<T>(Func<dynamic, T> action) => ScheduledTaskFolder.With(action);

    private static void Release(object? value) => ScheduledTaskFolder.Release(value);
}
