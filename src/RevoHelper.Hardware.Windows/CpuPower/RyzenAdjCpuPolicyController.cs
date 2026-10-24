using RevoHelper.Core.Control;

namespace RevoHelper.Hardware.Windows.CpuPower;

public sealed class RyzenAdjCpuPolicyController(IRyzenAdjClient client)
    : IVerifiableCpuPolicyController, ICpuPolicyRestorePointProvider, IDisposable
{
    private readonly SemaphoreSlim operationLock = new(1, 1);

    public bool IsAvailable => client.IsAvailable;

    public string AvailabilityReason => client.AvailabilityReason;

    public async ValueTask<CpuPolicy?> ReadCpuPolicyAsync(CancellationToken cancellationToken)
    {
        if (!client.IsAvailable)
        {
            return null;
        }

        var limits = await client.ReadPowerLimitsAsync(cancellationToken).ConfigureAwait(false);
        return limits is null
            ? null
            : ToPolicy(limits);
    }

    public async ValueTask<ControlResult> ApplyCpuPolicyAsync(
        CpuPolicy policy,
        CancellationToken cancellationToken)
    {
        if (!client.IsAvailable)
        {
            return ControlResult.Rejected(client.AvailabilityReason);
        }

        if (policy.Pl1Watts <= 0 || policy.Pl2Watts <= 0 || policy.Pl1Watts > policy.Pl2Watts)
        {
            return ControlResult.Rejected("AMD SMU 功耗值无效：要求 0 < PL1 <= PL2。");
        }

        await operationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var target = new RyzenAdjPowerLimits(policy.Pl1Watts, policy.Pl1Watts, policy.Pl2Watts);
            return await ApplyExactLockedAsync(target, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            operationLock.Release();
        }
    }

    public async ValueTask<CpuPolicyRestorePoint?> CaptureRestorePointAsync(CancellationToken cancellationToken)
    {
        if (!client.IsAvailable) return null;
        await operationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var original = await client.ReadPowerLimitsAsync(cancellationToken).ConfigureAwait(false);
            return original is null ? null : new(
                ToPolicy(original),
                token => RestoreExactAsync(original, token));
        }
        finally { operationLock.Release(); }
    }

    private async ValueTask<ControlResult> RestoreExactAsync(RyzenAdjPowerLimits target, CancellationToken token)
    {
        await operationLock.WaitAsync(token).ConfigureAwait(false);
        try { return await ApplyExactLockedAsync(target, token).ConfigureAwait(false); }
        finally { operationLock.Release(); }
    }

    private async ValueTask<ControlResult> ApplyExactLockedAsync(RyzenAdjPowerLimits target, CancellationToken token)
    {
        var original = await client.ReadPowerLimitsAsync(token).ConfigureAwait(false);
        if (original is null) return ControlResult.Rejected("无法读取 AMD SMU 原始功耗限制，未执行写入。");
        string failure;
        try
        {
            if (!await client.ApplyPowerLimitsAsync(target, token).ConfigureAwait(false))
                throw new InvalidDataException("RyzenAdj/SMU 拒绝写入功耗限制。");
            var readback = await client.ReadPowerLimitsAsync(token).ConfigureAwait(false);
            if (readback != target) throw new InvalidDataException("AMD SMU 回读与目标不一致。");
            return new(true, "AMD SMU 功耗限制已写入并完成真实回读。");
        }
        catch (Exception exception) { failure = exception.Message; }

        // Rejection/cancellation may occur after one native field was written.
        // Use a separate bounded recovery token, then verify all three fields.
        using var recovery = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        try
        {
            var accepted = await client.ApplyPowerLimitsAsync(original, recovery.Token).ConfigureAwait(false);
            var readback = await client.ReadPowerLimitsAsync(recovery.Token).ConfigureAwait(false);
            if (accepted && readback == original)
                return ControlResult.Rejected($"{failure} 已恢复原始限制。");
            return ControlResult.Rejected($"{failure} 原始限制恢复未确认，实际值请重新读取。");
        }
        catch (Exception exception)
        {
            return ControlResult.Rejected($"{failure} 原始限制恢复失败：{exception.Message}");
        }
    }

    public async ValueTask<ControlResult> VerifyWriteAccessAsync(CancellationToken cancellationToken)
    {
        if (!client.IsAvailable)
        {
            return ControlResult.Rejected(client.AvailabilityReason);
        }

        await operationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var original = await client.ReadPowerLimitsAsync(cancellationToken).ConfigureAwait(false);
            if (original is null)
            {
                return ControlResult.Rejected("无法读取 AMD SMU 原始功耗限制。");
            }

            var result = await ApplyExactLockedAsync(original, cancellationToken).ConfigureAwait(false);
            return result.Succeeded
                ? new ControlResult(true, "AMD SMU 已完成无状态变化的真实读写探测。")
                : result;
        }
        finally
        {
            operationLock.Release();
        }
    }

    public void Dispose()
    {
        operationLock.Dispose();
        client.Dispose();
    }

    private static CpuPolicy ToPolicy(RyzenAdjPowerLimits limits) => new(
        (int)decimal.Round(Math.Min(limits.StapmWatts, limits.SlowWatts), MidpointRounding.AwayFromZero),
        (int)decimal.Round(limits.FastWatts, MidpointRounding.AwayFromZero), null);
}
