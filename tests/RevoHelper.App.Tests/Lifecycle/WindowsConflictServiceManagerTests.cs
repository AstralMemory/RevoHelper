using RevoHelper.App.Lifecycle;

namespace RevoHelper.App.Tests.Lifecycle;

// All service observations and commands stay in memory.
public sealed class WindowsConflictServiceManagerTests
{
    [Fact]
    public async Task StopAsync_UsesDependencySafeOrderAndRejectsUnknownServices()
    {
        var fixture = new Fixture();

        await fixture.Manager.StopAsync(["ipfsvc", "XtuService", "dptftcs"], CancellationToken.None);

        Assert.Equal(["stop:XtuService", "stop:dptftcs", "stop:ipfsvc"], fixture.Commands);
        await Assert.ThrowsAsync<ArgumentException>(() =>
            fixture.Manager.StopAsync(["UnknownService"], CancellationToken.None));
    }

    [Fact]
    public async Task RestoreAsync_UsesReverseDependencyOrder()
    {
        var fixture = new Fixture("Stopped");

        await fixture.Manager.RestoreAsync(["XtuService", "dptftcs", "ipfsvc"], CancellationToken.None);

        Assert.Equal(["start:ipfsvc", "start:dptftcs", "start:XtuService"], fixture.Commands);
    }

    [Fact]
    public async Task StopAsync_WhenServiceCommandFails_ReportsTheSpecificServiceAndObservedState()
    {
        var fixture = new Fixture { ExitCode = 5, CompleteCommand = false };

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Manager.StopAsync(["dptftcs", "ipfsvc"], CancellationToken.None));

        Assert.Contains("dptftcs", exception.Message, StringComparison.Ordinal);
        Assert.Contains("5", exception.Message, StringComparison.Ordinal);
        Assert.Contains("Running", exception.Message, StringComparison.Ordinal);
        Assert.Equal(["stop:dptftcs"], fixture.Commands);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AlreadyInRequestedStateDoesNotSendAnotherCommand(bool restore)
    {
        var fixture = new Fixture(restore ? "Running" : "Stopped");

        if (restore) await fixture.Manager.RestoreAsync(["ipfsvc"], CancellationToken.None);
        else await fixture.Manager.StopAsync(["ipfsvc"], CancellationToken.None);

        Assert.Empty(fixture.Commands);
    }

    [Fact]
    public async Task NonzeroCommandWithFreshRequestedStateDoesNotReportFailure()
    {
        // Another controller may have completed the same operation meanwhile.
        var fixture = new Fixture { ExitCode = 1062 };

        await fixture.Manager.StopAsync(["XtuService"], CancellationToken.None);

        Assert.Equal("Stopped", fixture.States["XtuService"]);
    }

    [Fact]
    public async Task SuccessfulCommandWithUnavailableObservationDoesNotReportSuccess()
    {
        var fixture = new Fixture { FailObservationAfterCommand = true };

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Manager.StopAsync(["XtuService", "dptftcs"], CancellationToken.None));

