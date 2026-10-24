using RevoHelper.Core.Control;

namespace RevoHelper.Hardware.Windows.OemControl;

public sealed class GcuCpuPowerLimitController(IGcuCpuPolicyChannel channel) : ICpuPolicyController
{
    private readonly SemaphoreSlim operationLock = new(1, 1);

    public async ValueTask<CpuPolicy?> ReadCpuPolicyAsync(CancellationToken cancellationToken)
    {
        var status = await channel.ReadStatusAsync(cancellationToken).ConfigureAwait(false);
        return new CpuPolicy(status.Pl1Watts, status.Pl2Watts, null);
    }

    public async ValueTask<ControlResult> ApplyCpuPolicyAsync(CpuPolicy policy, CancellationToken cancellationToken)
    {
        await operationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        GcuCpuPolicyStatus? baseline = null;
        try
        {
            if (policy.MaxFrequencyMhz is not null)
            {
                return ControlResult.Rejected("当前测试版尚未开放 CPU 频率上限写入。");
            }

            baseline = await channel.ReadStatusAsync(cancellationToken).ConfigureAwait(false);
            var invalid = Validate(policy, baseline);
            if (invalid is not null)
            {
                return ControlResult.Rejected(invalid);
            }

            await ApplyPairAsync(baseline, policy.Pl1Watts, policy.Pl2Watts, cancellationToken).ConfigureAwait(false);
            var final = await channel.ReadStatusAsync(cancellationToken).ConfigureAwait(false);
            if (final.Pl1Watts != policy.Pl1Watts || final.Pl2Watts != policy.Pl2Watts)
            {
                throw new InvalidOperationException($"GCU 回读未收敛：PL1={final.Pl1Watts} W，PL2={final.Pl2Watts} W。");
            }

            return new ControlResult(true, $"已应用并回读：PL1 {final.Pl1Watts} W / PL2 {final.Pl2Watts} W。");
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            if (baseline is null)
            {
                return ControlResult.Rejected($"功耗设置失败：{exception.Message}");
            }

            try
            {
                var current = await channel.ReadStatusAsync(cancellationToken).ConfigureAwait(false);
                await ApplyPairAsync(current, baseline.Pl1Watts, baseline.Pl2Watts, cancellationToken).ConfigureAwait(false);
                var restored = await channel.ReadStatusAsync(cancellationToken).ConfigureAwait(false);
                if (restored.Pl1Watts == baseline.Pl1Watts && restored.Pl2Watts == baseline.Pl2Watts)
                {
                    return ControlResult.Rejected($"功耗设置失败，已恢复原值：{exception.Message}");
                }

                return ControlResult.Rejected($"功耗设置失败且恢复回读不一致。当前 PL1 {restored.Pl1Watts} W / PL2 {restored.Pl2Watts} W。");
            }
            catch (Exception rollbackException) when (rollbackException is not OperationCanceledException)
            {
                return ControlResult.Rejected($"功耗设置失败，恢复原值也失败：{exception.Message}；{rollbackException.Message}");
            }
        }
        finally
        {
            operationLock.Release();
        }
    }

    private async Task ApplyPairAsync(GcuCpuPolicyStatus current, int targetPl1, int targetPl2, CancellationToken cancellationToken)
    {
        if (targetPl1 > current.Pl2Watts)
        {
            await WriteAndVerifyPl2Async(targetPl2, cancellationToken).ConfigureAwait(false);
            await WriteAndVerifyPl1Async(targetPl1, cancellationToken).ConfigureAwait(false);
            return;
        }

        if (targetPl2 < current.Pl1Watts)
        {
            await WriteAndVerifyPl1Async(targetPl1, cancellationToken).ConfigureAwait(false);
            await WriteAndVerifyPl2Async(targetPl2, cancellationToken).ConfigureAwait(false);
            return;
        }

        if (current.Pl1Watts != targetPl1)
        {
            await WriteAndVerifyPl1Async(targetPl1, cancellationToken).ConfigureAwait(false);
        }

        if (current.Pl2Watts != targetPl2)
        {
            await WriteAndVerifyPl2Async(targetPl2, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task WriteAndVerifyPl1Async(int watts, CancellationToken cancellationToken)
    {
        await channel.WritePl1Async(watts, cancellationToken).ConfigureAwait(false);
        var readback = await channel.ReadStatusAsync(cancellationToken).ConfigureAwait(false);
        if (readback.Pl1Watts != watts)
        {
            throw new InvalidOperationException($"PL1 写入 {watts} W 后回读为 {readback.Pl1Watts} W。");
        }
    }

    private async Task WriteAndVerifyPl2Async(int watts, CancellationToken cancellationToken)
    {
        await channel.WritePl2Async(watts, cancellationToken).ConfigureAwait(false);
        var readback = await channel.ReadStatusAsync(cancellationToken).ConfigureAwait(false);
        if (readback.Pl2Watts != watts)
        {
            throw new InvalidOperationException($"PL2 写入 {watts} W 后回读为 {readback.Pl2Watts} W。");
        }
    }

    private static string? Validate(CpuPolicy policy, GcuCpuPolicyStatus status)
    {
        if (policy.Pl1Watts > policy.Pl2Watts)
        {
            return "PL1 不能高于 PL2。";
        }

        if (policy.Pl1Watts < status.Pl1MinimumWatts || policy.Pl1Watts > status.Pl1MaximumWatts)
        {
            return $"PL1 必须位于本机 GCU 报告的 {status.Pl1MinimumWatts}–{status.Pl1MaximumWatts} W 范围内。";
        }

        if (policy.Pl2Watts < status.Pl2MinimumWatts || policy.Pl2Watts > status.Pl2MaximumWatts)
        {
            return $"PL2 必须位于本机 GCU 报告的 {status.Pl2MinimumWatts}–{status.Pl2MaximumWatts} W 范围内。";
        }

        return null;
    }
}
