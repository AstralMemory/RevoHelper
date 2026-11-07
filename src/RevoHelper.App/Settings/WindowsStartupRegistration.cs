using System.Diagnostics;
using System.IO;
using System.Xml.Linq;

namespace RevoHelper.App.Settings;

public sealed class WindowsStartupRegistration : IStartupRegistration
{
    private readonly string executablePath;
    private readonly IStartupTaskPlatform platform;
    private readonly Func<string, Version?> readFileVersion;
    private readonly object gate = new();
    private readonly Guid ownerId = Guid.NewGuid();
    private Mutation? lastMutation;

    public WindowsStartupRegistration(
        string executablePath,
        IStartupTaskPlatform? platform = null,
        Func<string, Version?>? readFileVersion = null)
    {
        this.executablePath = Path.GetFullPath(executablePath);
        this.platform = platform ?? new WindowsStartupTaskPlatform();
        this.readFileVersion = readFileVersion ?? ReadFileVersion;
    }

    public bool RegisteredExecutableIsNewer()
    {
        var registered = RegisteredExecutablePath();
        if (registered is null) return false;
        var registeredVersion = readFileVersion(registered);
        var currentVersion = readFileVersion(executablePath);
        return registeredVersion is not null && currentVersion is not null && registeredVersion > currentVersion;
    }

    private string? RegisteredExecutablePath()
    {
        if (platform.ReadTaskXml() is { } xml) return StartupTaskDefinition.ReadCommand(xml);
        return platform.ReadLegacyRunCommand() is { Length: > 0 } command
            ? Environment.ExpandEnvironmentVariables(StartupCommand.ExtractExecutablePath(command))
            : null;
    }

