using RevoHelper.App.Charging;
using RevoHelper.Core.Control.ExperimentalCharge;

namespace RevoHelper.App.Tests.Charging;

// Entirely memory-only: no raw client, WMI, session files, processes, power APIs
// or SystemEvents. Delayed callbacks deliberately ignore cancellation in race
// tests so generation checks, rather than cooperative cancellation alone, win.
public sealed class SessionChargeControllerTests
{
    [Fact]
    public async Task DefaultOffNeverResumesAcrossPowerNotifications()
    {
        using var f = new Fixture();
        await f.Power(false); await f.Power(true); await f.Power(true);
        Assert.False(f.Controller.Status.Enabled);
        Assert.False(f.Controller.Status.PowerPaused);
        Assert.Empty(f.Inner.EnabledLimits);
    }

    [Fact]
    public async Task ConstructorDoesNotAdoptAnUnownedInnerEnabledSession()
    {
        var inner = new FakeController(); inner.Set(new(Enabled: true, LimitPercent: 80));
        var online = true;
        using var controller = new SessionChargeController(inner, () => online, (_, _) => Task.CompletedTask);
        online = false; await controller.SetPowerAvailableAsync(false);
        online = true; await controller.SetPowerAvailableAsync(true);
        Assert.Empty(inner.EnabledLimits);
    }

    [Fact]
    public async Task SuccessfulEnablePausesLogicallyOnAndResumesOnlyOnceAfterStableAc()
    {
        using var f = new Fixture();
        await f.Controller.EnableAsync(80);
        Assert.True(f.Controller.Status.Enabled); Assert.False(f.Controller.Status.PowerPaused);
        await f.Power(false);
        Assert.True(f.Controller.Status.Enabled); Assert.True(f.Controller.Status.PowerPaused);
        Assert.False(f.Inner.Status.Enabled); Assert.False(f.Controller.Status.RecoveryPending);
        Assert.True(f.Controller.Status.Message.Contains("等待", StringComparison.Ordinal)
            || f.Controller.Status.Message.Contains("暂停", StringComparison.Ordinal));
        var stopIndex = f.Inner.Calls.FindLastIndex(call => call == "stop");
        Assert.True(stopIndex >= 0);
        f.Delay = (duration, _) =>
        {
            Assert.True(duration > TimeSpan.Zero);
            Assert.False(f.Inner.Status.Enabled);
            f.Inner.Calls.Add("stable-ac-delay");
            return Task.CompletedTask;
        };
        await f.Power(true); await f.Power(true);
        Assert.Equal(new[] { 80, 80 }, f.Inner.EnabledLimits);
        Assert.True(f.Controller.Status.Enabled); Assert.False(f.Controller.Status.PowerPaused);
        Assert.True(f.Inner.Calls.IndexOf("stable-ac-delay") > stopIndex);
        Assert.True(f.Inner.Calls.FindLastIndex(call => call == "enable:80") > f.Inner.Calls.IndexOf("stable-ac-delay"));
    }

    [Theory]
    [InlineData(60)]
    [InlineData(100)]
    public async Task PausedTargetChangeUpdatesOnlySessionMemoryAndResumesTheLatestTarget(int target)
    {
        using var f = new Fixture();
        await f.Controller.EnableAsync(80); await f.Power(false);
        var stops = f.Inner.Stops;
        await f.Controller.EnableAsync(target);
        Assert.Equal(new[] { 80 }, f.Inner.EnabledLimits);
        Assert.Equal(stops, f.Inner.Stops);
        Assert.True(f.Controller.Status.Enabled); Assert.True(f.Controller.Status.PowerPaused);
        Assert.Equal(target, f.Controller.Status.LimitPercent);
        await f.Power(true);
        Assert.Equal(new[] { 80, target }, f.Inner.EnabledLimits);
        Assert.Equal(target, f.Controller.Status.LimitPercent);
        Assert.False(f.Controller.Status.PowerPaused);
    }

    [Fact]
    public async Task LogicalHundredIsDelegatedWithoutIntroducingAnyHardwareBackend()
    {
        using var f = new Fixture();
        await f.Controller.EnableAsync(100);
        Assert.Equal(new[] { 100 }, f.Inner.EnabledLimits);
        Assert.True(f.Controller.Status.Enabled); Assert.False(f.Controller.Status.PowerPaused);
        await f.Controller.StopAsync("manual off"); await f.Power(false); await f.Power(true);
        Assert.Equal(new[] { 100 }, f.Inner.EnabledLimits);
        Assert.False(f.Controller.Status.Enabled);
    }

