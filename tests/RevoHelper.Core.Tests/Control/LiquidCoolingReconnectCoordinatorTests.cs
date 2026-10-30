using RevoHelper.Core.Control;

namespace RevoHelper.Core.Tests.Control;

public sealed class LiquidCoolingReconnectCoordinatorTests
{
    private static readonly DateTimeOffset Started = new(2026, 10, 30, 22, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task NeverConnectedOrReadFailureNeverArmsOrConnects()
    {
        var f = new Fixture();
        f.Coordinator.Observe(null);
        f.Coordinator.Observe(State(false));
        f.Now += TimeSpan.FromMinutes(1);
        await f.Attempt();
        Assert.Null(f.Coordinator.Deadline);
        Assert.Empty(f.Targets);
    }

    [Fact]
    public async Task ConfirmedDisconnectRemembersSameDeviceForFixedTenMinutesAndThreeSecondAttempts()
    {
        var f = new Fixture(); f.DisconnectAfterConfirmedConnection();
        var deadline = f.Coordinator.Deadline;
        Assert.Equal(Started.AddMinutes(10), deadline);
        await f.Attempt(); Assert.Empty(f.Targets);
        f.Now += TimeSpan.FromSeconds(3);
        await f.Attempt(); Assert.Single(f.Targets); Assert.Equal("cooler-a", f.Targets[0]);
        await f.Attempt(); Assert.Single(f.Targets);
        f.Now += TimeSpan.FromSeconds(2); await f.Attempt(); Assert.Single(f.Targets);
        f.Now += TimeSpan.FromSeconds(1); await f.Attempt(); Assert.Equal(2, f.Targets.Count);
        f.Coordinator.Observe(State(false)); f.Coordinator.Observe(null);
        Assert.Equal(deadline, f.Coordinator.Deadline);
        f.Now = Started.AddMinutes(10); await f.Attempt();
        Assert.Equal(2, f.Targets.Count); Assert.Null(f.Coordinator.Deadline);
    }

    [Fact]
    public async Task NullStatusAndAcceptedCommandDoNotClaimReconnectedOrExtendDeadline()
    {
        var f = new Fixture(); f.DisconnectAfterConfirmedConnection();
        var deadline = f.Coordinator.Deadline;
        f.Now += TimeSpan.FromSeconds(3); f.Current = null;
        await f.Attempt(); Assert.Empty(f.Targets); Assert.Equal(deadline, f.Coordinator.Deadline);
        f.Now += TimeSpan.FromSeconds(3); f.Current = State(false);
        var accepted = await f.Attempt();
        Assert.True(accepted.Attempted); Assert.False(accepted.Connected);
        Assert.Equal(deadline, f.Coordinator.Deadline);
        f.Now += TimeSpan.FromSeconds(3); f.Current = State(true);
        var restored = await f.Attempt();
        Assert.True(restored.Connected); Assert.Null(f.Coordinator.Deadline);
        Assert.Single(f.Targets);
    }

    [Theory]
    [InlineData("cooler-b", false)]
    [InlineData("cooler-a", true)]
    public async Task ChangedSelectionOrPrimingCancelsWithoutSelectingOrScanning(string? device, bool priming)
    {
        var f = new Fixture(); f.DisconnectAfterConfirmedConnection();
        f.Now += TimeSpan.FromSeconds(3); f.Current = State(false, device, priming);
        await f.Attempt();
        Assert.Empty(f.Targets); Assert.Null(f.Coordinator.Deadline);
    }

    [Fact]
    public async Task TemporaryMissingDeviceIdPreservesOriginalDeadlineAndOnlyReconnectsAfterSameIdReturns()
    {
        var f = new Fixture(); f.Coordinator.Observe(State(true));
        f.Current = State(false, null); f.Coordinator.Observe(f.Current);
        var deadline = f.Coordinator.Deadline;
        Assert.Equal(Started.AddMinutes(10), deadline);
        f.Now += TimeSpan.FromSeconds(3); await f.Attempt();
        Assert.Empty(f.Targets); Assert.Equal(deadline, f.Coordinator.Deadline);
        f.Now += TimeSpan.FromSeconds(3); f.Current = State(false);
        await f.Attempt();
        Assert.Single(f.Targets); Assert.Equal("cooler-a", f.Targets[0]);
        Assert.Equal(deadline, f.Coordinator.Deadline);
    }

    [Fact]
    public void DeadlineExpiresEvenWhileAllLaterReadsAreUnavailable()
    {
        var f = new Fixture(); f.DisconnectAfterConfirmedConnection();
        f.Now = Started.AddMinutes(10); f.Coordinator.Observe(null);
        Assert.Null(f.Coordinator.Deadline);
    }

    [Fact]
    public async Task ManualCancellationSuppressesStaleConnectedPollUntilExplicitNewConnection()
    {
        var f = new Fixture(); f.DisconnectAfterConfirmedConnection();
        f.Coordinator.Cancel();
        f.Coordinator.Observe(State(true)); f.Coordinator.Observe(State(false));
        f.Now += TimeSpan.FromSeconds(3); await f.Attempt();
        Assert.Empty(f.Targets); Assert.Null(f.Coordinator.Deadline);
        f.Coordinator.RememberConnected(State(true)); f.Coordinator.Observe(State(false));
        f.Now += TimeSpan.FromSeconds(3); await f.Attempt();
        Assert.Single(f.Targets);
    }

    [Fact]
    public async Task ConcurrentAttemptsAreSingleFlightAndManualCancelInvalidatesLateResult()
    {
        var f = new Fixture(); f.DisconnectAfterConfirmedConnection(); f.Now += TimeSpan.FromSeconds(3);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = f.Coordinator.TryReconnectAsync(_ => ValueTask.FromResult<LiquidCoolingState?>(State(false)),
            async (_, token) => { entered.SetResult(); await release.Task.WaitAsync(token); return new(true, "accepted"); }, CancellationToken.None);
        await entered.Task;
        var concurrent = await f.Attempt(); Assert.False(concurrent.Attempted);
        f.Coordinator.Cancel(); release.TrySetResult();
        var cancelled = await first; Assert.False(cancelled.Connected);
        Assert.Null(f.Coordinator.Deadline);
    }

    [Fact]
    public async Task FailuresStayBoundedAndDoNotResetDeadline()
    {
        var f = new Fixture(); f.DisconnectAfterConfirmedConnection(); f.Now += TimeSpan.FromSeconds(3);
        var deadline = f.Coordinator.Deadline;
        var result = await f.Coordinator.TryReconnectAsync(_ => throw new IOException("temporary status failure"),
            (_, _) => throw new InvalidOperationException("must not connect"), CancellationToken.None);
        Assert.False(result.Connected); Assert.Equal(deadline, f.Coordinator.Deadline);
        f.Now = Started.AddMinutes(10); await f.Attempt(); Assert.Null(f.Coordinator.Deadline);
    }

    internal static LiquidCoolingState State(bool connected, string? device = "cooler-a", bool priming = false) =>
        new(connected, connected ? "Connected" : "Disconnected", null, null, null, null, [], device, null,
            CanControl: true, IsPriming: priming, MeterNormal: true);

    private sealed class Fixture
    {
        public DateTimeOffset Now = Started;
        public LiquidCoolingState? Current = State(false);
        public readonly List<string> Targets = [];
        public LiquidCoolingReconnectCoordinator Coordinator { get; }
        public Fixture() => Coordinator = new(() => Now);
        public void DisconnectAfterConfirmedConnection()
        { Coordinator.Observe(State(true)); Coordinator.Observe(State(false)); }
        public Task<LiquidCoolingReconnectResult> Attempt() => Coordinator.TryReconnectAsync(
            _ => ValueTask.FromResult(Current), (device, _) =>
            { Targets.Add(device); return ValueTask.FromResult(new ControlResult(true, "command accepted, not confirmed")); }, CancellationToken.None);
    }
}
