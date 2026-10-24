using System.IO;
using System.Xml;
using System.Xml.Linq;

namespace RevoHelper.App.Settings;

public enum BootWaterTaskState { NotInstalled, Installed, RepairRequired, ForeignTask, Error }

public sealed record BootWaterTaskStatus(BootWaterTaskState State, string Message, string? LastConnectionResult = null);

public interface IBootWaterTaskManager
{
    Task<BootWaterTaskStatus> ReadAsync(CancellationToken token);
    Task<BootWaterTaskStatus> InstallAsync(CancellationToken token);
    Task<BootWaterTaskStatus> RemoveAsync(CancellationToken token);
}

public interface IBootWaterTaskPlatform
{
    string? ReadTaskXml();
    bool IsInstalledHelperValid();
    string? ReadLastConnectionResult();
    void DeployVerifiedHelper();
    void RegisterTask(string xml);
    void RemoveTask();
}

public sealed class BootWaterTaskManager(IBootWaterTaskPlatform platform, string executablePath) : IBootWaterTaskManager
{
    private readonly SemaphoreSlim gate = new(1, 1);

    public Task<BootWaterTaskStatus> ReadAsync(CancellationToken token) => RunAsync(Read, token);

    public Task<BootWaterTaskStatus> InstallAsync(CancellationToken token) => RunAsync(() =>
    {
        var previous = platform.ReadTaskXml();
        if (previous is not null && !BootWaterTaskDefinition.IsOwned(previous, executablePath)) return Foreign();
        platform.DeployVerifiedHelper();
        try
        {
            platform.RegisterTask(BootWaterTaskDefinition.Create(executablePath));
            var result = Read();
            if (result.State != BootWaterTaskState.Installed) throw new IOException("任务注册后未通过状态回读。");
            return result with { Message = "已安装登录前连接任务；下次开机生效。" };
        }
        catch (Exception failure)
        {
            try
            {
                var current = platform.ReadTaskXml();
                if (current is not null && !BootWaterTaskDefinition.IsOwned(current, executablePath))
                    throw new IOException("同名任务归属已变化，不覆盖该任务。");
                if (previous is not null) platform.RegisterTask(previous);
                else if (current is not null) platform.RemoveTask();
                return new(BootWaterTaskState.Error, $"安装未完成，任务配置已恢复：{failure.Message}");
            }
            catch (Exception restoreFailure)
            {
                return new(BootWaterTaskState.Error, $"安装未完成，恢复也失败：{failure.Message}；{restoreFailure.Message}");
            }
        }
    }, token);

    public Task<BootWaterTaskStatus> RemoveAsync(CancellationToken token) => RunAsync(() =>
    {
        var current = platform.ReadTaskXml();
        if (current is not null && !BootWaterTaskDefinition.IsOwned(current, executablePath)) return Foreign();
        if (current is not null) platform.RemoveTask();
        if (platform.ReadTaskXml() is not null) throw new IOException("移除后仍读到任务，未确认成功。");
        return new(BootWaterTaskState.NotInstalled, "登录前任务已移除；日志及辅助文件保留。", platform.ReadLastConnectionResult());
    }, token);

    private BootWaterTaskStatus Read()
    {
        var xml = platform.ReadTaskXml();
        if (xml is null) return new(BootWaterTaskState.NotInstalled, "尚未安装登录前连接任务。", platform.ReadLastConnectionResult());
        if (!BootWaterTaskDefinition.IsOwned(xml, executablePath)) return Foreign();
        if (!BootWaterTaskDefinition.IsEnabledAndConfigured(xml) || !platform.IsInstalledHelperValid())
            return new(BootWaterTaskState.RepairRequired, "任务已存在，但配置或辅助文件需要修复。", platform.ReadLastConnectionResult());
        return new(BootWaterTaskState.Installed, "登录前连接任务已安装；实际连接结果见下方记录。", platform.ReadLastConnectionResult());
    }

    private static BootWaterTaskStatus Foreign() => new(BootWaterTaskState.ForeignTask,
        "发现同名但归属不符的任务，未修改。请检查 Windows 任务计划程序。");

    private async Task<BootWaterTaskStatus> RunAsync(Func<BootWaterTaskStatus> action, CancellationToken token)
    {
        await gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            return await Task.Run(() =>
            {
                token.ThrowIfCancellationRequested();
                try { return action(); }
                catch (Exception exception) { return new(BootWaterTaskState.Error, $"水冷任务操作失败：{exception.Message}"); }
            }, token).ConfigureAwait(false);
        }
        finally { gate.Release(); }
    }
}