    [Theory]
    [InlineData("stop")]
    [InlineData("request")]
    [InlineData("recover")]
    [InlineData("dispose")]
    public async Task ExplicitCancellationWhileResumeDelayIsQueuedCannotBeResurrected(string action)
    {
        using var f = new Fixture(); await f.Controller.EnableAsync(80); await f.Power(false);
        var entered = Signal(); var release = Signal();
        f.Delay = async (_, _) => { entered.TrySetResult(); await release.Task; };
        var resume = f.Power(true);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Task cancel = action switch
        {
            "stop" => f.Controller.StopAsync("manual off during resume"),
            "recover" => f.Controller.RecoverAsync(),
            _ => Task.CompletedTask
        };
        if (action == "request") f.Controller.RequestStop("manual off during resume");
        if (action == "dispose") f.Controller.Dispose();
        release.TrySetResult();
        await Task.WhenAll(resume, cancel).WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(new[] { 80 }, f.Inner.EnabledLimits);
        Assert.False(f.Controller.Status.Enabled); Assert.False(f.Controller.Status.PowerPaused);
        if (action != "dispose") { await f.Power(false); await f.Power(true); Assert.Single(f.Inner.EnabledLimits); }
    }

    [Fact]
    public async Task OfflineDuringQueuedResumeInvalidatesItAndLaterOnlineCreatesOnlyOneNewAttempt()
    {
        using var f = new Fixture(); await f.Controller.EnableAsync(80); await f.Power(false);
        var entered = Signal(); var release = Signal();
        f.Delay = async (_, _) => { entered.TrySetResult(); await release.Task; };
        var resume = f.Power(true); await entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var offline = f.Power(false);
        release.TrySetResult();
        await Task.WhenAll(resume, offline).WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Single(f.Inner.EnabledLimits);
        Assert.True(f.Controller.Status.Enabled); Assert.True(f.Controller.Status.PowerPaused);
        f.Delay = (_, _) => Task.CompletedTask;
        await f.Power(true); await f.Power(true);
        Assert.Equal(new[] { 80, 80 }, f.Inner.EnabledLimits);
    }

    [Fact]
    public async Task OnlineNotificationCannotOverrideTheActualAcProbe()
    {
        using var f = new Fixture(); await f.Controller.EnableAsync(80); await f.Power(false);
        await f.Controller.SetPowerAvailableAsync(true); // Real injected AC remains false.
        Assert.Single(f.Inner.EnabledLimits);
        Assert.True(f.Controller.Status.PowerPaused);
    }

    [Theory]
    [InlineData("pending")]
    [InlineData("busy")]
    [InlineData("throw")]
    public async Task UnconfirmedPowerStopNeverResumesOrLaundersPendingIntoEnabled(string failure)
    {
        using var f = new Fixture(); await f.Controller.EnableAsync(80);
        f.Inner.StopLeavesPending = failure == "pending";
        f.Inner.StopLeavesBusy = failure == "busy";
        if (failure == "throw") f.Inner.StopError = new IOException("recovery transport failed");
        await IgnoreFailure(() => f.Power(false));
        await IgnoreFailure(() => f.Power(true));
        Assert.Single(f.Inner.EnabledLimits);
        // Even if a later observer reports restored, a failed recovery is not a
        // fresh user authorization. Explicit off/recovery/enable is required.
        f.Inner.StopError = null; f.Inner.StopLeavesPending = false; f.Inner.StopLeavesBusy = false;
        f.Inner.Set(new(Message: "late restored"));
        await f.Power(false); await f.Power(true);
        Assert.Single(f.Inner.EnabledLimits);
    }

    [Fact]
    public async Task FailedInitialEnableNeverCreatesAnIntentForPowerResume()
    {
        using var f = new Fixture(); f.Inner.EnableError = new IOException("initial enable failed");
        await Assert.ThrowsAsync<IOException>(() => f.Controller.EnableAsync(80));
        f.Inner.EnableError = null; f.Inner.Set(new());
        await f.Power(false); await f.Power(true);
        Assert.Single(f.Inner.EnabledLimits); Assert.False(f.Controller.Status.Enabled);
    }

