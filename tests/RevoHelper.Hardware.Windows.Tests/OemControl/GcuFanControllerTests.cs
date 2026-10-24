using RevoHelper.Core.Fans;
using RevoHelper.Hardware.Windows.OemControl;

namespace RevoHelper.Hardware.Windows.Tests.OemControl;

public sealed class GcuFanControllerTests
{
    [Fact]
    public async Task ApplyAsync_WritesBothCurvesAndResponseSettingsThenConfirmsExactReadback()
    {
        var baseline = CreateTable("Profile1", 20);
        var target = CreateTable("Profile1", 25);
        var response = new FanResponseSettings(false, true, 18);
        var channel = new FakeFanChannel(baseline, new(true, false, 10));
        var controller = new GcuFanController(channel);

        var result = await controller.ApplyAsync(target, response, CancellationToken.None);

        Assert.True(result.Succeeded, result.Reason);
        Assert.Equal(target.Cpu, channel.Table.Cpu);
        Assert.Equal(target.Gpu, channel.Table.Gpu);
        Assert.False(channel.Table.IndependentCpuGpuControl);
        Assert.Equal(response, channel.Response);
        Assert.Equal(5, channel.Commands.Count);
        Assert.Contains("回读确认", result.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ApplyAsync_InvalidCurveNeverWrites()
    {
        var baseline = CreateTable("Profile1", 20);
        var invalid = baseline with { Cpu = baseline.Cpu.Take(15).ToArray() };
        var channel = new FakeFanChannel(baseline, new(true, true, 18));
        var controller = new GcuFanController(channel);

        var result = await controller.ApplyAsync(invalid, new(true, true, 18), CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Empty(channel.Commands);
    }

    [Fact]
    public async Task ApplyAsync_DraftFromDifferentProfileNeverWrites()
    {
        var channel = new FakeFanChannel(CreateTable("Profile2", 20), new(true, true, 300));
        var result = await new GcuFanController(channel).ApplyAsync(
            CreateTable("Profile1", 25), new(true, true, 300), default);

        Assert.False(result.Succeeded);
        Assert.Empty(channel.Commands);
    }

    [Fact]
    public async Task ApplyAsync_ExternalProfileChangeStopsFurtherWritesAndNeverRollsBackIntoOtherProfile()
    {
        var baseline = CreateTable("Profile1", 20);
        var channel = new FakeFanChannel(baseline, new(true, true, 300));
        var controller = new GcuFanController(channel, (_, _) => Task.CompletedTask,
            _ => ValueTask.FromResult<RevoHelper.Core.Control.PlatformModeSelection?>(
                new(RevoHelper.Core.Control.PlatformMode.Custom, channel.Commands.Count == 0 ? 1 : 2)));

        var result = await controller.ApplyAsync(CreateTable("Profile1", 25), new(true, true, 300), default);

        Assert.False(result.Succeeded);
        Assert.Single(channel.Commands);
        Assert.DoesNotContain("已恢复", result.Reason);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1000)]
    [InlineData(5000)]
    public async Task ApplyAsync_TransitionDelayUsesRawMillisecondsWithoutScaling(int milliseconds)
    {
        var table = CreateTable("Profile1", 20);
        var channel = new FakeFanChannel(table, new(true, true, 300));
        var controller = new GcuFanController(channel);

        var result = await controller.ApplyAsync(table, new(true, true, milliseconds), CancellationToken.None);

        Assert.True(result.Succeeded, result.Reason);
        Assert.Equal(milliseconds, channel.Response.TransitionSensitivity);
        Assert.Contains(channel.Commands, payload =>
            payload.Contains($"\"FanSwitchSpeed\":\"{milliseconds}\"", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(5001)]
    public async Task ApplyAsync_TransitionDelayOutsideSupportedRangeNeverWrites(int milliseconds)
    {
        var table = CreateTable("Profile1", 20);
        var channel = new FakeFanChannel(table, new(true, true, 300));
        var controller = new GcuFanController(channel);

        var result = await controller.ApplyAsync(table, new(true, true, milliseconds), CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Empty(channel.Commands);
    }

    [Fact]
    public async Task ApplyAsync_WhenReadbackStaysStale_RestoresThePriorCurves()
    {
        var baseline = CreateTable("Profile1", 20);
        var target = CreateTable("Profile1", 25);
        var channel = new FakeFanChannel(baseline, new(true, true, 18)) { IgnoreTargetWrites = true };
        var controller = new GcuFanController(channel, (_, _) => Task.CompletedTask);

        var result = await controller.ApplyAsync(target, new(false, true, 30), CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains("已恢复", result.Reason, StringComparison.Ordinal);
        Assert.Equal(baseline.Cpu, channel.Table.Cpu);
        Assert.Equal(baseline.Gpu, channel.Table.Gpu);
        Assert.Equal(baseline.IndependentCpuGpuControl, channel.Table.IndependentCpuGpuControl);
        Assert.Contains(channel.Commands, payload => payload.Contains("\"T0\":\"20\"", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RestoreAsync_UsesOfficialRestoreAndReturnsTheConfirmedTable()
    {
        var modified = CreateTable("Profile1", 25);
        var restored = CreateTable("Profile1", 20);
        var channel = new FakeFanChannel(modified, new(true, true, 18)) { OemRestoreTable = restored };
        var controller = new GcuFanController(channel);

        var result = await controller.RestoreAsync("Profile1", CancellationToken.None);

        Assert.True(result.Result.Succeeded, result.Result.Reason);
        Assert.Equal(restored, result.Table);
        Assert.Contains(channel.Commands, payload => payload.Contains("RESTORE_FAN_SPEED_CURVE_SETTING", StringComparison.Ordinal));
        Assert.DoesNotContain("原厂默认曲线并回读确认", result.Result.Reason);
        Assert.Contains("未确认", result.Result.Reason);
    }

    [Fact]
    public async Task RestoreAsync_UnchangedValidReadbackDoesNotClaimFactoryCurveConfirmed()
    {
        var modified = CreateTable("Profile1", 25);
        var channel = new FakeFanChannel(modified, new(true, true, 18));
        var result = await new GcuFanController(channel, (_, _) => Task.CompletedTask).RestoreAsync("Profile1", default);

        Assert.DoesNotContain("已恢复", result.Result.Reason);
        Assert.Contains("未确认", result.Result.Reason);
    }

    private static FanCurveTable CreateTable(string name, int firstDuty)
    {
        var points = Enumerable.Range(0, 16)
            .Select(index => new FanCurvePoint(index, 40 + index * 3, 38 + index * 3, Math.Min(95, firstDuty + index * 5)))
            .ToArray();
        points[^1] = points[^1] with { DutyPercent = 100 };
        return new(name, true, points, points);
    }

    private sealed class FakeFanChannel(FanCurveTable table, FanResponseSettings response) : IGcuFanControlChannel
    {
        private readonly FanCurveTable original = table;
        public FanCurveTable Table { get; private set; } = table;
        public FanResponseSettings Response { get; private set; } = response;
        public bool IgnoreTargetWrites { get; init; }
        public FanCurveTable? OemRestoreTable { get; init; }
        public List<string> Commands { get; } = [];

        public ValueTask<FanCurveTable?> ReadFanTableAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult<FanCurveTable?>(Table);

        public ValueTask<FanResponseSettings?> ReadFanResponseAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult<FanResponseSettings?>(Response);

        public ValueTask WriteFanControlAsync(string payload, CancellationToken cancellationToken)
        {
            Commands.Add(payload);
            using var document = System.Text.Json.JsonDocument.Parse(payload);
            var root = document.RootElement;
            var action = root.GetProperty("Action").GetString();
            if (action == "RESTORE_FAN_SPEED_CURVE_SETTING")
            {
                Table = OemRestoreTable ?? original;
            }
            else if (action == "SET_FAN_SPEED_CURVE_SETTING")
            {
                var duties = Enumerable.Range(0, 16)
                    .Select(index => int.Parse(root.GetProperty($"T{index}").GetString()!))
                    .ToArray();
                if (!IgnoreTargetWrites || duties[0] == original.Cpu[0].DutyPercent)
                {
                    var cpu = Table.Cpu.Select((point, index) => point with { DutyPercent = duties[index] }).ToArray();
                    var gpu = Table.Gpu.Select((point, index) => point with { DutyPercent = duties[index] }).ToArray();
                    Table = root.GetProperty("Type").GetString() == "CPU"
                        ? Table with { Cpu = cpu }
                        : Table with { Gpu = gpu };
                }
            }
            else if (action == "SET_FAN_CONTROL_RESPECTIVE")
            {
                var value = root.GetProperty("FanControlRespective").GetBoolean();
                Response = Response with { IndependentCpuGpuControl = value };
                Table = Table with { IndependentCpuGpuControl = value };
            }
            else if (root.TryGetProperty("FanSwitchSpeedEnabled", out var enabled))
            {
                Response = Response with { TransitionSensitivityEnabled = enabled.GetString() == "1" };
            }
            else if (root.TryGetProperty("FanSwitchSpeed", out var sensitivity))
            {
                Response = Response with { TransitionSensitivity = int.Parse(sensitivity.GetString()!) };
            }
            return ValueTask.CompletedTask;
        }
    }
}
