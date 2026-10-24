using System.Text.Json.Nodes;
using RevoHelper.Core.Control;
using RevoHelper.Hardware.Windows.OemControl;

namespace RevoHelper.Hardware.Windows.Tests.OemControl;

public sealed class GcuCustomPerformanceSettingTests
{
    private const string Status = """
        {"OperatingMode":"3","OverClockingSwitch":"1","TjMax":"105",
         "CPU_TccOffsetSwitch":"1","CPU_TccOffset":"10","CPU_PL4":"145",
         "CPU_PerformanceAndOverClockMenuSwitch":"1",
         "GPU_ConfigurableTGPSwitch":"1","GPU_ConfigurableTGPTarget":"130",
         "GPU_ConfigurableTGPMinimum":"95","GPU_ConfigurableTGPMaximum":"150",
         "GPU_DynamicBoostSwitch":"1","GPU_DynamicBoost":"20",
         "GPU_DynamicBoostMinimum":"5","GPU_DynamicBoostMaximum":"25",
         "GPU_CoreClockOffsetOC":"100","GPU_MemoryClockOffsetOC":"500",
         "GPU_CoreClockOffsetMinimum":"0","GPU_CoreClockOffsetMaximumHWOC":"250",
         "GPU_MemoryClockOffsetMinimumHWOC":"-1800","GPU_MemoryClockOffsetMaximumHWOC":"1800"}
        """;

    [Theory]
    [InlineData(CustomPerformanceSetting.GpuTgpWatts, 135, "GPU_ConfigurableTGPTarget", "GpuConfigurableTGPTarget")]
    [InlineData(CustomPerformanceSetting.GpuDynamicBoostWatts, 25, "GPU_DynamicBoost", "GpuDynamicBoost")]
    [InlineData(CustomPerformanceSetting.GpuDynamicBoostEnabled, 0, "GPU_DynamicBoostSwitch", "GpuDynamicBoostSwitch")]
    [InlineData(CustomPerformanceSetting.GpuOverclockingEnabled, 1, "OverClockingSwitch", "OverClockingSwitch")]
    [InlineData(CustomPerformanceSetting.CpuBurstPowerLimitWatts, 150, "CPU_PL4", "PL4")]
    public async Task SingleEdit_WritesOnlyRequestedSetting(CustomPerformanceSetting setting, int value, string statusKey, string commandKey)
    {
        var initial = setting == CustomPerformanceSetting.GpuOverclockingEnabled
            ? Change(Status, "OverClockingSwitch", 0)
            : Status;
        var channel = new Channel(Change(initial, statusKey, value));
        channel.Readbacks.Enqueue(initial);
        var result = await Controller(channel).ApplySettingAsync(setting, value, default);
        Assert.True(result.Succeeded, result.Reason);
        var command = JsonNode.Parse(Assert.Single(channel.Commands))!.AsObject();
        Assert.Equal(2, command.Count);
        Assert.Equal("SET_OPERATING_MODE_DETAIL", command["Action"]!.GetValue<string>());
        Assert.Equal(value.ToString(), command[commandKey]!.ToString());
    }

