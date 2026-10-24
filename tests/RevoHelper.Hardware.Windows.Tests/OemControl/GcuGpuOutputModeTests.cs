using RevoHelper.Core.Control;
using RevoHelper.Hardware.Windows.OemControl;

namespace RevoHelper.Hardware.Windows.Tests.OemControl;

public sealed class GcuGpuOutputModeTests
{
    [Theory]
    [InlineData(GpuOutputMode.Hybrid, "{\"Action\":\"DGPU_DIRECT_CONNECT_TOGGLE_OFF\"}")]
    [InlineData(GpuOutputMode.Discrete, "{\"Action\":\"DGPU_DIRECT_CONNECT_TOGGLE_ON\"}")]
    [InlineData(GpuOutputMode.Integrated, "{\"Action\":\"DGPU_DIRECT_CONNECT_TOGGLE_IGPU\"}")]
    public void Select_UsesOfficialAction(GpuOutputMode mode, string expected) =>
        Assert.Equal(expected, GcuGpuOutputModeCommand.Select(mode));

    [Theory]
    [InlineData("DGPU_DIRECT_CONNECT_TOGGLE_OFF", GpuOutputMode.Hybrid)]
    [InlineData("DGPU_DIRECT_CONNECT_TOGGLE_ON", GpuOutputMode.Discrete)]
    [InlineData("DGPU_DIRECT_CONNECT_TOGGLE_IGPU", GpuOutputMode.Integrated)]
    public void Parser_MapsKnownReadback(string status, GpuOutputMode expected)
    {
        var json = "{\"MySetting\":{\"DiscreteGpuDirectConnectionSwitch_Status\":\"" + status +
                   "\",\"DiscreteGpuDirectConnectionSwitch_Support\":\"1\",\"IGpuOnlyConnectionSwitch_Support\":\"1\"}}";

        var parsed = GcuGpuOutputModeStatusParser.Parse(json);

        Assert.NotNull(parsed);
        Assert.Equal(expected, parsed.Mode);
        Assert.True(parsed.SupportsDirectSwitch);
        Assert.True(parsed.SupportsIntegrated);
    }

    [Fact]
    public async Task Controller_RejectsIntegratedBeforeWritingWhenMachineDoesNotSupportIt()
    {
        var channel = new FakeChannel(new(GpuOutputMode.Hybrid, true, false));
        var controller = new GcuGpuOutputModeController(channel, new FakeCapabilities(true, false));

        var result = await controller.SetModeAsync(GpuOutputMode.Integrated, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Empty(channel.Writes);
    }

    [Fact]
    public async Task Controller_VerifiesReadbackBeforeReportingSuccess()
    {
        var channel = new FakeChannel(new(GpuOutputMode.Hybrid, true, true));
        var controller = new GcuGpuOutputModeController(channel, new FakeCapabilities(true, true));

        var result = await controller.SetModeAsync(GpuOutputMode.Discrete, CancellationToken.None);

        Assert.True(result.Succeeded, result.Reason);
        Assert.Equal([GpuOutputMode.Discrete], channel.Writes);
        Assert.Equal(GpuOutputMode.Discrete, channel.Status.Mode);
    }

    private sealed class FakeCapabilities(bool direct, bool integrated) : IGpuOutputModeCapabilitySource
    {
        public GpuOutputModeCapabilities Read() => new(direct, direct, integrated);
    }

    private sealed class FakeChannel(GcuGpuOutputModeStatus status) : IGcuGpuOutputModeChannel
    {
        public GcuGpuOutputModeStatus Status { get; private set; } = status;
        public List<GpuOutputMode> Writes { get; } = [];

        public ValueTask<GcuGpuOutputModeStatus> ReadStatusAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult(Status);

        public ValueTask WriteModeAsync(GpuOutputMode mode, CancellationToken cancellationToken)
        {
            Writes.Add(mode);
            Status = Status with { Mode = mode };
            return ValueTask.CompletedTask;
        }
    }
}