public static class BootWaterTaskDefinition
{
    private static readonly XNamespace Ns = "http://schemas.microsoft.com/windows/2004/02/mit/task";

    public static string Create(string executablePath)
    {
        if (!Path.IsPathFullyQualified(executablePath)) throw new ArgumentException("辅助程序必须为绝对路径。", nameof(executablePath));
        // Inherit the root namespace. Task Scheduler's XML parser rejects a redundant
        // namespace declaration on Exec/Command although general XML parsers accept it.
        var path = new XElement("Command", Path.GetFullPath(executablePath));
        return $"""
            <Task version="1.2" xmlns="{Ns}">
              <RegistrationInfo><Source>RevoHelper.BootWater</Source><Author>RevoHelper</Author></RegistrationInfo>
              <Triggers><BootTrigger><Enabled>true</Enabled><Delay>PT3S</Delay></BootTrigger></Triggers>
              <Principals><Principal id="Author"><UserId>S-1-5-18</UserId><RunLevel>HighestAvailable</RunLevel></Principal></Principals>
              <Settings><MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy><DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries><StopIfGoingOnBatteries>false</StopIfGoingOnBatteries><StartWhenAvailable>true</StartWhenAvailable><Enabled>true</Enabled><ExecutionTimeLimit>PT45S</ExecutionTimeLimit></Settings>
              <Actions Context="Author"><Exec>{path.ToString(SaveOptions.DisableFormatting)}<Arguments>--boot</Arguments></Exec></Actions>
            </Task>
            """;
    }

    public static bool IsOwned(string xml, string executablePath)
    {
        try
        {
            var root = Parse(xml);
            var actions = root.Element(Ns + "Actions")?.Elements().ToArray();
            var principals = root.Element(Ns + "Principals")?.Elements().ToArray();
            var triggers = root.Element(Ns + "Triggers")?.Elements().ToArray();
            if (actions is not { Length: 1 } || actions[0].Name != Ns + "Exec" ||
                principals is not { Length: 1 } || triggers is not { Length: 1 } || triggers[0].Name != Ns + "BootTrigger") return false;
            var user = (string?)principals[0].Element(Ns + "UserId");
            var command = (string?)actions[0].Element(Ns + "Command");
            return user is "S-1-5-18" or "SYSTEM" &&
                (string?)actions[0].Element(Ns + "Arguments") == "--boot" &&
                command is not null && Path.IsPathFullyQualified(command) &&
                string.Equals(Path.GetFullPath(command), Path.GetFullPath(executablePath), StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception exception) when (exception is XmlException or ArgumentException or InvalidOperationException or NotSupportedException) { return false; }
    }

    public static bool IsEnabledAndConfigured(string xml)
    {
        var root = Parse(xml);
        var settings = root.Element(Ns + "Settings");
        return root.Descendants(Ns + "Enabled").All(e => e.Value == "true") &&
            (string?)settings?.Element(Ns + "ExecutionTimeLimit") == "PT45S" &&
            (string?)settings?.Element(Ns + "DisallowStartIfOnBatteries") == "false" &&
            (string?)settings?.Element(Ns + "StopIfGoingOnBatteries") == "false" &&
            (string?)settings?.Element(Ns + "StartWhenAvailable") == "true" &&
            (string?)settings?.Element(Ns + "MultipleInstancesPolicy") == "IgnoreNew" &&
            // SYSTEM uses the API's TASK_LOGON_SERVICE_ACCOUNT (5). "ServiceAccount"
            // is NOT a valid XML LogonType value and must be omitted from task XML.
            !root.Descendants(Ns + "LogonType").Any() &&
            root.Descendants(Ns + "Delay").SingleOrDefault()?.Value == "PT3S" &&
            root.Descendants(Ns + "RunLevel").SingleOrDefault()?.Value == "HighestAvailable";
    }

    private static XElement Parse(string xml)
    {
        using var reader = XmlReader.Create(new StringReader(xml), new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 65536 });
        var root = XElement.Load(reader);
        if (root.Name != Ns + "Task") throw new XmlException("不是有效任务定义。");
        return root;
    }
}