        Assert.Contains("XtuService", exception.Message, StringComparison.Ordinal);
        Assert.Contains("状态不可用", exception.Message, StringComparison.Ordinal);
        Assert.Equal(["stop:XtuService"], fixture.Commands);
    }

    [Fact]
    public async Task PreCancelledOperationDoesNotSendAServiceCommand()
    {
        var fixture = new Fixture();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            fixture.Manager.StopAsync(["XtuService"], cancellation.Token));

        Assert.Empty(fixture.Commands);
    }

    [Fact]
    public async Task WaitsForRequestedTerminalStateBeforeCommandingTheNextService()
    {
        var fixture = new Fixture { CompleteCommand = false };
        fixture.AfterCommand = (_, name) =>
        {
            if (name == "dptftcs") Assert.Equal("Stopped", fixture.States["XtuService"]);
            fixture.States[name] = "Stop Pending";
        };
        fixture.AfterDelay = () =>
        {
            var name = fixture.Commands[^1].Split(':')[1];
            fixture.States[name] = "Stopped";
        };

        await fixture.Manager.StopAsync(["XtuService", "dptftcs"], CancellationToken.None);

        Assert.Equal(["stop:XtuService", "stop:dptftcs"], fixture.Commands);
        Assert.Equal(2, fixture.Delays);
        Assert.Contains(fixture.Logs, line => line.Contains("value=Stop Pending", StringComparison.Ordinal));
        Assert.Contains(fixture.Logs, line => line.Contains("completed service=dptftcs", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RestoreWaitsThroughStartPendingEvenWhenScReportsARacingAlreadyStartedCommand()
    {
        var fixture = new Fixture("Stopped") { CompleteCommand = false, ExitCode = 1056 };
        fixture.AfterCommand = (_, name) => fixture.States[name] = "Start Pending";
        fixture.AfterDelay = () => fixture.States["ipfsvc"] = "Running";

        await fixture.Manager.RestoreAsync(["ipfsvc"], CancellationToken.None);

        Assert.Equal(1, fixture.Delays);
        Assert.Equal("Running", fixture.States["ipfsvc"]);
    }

    [Theory]
    [InlineData("Stop Pending")]
    [InlineData("Running")]
    public async Task CommandSuccessWithoutTerminalStateIsBoundedAndNeverReachesTheNextService(string unchangedState)
    {
        var fixture = new Fixture { CompleteCommand = false };
        fixture.AfterCommand = (_, name) => fixture.States[name] = unchangedState;

        var exception = await Assert.ThrowsAsync<TimeoutException>(() =>
            fixture.Manager.StopAsync(["XtuService", "dptftcs"], CancellationToken.None));

        Assert.Equal(39, fixture.Delays); // Forty observations, with no real waits.
        Assert.Contains("XtuService", exception.Message, StringComparison.Ordinal);
        Assert.Contains(unchangedState, exception.Message, StringComparison.Ordinal);
        Assert.Equal(["stop:XtuService"], fixture.Commands);
    }

    [Fact]
    public async Task CancellationWhileWaitingDoesNotReachTheNextService()
    {
        var fixture = new Fixture { CompleteCommand = false };
        using var cancellation = new CancellationTokenSource();
        fixture.AfterCommand = (_, name) => fixture.States[name] = "Stop Pending";
        fixture.AfterDelay = cancellation.Cancel;

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            fixture.Manager.StopAsync(["XtuService", "dptftcs"], cancellation.Token));

        Assert.Equal(["stop:XtuService"], fixture.Commands);
    }

    [Fact]
    public async Task MissingServiceDoesNotSendACommandOrReportSuccess()
    {
        var fixture = new Fixture();
        fixture.States.Remove("XtuService");

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Manager.StopAsync(["XtuService"], CancellationToken.None));

        Assert.Contains("XtuService", exception.Message, StringComparison.Ordinal);
        Assert.Contains("未安装", exception.Message, StringComparison.Ordinal);
        Assert.Empty(fixture.Commands);
    }

    [Fact]
    public async Task DiagnosticObserverFailureDoesNotAffectServiceControl()
    {
        var fixture = new Fixture { ThrowDiagnostic = true };

        await fixture.Manager.StopAsync(["XtuService"], CancellationToken.None);

        Assert.Equal("Stopped", fixture.States["XtuService"]);
        Assert.Single(fixture.Commands);
    }

    [Fact]
    public async Task DetectAsyncCancellationDoesNotWaitForABlockedSnapshotToFinish()
    {
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        using var cancellation = new CancellationTokenSource();
        var snapshotFinished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var detector = new ConflictServiceStatusDetector(() =>
        {
            try
            {
                entered.Set();
                if (!release.Wait(TimeSpan.FromSeconds(2))) throw new TimeoutException("Test snapshot was not released.");
                return [];
            }
            finally { snapshotFinished.TrySetResult(); }
        });
        var manager = new WindowsConflictServiceManager((_, _, _) => throw new InvalidOperationException("No command expected."), detector);
        var detecting = manager.DetectAsync(cancellation.Token);
        try
        {
            Assert.True(entered.Wait(TimeSpan.FromSeconds(2)));
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => detecting.WaitAsync(TimeSpan.FromSeconds(1)));
        }
        finally
        {
            release.Set();
            await snapshotFinished.Task.WaitAsync(TimeSpan.FromSeconds(1));
        }
    }

    private sealed class Fixture
    {
        public readonly Dictionary<string, string> States;
        public readonly List<string> Commands = [];
        public readonly WindowsConflictServiceManager Manager;
        public readonly List<string> Logs = [];
        public int ExitCode;
        public int Delays;
        public bool CompleteCommand = true;
        public bool FailObservationAfterCommand;
        public bool ThrowDiagnostic;
        public Action<string, string>? AfterCommand;
        public Action? AfterDelay;

        public Fixture(string initialState = "Running")
        {
            States = new(StringComparer.OrdinalIgnoreCase)
            {
                ["XtuService"] = initialState, ["dptftcs"] = initialState, ["ipfsvc"] = initialState
            };
            var detector = new ConflictServiceStatusDetector(() =>
            {
                if (FailObservationAfterCommand && Commands.Count != 0)
                    throw new InvalidOperationException("simulated snapshot failure");
                return States.Select(pair => new WindowsServiceIdentity(pair.Key, pair.Value, "Auto")).ToArray();
            });
            Manager = new WindowsConflictServiceManager((command, name, _) =>
            {
                Commands.Add($"{command}:{name}");
                if (CompleteCommand) States[name] = command == "stop" ? "Stopped" : "Running";
                AfterCommand?.Invoke(command, name);
                return Task.FromResult(ExitCode);
            }, detector, (interval, token) =>
            {
                Assert.Equal(TimeSpan.FromMilliseconds(250), interval);
                token.ThrowIfCancellationRequested();
                Delays++;
                AfterDelay?.Invoke();
                return Task.CompletedTask;
            }, (area, message) =>
            {
                Logs.Add($"{area}: {message}");
                if (ThrowDiagnostic) throw new IOException("simulated diagnostic failure");
            });
        }
    }
}
