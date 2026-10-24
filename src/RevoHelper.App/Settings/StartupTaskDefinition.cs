using System.IO;
using System.Xml;
using System.Xml.Linq;
using System.Security.Principal;

namespace RevoHelper.App.Settings;

public static class StartupTaskDefinition
{
    private static readonly XNamespace Ns = "http://schemas.microsoft.com/windows/2004/02/mit/task";

    public static string Create(string executablePath, string userSid)
    {
        if (!Path.IsPathFullyQualified(executablePath))
            throw new ArgumentException("启动程序必须为绝对路径。", nameof(executablePath));
        if (string.IsNullOrWhiteSpace(userSid))
            throw new ArgumentException("当前 Windows 用户 SID 不可用。", nameof(userSid));

        var executable = Path.GetFullPath(executablePath);
        var document = new XDocument(
            new XElement(Ns + "Task",
                new XAttribute("version", "1.2"),
                new XElement(Ns + "RegistrationInfo",
                    new XElement(Ns + "Source", "RevoHelper.Startup"),
                    new XElement(Ns + "Author", "RevoHelper")),
                new XElement(Ns + "Triggers",
                    new XElement(Ns + "LogonTrigger",
                        new XElement(Ns + "Enabled", "true"),
                        new XElement(Ns + "UserId", userSid),
                        new XElement(Ns + "Delay", "PT3S"))),
                new XElement(Ns + "Principals",
                    new XElement(Ns + "Principal",
                        new XAttribute("id", "Author"),
                        new XElement(Ns + "UserId", userSid),
                        new XElement(Ns + "LogonType", "InteractiveToken"),
                        new XElement(Ns + "RunLevel", "HighestAvailable"))),
                new XElement(Ns + "Settings",
                    new XElement(Ns + "MultipleInstancesPolicy", "IgnoreNew"),
                    new XElement(Ns + "DisallowStartIfOnBatteries", "false"),
                    new XElement(Ns + "StopIfGoingOnBatteries", "false"),
                    new XElement(Ns + "StartWhenAvailable", "true"),
                    new XElement(Ns + "Enabled", "true"),
                    new XElement(Ns + "ExecutionTimeLimit", "PT0S")),
                new XElement(Ns + "Actions",
                    new XAttribute("Context", "Author"),
                    new XElement(Ns + "Exec",
                        new XElement(Ns + "Command", executable),
                        new XElement(Ns + "Arguments", StartupCommand.StartupArgument),
                        new XElement(Ns + "WorkingDirectory", Path.GetDirectoryName(executable))))));
        return document.ToString(SaveOptions.DisableFormatting);
    }

    public static bool IsOwned(string xml)
    {
        try
        {
            var root = Parse(xml);
            var registration = root.Element(Ns + "RegistrationInfo");
            var triggers = root.Element(Ns + "Triggers")?.Elements().ToArray();
            var principals = root.Element(Ns + "Principals")?.Elements().ToArray();
            var actions = root.Element(Ns + "Actions")?.Elements().ToArray();
            return (string?)registration?.Element(Ns + "Author") == "RevoHelper" &&
                   (string?)registration?.Element(Ns + "Source") == "RevoHelper.Startup" &&
                   triggers is { Length: 1 } && triggers[0].Name == Ns + "LogonTrigger" &&
                   principals is { Length: 1 } && principals[0].Name == Ns + "Principal" &&
                   actions is { Length: 1 } && actions[0].Name == Ns + "Exec" &&
                   (string?)actions[0].Element(Ns + "Arguments") == StartupCommand.StartupArgument;
        }
        catch (Exception exception) when (exception is XmlException or InvalidOperationException)
        {
            return false;
        }
    }

    public static bool IsOwnedAndEnabled(string xml)
    {
        if (!IsOwned(xml)) return false;
        var root = Parse(xml);
        return root.Descendants(Ns + "Enabled").All(value => value.Value == "true");
    }

    /// <summary>Application branding alone is not permission to migrate or remove another user's task.</summary>
    public static bool IsOwnedByUser(string xml, string userSid)
    {
        if (string.IsNullOrWhiteSpace(userSid) || !IsOwned(xml)) return false;
        try
        {
            var root = Parse(xml);
            var trigger = root.Descendants(Ns + "LogonTrigger").Single();
            var principal = root.Descendants(Ns + "Principal").Single();
            return RepresentsUser((string?)trigger.Element(Ns + "UserId"), userSid) &&
                   RepresentsUser((string?)principal.Element(Ns + "UserId"), userSid);
        }
        catch (Exception exception) when (exception is XmlException or ArgumentException or InvalidOperationException or NotSupportedException)
        {
            return false;
        }
    }

    public static bool IsEnabledFor(string xml, string executablePath, string userSid)
    {
        try
        {
            if (!IsOwnedByUser(xml, userSid) || !Path.IsPathFullyQualified(executablePath)) return false;
            var root = Parse(xml);
            var trigger = root.Descendants(Ns + "LogonTrigger").Single();
            var principal = root.Descendants(Ns + "Principal").Single();
            var action = root.Descendants(Ns + "Exec").Single();
            var command = (string?)action.Element(Ns + "Command");
            var workingDirectory = (string?)action.Element(Ns + "WorkingDirectory");
            var executable = Path.GetFullPath(executablePath);
            return RepresentsUser((string?)trigger.Element(Ns + "UserId"), userSid) &&
                   (string?)trigger.Element(Ns + "Enabled") is not "false" &&
                   RepresentsUser((string?)principal.Element(Ns + "UserId"), userSid) &&
                   (string?)principal.Element(Ns + "LogonType") == "InteractiveToken" &&
                   (string?)principal.Element(Ns + "RunLevel") == "HighestAvailable" &&
                   command is not null && Path.IsPathFullyQualified(command) &&
                   string.Equals(Path.GetFullPath(command), executable, StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(workingDirectory, Path.GetDirectoryName(executable), StringComparison.OrdinalIgnoreCase) &&
                   root.Descendants(Ns + "Enabled").All(value => value.Value == "true") &&
                   (string?)root.Descendants(Ns + "DisallowStartIfOnBatteries").Single() == "false" &&
                   (string?)root.Descendants(Ns + "StopIfGoingOnBatteries").Single() == "false" &&
                   (string?)root.Descendants(Ns + "StartWhenAvailable").Single() == "true" &&
                   (string?)root.Descendants(Ns + "MultipleInstancesPolicy").Single() == "IgnoreNew";
        }
        catch (Exception exception) when (exception is XmlException or ArgumentException or InvalidOperationException or NotSupportedException)
        {
            return false;
        }
    }

    private static bool RepresentsUser(string? taskUser, string userSid)
    {
        if (string.Equals(taskUser, userSid, StringComparison.OrdinalIgnoreCase)) return true;
        if (string.IsNullOrWhiteSpace(taskUser)) return false;
        // A different explicit SID cannot translate into this user; avoid unnecessary account/domain lookups.
        if (taskUser.StartsWith("S-1-", StringComparison.OrdinalIgnoreCase)) return false;
        try
        {
            return string.Equals(
                new NTAccount(taskUser).Translate(typeof(SecurityIdentifier)).Value,
                userSid,
                StringComparison.OrdinalIgnoreCase);
        }
        catch (IdentityNotMappedException)
        {
            return false;
        }
    }

    private static XElement Parse(string xml)
    {
        using var reader = XmlReader.Create(new StringReader(xml), new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersInDocument = 65536
        });
        var root = XElement.Load(reader);
        if (root.Name != Ns + "Task") throw new XmlException("不是有效的 RevoHelper 启动任务。");
        return root;
    }
}