    [Fact]
    public async Task RawNormalRestorationBeforeTheAppOfflineEventPreservesOnlyTheAlreadySuccessfulIntent()
    {
        using var f = new Fixture(); await f.Controller.EnableAsync(80);
        f.Online = false;
        f.Inner.Set(new(LimitPercent: 80, Message: "raw guardian restored after unplug"));
        await f.Controller.SetPowerAvailableAsync(false);
        Assert.True(f.Controller.Status.Enabled); Assert.True(f.Controller.Status.PowerPaused);
        await f.Power(true);
        Assert.Equal(new[] { 80, 80 }, f.Inner.EnabledLimits);
    }

    [Fact]
    public async Task GenuineRawHealthStopWhileAcIsOnlineClearsTheResumeIntent()
    {
        using var f = new Fixture(); await f.Controller.EnableAsync(80);
        f.Inner.Set(new(LimitPercent: 80, Message: "raw stopped because platform changed"));
        await f.Power(false); await f.Power(true);
        Assert.Single(f.Inner.EnabledLimits); Assert.False(f.Controller.Status.Enabled);
    }

    [Fact]
    public async Task RecoveryPendingIsNeverMistakenForAnOfflinePause()
    {
        using var f = new Fixture(); await f.Controller.EnableAsync(80); f.Online = false;
        f.Inner.Set(new(RecoveryPending: true, LimitPercent: 80, Message: "platform restore unconfirmed"));
        await IgnoreFailure(() => f.Controller.SetPowerAvailableAsync(false));
        await IgnoreFailure(() => f.Power(true));
        Assert.Single(f.Inner.EnabledLimits);
        Assert.True(f.Controller.Status.RecoveryPending);
        Assert.False(f.Controller.Status.PowerPaused);
    }

    [Fact]
    public async Task ResumeEnableFailureIsOneAttemptNotAnAutomaticRetryLoop()
    {
        using var f = new Fixture(); await f.Controller.EnableAsync(80); await f.Power(false);
        f.Inner.EnableError = new IOException("resume failed");
        await IgnoreFailure(() => f.Power(true));
        f.Inner.EnableError = null; f.Inner.Set(new());
        await f.Power(true); await f.Power(false); await f.Power(true);
        Assert.Equal(new[] { 80, 80 }, f.Inner.EnabledLimits);
        Assert.False(f.Controller.Status.Enabled);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UnknownAcProbeFailureClearsIntentInsteadOfTreatingItAsAResumableUnplug(bool duringResume)
    {
        using var f = new Fixture(); await f.Controller.EnableAsync(80);
        if (duringResume) await f.Power(false);
        f.ProbeError = new InvalidOperationException("power state unavailable");

        if (duringResume) await f.Power(true);
        else f.Inner.Set(new(Message: "raw stopped while power status was unknown"));

        Assert.False(f.Controller.Status.Enabled);
        Assert.False(f.Controller.Status.PowerPaused);
        f.ProbeError = null;
        await f.Power(true); await f.Power(false); await f.Power(true);
        Assert.Equal(new[] { 80 }, f.Inner.EnabledLimits);
        Assert.False(f.Controller.Status.Enabled);
    }

    [Fact]
    public async Task UncooperativeLateResumeEnableAfterManualOffIsCompensatedByStop()
    {
        using var f = new Fixture(); await f.Controller.EnableAsync(80); await f.Power(false);
        f.Inner.EnableHold = Signal();
        f.Inner.EnableEntered = Signal();
        var resume = f.Power(true);
        await f.Inner.EnableEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));

        var stop = f.Controller.StopAsync("manual off while raw enable ignores cancellation");
        Assert.False(f.Controller.Status.Enabled);
        Assert.False(f.Controller.Status.PowerPaused);
        var stopsBeforeLateEnable = f.Inner.Stops;
        f.Inner.EnableHold.TrySetResult();
        await Task.WhenAll(resume, stop).WaitAsync(TimeSpan.FromSeconds(2));