    [Theory]
    [InlineData(CustomPerformanceSetting.GpuTgpWatts, 135)]
    [InlineData(CustomPerformanceSetting.GpuDynamicBoostEnabled, 0)]
    [InlineData(CustomPerformanceSetting.GpuDynamicBoostWatts, 25)]
    public async Task SuperPerformanceOn_AllowsManualPowerSettingsWithoutReplayingOtherFields(CustomPerformanceSetting setting, int value)
    {
        var statusKey = setting switch
        {
            CustomPerformanceSetting.GpuTgpWatts => "GPU_ConfigurableTGPTarget",
            CustomPerformanceSetting.GpuDynamicBoostEnabled => "GPU_DynamicBoostSwitch",
            CustomPerformanceSetting.GpuDynamicBoostWatts => "GPU_DynamicBoost",
            _ => throw new ArgumentOutOfRangeException(nameof(setting))
        };
        var channel = new Channel(Change(Status, statusKey, value));
        channel.Readbacks.Enqueue(Status);
        var result = await Controller(channel).ApplySettingAsync(setting, value, default);
        Assert.True(result.Succeeded, result.Reason);
        Assert.Single(channel.Commands);
        Assert.DoesNotContain(channel.Commands, command => command.Contains("OverClockingSwitch", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(CustomPerformanceSetting.GpuTgpWatts, 135)]
    [InlineData(CustomPerformanceSetting.GpuDynamicBoostEnabled, 0)]
    [InlineData(CustomPerformanceSetting.GpuDynamicBoostWatts, 25)]
    [InlineData(CustomPerformanceSetting.GpuCoreOffsetMhz, 100)]
    [InlineData(CustomPerformanceSetting.GpuMemoryOffsetMhz, -100)]
    public async Task SuperPerformanceOff_RejectsAllDependentGpuControls(CustomPerformanceSetting setting, int value)
    {
        var channel = new Channel(Change(Status, "OverClockingSwitch", 0));
        var result = await Controller(channel).ApplySettingAsync(setting, value, default);
        Assert.False(result.Succeeded);
        Assert.Contains("开启", result.Reason);
        Assert.Empty(channel.Commands);
    }

    [Fact]
    public async Task TurningSuperPerformanceOff_WritesZeroOnlyAndDoesNotReplayPowerDraft()
    {
        var channel = new Channel(Change(Status, "OverClockingSwitch", 0));
        channel.Readbacks.Enqueue(Status);
        Assert.True((await Controller(channel).ApplySettingAsync(CustomPerformanceSetting.GpuOverclockingEnabled, 0, default)).Succeeded);
        Assert.Equal(GcuCustomPerformanceCommand.OverclockingSwitch(false), Assert.Single(channel.Commands));
    }

    [Fact]
    public async Task MissingPowerRange_DoesNotFallBackToGenericLimits()
    {
        var channel = new Channel(Status.Replace("GPU_ConfigurableTGPMinimum", "MissingMinimum"));
        Assert.False((await Controller(channel).ApplySettingAsync(CustomPerformanceSetting.GpuTgpWatts, 135, default)).Succeeded);
        Assert.Empty(channel.Commands);
    }

    [Fact]
    public async Task UnchangedSetting_DoesNotReplayWrite()
    {
        var channel = new Channel(Status);
        Assert.True((await Controller(channel).ApplySettingAsync(CustomPerformanceSetting.GpuTgpWatts, 130, default)).Succeeded);
        Assert.Empty(channel.Commands);
        Assert.Equal(1, channel.Reads);
    }

    [Fact]
    public async Task NegativeMemoryOffset_IsWrittenSignedWithoutChangingCoreOrPower()
    {
        var on = Change(Status, "OverClockingSwitch", 1);
        var channel = new Channel(Change(on, "GPU_MemoryClockOffsetOC", -100));
        channel.Readbacks.Enqueue(on);
        Assert.True((await Controller(channel).ApplySettingAsync(CustomPerformanceSetting.GpuMemoryOffsetMhz, -100, default)).Succeeded);
        Assert.Equal(GcuCustomPerformanceCommand.GpuMemoryOffset(-100), Assert.Single(channel.Commands));
    }

    [Fact]
    public async Task NegativeCoreOffset_UsesExplicitHwocMinimumWhenAvailable()
    {
        var on = Change(Change(Status, "OverClockingSwitch", 1), "GPU_CoreClockOffsetMinimumHWOC", -300);
        var channel = new Channel(Change(on, "GPU_CoreClockOffsetOC", -100));
        channel.Readbacks.Enqueue(on);
        Assert.True((await Controller(channel).ApplySettingAsync(CustomPerformanceSetting.GpuCoreOffsetMhz, -100, default)).Succeeded);
        Assert.Equal(GcuCustomPerformanceCommand.GpuCoreOffset(-100), Assert.Single(channel.Commands));
    }

    [Fact]
    public async Task NegativeCoreOffset_UsesBoundedUserInputWhenLegacyMinimumIsZero()
    {
        var channel = new Channel(Change(Status, "GPU_CoreClockOffsetOC", -300));
        channel.Readbacks.Enqueue(Status);
        var result = await Controller(channel).ApplySettingAsync(CustomPerformanceSetting.GpuCoreOffsetMhz, -300, default);
        Assert.True(result.Succeeded, result.Reason);
        Assert.Equal(GcuCustomPerformanceCommand.GpuCoreOffset(-300), Assert.Single(channel.Commands));
    }

    [Fact]
    public async Task DynamicBoostOn_ReappliesLiveWattsIfDriverCeilingDoesNotRecover()
    {
        var off = Change(Status, "GPU_DynamicBoostSwitch", 0);
        var channel = new Channel(Status);
        channel.Readbacks.Enqueue(off);
        var ceilings = new Queue<double?>([110, 110, 110, 110, 125]);
        var controller = new GcuCustomPerformanceController(channel, Path.GetTempPath(),
            (_, _) => Task.CompletedTask,
            _ => ValueTask.FromResult(ceilings.Dequeue()));

        var result = await controller.ApplySettingAsync(CustomPerformanceSetting.GpuDynamicBoostEnabled, 1, default);

        Assert.True(result.Succeeded, result.Reason);
        Assert.Equal(new[]
        {
            GcuCustomPerformanceCommand.GpuDynamicBoostSwitch(true),
            GcuCustomPerformanceCommand.GpuDynamicBoost(5),
            GcuCustomPerformanceCommand.GpuDynamicBoost(20)
        }, channel.Commands);
    }

    [Fact]
    public async Task DynamicBoostWatts_OemReadbackWithoutDriverChangeDoesNotClaimPhysicalEffect()
    {
        var channel = new Channel(Change(Status, "GPU_DynamicBoost", 5));
        channel.Readbacks.Enqueue(Status);
        var controller = new GcuCustomPerformanceController(channel, Path.GetTempPath(),
            (_, _) => Task.CompletedTask,
            _ => ValueTask.FromResult<double?>(125));

        var result = await controller.ApplySettingAsync(CustomPerformanceSetting.GpuDynamicBoostWatts, 5, default);

        Assert.False(result.Succeeded);
        Assert.Contains("驱动", result.Reason);
        Assert.Equal(GcuCustomPerformanceCommand.GpuDynamicBoost(5), Assert.Single(channel.Commands));
    }

    [Theory]
    [InlineData("OperatingMode", 1, CustomPerformanceSetting.GpuTgpWatts, 135)]
    [InlineData("GPU_ConfigurableTGPMinimum", 140, CustomPerformanceSetting.GpuTgpWatts, 135)]
    [InlineData("OverClockingSwitch", 0, CustomPerformanceSetting.GpuTgpWatts, 135)]
    [InlineData("OverClockingSwitch", 0, CustomPerformanceSetting.GpuCoreOffsetMhz, 100)]
    [InlineData("GPU_DynamicBoostSwitch", 0, CustomPerformanceSetting.GpuDynamicBoostWatts, 25)]
    public async Task UnsupportedOrInactiveSetting_IsNotWritten(string key, int current, CustomPerformanceSetting setting, int value)
    {
        var channel = new Channel(Change(Status, key, current));
        Assert.False((await Controller(channel).ApplySettingAsync(setting, value, default)).Succeeded);
        Assert.Empty(channel.Commands);
    }

    [Fact]
    public async Task StaleReadback_DoesNotClaimSuccessOrReplayWrite()
    {
        var channel = new Channel(Status);
        var result = await Controller(channel).ApplySettingAsync(CustomPerformanceSetting.GpuTgpWatts, 135, default);
        Assert.False(result.Succeeded);
        Assert.Contains("未确认", result.Reason);
        Assert.Single(channel.Commands);
        Assert.Equal(4, channel.Reads);
    }

    [Fact]
    public async Task TgpEdit_EnablesOnlyItsOwnSwitchWhenNeeded()
    {
        var channel = new Channel(Change(Status, "GPU_ConfigurableTGPTarget", 135));
        channel.Readbacks.Enqueue(Change(Status, "GPU_ConfigurableTGPSwitch", 0));
        Assert.True((await Controller(channel).ApplySettingAsync(CustomPerformanceSetting.GpuTgpWatts, 135, default)).Succeeded);
        Assert.Equal(new[] { GcuCustomPerformanceCommand.GpuConfigurableTgpSwitch(true), GcuCustomPerformanceCommand.GpuConfigurableTgp(135) }, channel.Commands);
    }

    [Fact]
    public async Task ModeChangesDuringReadback_RejectsMatchingValueInWrongMode()
    {
        var channel = new Channel(Change(Change(Status, "GPU_ConfigurableTGPTarget", 135), "OperatingMode", 1));
        channel.Readbacks.Enqueue(Status);
        Assert.False((await Controller(channel).ApplySettingAsync(CustomPerformanceSetting.GpuTgpWatts, 135, default)).Succeeded);
        Assert.Single(channel.Commands);
    }

    [Fact]
    public async Task SameCustomModeProfileChangesDuringInitialRead_PreventsAnySettingWrite()
    {
        var channel = new Channel(Change(Status, "CPU_PL4", 150));
        channel.Readbacks.Enqueue(Status);
        var controller = new GcuCustomPerformanceController(channel, Path.GetTempPath(),
            (_, _) => Task.CompletedTask, readMode: _ => ValueTask.FromResult<PlatformModeSelection?>(
                new(PlatformMode.Custom, channel.Reads == 0 ? 1 : 2)));

        var result = await controller.ApplySettingAsync(CustomPerformanceSetting.CpuBurstPowerLimitWatts, 150, default);

        Assert.False(result.Succeeded);
        Assert.Empty(channel.Commands);
    }

    [Fact]
    public async Task SameCustomModeProfileChangesAfterTgpSwitch_StopsBeforeTargetWrite()
    {
        var channel = new Channel(Change(Status, "GPU_ConfigurableTGPTarget", 135));
        channel.Readbacks.Enqueue(Change(Status, "GPU_ConfigurableTGPSwitch", 0));
        var controller = new GcuCustomPerformanceController(channel, Path.GetTempPath(),
            (_, _) => Task.CompletedTask, readMode: _ => ValueTask.FromResult<PlatformModeSelection?>(
                new(PlatformMode.Custom, channel.Commands.Count == 0 ? 1 : 2)));

        var result = await controller.ApplySettingAsync(CustomPerformanceSetting.GpuTgpWatts, 135, default);

        Assert.False(result.Succeeded);
        Assert.Equal(GcuCustomPerformanceCommand.GpuConfigurableTgpSwitch(true), Assert.Single(channel.Commands));
    }

    [Fact]
    public async Task SameCustomModeProfileChangesDuringConfirmation_DoesNotClaimSuccess()
    {
        var channel = new Channel(Change(Status, "CPU_PL4", 150));
        channel.Readbacks.Enqueue(Status);
        var controller = new GcuCustomPerformanceController(channel, Path.GetTempPath(),
            (_, _) => Task.CompletedTask, readMode: _ => ValueTask.FromResult<PlatformModeSelection?>(
                new(PlatformMode.Custom, channel.Reads < 2 ? 1 : 2)));

        var result = await controller.ApplySettingAsync(CustomPerformanceSetting.CpuBurstPowerLimitWatts, 150, default);

        Assert.False(result.Succeeded);
        Assert.Single(channel.Commands);
    }

    [Fact]
    public async Task ProfileAwareSetting_RejectsDraftForOtherCustomProfile()
    {
        var channel = new Channel(Change(Status, "CPU_PL4", 150));
        channel.Readbacks.Enqueue(Status);
        ICustomPerformanceController controller = new GcuCustomPerformanceController(channel, Path.GetTempPath(),
            (_, _) => Task.CompletedTask, readMode: _ => ValueTask.FromResult<PlatformModeSelection?>(new(PlatformMode.Custom, 2)));

        Assert.False((await controller.ApplySettingAsync(1, CustomPerformanceSetting.CpuBurstPowerLimitWatts, 150, default)).Succeeded);
        Assert.Empty(channel.Commands);
    }

    [Fact]
    public async Task Snapshot_SlotChangesWhileReadingDoesNotReturnForeignProfileAsRequestedDraft()
    {
        var channel = new Channel(Status);
        var controller = new GcuCustomPerformanceController(channel, Path.GetTempPath(),
            readMode: _ => ValueTask.FromResult<PlatformModeSelection?>(new(PlatformMode.Custom, channel.Reads == 0 ? 1 : 2)));

        var result = await controller.ReadSnapshotAsync(1, default);

        Assert.Null(result.State);
        Assert.Null(result.Gpu);
        Assert.Empty(channel.Commands);
    }

    private static ICustomPerformanceController Controller(Channel channel) =>
        new GcuCustomPerformanceController(channel, Path.GetTempPath(), (_, _) => Task.CompletedTask);

    private static string Change(string source, string key, int value)
    {
        var node = JsonNode.Parse(source)!;
        node[key] = value.ToString();
        return node.ToJsonString();
    }

    private sealed class Channel(string status) : IGcuCustomPerformanceChannel
    {
        public Queue<string> Readbacks { get; } = [];
        public List<string> Commands { get; } = [];
        public int Reads { get; private set; }
        public ValueTask<string> ReadCustomPerformanceStatusAsync(CancellationToken token)
        { Reads++; return ValueTask.FromResult(Readbacks.Count > 0 ? Readbacks.Dequeue() : status); }
        public ValueTask WriteCustomPerformanceAsync(string payload, CancellationToken token)
        { Commands.Add(payload); return ValueTask.CompletedTask; }
    }
}
