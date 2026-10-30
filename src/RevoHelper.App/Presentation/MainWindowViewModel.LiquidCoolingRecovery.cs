using RevoHelper.Core.Control;

namespace RevoHelper.App.Presentation;

public sealed partial class MainWindowViewModel
{
    private readonly SemaphoreSlim liquidCoolingOperationGate = new(1, 1);
    private readonly CancellationTokenSource liquidCoolingLifetime = new();
    private readonly LiquidCoolingReconnectCoordinator liquidCoolingReconnect;
    private CancellationTokenSource? liquidCoolingConnectCancellation;
    private long liquidCoolingSelectionRevision;
    private bool applyingLiquidCoolingState;
    private bool liquidCoolingSelectionDraft;
    private bool liquidCoolingStartupAutoConnectSuppressed;
    private int liquidCoolingMonitorRunning;
    public bool IsLiquidCoolingMonitoring => Volatile.Read(ref liquidCoolingMonitorRunning) != 0;

    // Owned by App lifetime, not the visible drawer: hiding/minimizing the UI
    // does not stop monitoring. Only existing GCU state/connect are used, no BLE scan.
    public async Task MonitorLiquidCoolingAsync(CancellationToken cancellationToken)
    {
        if (quickControlsController is null || isShuttingDown
            || Interlocked.CompareExchange(ref liquidCoolingMonitorRunning, 1, 0) != 0) return;
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, liquidCoolingLifetime.Token);
        try
        {
            while (!lifetime.IsCancellationRequested)
            {
                await RefreshLiquidCoolingRecoveryAsync(lifetime.Token);
                await Task.Delay(LiquidCoolingReconnectCoordinator.RetryInterval, lifetime.Token);
            }
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        finally { Volatile.Write(ref liquidCoolingMonitorRunning, 0); }
    }

    public async Task RefreshLiquidCoolingRecoveryAsync(CancellationToken cancellationToken)
    {
        if (quickControlsController is null || isShuttingDown
            || !await liquidCoolingOperationGate.WaitAsync(0, cancellationToken)) return;
        var revision = liquidCoolingSelectionRevision;
        var previousDeadline = liquidCoolingReconnect.Deadline;
        try
        {
            var state = await quickControlsController.ReadLiquidCoolingAsync(cancellationToken);
            if (isShuttingDown || revision != liquidCoolingSelectionRevision) return;
            ApplyLiquidCooling(state);
            if (previousDeadline is null && liquidCoolingReconnect.Deadline is not null)
                LiquidCoolingStatusText = "水冷意外断开；10分钟内每3秒尝试重连同一设备。";
            if (liquidCoolingReconnect.Deadline is not null) IsLiquidCoolingBusy = true;
            var result = await liquidCoolingReconnect.TryReconnectAsync(
                quickControlsController.ReadLiquidCoolingAsync,
                (device, token) => !isShuttingDown && revision == liquidCoolingSelectionRevision
                    && string.Equals(device, SelectedLiquidCoolingDeviceId, StringComparison.OrdinalIgnoreCase)
                    ? quickControlsController.ConnectLiquidCoolingAsync(token)
                    : ValueTask.FromResult(ControlResult.Rejected("水冷重连已取消。")), cancellationToken);
            if (isShuttingDown || revision != liquidCoolingSelectionRevision) return;
            if (result.State is not null) ApplyLiquidCooling(result.State);
            if (result.Connected && previousDeadline is not null)
                LiquidCoolingStatusText = "水冷已自动重连。";
            else if (previousDeadline is { } end && utcNow() >= end && liquidCoolingReconnect.Deadline is null)
                LiquidCoolingStatusText = "水冷重连等待已超过10分钟；请手动连接。";
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        { /* Unavailable GCU state is not a confirmed disconnect; retry quietly. */ }
        finally { IsLiquidCoolingBusy = false; liquidCoolingOperationGate.Release(); }
    }

    private void CancelLiquidCoolingRecovery()
    {
        Interlocked.Increment(ref liquidCoolingSelectionRevision);
        liquidCoolingStartupAutoConnectSuppressed = true;
        liquidCoolingReconnect.Cancel();
        try { liquidCoolingConnectCancellation?.Cancel(); } catch (ObjectDisposedException) { }
    }

    private void RememberLiquidCoolingConnection(LiquidCoolingState? state)
    {
        ApplyLiquidCooling(state);
        liquidCoolingReconnect.RememberConnected(state);
        if (state is { Connected: true, IsPriming: false }) liquidCoolingStartupAutoConnectSuppressed = false;
    }
}
