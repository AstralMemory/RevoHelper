using RevoHelper.App.Lifecycle;

namespace RevoHelper.App.Tests.Lifecycle;

public sealed class OfficialFrontendExitTests
{
    [Fact]
    public void CloseNow_ClosesTheWindowAndTheTrayAndLogsEach()
    {
        var logs = new List<string>();
        var frontends = new FakeFrontends { Running = _ => [new(1, "CCUWinUI"), new(2, "SystrayComponent")] };

        var result = new OfficialFrontendExit(frontends, NoDelay, logs.Add).CloseNow();

        Assert.Equal(new OfficialFrontendExitResult(2, 0, false), result);
        Assert.Equal(new[] { "closed CCUWinUI#1", "closed SystrayComponent#2" }, logs);
    }

    [Fact]
    public async Task Watch_ClosesTheTray_ThenKeepsCheckingForThreeMinutes()
    {
        var tray = new OfficialFrontendProcess(20336, "SystrayComponent");
        var frontends = new FakeFrontends { Running = round => round == 0 ? [tray] : [] };
        var reports = new List<OfficialFrontendExitResult>();

        var result = await new OfficialFrontendExit(frontends, NoDelay).WatchAsync(reports.Add, CancellationToken.None);

        Assert.Equal(new[] { tray }, frontends.Closed);
        Assert.Equal(new[] { new OfficialFrontendExitResult(1, 0, false) }, reports);
        Assert.Equal(new OfficialFrontendExitResult(1, 0, false), result);
        Assert.Equal(1 + (int)(OfficialFrontendExit.WatchDuration / OfficialFrontendExit.PollInterval), frontends.Finds);
    }

    // V1.0.8 Snapshot 7: kept light, 19 checks over the three minutes instead of 91.
    [Fact]
    public async Task Watch_ChecksEveryTenSecondsForThreeMinutes()
    {
        var delays = new List<TimeSpan>();
        var frontends = new FakeFrontends();

        await new OfficialFrontendExit(frontends, (interval, _) =>
        {
            delays.Add(interval);
            return Task.CompletedTask;
        }).WatchAsync(_ => { }, CancellationToken.None);

        Assert.Equal(19, frontends.Finds);
        Assert.Equal(18, delays.Count);
        Assert.All(delays, interval => Assert.Equal(TimeSpan.FromSeconds(10), interval));
    }

    // After sign-in the official service starts its tray later than RevoHelper.
    [Fact]
    public async Task Watch_ReportsNothingRunningOnce_ThenClosesATrayThatStartsLater()
    {
        var tray = new OfficialFrontendProcess(7, "SystrayComponent");
        var frontends = new FakeFrontends { Running = round => round == 10 ? [tray] : [] };
        var reports = new List<OfficialFrontendExitResult>();

        await new OfficialFrontendExit(frontends, NoDelay).WatchAsync(reports.Add, CancellationToken.None);

        Assert.Equal(new[] { OfficialFrontendExitResult.None, new OfficialFrontendExitResult(1, 0, false) }, reports);
    }

    [Fact]
    public async Task Startup_FirstRoundAndWatchShareTheLimitAndAnotherNameCanStillClose()
    {
        var window = new OfficialFrontendProcess(200, "CCUWinUI");
        var frontends = new FakeFrontends
        {
            Running = round => round == 7
                ? [new(100 + round, "SystrayComponent"), window]
                : [new(100 + round, "SystrayComponent")]
        };
        var exit = new OfficialFrontendExit(frontends, NoDelay);
        var startup = exit.StartStartupWatch();
        Assert.Single(frontends.Closed);

        var result = await exit.WatchAsync(_ => { }, CancellationToken.None, startup);

        Assert.Equal(OfficialFrontendExit.MaximumClosesPerName, frontends.Closed.Count(p => p.Name == "SystrayComponent"));
        Assert.Contains(window, frontends.Closed);
        Assert.Equal(new OfficialFrontendExitResult(4, 0, true), result);
        Assert.Equal(19, frontends.Finds);
    }

