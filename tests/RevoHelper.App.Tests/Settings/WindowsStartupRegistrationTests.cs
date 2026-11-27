using System.Xml.Linq;
using System.Security.Principal;
using Microsoft.Win32;
using RevoHelper.App.Lifecycle;
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

    [Theory]
    [InlineData("1.0.3.9", "1.0.4.5", true)]
    [InlineData("1.0.4.5", "1.0.4.5", true)]
    [InlineData(null, "1.0.4.5", true)]
    [InlineData("1.0.4.6", "1.0.4.5", false)]
    public void Synchronize_MovesAnEnabledTaskUnlessItPointsAtANewerVersion(string? registered, string current, bool moved)
    {
        const string oldPath = @"C:\Old\RevoHelper.exe";
        var original = StartupTaskDefinition.Create(oldPath, Sid);
        var platform = new FakeStartupTaskPlatform { CurrentUserSid = Sid, TaskXml = original };
        var registration = new WindowsStartupRegistration(Executable, platform,
            path => string.Equals(path, oldPath, StringComparison.OrdinalIgnoreCase)
                ? registered is null ? null : Version.Parse(registered)
                : Version.Parse(current));

        Assert.Equal(moved, StartupRegistrationSynchronizer.Synchronize(registration));

        if (moved) Assert.True(StartupTaskDefinition.IsEnabledFor(platform.TaskXml!, Executable, Sid));
        else Assert.Equal(original, platform.TaskXml);
    }

    [Fact]
    public void Synchronize_DoesNotCreateATaskWhenStartupIsOff()
    {
        var platform = new FakeStartupTaskPlatform { CurrentUserSid = Sid };
        var registration = new WindowsStartupRegistration(Executable, platform, _ => new Version(1, 0, 4, 5));

        Assert.False(StartupRegistrationSynchronizer.Synchronize(registration));
        Assert.Null(platform.TaskXml);
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

    private const string MovedExecutable = @"C:\Users\u\Desktop\RevoHelper.exe";

    // Switching on names the file where it is at that moment, not where RevoHelper started from.
    [Fact]
    public void SwitchingOn_NamesTheProgramFileWhereItIsNow()
    {
        var platform = new FakeStartupTaskPlatform { CurrentUserSid = Sid };
        var place = Executable;
        var registration = new WindowsStartupRegistration(() => place, platform);
        place = MovedExecutable;

        registration.SetEnabled(true);
        Assert.Equal(MovedExecutable, StartupTaskDefinition.ReadCommand(platform.TaskXml!));
        Assert.True(registration.IsEnabled());
    }

    // When the file cannot be confirmed, only switching on is refused; switching off still works.
    [Fact]
    public void AnUnconfirmedProgramFile_BlocksOnlySwitchingOn()
    {
        var old = StartupTaskDefinition.Create(Executable, Sid);
        var platform = new FakeStartupTaskPlatform { CurrentUserSid = Sid, TaskXml = old };
        var registration = new WindowsStartupRegistration(
            () => throw new InvalidOperationException(RevoHelper.App.Lifecycle.RunningExecutable.NotFoundMessage), platform);

        Assert.False(registration.IsEnabled());
        Assert.False(registration.RegisteredExecutableIsNewer());
        var error = Assert.Throws<InvalidOperationException>(() => registration.SetEnabled(true));
        Assert.Equal(RevoHelper.App.Lifecycle.RunningExecutable.NotFoundMessage, error.Message);
        Assert.Equal(old, platform.TaskXml);

        registration.SetEnabled(false);
        Assert.Null(platform.TaskXml);
    }

    // A copy moved while running takes along the logon task that names its old place.
    [Fact]
    public void FollowMove_PointsTheEnabledTaskForTheOldPlaceAtTheNewOne()
    {
        var platform = new FakeStartupTaskPlatform { CurrentUserSid = Sid, TaskXml = StartupTaskDefinition.Create(Executable, Sid) };
        var place = Executable;
        var registration = new WindowsStartupRegistration(() => place, platform);
        place = MovedExecutable;
        Assert.False(registration.IsEnabled());                    // it names a file that is no longer there

        Assert.True(registration.FollowMove([Executable], MovedExecutable, path => path == MovedExecutable));
        Assert.Equal(MovedExecutable, StartupTaskDefinition.ReadCommand(platform.TaskXml!));
        Assert.True(registration.IsEnabled());
        Assert.Equal((0, 1), (platform.RegisterCalls, platform.UpdateCalls));
    }

    // Only the program path and its folder change: a delay the user set stays.
    [Fact]
    public void FollowMove_KeepsEverythingButThePath()
    {
        var task = XElement.Parse(StartupTaskDefinition.Create(Executable, Sid));
        task.Descendants(Ns + "Delay").Single().Value = "PT45S";
        var platform = new FakeStartupTaskPlatform { CurrentUserSid = Sid, TaskXml = task.ToString() };
        var registration = new WindowsStartupRegistration(() => MovedExecutable, platform);

        Assert.True(registration.FollowMove([Executable], MovedExecutable, path => path == MovedExecutable));
        var exec = task.Descendants(Ns + "Exec").Single();
        exec.Element(Ns + "Command")!.Value = MovedExecutable;
        exec.Element(Ns + "WorkingDirectory")!.Value = @"C:\Users\u\Desktop";
        Assert.True(XNode.DeepEquals(task, XElement.Parse(platform.TaskXml!)));
    }

    [Fact]
    public void FollowMove_LeavesEveryOtherTaskAlone()
    {
        static bool OnlyTheNewPlace(string path) => path == MovedExecutable;
        var disabled = XElement.Parse(StartupTaskDefinition.Create(Executable, Sid));
        disabled.Element(Ns + "Settings")!.Element(Ns + "Enabled")!.Value = "false";
        var cases = new (string? Task, Func<string, bool> Exists)[]
        {
            (disabled.ToString(), OnlyTheNewPlace),                                        // switched off
            (null, OnlyTheNewPlace),                                                       // removed: not created again
            (StartupTaskDefinition.Create(@"D:\Other\RevoHelper.exe", Sid), OnlyTheNewPlace), // a place this file was never at
            (StartupTaskDefinition.Create(Executable, OtherSid), OnlyTheNewPlace),          // another user's
            (StartupTaskDefinition.Create(Executable, Sid), _ => true),                     // a file is back at the old place
            (StartupTaskDefinition.Create(Executable, Sid), _ => false),                    // the new place is gone too
            (StartupTaskDefinition.Create(MovedExecutable, Sid), OnlyTheNewPlace),          // already followed
        };
        foreach (var (task, exists) in cases)
        {
            var platform = new FakeStartupTaskPlatform { CurrentUserSid = Sid, TaskXml = task };
            var registration = new WindowsStartupRegistration(() => MovedExecutable, platform);
            Assert.False(registration.FollowMove([Executable], MovedExecutable, exists));
            Assert.Equal(task, platform.TaskXml);
            Assert.Equal((0, 0), (platform.RegisterCalls, platform.UpdateCalls));
        }
    }

    // When Windows does not confirm the change, nothing is written back: the task may have been
    // removed, switched off or pointed elsewhere in the meantime, and that stays as it is.
    [Fact]
    public void FollowMove_WritesNothingBackWhenTheChangeIsNotConfirmed()
    {
        var disabled = XElement.Parse(StartupTaskDefinition.Create(MovedExecutable, Sid));
        disabled.Element(Ns + "Settings")!.Element(Ns + "Enabled")!.Value = "false";
        foreach (var meanwhile in new[] { null, disabled.ToString(), StartupTaskDefinition.Create(@"D:\Other\RevoHelper.exe", Sid) })
        {
            var platform = new FakeStartupTaskPlatform { CurrentUserSid = Sid, TaskXml = StartupTaskDefinition.Create(Executable, Sid) };
            platform.AfterUpdate = () => platform.TaskXml = meanwhile;
            var registration = new WindowsStartupRegistration(() => MovedExecutable, platform);

            Assert.Throws<InvalidOperationException>(() => registration.FollowMove([Executable], MovedExecutable, path => path == MovedExecutable));
            Assert.Equal(meanwhile, platform.TaskXml);
            Assert.Equal((0, 1), (platform.RegisterCalls, platform.UpdateCalls));
        }
    }

    // A task removed just before the change is not created again: the change only updates.
    [Fact]
    public void FollowMove_ATaskRemovedJustBeforeStaysRemoved()
    {
        var platform = new FakeStartupTaskPlatform { CurrentUserSid = Sid, TaskXml = StartupTaskDefinition.Create(Executable, Sid) };
        platform.BeforeUpdate = () => platform.TaskXml = null;
        var registration = new WindowsStartupRegistration(() => MovedExecutable, platform);

        Assert.Throws<IOException>(() => registration.FollowMove([Executable], MovedExecutable, path => path == MovedExecutable));
        Assert.Null(platform.TaskXml);
        Assert.Equal(0, platform.RegisterCalls);
    }

    // The task follows wherever the file goes, from whichever place it was last pointed at:
    // A to B to C, back to A, and on again.
    [Fact]
    public void AFileMovedAgainAndAgain_TakesItsTaskAlong()
    {
        const string A = Executable, B = MovedExecutable, C = @"C:\Tools\RevoHelper.exe";
        var platform = new FakeStartupTaskPlatform { CurrentUserSid = Sid, TaskXml = StartupTaskDefinition.Create(A, Sid) };
        var now = A;
        var registration = new WindowsStartupRegistration(() => now, platform);
        var follower = new ExecutableMoveFollower(A, () => now, (places, to) => registration.FollowMove(places, to, path => path == now));

        Assert.Null(follower.Check());
        Assert.Equal(0, platform.ReadCalls);                         // not moved: the task is not even read
        foreach (var place in new[] { B, C, A, B })
        {
            now = place;
            Assert.Equal("logon task follows the moved program file", follower.Check());
            Assert.Equal(place, StartupTaskDefinition.ReadCommand(platform.TaskXml!));
            Assert.True(registration.IsEnabled());
        }
        var reads = platform.ReadCalls;
        Assert.Null(follower.Check());
        Assert.Equal(reads, platform.ReadCalls);
        Assert.Equal(0, platform.RegisterCalls);
    }

    // A failed attempt is tried again at the next check; a place that keeps failing is left after
    // a few tries, and so is one with nothing to follow, until the file moves again.
    [Fact]
    public void AFailedFollow_IsTriedAgain_ButNotForEver()
    {
        var platform = new FakeStartupTaskPlatform { CurrentUserSid = Sid, TaskXml = StartupTaskDefinition.Create(Executable, Sid), FailUpdates = 1 };
        var now = Executable;
        var registration = new WindowsStartupRegistration(() => now, platform);
        var follower = new ExecutableMoveFollower(Executable, () => now, (places, to) => registration.FollowMove(places, to, path => path == now));

        now = MovedExecutable;
        Assert.StartsWith("follow-move IOException", follower.Check());
        Assert.Equal(Executable, StartupTaskDefinition.ReadCommand(platform.TaskXml!));
        Assert.Equal("logon task follows the moved program file", follower.Check());
        Assert.Equal(MovedExecutable, StartupTaskDefinition.ReadCommand(platform.TaskXml!));

        platform.FailUpdates = int.MaxValue;
        now = @"C:\Tools\RevoHelper.exe";
        for (var attempt = 0; attempt < ExecutableMoveFollower.Attempts; attempt++) Assert.NotNull(follower.Check());
        Assert.Null(follower.Check());
        Assert.Equal(2 + ExecutableMoveFollower.Attempts, platform.UpdateCalls);

        // Switched off in the meantime: nothing to follow, and not looked at again.
        platform.FailUpdates = 0;
        var disabled = XElement.Parse(platform.TaskXml!);
        disabled.Element(Ns + "Settings")!.Element(Ns + "Enabled")!.Value = "false";
        platform.TaskXml = disabled.ToString();
        now = @"C:\Tools\Again\RevoHelper.exe";
        Assert.Null(follower.Check());
        var reads = platform.ReadCalls;
        Assert.Null(follower.Check());
        Assert.Equal(reads, platform.ReadCalls);
        Assert.Equal(disabled.ToString(), platform.TaskXml);
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
        public int UpdateCalls { get; private set; }
        public int ReadCalls { get; private set; }
        public int FailUpdates { get; set; }
        public Action? BeforeUpdate { get; set; }
        public Action? AfterUpdate { get; set; }

        // Like TASK_UPDATE: fails rather than create a task that is not there.
        public void UpdateTask(string xml)
        {
            UpdateCalls++;
            BeforeUpdate?.Invoke();
            if (FailUpdates > 0) { FailUpdates--; throw new IOException("update failed"); }
            if (TaskXml is null) throw new IOException("task not found");
            TaskXml = xml;
            AfterUpdate?.Invoke();
        }

        public string? ReadTaskXml()
        {
            ReadCalls++;
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
