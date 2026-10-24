using System.IO;

namespace RevoHelper.App.Settings;

public sealed class WindowsStartupRegistration : IStartupRegistration
{
    private readonly string executablePath;
    private readonly IStartupTaskPlatform platform;

    public WindowsStartupRegistration(string executablePath, IStartupTaskPlatform? platform = null)
    {
        this.executablePath = Path.GetFullPath(executablePath);
        this.platform = platform ?? new WindowsStartupTaskPlatform();
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
        var current = platform.ReadTaskXml();
        if (current is not null && !StartupTaskDefinition.IsOwned(current))
            throw new InvalidOperationException("发现同名但不属于 RevoHelper 的启动任务，未覆盖。");
        if (current is not null && !StartupTaskDefinition.IsOwnedByUser(current, platform.CurrentUserSid))
            throw new InvalidOperationException("发现其他 Windows 用户的 RevoHelper 启动任务，未覆盖或删除。");

        if (enabled)
        {
            platform.RegisterTask(StartupTaskDefinition.Create(executablePath, platform.CurrentUserSid));
            if (!IsEnabled()) throw new InvalidOperationException("Windows 未确认 RevoHelper 登录任务状态。");
            platform.RemoveLegacyRun();
        }
        else
        {
            if (current is not null) platform.RemoveTask();
            platform.RemoveLegacyRun();
        }

        if (IsEnabled() != enabled)
            throw new InvalidOperationException("Windows 未确认 RevoHelper 启动项状态。");
    }
}

public static class StartupApproval
{
    public static bool IsDisabled(object? value) =>
        value is byte[] { Length: > 0 } bytes && bytes[0] == 0x03;
}
