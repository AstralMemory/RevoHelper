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

    [Theory]
    // Live GCU answer decides, whatever the registry says.
    [InlineData(true, null, true)]
    [InlineData(true, false, true)]
    [InlineData(false, true, false)]
    // GCU silent: an explicit registry value speaks, and missing values stay attemptable.
    [InlineData(null, true, true)]
    [InlineData(null, false, false)]
    [InlineData(null, null, true)]
    public async Task Controller_DistinguishesMissingSupportInformationFromExplicitNo(
        bool? live, bool? machine, bool allowed)
    {
        var channel = new FakeChannel(new(GpuOutputMode.Hybrid, live, live));
        var controller = new GcuGpuOutputModeController(channel, new FakeCapabilities(machine, machine));

        var state = await controller.ReadStateAsync(CancellationToken.None);
        var discrete = await controller.SetModeAsync(GpuOutputMode.Discrete, CancellationToken.None);
        var integrated = await controller.SetModeAsync(GpuOutputMode.Integrated, CancellationToken.None);

        Assert.Equal(allowed, state!.Capabilities.SupportsDiscrete);
        Assert.Equal(allowed, state.Capabilities.SupportsIntegrated);
        Assert.Equal(allowed, discrete.Succeeded);
        Assert.Equal(allowed, integrated.Succeeded);
        Assert.Equal(allowed ? [GpuOutputMode.Discrete, GpuOutputMode.Integrated] : [], channel.Writes);
    }

    [Theory]
    [InlineData(1, true)]
    [InlineData(0, false)]
    [InlineData(1L, true)]
    [InlineData(0L, false)]
    [InlineData("1", true)]
    [InlineData("true", true)]
    [InlineData("0", false)]
    [InlineData("false", false)]
    [InlineData("unexpected", null)]
    [InlineData(null, null)]
    public void RegistryFlag_MissingOrUnreadableIsUnknownNotUnsupported(object? value, bool? expected) =>
        Assert.Equal(expected, WindowsGpuOutputModeCapabilitySource.ParseFlag(value));

    private sealed class FakeCapabilities(bool? direct, bool? integrated) : IGpuOutputModeCapabilitySource
    {
        public GpuOutputMachineSupport Read() => new(direct, integrated);
    }

    [Theory]
    [InlineData("NVIDIA GeForce RTX 5090 Laptop GPU", true)]
    [InlineData("AMD Radeon RX 7600M XT", true)]
    [InlineData("Intel(R) Graphics", false)]
    [InlineData("AMD Radeon(TM) 780M Graphics", false)]
    [InlineData("MuMu Virtual Display Adapter", null)]
    [InlineData("", null)]
    public void PanelAdapter_IsClassifiedByNameOrLeftUnknown(string name, bool? discrete) =>
        Assert.Equal(discrete, RevoHelper.Hardware.Windows.Display.Win32DisplayModeApi.ClassifyAdapterAsDiscrete(name));

    [Fact]
    public async Task ReadState_ReportsThePanelSoAPendingSettingIsVisible_AndSurvivesAProbeFailure()
    {
        var channel = new FakeChannel(new(GpuOutputMode.Discrete, true, true));
        var pending = await new GcuGpuOutputModeController(channel, new FakeCapabilities(true, true), () => false)
            .ReadStateAsync(CancellationToken.None);
        Assert.True(pending!.IsPendingRestart);

        var broken = await new GcuGpuOutputModeController(channel, new FakeCapabilities(true, true),
            () => throw new InvalidOperationException("display query failed")).ReadStateAsync(CancellationToken.None);
        Assert.Null(broken!.PanelOnDiscreteGpu);
        Assert.False(broken.IsPendingRestart);
    }

    // Read-only check against this machine; prints nothing and never fails on an unknown panel.
    [Fact]
    public void PanelProbe_RunsWithoutThrowing()
    {
        var value = RevoHelper.Hardware.Windows.Display.Win32DisplayModeApi.ReadInternalPanelOnDiscreteGpu();
        if (Environment.GetEnvironmentVariable("REVOHELPER_PANEL_PROBE_FILE") is { Length: > 0 } file)
            File.WriteAllText(file, value?.ToString() ?? "null");
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