    private static Version? ReadFileVersion(string path)
    {
        try
        {
            return File.Exists(path) && Version.TryParse(FileVersionInfo.GetVersionInfo(path).FileVersion, out var version)
                ? version
                : null;
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public bool IsEnabled()
    {
        var xml = platform.ReadTaskXml();
        return xml is not null &&
               StartupTaskDefinition.IsEnabledFor(xml, executablePath, platform.CurrentUserSid);
    }

    public bool HasEnabledRegistration()
    {
        var xml = platform.ReadTaskXml();
        if (xml is not null)
            return StartupTaskDefinition.IsOwnedByUser(xml, platform.CurrentUserSid) &&
                   StartupTaskDefinition.IsOwnedAndEnabled(xml);
        return platform.ReadLegacyRunCommand() is { Length: > 0 } && !platform.IsLegacyRunDisabled();
    }

    public void SetEnabled(bool enabled)
    {
        lock (gate)
        {
            lastMutation = null;
            var before = CaptureWindowsState();
            var mutation = new Mutation(before, enabled);
            lastMutation = mutation;
            try
            {
                mutation.TaskAttempted = true;
                if (enabled)
                    platform.RegisterTask(StartupTaskDefinition.Create(executablePath, before.UserSid));
                else if (before.TaskXml is not null)
                    platform.RemoveTask();

                var observedTask = platform.ReadTaskXml();
                if (enabled ? !TaskEnabled(observedTask, before.UserSid) : observedTask is not null)
                    throw new InvalidOperationException("Windows 未确认 RevoHelper 登录任务状态。");
                mutation.ObservedTaskXml = observedTask;
                mutation.HasTaskReadback = true;
                platform.RemoveLegacyRun();
                if (!LegacyStartupState.Empty.Matches(platform.CaptureLegacyState()))
                    throw new InvalidOperationException("Windows 未确认旧启动项已清理。");
                var finalTask = platform.ReadTaskXml();
                if (!SameTask(finalTask, mutation.ObservedTaskXml))
                    throw new InvalidOperationException("Windows 未确认 RevoHelper 启动项状态。");
            }
            catch (Exception failure)
            {
                try { RestoreWindowsState(before, mutation); }
                catch (Exception recoveryFailure)
                {
                    throw new AggregateException("启动项操作失败，且原状态恢复未完成。", failure, recoveryFailure);
                }
                lastMutation = null;
                throw;
            }
        }
    }

    public StartupRegistrationState CaptureState()
    {
        lock (gate) return CaptureWindowsState();
    }

    public void RestoreState(StartupRegistrationState state)
    {
        lock (gate)
        {
            if (state is not WindowsState before || before.OwnerId != ownerId)
                throw new InvalidOperationException("启动项快照不属于本次控制实例。");
            if (lastMutation is null || !SameSnapshot(before, lastMutation.Before))
            {
                if (SameSnapshot(before, CaptureWindowsState())) return;
                throw new InvalidOperationException("启动项已发生其他变更，未使用过期快照覆盖。");
            }
            RestoreWindowsState(before, lastMutation);
            lastMutation = null;
        }
    }

    private WindowsState CaptureWindowsState()
    {
        var sid = platform.CurrentUserSid;
        var task = platform.ReadTaskXml();
        EnsureOwned(task, sid);
        return new(TaskEnabled(task, sid), ownerId, sid, task, platform.CaptureLegacyState());
    }

    private void RestoreWindowsState(WindowsState before, Mutation mutation)
    {
        if (!string.Equals(platform.CurrentUserSid, before.UserSid, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("当前 Windows 用户已改变，未恢复其他用户的启动项。");
        var currentTask = platform.ReadTaskXml();
        EnsureOwned(currentTask, before.UserSid);
        var taskUnchanged = SameTask(currentTask, before.TaskXml);
        var isOurResult = mutation.HasTaskReadback
            ? SameTask(currentTask, mutation.ObservedTaskXml)
            : mutation.TaskAttempted && (mutation.Enabled
                ? TaskEnabled(currentTask, before.UserSid)
                : currentTask is null);
        if (!taskUnchanged && !isOurResult)
            throw new InvalidOperationException("启动任务已被其他操作修改，未覆盖。");
        var currentLegacy = platform.CaptureLegacyState();
        if (!before.Legacy.CanRestoreFrom(currentLegacy))
            throw new InvalidOperationException("旧启动项已被其他操作修改，未覆盖。");

        if (!taskUnchanged)
        {
            if (before.TaskXml is null) platform.RemoveTask();
            else platform.RegisterTask(before.TaskXml);
        }
        if (!before.Legacy.Matches(currentLegacy)) platform.RestoreLegacyState(before.Legacy);
        if (!SameTask(platform.ReadTaskXml(), before.TaskXml) ||
            !before.Legacy.Matches(platform.CaptureLegacyState()))
            throw new InvalidOperationException("启动项原状态恢复后回读不一致。");
    }

    private bool TaskEnabled(string? task, string sid) => task is not null &&
        StartupTaskDefinition.IsEnabledFor(task, executablePath, sid);

    private static void EnsureOwned(string? task, string sid)
    {
        if (task is not null && !StartupTaskDefinition.IsOwned(task))
            throw new InvalidOperationException("发现同名但不属于 RevoHelper 的启动任务，未覆盖。");
        if (task is not null && !StartupTaskDefinition.IsOwnedByUser(task, sid))
            throw new InvalidOperationException("发现其他 Windows 用户的 RevoHelper 启动任务，未覆盖或删除。");
    }

    private static bool SameSnapshot(WindowsState a, WindowsState b) => a.OwnerId == b.OwnerId &&
        a.UserSid == b.UserSid && SameTask(a.TaskXml, b.TaskXml) && a.Legacy.Matches(b.Legacy);

    private static bool SameTask(string? a, string? b) => a is null || b is null
        ? a is null && b is null
        : XNode.DeepEquals(XElement.Parse(a), XElement.Parse(b));

    private sealed record WindowsState(bool Enabled, Guid OwnerId, string UserSid,
        string? TaskXml, LegacyStartupState Legacy) : StartupRegistrationState(Enabled);

    private sealed class Mutation(WindowsState before, bool enabled)
    {
        public WindowsState Before { get; } = before;
        public bool Enabled { get; } = enabled;
        public bool TaskAttempted { get; set; }
        public bool HasTaskReadback { get; set; }
        public string? ObservedTaskXml { get; set; }
    }
}

public static class StartupApproval
{
    public static bool IsDisabled(object? value) =>
        value is byte[] { Length: > 0 } bytes && bytes[0] == 0x03;
}