    // 2026-11-26: after a wake the official service started again and opened its tray 47 s later.
    [Fact]
    public async Task TrayOnlyWatch_ClosesATrayThatStartsLater_AndLeavesTheWindow()
    {
        var window = new OfficialFrontendProcess(1, "CCUWinUI");
        var tray = new OfficialFrontendProcess(34040, "SystrayComponent");
        var frontends = new FakeFrontends { Running = round => round >= 5 ? [window, tray] : [window] };
        var reports = new List<OfficialFrontendExitResult>();

        var exit = new OfficialFrontendExit(frontends, NoDelay);
        var result = await exit.WatchAsync(reports.Add, CancellationToken.None, exit.CreateWatch(trayOnly: true));

        Assert.Equal(new[] { tray }, frontends.Closed.Distinct());
        Assert.DoesNotContain(window, frontends.Closed);
        Assert.Equal(OfficialFrontendExitResult.None, reports[0]);
        Assert.Equal(19, frontends.Finds);
        Assert.Equal(3, result.Closed);
    }

    // A wake ten seconds before a watch ends: the tray gets three more minutes from the wake, while
    // the window keeps the watch's own time, since after that a window is open because someone opened it.
    [Fact]
    public async Task AWakeNearTheEnd_GivesTheTrayThreeMoreMinutes_ButNotTheWindow()
    {
        var clock = new ManualClock();
        var start = clock.GetUtcNow();
        var window = new OfficialFrontendProcess(1, "CCUWinUI");
        var tray = new OfficialFrontendProcess(2, "SystrayComponent");
        var frontends = new FakeFrontends
        {
            Running = _ => clock.GetUtcNow() - start > OfficialFrontendExit.WatchDuration ? [window, tray] : []
        };
        OfficialFrontendStartupWatch? watch = null;
        OfficialFrontendExit? exit = null;
        var woke = false;
        exit = new OfficialFrontendExit(frontends, (interval, _) =>
        {
            clock.Advance(interval);
            if (!woke && clock.GetUtcNow() - start >= OfficialFrontendExit.WatchDuration - TimeSpan.FromSeconds(10))
            {
                woke = true;
                Assert.True(exit!.ExtendTray(watch!));
            }
            return Task.CompletedTask;
        }, timeProvider: clock);

        watch = exit.StartStartupWatch();
        await exit.WatchAsync(_ => { }, CancellationToken.None, watch);

        Assert.Contains(tray, frontends.Closed);
        Assert.DoesNotContain(window, frontends.Closed);
        Assert.True(clock.GetUtcNow() - start > OfficialFrontendExit.WatchDuration + TimeSpan.FromMinutes(2));
    }

    // The computer sleeps through a watch's wait and wakes an hour later. The wake comes before the
    // watch notices that its time is up: the tray is still looked for, the window is not.
    [Fact]
    public async Task AWakeAfterSleepingThroughTheWatch_StillClosesTheTray()
    {
        var clock = new ManualClock();
        var window = new OfficialFrontendProcess(1, "CCUWinUI");
        var tray = new OfficialFrontendProcess(2, "SystrayComponent");
        var slept = false;
        var frontends = new FakeFrontends { Running = _ => slept ? [window, tray] : [] };
        OfficialFrontendStartupWatch? watch = null;
        OfficialFrontendExit? exit = null;
        exit = new OfficialFrontendExit(frontends, (interval, _) =>
        {
            if (slept) clock.Advance(interval);
            else
            {
                clock.Advance(TimeSpan.FromHours(1));
                slept = true;
                Assert.True(exit!.ExtendTray(watch!));
            }
            return Task.CompletedTask;
        }, timeProvider: clock);

        watch = exit.StartStartupWatch();
        await exit.WatchAsync(_ => { }, CancellationToken.None, watch);

        Assert.Equal(new[] { tray }, frontends.Closed.Distinct());
    }

