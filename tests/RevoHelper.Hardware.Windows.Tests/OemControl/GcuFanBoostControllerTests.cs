using RevoHelper.Core.Control;
using RevoHelper.Hardware.Windows.OemControl;

namespace RevoHelper.Hardware.Windows.Tests.OemControl;

public sealed class GcuFanBoostControllerTests
{
    [Fact]
    public async Task ReadAsync_WhenStatusFieldExists_ReportsSupportedState()
    {
        var state = await new GcuFanBoostController(new FakeChannel(Status(1))).ReadAsync(default);

        Assert.True(state.Supported);
        Assert.True(state.Enabled);
    }

    [Fact]
    public async Task ReadAsync_WhenStatusFieldIsMissing_ReportsUnsupported()
    {
        var state = await new GcuFanBoostController(new FakeChannel(Status(null))).ReadAsync(default);

        Assert.False(state.Supported);
        Assert.False(state.Enabled);
    }

    [Fact]
    public async Task SetAsync_WritesOfficialActionAndRequiresMatchingReadback()
    {
        var channel = new FakeChannel(Status(0));
        var result = await new GcuFanBoostController(channel, NoDelay).SetAsync(true, default);

        Assert.True(result.Succeeded, result.Reason);
        Assert.Equal(["{\"Action\":\"FAN_BOOST_ON\"}"], channel.Writes);
        Assert.Equal(1, channel.Status.FanBoostEnable);
    }

    [Fact]
    public async Task SetAsync_WhenUnsupported_DoesNotWrite()
    {
        var channel = new FakeChannel(Status(null));
        var result = await new GcuFanBoostController(channel, NoDelay).SetAsync(true, default);

        Assert.False(result.Succeeded);
        Assert.Empty(channel.Writes);
    }

    [Fact]
    public async Task SetAsync_WhenTargetDoesNotConverge_RestoresBaseline()
    {
        var channel = new FakeChannel(Status(0)) { IgnoreFirstWrite = true };
        var result = await new GcuFanBoostController(channel, NoDelay).SetAsync(true, default);

        Assert.False(result.Succeeded);
        Assert.Contains("恢复", result.Reason, StringComparison.Ordinal);
        Assert.Equal([
            "{\"Action\":\"FAN_BOOST_ON\"}",
            "{\"Action\":\"FAN_BOOST_OFF\"}"
        ], channel.Writes);
        Assert.Equal(0, channel.Status.FanBoostEnable);
    }

    private static Task NoDelay(TimeSpan delay, CancellationToken token) => Task.CompletedTask;

    private static GcuCpuPolicyStatus Status(int? enabled) =>
        new(45, 65, 5, 210, 5, 210, FanBoostEnable: enabled);

    private sealed class FakeChannel(GcuCpuPolicyStatus status) : IGcuFanBoostChannel
    {
        private int writeCount;
        public GcuCpuPolicyStatus Status { get; private set; } = status;
        public List<string> Writes { get; } = [];
        public bool IgnoreFirstWrite { get; init; }

        public ValueTask<GcuCpuPolicyStatus> ReadFanBoostStatusAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult(Status);

        public ValueTask WriteFanBoostAsync(string payload, CancellationToken cancellationToken)
        {
            writeCount++;
            Writes.Add(payload);
            if (!IgnoreFirstWrite || writeCount > 1)
            {
                Status = Status with
                {
                    FanBoostEnable = payload.Contains("_ON", StringComparison.Ordinal) ? 1 : 0
                };
            }
            return ValueTask.CompletedTask;
        }
    }
}
