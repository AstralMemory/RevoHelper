using RevoHelper.Core.Control;
using RevoHelper.Hardware.Windows.OemControl;

namespace RevoHelper.Hardware.Windows.Tests.OemControl;

public sealed class GcuCustomPerformanceControllerTests
{
    private const string ClockStatus = """
        {"OperatingMode":"3","OverClockingSwitch":"1",
         "GPU_CoreClockOffsetOC":"100","GPU_MemoryClockOffsetOC":"500",
         "GPU_CoreClockOffsetMinimum":"0","GPU_CoreClockOffsetMaximumHWOC":"250",
         "GPU_MemoryClockOffsetMinimumHWOC":"-1800","GPU_MemoryClockOffsetMaximumHWOC":"1800"}
        """;

    [Fact]
    public async Task OffsetEdit_WritesOnlyChangedClockFieldAndChecksFreshReadback()
    {
        var channel = new FakeCustomChannel(ClockStatus.Replace("\"100\"", "\"125\""));
        channel.Statuses.Enqueue(ClockStatus);
        ICustomPerformanceController controller = new GcuCustomPerformanceController(channel, Path.GetTempPath(), (_, _) => Task.CompletedTask);
        var result = await controller.ApplyGpuClockOffsetsAsync(new(125, 500), default);
        Assert.True(result.Succeeded);
        Assert.Single(channel.Commands);
        Assert.Equal(GcuCustomPerformanceCommand.GpuCoreOffset(125), channel.Commands[0]);
        Assert.Equal(2, channel.Reads);
    }

    [Fact]
    public async Task ProfileAwareClockEdit_RejectsDraftForOtherCustomProfile()
    {
        var channel = new FakeCustomChannel(ClockStatus.Replace("\"100\"", "\"125\""));
        channel.Statuses.Enqueue(ClockStatus);
        ICustomPerformanceController controller = new GcuCustomPerformanceController(channel, Path.GetTempPath(),
            (_, _) => Task.CompletedTask, readMode: _ => ValueTask.FromResult<PlatformModeSelection?>(new(PlatformMode.Custom, 2)));

        Assert.False((await controller.ApplyGpuClockOffsetsAsync(1, new(125, 500), default)).Succeeded);
        Assert.Empty(channel.Commands);
    }

    [Theory]
    [InlineData("\"OperatingMode\":\"3\"", "\"OperatingMode\":\"1\"", 125)]
    [InlineData("\"OverClockingSwitch\":\"1\"", "\"OverClockingSwitch\":\"0\"", 125)]
    [InlineData("GPU_CoreClockOffsetOC", "MissingCore", 125)]
    [InlineData("GPU_CoreClockOffsetMinimum", "MissingMinimum", 125)]
    [InlineData("unused", "unused", 300)]
    public async Task OffsetEdit_RejectsUnavailableOrOutOfOemRangeWithoutWriting(string oldText, string newText, int core)
    {
        var channel = new FakeCustomChannel(ClockStatus.Replace(oldText, newText));
        ICustomPerformanceController controller = new GcuCustomPerformanceController(channel, Path.GetTempPath(), (_, _) => Task.CompletedTask);
        Assert.False((await controller.ApplyGpuClockOffsetsAsync(new(core, 500), default)).Succeeded);
        Assert.Empty(channel.Commands);
    }

    [Fact]
    public async Task OffsetEdit_StaleReadbackNeverReportsSuccessOrReplaysWrites()
    {
        var channel = new FakeCustomChannel(ClockStatus);
        ICustomPerformanceController controller = new GcuCustomPerformanceController(channel, Path.GetTempPath(), (_, _) => Task.CompletedTask);
        Assert.False((await controller.ApplyGpuClockOffsetsAsync(new(125, 600), default)).Succeeded);
        Assert.Equal(2, channel.Commands.Count);
        Assert.Equal(4, channel.Reads);
    }

    [Fact]
    public async Task OffsetRead_IntersectsOemLimitsWithNarrowerInputBounds()
    {
        var controller = new GcuCustomPerformanceController(new FakeCustomChannel(ClockStatus), Path.GetTempPath());
        var state = await controller.ReadGpuClockOffsetsAsync(default);
        Assert.NotNull(state);
        Assert.Equal(new GpuClockOffsetLimits(-300, 250, -1000, 1500), state.Limits);
        Assert.Equal(new GpuClockOffsets(100, 500), state.Offsets);
    }

    [Fact]
    public async Task OffsetEdit_MemoryOnlyDoesNotResendCoreOffset()
    {
        var channel = new FakeCustomChannel(ClockStatus.Replace("\"500\"", "\"600\""));
        channel.Statuses.Enqueue(ClockStatus);
        var controller = new GcuCustomPerformanceController(channel, Path.GetTempPath());
        Assert.True((await controller.ApplyGpuClockOffsetsAsync(new(100, 600), default)).Succeeded);
        Assert.Equal(GcuCustomPerformanceCommand.GpuMemoryOffset(600), Assert.Single(channel.Commands));
    }