    // Once a watch has ended, a wake starts a new one instead.
    [Fact]
    public async Task AWatchThatIsOver_IsNotExtended()
    {
        var exit = new OfficialFrontendExit(new FakeFrontends(), NoDelay);
        var watch = exit.StartStartupWatch();
        await exit.WatchAsync(_ => { }, CancellationToken.None, watch);

        Assert.False(exit.ExtendTray(watch));
    }

    // A tray that keeps coming back is given up on after a few closes; a wake, or a new process of
    // the official service, gives it a few more, and no more than that.
    [Fact]
    public async Task EachWakeGivesTheTrayAFewClosesOfItsOwn()
    {
        var frontends = new FakeFrontends { Running = round => [new(100 + round, "SystrayComponent")] };
        OfficialFrontendStartupWatch? watch = null;
        OfficialFrontendExit? exit = null;
        var waits = 0;
        exit = new OfficialFrontendExit(frontends, (_, _) =>
        {
            if (++waits == 10) Assert.True(exit!.ExtendTray(watch!));
            return Task.CompletedTask;
        });
        watch = exit.CreateWatch(trayOnly: true);

        var result = await exit.WatchAsync(_ => { }, CancellationToken.None, watch);

        Assert.Equal(2 * OfficialFrontendExit.MaximumClosesPerName, frontends.Closed.Count);
        Assert.True(result.GaveUp);
    }

    [Fact]
    public async Task Watch_CountsAWindowThatWillNotCloseAndStopsRetryingIt()
    {
        var frontends = new FakeFrontends { Running = _ => [new(5, "CCUWinUI")], CloseResult = _ => OfficialFrontendCloseOutcome.Failed };

        var result = await new OfficialFrontendExit(frontends, NoDelay).WatchAsync(_ => { }, CancellationToken.None);

        Assert.Equal(new OfficialFrontendExitResult(0, 3, true), result);
    }

    [Fact]
    public async Task ListingFailureIsUnknownForInspectionAndExit()
    {
        var frontends = new FakeFrontends { Running = _ => throw new InvalidOperationException("denied") };

        var exit = new OfficialFrontendExit(frontends, NoDelay);
        Assert.True(exit.Inspect().HasUnknown);
        Assert.True(exit.CloseNow().HasUnknown);
        var result = await exit.WatchAsync(_ => { }, CancellationToken.None);

        Assert.True(result.HasUnknown);
        Assert.Empty(frontends.Closed);
    }

    [Fact]
    public async Task Watch_DoesNotSpendTheCloseBudgetOnTargetsThatHaveAlreadyGone()
    {
        int gone = 3;
        var frontends = new FakeFrontends
        {
            Running = _ => [new(100, "SystrayComponent")],
            CloseResult = _ => gone-- > 0 ? OfficialFrontendCloseOutcome.Gone : OfficialFrontendCloseOutcome.Closed
        };
        var result = await new OfficialFrontendExit(frontends, NoDelay).WatchAsync(_ => { }, CancellationToken.None);

        Assert.Equal(new OfficialFrontendExitResult(3, 0, true), result);
        Assert.Equal(6, frontends.Closed.Count);
    }

