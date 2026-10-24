using RevoHelper.Core.Control;

namespace RevoHelper.Core.Tests.Control;

public sealed class LiquidCoolingAutoConnectorTests
{
    [Fact]
    public async Task TryConnect_AlreadyConnected_DoesNotWrite()
    {
        var connectCalls = 0;
        var connector = new LiquidCoolingAutoConnector(
            _ => ValueTask.FromResult<LiquidCoolingState?>(State(connected: true, selectedDeviceId: "CoolingSystem (59:05)")),
            _ =>
            {
                connectCalls++;
                return ValueTask.FromResult(new ControlResult(true, "unexpected"));
            },
            (_, _) => Task.CompletedTask);

        var result = await connector.TryConnectAsync(CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Contains("已经连接", result.Reason, StringComparison.Ordinal);
        Assert.Equal(0, connectCalls);
    }

    [Fact]
    public async Task TryConnect_RememberedDeviceBecomesAvailable_SendsOneConnect()
    {
        var readCalls = 0;
        var connectCalls = 0;
        var delayCalls = 0;
        var connector = new LiquidCoolingAutoConnector(
            _ => ValueTask.FromResult<LiquidCoolingState?>(++readCalls == 1
                ? null
                : State(connected: false, selectedDeviceId: "CoolingSystem (59:05)")),
            _ =>
            {
                connectCalls++;
                return ValueTask.FromResult(new ControlResult(true, "水冷设备已连接并回读确认。"));
            },
            (_, _) =>
            {
                delayCalls++;
                return Task.CompletedTask;
            },
            maxReadAttempts: 3);

        var result = await connector.TryConnectAsync(CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(2, readCalls);
        Assert.Equal(1, delayCalls);
        Assert.Equal(1, connectCalls);
    }

    [Fact]
    public async Task TryConnect_NoRememberedDevice_DoesNotWriteOrRetry()
    {
        var connectCalls = 0;
        var delayCalls = 0;
        var connector = new LiquidCoolingAutoConnector(
            _ => ValueTask.FromResult<LiquidCoolingState?>(State(connected: false, selectedDeviceId: null)),
            _ =>
            {
                connectCalls++;
                return ValueTask.FromResult(new ControlResult(true, "unexpected"));
            },
            (_, _) =>
            {
                delayCalls++;
                return Task.CompletedTask;
            });

        var result = await connector.TryConnectAsync(CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains("首次手动", result.Reason, StringComparison.Ordinal);
        Assert.Equal(0, connectCalls);
        Assert.Equal(0, delayCalls);
    }

    [Fact]
    public async Task TryConnect_StatusUnavailable_StopsAfterBoundedRetries()
    {
        var readCalls = 0;
        var delayCalls = 0;
        var connector = new LiquidCoolingAutoConnector(
            _ =>
            {
                readCalls++;
                return ValueTask.FromResult<LiquidCoolingState?>(null);
            },
            _ => ValueTask.FromResult(new ControlResult(true, "unexpected")),
            (_, _) =>
            {
                delayCalls++;
                return Task.CompletedTask;
            },
            maxReadAttempts: 3);

        var result = await connector.TryConnectAsync(CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains("未返回水冷状态", result.Reason, StringComparison.Ordinal);
        Assert.Equal(3, readCalls);
        Assert.Equal(2, delayCalls);
    }

    [Fact]
    public async Task TryConnect_ControlChannelStartsLate_RetriesTransientReadFailure()
    {
        var readCalls = 0;
        var connector = new LiquidCoolingAutoConnector(
            _ =>
            {
                readCalls++;
                if (readCalls == 1) throw new InvalidOperationException("GCU 尚未连接");
                return ValueTask.FromResult<LiquidCoolingState?>(State(
                    connected: false,
                    selectedDeviceId: "CoolingSystem (59:05)"));
            },
            _ => ValueTask.FromResult(new ControlResult(true, "水冷设备已连接并回读确认。")),
            (_, _) => Task.CompletedTask,
            maxReadAttempts: 3);

        var result = await connector.TryConnectAsync(CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(2, readCalls);
    }

    [Fact]
    public async Task TryConnect_Priming_DoesNotWrite()
    {
        var connectCalls = 0;
        var connector = new LiquidCoolingAutoConnector(
            _ => ValueTask.FromResult<LiquidCoolingState?>(State(
                connected: false,
                selectedDeviceId: "CoolingSystem (59:05)",
                isPriming: true)),
            _ =>
            {
                connectCalls++;
                return ValueTask.FromResult(new ControlResult(true, "unexpected"));
            },
            (_, _) => Task.CompletedTask);

        var result = await connector.TryConnectAsync(CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains("注水模式", result.Reason, StringComparison.Ordinal);
        Assert.Equal(0, connectCalls);
    }

    [Fact]
    public async Task TryConnect_ConnectCommandThrows_ReturnsTruthfulFailure()
    {
        var connector = new LiquidCoolingAutoConnector(
            _ => ValueTask.FromResult<LiquidCoolingState?>(State(
                connected: false,
                selectedDeviceId: "CoolingSystem (59:05)")),
            _ => throw new InvalidOperationException("BT_LC 通道断开"),
            (_, _) => Task.CompletedTask);

        var result = await connector.TryConnectAsync(CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains("BT_LC 通道断开", result.Reason, StringComparison.Ordinal);
    }

    private static LiquidCoolingState State(
        bool connected,
        string? selectedDeviceId,
        bool isPriming = false) => new(
        connected,
        connected ? "Connected" : "Disconnected",
        LiquidCoolingFanMode.Balanced,
        LiquidCoolingPumpMode.Maximum,
        connected ? 40 : 0,
        connected ? 100 : 0,
        string.IsNullOrWhiteSpace(selectedDeviceId) ? [] : [new(selectedDeviceId)],
        selectedDeviceId,
        "LCT22002 v2.0.0.4",
        CanControl: true,
        IsPriming: isPriming,
        MeterNormal: true);
}
