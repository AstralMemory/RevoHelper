using RevoHelper.App.Charging;

namespace RevoHelper.App.Tests.Charging;

// No real directory scans, session file I/O, events, helpers, or EC access.
// Only logical 100 is enabled, with injected in-memory pending-session discovery.
public sealed class ExperimentalChargeClientTests
{
    [Fact]
    public async Task HundredIsLogicalEnabledWithoutASessionAndStopFullyDisablesIt()
    {
        var scans = 0;
        using var client = new ExperimentalChargeClient(() => { scans++; return null; });
        Assert.False(client.Status.Enabled); Assert.Equal(0, scans);
        await client.EnableAsync(100);
        Assert.True(client.Status.Enabled); Assert.Equal(100, client.Status.LimitPercent);
        Assert.False(client.Status.Busy); Assert.False(client.Status.RecoveryPending); Assert.Null(client.Status.SessionId);
        Assert.Contains("不限制", client.Status.Message); Assert.Contains("原生策略", client.Status.Message);
        Assert.Equal(1, scans);
        await client.EnableAsync(100);
        Assert.Equal(1, scans); // A repeated logical-on does not spawn/recreate anything.
        await client.StopAsync("关闭");
        Assert.False(client.Status.Enabled); Assert.False(client.Status.Busy); Assert.False(client.Status.RecoveryPending);
        Assert.Null(client.Status.SessionId);
    }

    [Fact]
    public async Task RequestStopAndDisposeClearLogicalHundredWithoutWaitingForAHelper()
    {
        var client = new ExperimentalChargeClient(() => null);
        await client.EnableAsync(100);
        client.RequestStop("模拟拔电或睡眠");
        Assert.False(client.Status.Enabled); Assert.False(client.Status.Busy);
        await client.EnableAsync(100);
        Assert.True(client.Status.Enabled);
        client.Dispose(); client.Dispose();
        Assert.False(client.Status.Enabled); Assert.False(client.Status.Busy); Assert.Null(client.Status.SessionId);
    }

    [Fact]
    public async Task PendingRecoveryIsNeverBypassedByHundred()
    {
        var pending = new ChargeSessionFiles("bb01ce2a69fe4a9f8875cebd148052a1");
        using var client = new ExperimentalChargeClient(() => pending);
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.EnableAsync(100));
        Assert.False(client.Status.Enabled); Assert.True(client.Status.RecoveryPending);
        Assert.Equal(pending.SessionId, client.Status.SessionId);
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.EnableAsync(100));
        Assert.False(client.Status.Enabled); Assert.True(client.Status.RecoveryPending);
    }

    [Fact]
    public async Task StopWhilePendingInspectionRunsWinsOverLogicalHundredEnable()
    {
        using var entered = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
        using var client = new ExperimentalChargeClient(() =>
        {
            entered.Set();
            if (!release.Wait(TimeSpan.FromSeconds(2))) throw new TimeoutException("Test wait expired.");
            return null;
        });
        var enable = Task.Run(() => client.EnableAsync(100));
        try
        {
            Assert.True(entered.Wait(TimeSpan.FromSeconds(2)));
            client.RequestStop("停止最新请求");
        }
        finally { release.Set(); }
        await enable;
        Assert.False(client.Status.Enabled); Assert.False(client.Status.Busy); Assert.Null(client.Status.SessionId);
    }

    [Fact]
    public async Task HundredDoesNotClearARecoveryRecordDiscoveredAfterLogicalEnable()
    {
        ChargeSessionFiles? pending = null;
        using var client = new ExperimentalChargeClient(() => pending);
        await client.EnableAsync(100);
        pending = new("bb01ce2a69fe4a9f8875cebd148052a1");
        client.InspectPendingRecovery();
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.EnableAsync(100));
        Assert.True(client.Status.RecoveryPending); Assert.False(client.Status.Enabled);
    }

    [Fact]
    public async Task ImmediateStopObserverCannotLeaveLogicalHundredEnabled()
    {
        using var client = new ExperimentalChargeClient(() => null);
        client.StatusChanged += (_, _) =>
        {
            if (client.Status.Enabled) client.RequestStop("开启期间立即停止");
        };
        await client.EnableAsync(100);
        Assert.False(client.Status.Enabled); Assert.False(client.Status.Busy); Assert.Null(client.Status.SessionId);
    }

    [Theory]
    [InlineData(0)] [InlineData(59)] [InlineData(75)] [InlineData(99)] [InlineData(101)]
    public async Task InvalidLogicalSelectionsFailBeforePendingInspection(int limit)
    {
        var scans = 0;
        using var client = new ExperimentalChargeClient(() => { scans++; return null; });
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => client.EnableAsync(limit));
        Assert.Equal(0, scans); Assert.False(client.Status.Enabled);
    }
}