        Assert.True(f.Inner.Stops > stopsBeforeLateEnable);
        Assert.False(f.Inner.Status.Enabled);
        Assert.False(f.Inner.Status.Busy);
        Assert.False(f.Controller.Status.Enabled);
        Assert.False(f.Controller.Status.PowerPaused);
        await f.Power(false); await f.Power(true);
        Assert.Equal(new[] { 80, 80 }, f.Inner.EnabledLimits);
    }

    [Fact]
    public async Task UncooperativeOldStopCompletesBeforeANewerExplicitEnableAndLateNotificationCannotUndoIt()
    {
        using var f = new Fixture(); await f.Controller.EnableAsync(80);
        f.Inner.StopHold = Signal();
        f.Inner.StopEntered = Signal();
        var stop = f.Controller.StopAsync("manual off before choosing another limit");
        await f.Inner.StopEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));

        var enable = f.Controller.EnableAsync(60);
        Assert.Equal(new[] { 80 }, f.Inner.EnabledLimits);
        f.Inner.StopHold.TrySetResult();
        await Task.WhenAll(stop, enable).WaitAsync(TimeSpan.FromSeconds(2));
        f.Inner.Notify(); // A queued callback re-reads current status, not the old stop record.

        Assert.Equal(new[] { 80, 60 }, f.Inner.EnabledLimits);
        Assert.True(f.Inner.Calls.IndexOf("stop") < f.Inner.Calls.IndexOf("enable:60"));
        Assert.True(f.Inner.Status.Enabled);
        Assert.True(f.Controller.Status.Enabled);
        Assert.Equal(60, f.Controller.Status.LimitPercent);
        Assert.False(f.Controller.Status.PowerPaused);
    }

    private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static async Task IgnoreFailure(Func<Task> action)
    { try { await action(); } catch (Exception exception) when (exception is IOException or InvalidOperationException) { } }

    private sealed class Fixture : IDisposable
    {
        public bool Online = true;
        public Exception? ProbeError;
        public FakeController Inner { get; } = new();
        public Func<TimeSpan, CancellationToken, Task> Delay = (_, _) => Task.CompletedTask;
        public SessionChargeController Controller { get; }
        public Fixture() => Controller = new(Inner, ReadAc, (time, token) => Delay(time, token));
        private bool ReadAc() => ProbeError is { } error ? throw error : Online;
        public Task Power(bool online) { Online = online; return Controller.SetPowerAvailableAsync(online); }
        public void Dispose() => Controller.Dispose();
    }

    private sealed class FakeController : IExperimentalChargeController
    {
        public ExperimentalChargeStatus Status { get; private set; } = new();
        public event EventHandler? StatusChanged;
        public List<int> EnabledLimits { get; } = [];
        public List<string> Calls { get; } = [];
        public int Stops;
        public int StopRequests;
        public int Recoveries;
        public bool StopLeavesPending;
        public bool StopLeavesBusy;
        public Exception? EnableError;
        public Exception? StopError;
        public TaskCompletionSource? EnableHold;
        public TaskCompletionSource? StopHold;
        public TaskCompletionSource EnableEntered = Signal();
        public TaskCompletionSource StopEntered = Signal();
        public void Set(ExperimentalChargeStatus status) { Status = status; StatusChanged?.Invoke(this, EventArgs.Empty); }
        public void Notify() => StatusChanged?.Invoke(this, EventArgs.Empty);
        public async Task EnableAsync(int limitPercent, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            EnabledLimits.Add(limitPercent); Calls.Add("enable:" + limitPercent);
            EnableEntered.TrySetResult();
            if (EnableHold is not null) await EnableHold.Task; // Deliberately ignores token cancellation.
            if (EnableError is not null) throw EnableError;
            Set(new(Enabled: true, LimitPercent: limitPercent, Message: "fake active"));
        }
        public async Task StopAsync(string reason, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Stops++; Calls.Add("stop");
            StopEntered.TrySetResult();
            if (StopHold is not null) await StopHold.Task; // Deliberately ignores token cancellation.
            if (StopError is not null) throw StopError;
            Set(new(Busy: StopLeavesBusy, RecoveryPending: StopLeavesPending,
                LimitPercent: Status.LimitPercent, Message: "fake restored"));
        }
        public Task RecoverAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Recoveries++; Calls.Add("recover");
            Set(new(LimitPercent: Status.LimitPercent, Message: "fake explicit recovery"));
            return Task.CompletedTask;
        }
        public void RequestStop(string reason)
        {
            StopRequests++; Calls.Add("request-stop");
            if (Status.Enabled || Status.Busy) Set(Status with { Enabled = false, Busy = true, Message = "fake stopping" });
        }
    }
}
