using RevoHelper.Core.Control;

namespace RevoHelper.Hardware.Windows.OemControl;

public interface IGcuFanBoostChannel
{
    ValueTask<GcuCpuPolicyStatus> ReadFanBoostStatusAsync(CancellationToken cancellationToken);
    ValueTask WriteFanBoostAsync(string payload, CancellationToken cancellationToken);
}

public sealed class GcuFanBoostController(
    IGcuFanBoostChannel channel,
    Func<TimeSpan, CancellationToken, Task>? verificationDelay = null) : IFanBoostController
{
    private readonly SemaphoreSlim operationLock = new(1, 1);
    private readonly Func<TimeSpan, CancellationToken, Task> delay = verificationDelay ?? Task.Delay;

    public async ValueTask<FanBoostState> ReadAsync(CancellationToken cancellationToken)
    {
        var status = await channel.ReadFanBoostStatusAsync(cancellationToken).ConfigureAwait(false);
        return status.FanBoostEnable is { } enabled
            ? new(true, enabled == 1)
            : new(false, false);
    }

    public async ValueTask<ControlResult> SetAsync(bool enabled, CancellationToken cancellationToken)
    {
        await operationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var baseline = await channel.ReadFanBoostStatusAsync(cancellationToken).ConfigureAwait(false);
            if (baseline.FanBoostEnable is not { } baselineValue)
            {
                return ControlResult.Rejected("当前机型未返回 FanBoostEnable，已阻止强冷写入。");
            }

            var baselineEnabled = baselineValue == 1;
            if (baselineEnabled == enabled)
            {
                return new(true, enabled ? "强冷已经开启。" : "强冷已经关闭。");
            }

            await channel.WriteFanBoostAsync(GcuFanBoostCommand.Set(enabled), cancellationToken)
                .ConfigureAwait(false);
            if (await WaitForAsync(enabled, cancellationToken).ConfigureAwait(false))
            {
                return new(true, enabled ? "强冷已开启并回读确认。" : "强冷已关闭并回读确认。");
            }

            await channel.WriteFanBoostAsync(GcuFanBoostCommand.Set(baselineEnabled), cancellationToken)
                .ConfigureAwait(false);
            return await WaitForAsync(baselineEnabled, cancellationToken).ConfigureAwait(false)
                ? ControlResult.Rejected("强冷状态未确认，已恢复操作前状态。")
                : ControlResult.Rejected("强冷状态未确认，恢复操作前状态也未取得一致回读。");
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return ControlResult.Rejected($"强冷控制失败：{exception.Message}");
        }
        finally
        {
            operationLock.Release();
        }
    }

    private async Task<bool> WaitForAsync(bool enabled, CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 4; attempt++)
        {
            if (attempt > 0)
            {
                await delay(TimeSpan.FromMilliseconds(220), cancellationToken).ConfigureAwait(false);
            }
            var status = await channel.ReadFanBoostStatusAsync(cancellationToken).ConfigureAwait(false);
            if (status.FanBoostEnable == (enabled ? 1 : 0)) return true;
        }
        return false;
    }
}