    [Fact]
    public async Task Apply_WritesEveryOfficialAdvancedFieldAndConfirmsReadback()
    {
        const string status = """
            {"OverClockingEnabled":1,"CPU":{"TccOffsetSwitch":1,"TccOffset":10,"PL4":145,"PerformanceAndOverClockMenuSwitch":1},
             "GPU":{"ConfigurableTGPSwitch":1,"ConfigurableTGPTarget":150,"DynamicBoostSwitch":1,"DynamicBoost":25,"CoreClockOffsetOC":100,"MemoryClockOffsetOC":500}}
            """;
        var channel = new FakeCustomChannel(status);
        var controller = new GcuCustomPerformanceController(channel, Path.GetTempPath());
        var target = new CustomPerformanceState(true, true, 95, 145, true, 150, true, 25, 100, 500);

        var result = await controller.ApplyAsync(1, target, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Contains("回读确认", result.Reason, StringComparison.Ordinal);
        Assert.Equal(11, channel.Commands.Count);
        Assert.Contains(channel.Commands, command => command.Contains("GpuConfigurableTGPSwitch", StringComparison.Ordinal));
        Assert.All(channel.Commands, command => Assert.Contains("SET_OPERATING_MODE_DETAIL", command));
    }

    [Fact]
    public async Task ApplyGpu_WritesOnlyGpuFieldsForTheCurrentOemMode()
    {
        const string status = """
            {"OverClockingEnabled":1,
             "GPU":{"ConfigurableTGPSwitch":1,"ConfigurableTGPTarget":150,"DynamicBoostSwitch":1,"DynamicBoost":25,"CoreClockOffsetOC":100,"MemoryClockOffsetOC":500}}
            """;
        var channel = new FakeCustomChannel(status);
        var controller = new GcuCustomPerformanceController(channel, Path.GetTempPath());
        var target = new GpuPerformanceState(true, 150, true, 25, 100, 500);

        var result = await controller.ApplyGpuAsync(target, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(7, channel.Commands.Count);
        Assert.Contains(channel.Commands, command => command.Contains("OverClockingSwitch", StringComparison.Ordinal));
        Assert.Contains(channel.Commands, command => command.Contains("GpuConfigurableTGPSwitch", StringComparison.Ordinal));
        Assert.Contains(channel.Commands, command => command.Contains("GpuConfigurableTGPTarget", StringComparison.Ordinal));
        Assert.DoesNotContain(channel.Commands, command =>
            command.Contains("CpuTcc", StringComparison.Ordinal) || command.Contains("PL4", StringComparison.Ordinal) ||
            command.Contains("CPUPerformance", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ApplyGpu_WhenOemReadbackDoesNotMatch_ReturnsRejectedInsteadOfFalseSuccess()
    {
        const string status = """
            {"OverClockingEnabled":0,
             "GPU":{"ConfigurableTGPSwitch":0,"ConfigurableTGPTarget":115,"DynamicBoostSwitch":0,"DynamicBoost":0,"CoreClockOffsetOC":0,"MemoryClockOffsetOC":0}}
            """;
        var controller = new GcuCustomPerformanceController(new FakeCustomChannel(status), Path.GetTempPath());

        var result = await controller.ApplyGpuAsync(
            new GpuPerformanceState(true, 150, true, 25, 100, 500), CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains("未确认", result.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ApplyGpu_WaitsForOemStatusToConvergeBeforeConfirming()
    {
        const string stale = """
            {"OverClockingEnabled":0,
             "GPU":{"ConfigurableTGPSwitch":0,"ConfigurableTGPTarget":115,"DynamicBoostSwitch":0,"DynamicBoost":0,"CoreClockOffsetOC":0,"MemoryClockOffsetOC":0}}
            """;
        const string targetStatus = """
            {"OverClockingEnabled":1,
             "GPU":{"ConfigurableTGPSwitch":1,"ConfigurableTGPTarget":150,"DynamicBoostSwitch":1,"DynamicBoost":25,"CoreClockOffsetOC":100,"MemoryClockOffsetOC":500}}
            """;
        var channel = new FakeCustomChannel(stale);
        channel.Statuses.Enqueue(stale);
        channel.Statuses.Enqueue(targetStatus);
        var controller = new GcuCustomPerformanceController(
            channel, Path.GetTempPath(), (_, _) => Task.CompletedTask);

        var result = await controller.ApplyGpuAsync(
            new GpuPerformanceState(true, 150, true, 25, 100, 500), CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(2, channel.Reads);
    }

    private sealed class FakeCustomChannel(string status) : IGcuCustomPerformanceChannel
    {
        public List<string> Commands { get; } = [];
        public Queue<string> Statuses { get; } = [];
        public int Reads { get; private set; }
        public ValueTask<string> ReadCustomPerformanceStatusAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult(ReadStatus());
        public ValueTask WriteCustomPerformanceAsync(string payload, CancellationToken cancellationToken)
        {
            Commands.Add(payload);
            return ValueTask.CompletedTask;
        }

        private string ReadStatus()
        {
            Reads++;
            return Statuses.Count > 0 ? Statuses.Dequeue() : status;
        }
    }
}
