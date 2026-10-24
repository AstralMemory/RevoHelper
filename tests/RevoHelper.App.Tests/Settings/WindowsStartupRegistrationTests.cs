using System.Xml.Linq;
using System.Security.Principal;
using RevoHelper.App.Settings;

namespace RevoHelper.App.Tests.Settings;

public sealed class WindowsStartupRegistrationTests
{
    private const string Executable = @"C:\Apps\RevoHelper\RevoHelper.exe";
    private const string Sid = "S-1-5-21-111-222-333-1001";
    private const string OtherSid = "S-1-5-21-111-222-333-1002";
    private static readonly XNamespace Ns = "http://schemas.microsoft.com/windows/2004/02/mit/task";

    [Fact]
    public void Synchronize_DoesNotReenableUserDisabledTaskWhenExecutableWasRenamed()
    {
        var task = XElement.Parse(StartupTaskDefinition.Create(@"C:\Old\RevoHelper.exe", Sid));
        task.Element(Ns + "Settings")!.Element(Ns + "Enabled")!.Value = "false";
        var original = task.ToString();
        var platform = new FakeStartupTaskPlatform { CurrentUserSid = Sid, TaskXml = original };
        var registration = new WindowsStartupRegistration(Executable, platform);
        Assert.False(StartupRegistrationSynchronizer.Synchronize(registration));
        Assert.Equal(original, platform.TaskXml);
    }

    [Fact]
    public void Synchronize_UpdatesEnabledTaskToCurrentRenamedExecutable()
    {
        var platform = new FakeStartupTaskPlatform
        {
            CurrentUserSid = Sid,
            TaskXml = StartupTaskDefinition.Create(@"C:\Old\RevoHelper.exe", Sid)
        };
        var registration = new WindowsStartupRegistration(Executable, platform);
        Assert.True(StartupRegistrationSynchronizer.Synchronize(registration));
        Assert.True(StartupTaskDefinition.IsEnabledFor(platform.TaskXml!, Executable, Sid));
    }

    [Fact]
    public void Definition_CreatesElevatedCurrentUserLogonTask()
    {
        var xml = StartupTaskDefinition.Create(Executable, Sid);
        var root = XElement.Parse(xml);

        Assert.Equal(Sid, (string?)root.Descendants(Ns + "LogonTrigger").Single().Element(Ns + "UserId"));
        Assert.Equal(Sid, (string?)root.Descendants(Ns + "Principal").Single().Element(Ns + "UserId"));
        Assert.Equal("InteractiveToken", (string?)root.Descendants(Ns + "LogonType").Single());
        Assert.Equal("HighestAvailable", (string?)root.Descendants(Ns + "RunLevel").Single());
        Assert.Equal(Executable, (string?)root.Descendants(Ns + "Command").Single());
        Assert.Equal("--startup", (string?)root.Descendants(Ns + "Arguments").Single());
        Assert.Equal(@"C:\Apps\RevoHelper", (string?)root.Descendants(Ns + "WorkingDirectory").Single());
        Assert.Equal("false", (string?)root.Descendants(Ns + "DisallowStartIfOnBatteries").Single());
        Assert.Equal("true", (string?)root.Descendants(Ns + "StartWhenAvailable").Single());
        var registrationNames = root.Element(Ns + "RegistrationInfo")!.Elements()
            .Select(element => element.Name.LocalName).ToArray();
        Assert.True(Array.IndexOf(registrationNames, "Source") < Array.IndexOf(registrationNames, "Author"));
        Assert.True(StartupTaskDefinition.IsEnabledFor(xml, Executable, Sid));
    }

    [Fact]
    public void IsEnabledFor_AcceptsTaskSchedulerNormalizedTriggerAccountName()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var currentSid = identity.User!.Value;
        var root = XElement.Parse(StartupTaskDefinition.Create(Executable, currentSid));
        root.Descendants(Ns + "LogonTrigger").Single()
            .Element(Ns + "UserId")!.Value = identity.Name;

