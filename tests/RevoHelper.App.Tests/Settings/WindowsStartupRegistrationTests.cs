using System.Xml.Linq;
using System.Security.Principal;
using Microsoft.Win32;
using RevoHelper.App.Settings;

namespace RevoHelper.App.Tests.Settings;

public sealed class WindowsStartupRegistrationTests
{
    private const string Executable = @"C:\Apps\RevoHelper\RevoHelper.exe";
    private const string Sid = "S-1-5-21-111-222-333-1001";
    private const string OtherSid = "S-1-5-21-111-222-333-1002";
    private static readonly XNamespace Ns = "http://schemas.microsoft.com/windows/2004/02/mit/task";

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void CleanupFailsAfterTaskMutation_RestoresTaskAndExactLegacyValues(bool enable)
    {
        var old = StartupTaskDefinition.Create(@"C:\Old\RevoHelper.exe", Sid);
        var legacy = LegacyWithApproval();
        var platform = new FakeStartupTaskPlatform { CurrentUserSid = Sid, TaskXml = old,
            LegacyState = legacy, FailCleanupOnceAfterMutation = true };
        var registration = new WindowsStartupRegistration(Executable, platform);
        Assert.ThrowsAny<Exception>(() => registration.SetEnabled(enable));
        Assert.Equal(old, platform.TaskXml);
        Assert.True(legacy.Matches(platform.CaptureLegacyState()));
    }

    [Fact]
    public void RegistrationThrowsAfterWritingTask_RestoresPreviousTask()
    {
        var old = StartupTaskDefinition.Create(@"C:\Old\RevoHelper.exe", Sid);
        var platform = new FakeStartupTaskPlatform { CurrentUserSid = Sid, TaskXml = old,
            FailRegisterOnceAfterMutation = true };
        var registration = new WindowsStartupRegistration(Executable, platform);
        Assert.ThrowsAny<Exception>(() => registration.SetEnabled(true));
        Assert.Equal(old, platform.TaskXml);
    }

