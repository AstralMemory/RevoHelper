using RevoHelper.Core.Control;

namespace RevoHelper.Hardware.Windows.CpuPower;

public sealed class RyzenAdjCpuPolicyController(IRyzenAdjClient client)
    : IVerifiableCpuPolicyController, ICpuPolicyRestorePointProvider, ICpuPolicySessionRoot, IDisposable
{
    private readonly SemaphoreSlim operationLock = new(1, 1);
    // Ownership compares all three limits as read: the UI policy merges STAPM and Slow
    // into one rounded value and would miss a change to either.
    private readonly DirectPowerLimitOwnership<RyzenAdjPowerLimits> ownership = new(
        (left, right) => left == right, PartOf);

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
            var original = await client.ReadPowerLimitsAsync(cancellationToken).ConfigureAwait(false);
            if (original is null) return ControlResult.Rejected("无法读取 AMD SMU 原始功耗限制，未执行写入。");
            var target = new RyzenAdjPowerLimits(policy.Pl1Watts, policy.Pl1Watts, policy.Pl2Watts);
            var root = ownership.BeginWrite(original);
            var (result, after) = await ApplyExactLockedAsync(target, original, cancellationToken).ConfigureAwait(false);
            // Unreadable afterwards: the ownership stays as it was, and the target is not taken as written.
            if (result.Succeeded) ownership.Confirmed(root, original, target);
            else if (after is not null) ownership.WriteFailed(root, original, target, after);
            return result;
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
            if (original is null) return null;
            var snapshot = ownership.Capture(original);
            return new(ToPolicy(original), token => RestoreCapturedAsync(original, snapshot, token));
        }
        finally { operationLock.Release(); }
    }

    private async ValueTask<ControlResult> RestoreCapturedAsync(
        RyzenAdjPowerLimits original,
        DirectPowerLimitOwnership<RyzenAdjPowerLimits>.Snapshot snapshot,
        CancellationToken token)
    {
        await operationLock.WaitAsync(token).ConfigureAwait(false);
        try
        {
            var current = await client.ReadPowerLimitsAsync(token).ConfigureAwait(false);
            if (current is null) return ControlResult.Rejected("无法读取 AMD SMU 当前功耗限制，未恢复旧快照。");
            // Already there: nothing to write, and whoever put it there keeps it.
            if (current == original) return new(true, "AMD SMU 原始功耗限制未改变。");
            switch (ownership.Claim(current))
            {
                case DirectPowerLimitClaim.Unconfirmed:
                    return ControlResult.Rejected("最近一次 AMD SMU 写入未确认，已保留当前值，未恢复旧快照。");
                case not DirectPowerLimitClaim.Owned:
                    return ControlResult.Rejected("检测到外部 AMD SMU 功耗修改，已保留当前值，未恢复旧快照。");
            }
            if (!ownership.MayRestore(original, current))
                return ControlResult.Rejected("当前 AMD SMU 写入不是从这个快照开始的，已保留当前值，未恢复旧快照。");

            var (result, after) = await ApplyExactLockedAsync(original, current, token).ConfigureAwait(false);
            if (result.Succeeded) ownership.Restored(snapshot, original);
            else if (after is not null) ownership.RestoreFailed(current, original, after, snapshot);
            return result.Succeeded ? new(true, "AMD SMU 原始功耗限制已恢复并完成真实回读。") : result;
        }
        finally { operationLock.Release(); }
    }

    public async ValueTask<ControlResult> RestoreSessionRootAsync(CancellationToken cancellationToken)
    {
        await operationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!ownership.HasState) return new(true, "本次没有需要撤回的 AMD SMU 功耗写入。");
            if (!client.IsAvailable) return ControlResult.Rejected(client.AvailabilityReason);
            var current = await client.ReadPowerLimitsAsync(cancellationToken).ConfigureAwait(false);
            if (current is null) return ControlResult.Rejected("退出时无法读取 AMD SMU 功耗限制，未恢复。");
            switch (ownership.Claim(current))
            {
                case DirectPowerLimitClaim.ChangedElsewhere:
                    ownership.Clear();
                    return ControlResult.Rejected("AMD SMU 功耗限制已被外部修改，保留当前值。");
                case DirectPowerLimitClaim.Unconfirmed:
                    return ControlResult.Rejected("最近一次 AMD SMU 写入未确认，保留当前值。");
                case DirectPowerLimitClaim.None:
                    return new(true, "本次没有需要撤回的 AMD SMU 功耗写入。");
            }

            ownership.TryGetRoot(out var root);
            if (current == root)
            {
                ownership.Clear();
                return new(true, "AMD SMU 已是本次首次写入前的功耗限制。");
            }

            var (result, after) = await ApplyExactLockedAsync(root, current, cancellationToken).ConfigureAwait(false);
            if (result.Succeeded)
            {
                ownership.Clear();
                return new(true, "AMD SMU 已恢复本次首次写入前的功耗限制并完成真实回读。");
            }
            if (after is not null) ownership.RestoreFailed(current, root, after, restoring: null);
            return result;
        }
        finally { operationLock.Release(); }
    }

    // Writes target over original and reads it back; on failure puts original back, unless a
    // field holds neither value. Returns the result with the last state read (null when
    // nothing could be read).
    private async ValueTask<(ControlResult Result, RyzenAdjPowerLimits? After)> ApplyExactLockedAsync(
        RyzenAdjPowerLimits target,
        RyzenAdjPowerLimits original,
        CancellationToken token)
    {
        string failure;
        RyzenAdjPowerLimits? observed = null;
        try
        {
            if (!await client.ApplyPowerLimitsAsync(target, token).ConfigureAwait(false))
                throw new InvalidDataException("RyzenAdj/SMU 拒绝写入功耗限制。");
            observed = await client.ReadPowerLimitsAsync(token).ConfigureAwait(false);
            if (observed != target) throw new InvalidDataException("AMD SMU 回读与目标不一致。");
            return (new(true, "AMD SMU 功耗限制已写入并完成真实回读。"), observed);
        }
        catch (Exception exception) { failure = exception.Message; }

        // Rejection/cancellation may occur after one native field was written.
        // Use a separate bounded recovery token, then verify all three fields.
        using var recovery = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        // A field from neither state means firmware or another tool wrote in between: putting
        // original back would overwrite that. Unreadable or part-way still gets the recovery.
        observed ??= await TryReadAsync(recovery.Token).ConfigureAwait(false);
        if (observed is not null && !PartOf(original, target, observed))
            return (ControlResult.Rejected($"{failure} 当前限制已被其它程序或固件改写，未写回原始限制。"), observed);
        try
        {
            var accepted = await client.ApplyPowerLimitsAsync(original, recovery.Token).ConfigureAwait(false);
            var readback = await client.ReadPowerLimitsAsync(recovery.Token).ConfigureAwait(false);
            if (accepted && readback == original)
                return (ControlResult.Rejected($"{failure} 已恢复原始限制。"), readback);
            return (ControlResult.Rejected($"{failure} 原始限制恢复未确认，实际值请重新读取。"), readback);
        }
        catch (Exception exception)
        {
            return (ControlResult.Rejected($"{failure} 原始限制恢复失败：{exception.Message}"), null);
        }
    }

    private async ValueTask<RyzenAdjPowerLimits?> TryReadAsync(CancellationToken token)
    {
        try { return await client.ReadPowerLimitsAsync(token).ConfigureAwait(false); }
        catch (Exception) { return null; }
    }

    // Every field from one of the two states: a part-way write, not another writer's value.
    private static bool PartOf(RyzenAdjPowerLimits before, RyzenAdjPowerLimits requested, RyzenAdjPowerLimits after) =>
        (after.StapmWatts == before.StapmWatts || after.StapmWatts == requested.StapmWatts) &&
        (after.SlowWatts == before.SlowWatts || after.SlowWatts == requested.SlowWatts) &&
        (after.FastWatts == before.FastWatts || after.FastWatts == requested.FastWatts);

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

            var (result, _) = await ApplyExactLockedAsync(original, original, cancellationToken).ConfigureAwait(false);
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