    [Fact]
    public async Task Watch_ResumingBeyondItsWallClockDeadlineDoesNotCloseANewWindow()
    {
        var clock = new ManualClock();
        var frontends = new FakeFrontends { Running = round => round == 0 ? [] : [new(7, "CCUWinUI")] };
        var exit = new OfficialFrontendExit(frontends, (_, _) =>
        {
            clock.Advance(TimeSpan.FromHours(1));
            return Task.CompletedTask;
        }, timeProvider: clock);

        var startup = exit.StartStartupWatch();
        await exit.WatchAsync(_ => { }, CancellationToken.None, startup);

        Assert.Equal(1, frontends.Finds);
        Assert.Empty(frontends.Closed);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void CloseRound_ChecksStopConditionAgainBetweenTargets(bool cancel)
    {
        var clock = new ManualClock();
        using var cancellation = new CancellationTokenSource();
        var window = new OfficialFrontendProcess(7, "CCUWinUI");
        var tray = new OfficialFrontendProcess(8, "SystrayComponent");
        var frontends = new FakeFrontends
        {
            Running = _ => [window, tray],
            CloseResult = _ =>
            {
                if (cancel) cancellation.Cancel();
                else clock.Advance(TimeSpan.FromHours(1));
                return OfficialFrontendCloseOutcome.Closed;
            }
        };
        var exit = new OfficialFrontendExit(frontends, NoDelay, timeProvider: clock);
        if (cancel) Assert.ThrowsAny<OperationCanceledException>(() => exit.StartStartupWatch(cancellation.Token));
        else exit.StartStartupWatch();

        Assert.Equal(new[] { window }, frontends.Closed);
    }

    [Fact]
    public async Task ManualClickAndStartupRoundCanOnlyCloseAnInstanceOnce()
    {
        var window = new OfficialFrontendProcess(7, "CCUWinUI");
        using var release = new ManualResetEventSlim();
        using var secondSnapshot = new ManualResetEventSlim();
        var closing = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var clickStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        bool running = true;
        var frontends = new FakeFrontends
        {
            Running = round =>
            {
                if (round > 0) secondSnapshot.Set();
                return running ? [window] : [];
            },
            CloseResult = _ =>
            {
                closing.TrySetResult();
                if (!release.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException();
                running = false;
                return OfficialFrontendCloseOutcome.Closed;
            }
        };
        var exit = new OfficialFrontendExit(frontends, NoDelay);
        var startup = Task.Run(() => exit.StartStartupWatch());
        await closing.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var click = Task.Run(() =>
        {
            clickStarted.SetResult();
            return exit.CloseNow();
        });
        try
        {
            await clickStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.False(secondSnapshot.Wait(TimeSpan.FromMilliseconds(150)));
        }
        finally { release.Set(); }
        await startup;
        var result = await click;

        Assert.Equal(new[] { window }, frontends.Closed);
        Assert.Equal(OfficialFrontendExitResult.None, result);
    }

    [Fact]
    public async Task Watch_StopsWhenCancelled()
    {
        using var cancellation = new CancellationTokenSource();
        var frontends = new FakeFrontends();
        var exit = new OfficialFrontendExit(frontends, (_, token) =>
        {
            cancellation.Cancel();
            return Task.FromCanceled(token);
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => exit.WatchAsync(_ => { }, cancellation.Token));
        Assert.Equal(1, frontends.Finds);
    }

    private static Task NoDelay(TimeSpan delay, CancellationToken cancellationToken) => Task.CompletedTask;

    private sealed class FakeFrontends : IOfficialFrontendProcesses
    {
        public Func<int, IReadOnlyList<OfficialFrontendProcess>> Running { get; init; } = _ => [];
        public Func<OfficialFrontendProcess, OfficialFrontendCloseOutcome> CloseResult { get; init; } = _ => OfficialFrontendCloseOutcome.Closed;
        public int Finds { get; private set; }
        public List<OfficialFrontendProcess> Closed { get; } = [];

        public OfficialFrontendSnapshot Find() => new(Running(Finds++));

        public OfficialFrontendCloseOutcome Close(OfficialFrontendProcess process, Func<bool> mayClose)
        {
            if (!mayClose()) return OfficialFrontendCloseOutcome.Skipped;
            Closed.Add(process);
            return CloseResult(process);
        }
    }

    private sealed class ManualClock : TimeProvider
    {
        private DateTimeOffset now = DateTimeOffset.UnixEpoch;
        public override DateTimeOffset GetUtcNow() => now;
        public void Advance(TimeSpan duration) => now += duration;
    }
}
