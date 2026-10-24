namespace RevoHelper.Core.Control;

public sealed class LiquidCoolingAutoConnector
{
    private readonly Func<CancellationToken, ValueTask<LiquidCoolingState?>> readState;
    private readonly Func<CancellationToken, ValueTask<ControlResult>> connect;
    private readonly Func<TimeSpan, CancellationToken, Task> delay;
    private readonly int maxReadAttempts;
    private readonly TimeSpan retryDelay;

    public LiquidCoolingAutoConnector(
        Func<CancellationToken, ValueTask<LiquidCoolingState?>> readState,
        Func<CancellationToken, ValueTask<ControlResult>> connect,
        Func<TimeSpan, CancellationToken, Task>? delay = null,
        int maxReadAttempts = 12,
        TimeSpan? retryDelay = null)
    {
        ArgumentNullException.ThrowIfNull(readState);
        ArgumentNullException.ThrowIfNull(connect);
        if (maxReadAttempts < 1) throw new ArgumentOutOfRangeException(nameof(maxReadAttempts));

        this.readState = readState;
        this.connect = connect;
        this.delay = delay ?? Task.Delay;
        this.maxReadAttempts = maxReadAttempts;
        this.retryDelay = retryDelay ?? TimeSpan.FromMilliseconds(500);
    }

    public async Task<ControlResult> TryConnectAsync(CancellationToken cancellationToken)
    {
        LiquidCoolingState? lastState = null;
        Exception? lastReadFailure = null;

        for (var attempt = 0; attempt < maxReadAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                lastState = await readState(cancellationToken).ConfigureAwait(false);
                lastReadFailure = null;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                lastReadFailure = exception;
                lastState = null;
            }

            if (lastState?.Connected == true)
                return new(true, "水冷设备已经连接，无需重复发送连接命令。");
            if (lastState?.IsPriming == true)
                return ControlResult.Rejected("水冷设备处于注水模式，自动连接未执行。");
            if (lastState is not null && string.IsNullOrWhiteSpace(lastState.SelectedDeviceId))
                return ControlResult.Rejected("尚未保存水冷设备；请首次手动扫描、选择并连接一次。");
            if (lastState is { CanControl: true })
            {
                try
                {
                    return await connect(cancellationToken).ConfigureAwait(false);
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    return ControlResult.Rejected($"水冷自动连接失败：{exception.Message}");
                }
            }

            if (attempt < maxReadAttempts - 1)
                await delay(retryDelay, cancellationToken).ConfigureAwait(false);
        }

        return lastReadFailure is null
            ? ControlResult.Rejected("GCU 在限定时间内未返回水冷状态或状态暂不可控制，未发送连接命令。")
            : ControlResult.Rejected($"GCU 水冷控制通道未就绪：{lastReadFailure.Message}");
    }
}