        Assert.True(StartupTaskDefinition.IsEnabledFor(
            root.ToString(SaveOptions.DisableFormatting), Executable, currentSid));
    }

    [Fact]
    public void IsEnabledFor_AcceptsTaskSchedulerOmittedDefaultEnabledElement()
    {
        var root = XElement.Parse(StartupTaskDefinition.Create(Executable, Sid));
        root.Descendants(Ns + "LogonTrigger").Single().Element(Ns + "Enabled")!.Remove();

        Assert.True(StartupTaskDefinition.IsEnabledFor(
            root.ToString(SaveOptions.DisableFormatting), Executable, Sid));
    }

    [Fact]
    public void IsEnabled_RequiresOwnedTaskAtCurrentExecutablePath()
    {
        var platform = new FakeStartupTaskPlatform
        {
            CurrentUserSid = Sid,
            TaskXml = StartupTaskDefinition.Create(@"C:\Old\RevoHelper.exe", Sid)
        };
        var registration = new WindowsStartupRegistration(Executable, platform);

        Assert.False(registration.IsEnabled());
        Assert.True(registration.HasEnabledRegistration());
    }

    [Fact]
    public void SetEnabled_MigratesApprovedLegacyRunOnlyAfterTaskReadbackSucceeds()
    {
        var platform = new FakeStartupTaskPlatform
        {
            CurrentUserSid = Sid,
            LegacyRunCommand = "\"C:\\Old\\RevoHelper.exe\" --startup"
        };
        var registration = new WindowsStartupRegistration(Executable, platform);

        registration.SetEnabled(true);

        Assert.True(registration.IsEnabled());
        Assert.Null(platform.LegacyRunCommand);
        Assert.Equal(1, platform.RegisterCalls);
    }

    [Fact]
    public void SetEnabled_WhenTaskRegistrationFails_PreservesLegacyRun()
    {
        var platform = new FakeStartupTaskPlatform
        {
            CurrentUserSid = Sid,
            LegacyRunCommand = "\"C:\\Old\\RevoHelper.exe\" --startup",
            RegisterException = new InvalidOperationException("simulated registration failure")
        };
        var registration = new WindowsStartupRegistration(Executable, platform);

        Assert.Throws<InvalidOperationException>(() => registration.SetEnabled(true));

        Assert.NotNull(platform.LegacyRunCommand);
    }

    [Fact]
    public void SetEnabledFalse_RemovesOwnedTaskAndLegacyRun()
    {
        var platform = new FakeStartupTaskPlatform
        {
            CurrentUserSid = Sid,
            TaskXml = StartupTaskDefinition.Create(Executable, Sid),
            LegacyRunCommand = StartupCommand.Build(Executable)
        };
        var registration = new WindowsStartupRegistration(Executable, platform);

        registration.SetEnabled(false);

        Assert.Null(platform.TaskXml);
        Assert.Null(platform.LegacyRunCommand);
    }

    [Fact]
    public void SetEnabled_DoesNotReplaceForeignSameNameTask()
    {
        var platform = new FakeStartupTaskPlatform
        {
            CurrentUserSid = Sid,
            TaskXml = "<Task xmlns=\"http://schemas.microsoft.com/windows/2004/02/mit/task\"><RegistrationInfo><Source>Other</Source></RegistrationInfo></Task>"
        };
        var registration = new WindowsStartupRegistration(Executable, platform);

        Assert.Throws<InvalidOperationException>(() => registration.SetEnabled(true));
        Assert.Equal(0, platform.RegisterCalls);
    }

    [Fact]
    public void HasEnabledRegistration_DoesNotClaimAnotherWindowsUsersTask()
    {
        var platform = new FakeStartupTaskPlatform
        {
            CurrentUserSid = Sid,
            TaskXml = StartupTaskDefinition.Create(Executable, OtherSid)
        };
        var registration = new WindowsStartupRegistration(Executable, platform);

        Assert.False(registration.IsEnabled());
        Assert.False(registration.HasEnabledRegistration());
    }

    [Fact]
    public void Synchronize_DoesNotTakeOverAnotherWindowsUsersTask()
    {
        var original = StartupTaskDefinition.Create(@"C:\OtherUser\RevoHelper.exe", OtherSid);
        var platform = new FakeStartupTaskPlatform { CurrentUserSid = Sid, TaskXml = original };
        var registration = new WindowsStartupRegistration(Executable, platform);

        Assert.False(StartupRegistrationSynchronizer.Synchronize(registration));

        Assert.Equal(original, platform.TaskXml);
        Assert.Equal(0, platform.RegisterCalls);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void SetEnabled_DoesNotReplaceOrRemoveAnotherWindowsUsersTask(bool enabled)
    {
        var original = StartupTaskDefinition.Create(@"C:\OtherUser\RevoHelper.exe", OtherSid);
        var legacy = StartupCommand.Build(Executable);
        var platform = new FakeStartupTaskPlatform
        {
            CurrentUserSid = Sid,
            TaskXml = original,
            LegacyRunCommand = legacy
        };
        var registration = new WindowsStartupRegistration(Executable, platform);

        Assert.Throws<InvalidOperationException>(() => registration.SetEnabled(enabled));

        Assert.Equal(original, platform.TaskXml);
        Assert.Equal(legacy, platform.LegacyRunCommand);
        Assert.Equal(0, platform.RegisterCalls);
    }

    [Fact]
    public void SetEnabled_DoesNotTakeOverTaskWithForeignTriggerAndCurrentPrincipal()
    {
        var task = XElement.Parse(StartupTaskDefinition.Create(Executable, Sid));
        task.Descendants(Ns + "LogonTrigger").Single().Element(Ns + "UserId")!.Value = OtherSid;
        var original = task.ToString();
        var platform = new FakeStartupTaskPlatform { CurrentUserSid = Sid, TaskXml = original };
        var registration = new WindowsStartupRegistration(Executable, platform);

        Assert.False(registration.HasEnabledRegistration());
        Assert.Throws<InvalidOperationException>(() => registration.SetEnabled(true));
        Assert.Equal(original, platform.TaskXml);
    }

    private sealed class FakeStartupTaskPlatform : IStartupTaskPlatform
    {
        public required string CurrentUserSid { get; init; }
        public string? TaskXml { get; set; }
        public string? LegacyRunCommand { get; set; }
        public bool LegacyRunDisabled { get; init; }
        public Exception? RegisterException { get; init; }
        public int RegisterCalls { get; private set; }

        public string? ReadTaskXml() => TaskXml;
        public void RegisterTask(string xml)
        {
            RegisterCalls++;
            if (RegisterException is not null) throw RegisterException;
            TaskXml = xml;
        }
        public void RemoveTask() => TaskXml = null;
        public string? ReadLegacyRunCommand() => LegacyRunCommand;
        public bool IsLegacyRunDisabled() => LegacyRunDisabled;
        public void RemoveLegacyRun() => LegacyRunCommand = null;
    }
}
