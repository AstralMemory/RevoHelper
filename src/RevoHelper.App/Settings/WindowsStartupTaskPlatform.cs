using System.Runtime.InteropServices;
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

    public string? ReadTaskXml() => WithFolder<string?>(folder =>
    {
        object? task = null;
        try
        {
            task = folder.GetTask(TaskName);
            return (string)((dynamic)task).Xml;
        }
        catch (COMException exception) when (exception.HResult == unchecked((int)0x80070002))
        {
            return null;
        }
        finally
        {
            Release(task);
        }
    });

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

    public void RemoveTask()
    {
        RequireAdministrator();
        WithFolder<object?>(folder =>
        {
            folder.DeleteTask(TaskName, 0);
            return null;
        });
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

    private static T WithFolder<T>(Func<dynamic, T> action)
    {
        object? service = null;
        object? folder = null;
        try
        {
            service = Activator.CreateInstance(Type.GetTypeFromProgID("Schedule.Service", true)!);
            ((dynamic)service!).Connect();
            folder = ((dynamic)service).GetFolder("\\");
            return action(folder);
        }
        finally
        {
            Release(folder);
            Release(service);
        }
    }

    private static void Release(object? value)
    {
        if (value is not null && Marshal.IsComObject(value)) Marshal.FinalReleaseComObject(value);
    }
}