    [Fact]
    public void CapturedState_RestoresDisabledTaskOldPathAndApprovalBytesAfterSuccessfulEnable()
    {
        var task = XElement.Parse(StartupTaskDefinition.Create(@"C:\Old\RevoHelper.exe", Sid));
        task.Element(Ns + "Settings")!.Element(Ns + "Enabled")!.Value = "false";
        var old = task.ToString();
        var legacy = LegacyWithApproval();
        var platform = new FakeStartupTaskPlatform { CurrentUserSid = Sid, TaskXml = old, LegacyState = legacy };
        IStartupRegistration registration = new WindowsStartupRegistration(Executable, platform);
        var state = registration.CaptureState();
        registration.SetEnabled(true);
        Assert.True(registration.IsEnabled());
        registration.RestoreState(state);
        Assert.Equal(old, platform.TaskXml);
        Assert.True(legacy.Matches(platform.CaptureLegacyState()));
        Assert.False(registration.IsEnabled());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RestoreState_DoesNotOverwriteExternalTaskChange(bool differentUser)
    {
        var platform = new FakeStartupTaskPlatform { CurrentUserSid = Sid };
        IStartupRegistration registration = new WindowsStartupRegistration(Executable, platform);
        var state = registration.CaptureState();
        registration.SetEnabled(true);
        var external = StartupTaskDefinition.Create(@"C:\External\RevoHelper.exe", differentUser ? OtherSid : Sid);
        platform.TaskXml = external;
        Assert.ThrowsAny<Exception>(() => registration.RestoreState(state));
        Assert.Equal(external, platform.TaskXml);
    }

    [Fact]
    public void CleanupAndRollbackFail_ReportsBothFailures()
    {
        var platform = new FakeStartupTaskPlatform { CurrentUserSid = Sid,
            LegacyState = LegacyWithApproval(), FailCleanupOnceAfterMutation = true,
            RestoreLegacyException = new IOException("restore failure") };
        var registration = new WindowsStartupRegistration(Executable, platform);
        var exception = Assert.ThrowsAny<Exception>(() => registration.SetEnabled(true));
        Assert.Contains("cleanup failure", exception.ToString());
        Assert.Contains("restore failure", exception.ToString());
    }

    [Fact]
    public void RestoreState_DoesNotOverwriteExternalLegacyChange()
    {
        var platform = new FakeStartupTaskPlatform { CurrentUserSid = Sid, LegacyState = LegacyWithApproval() };
        IStartupRegistration registration = new WindowsStartupRegistration(Executable, platform);
        var state = registration.CaptureState();
        registration.SetEnabled(true);
        platform.LegacyRunCommand = "external.exe";
        var currentTask = platform.TaskXml;
        Assert.ThrowsAny<Exception>(() => registration.RestoreState(state));
        Assert.Equal("external.exe", platform.LegacyRunCommand);
        Assert.Equal(currentTask, platform.TaskXml);
    }

    [Fact]
    public void Disable_UnremovedTaskAtOldPath_IsNotReportedAsSuccess()
    {
        var old = StartupTaskDefinition.Create(@"C:\Old\RevoHelper.exe", Sid);
        var platform = new FakeStartupTaskPlatform { CurrentUserSid = Sid, TaskXml = old, IgnoreRemoveTask = true };
        var registration = new WindowsStartupRegistration(Executable, platform);
        Assert.False(registration.IsEnabled()); // Current executable differs, but an old task still exists.
        Assert.ThrowsAny<Exception>(() => registration.SetEnabled(false));
        Assert.Equal(old, platform.TaskXml);
    }

    [Fact]
    public void RegistrationReadbackSeesExternalReplacement_DoesNotClaimOrRollbackIt()
    {
        var external = StartupTaskDefinition.Create(@"C:\External\RevoHelper.exe", Sid);
        var platform = new FakeStartupTaskPlatform { CurrentUserSid = Sid, ReplaceTaskAfterRegistration = external };
        var registration = new WindowsStartupRegistration(Executable, platform);
        Assert.ThrowsAny<Exception>(() => registration.SetEnabled(true));
        Assert.Equal(external, platform.TaskXml);
    }

    [Fact]
    public void ReadbackThrowsAfterRegistration_RestoresOriginalAbsence()
    {
        var platform = new FakeStartupTaskPlatform { CurrentUserSid = Sid, FailReadOnceAfterRegister = true };
        var registration = new WindowsStartupRegistration(Executable, platform);
        Assert.ThrowsAny<Exception>(() => registration.SetEnabled(true));
        Assert.Null(platform.TaskXml);
    }

    private static LegacyStartupState LegacyWithApproval() => new(
        new(RegistryValueKind.ExpandString, @"%USERPROFILE%\Old\RevoHelper.exe --startup"),
        new(RegistryValueKind.Binary, new byte[] { 3, 0, 0, 0, 1, 2, 3, 4, 5, 6, 7, 8 }),
        new(RegistryValueKind.Binary, new byte[] { 2, 0, 0, 0, 8, 7, 6, 5, 4, 3, 2, 1 }));

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
        public LegacyStartupState LegacyState { get; set; } = LegacyStartupState.Empty;
        public string? LegacyRunCommand
        {
            get => LegacyState.Run?.Value as string;
            set => LegacyState = LegacyState with { Run = value is null ? null : new(RegistryValueKind.String, value) };
        }
        public bool LegacyRunDisabled { get; init; }
        public Exception? RegisterException { get; init; }
        public bool FailCleanupOnceAfterMutation { get; set; }
        public bool FailRegisterOnceAfterMutation { get; set; }
        public Exception? RestoreLegacyException { get; init; }
        public bool IgnoreRemoveTask { get; init; }
        public string? ReplaceTaskAfterRegistration { get; init; }
        public bool FailReadOnceAfterRegister { get; set; }
        private bool failReadOnce;
        public int RegisterCalls { get; private set; }

        public string? ReadTaskXml()
        {
            if (failReadOnce)
            {
                failReadOnce = false;
                throw new IOException("readback failure");
            }
            return TaskXml;
        }
        public void RegisterTask(string xml)
        {
            RegisterCalls++;
            if (RegisterException is not null) throw RegisterException;
            TaskXml = xml;
            if (ReplaceTaskAfterRegistration is not null) TaskXml = ReplaceTaskAfterRegistration;
            if (FailReadOnceAfterRegister)
            {
                FailReadOnceAfterRegister = false;
                failReadOnce = true;
            }
            if (FailRegisterOnceAfterMutation)
            {
                FailRegisterOnceAfterMutation = false;
                throw new IOException("register failed after mutation");
            }
        }
        public void RemoveTask() { if (!IgnoreRemoveTask) TaskXml = null; }
        public string? ReadLegacyRunCommand() => LegacyRunCommand;
        public bool IsLegacyRunDisabled() => LegacyRunDisabled;
        public void RemoveLegacyRun()
        {
            LegacyState = LegacyStartupState.Empty;
            if (FailCleanupOnceAfterMutation)
            {
                FailCleanupOnceAfterMutation = false;
                throw new IOException("cleanup failure");
            }
        }
        public LegacyStartupState CaptureLegacyState() => LegacyState;
        public void RestoreLegacyState(LegacyStartupState state)
        {
            if (RestoreLegacyException is not null) throw RestoreLegacyException;
            LegacyState = state;
        }
    }
}
