using RevoHelper.Core.Control;

namespace RevoHelper.Hardware.Windows.CpuPower;

public sealed class IntelRaplCpuPolicyController(IMsrAccess msrAccess)
    : IVerifiableCpuPolicyController, ICpuPolicyRestorePointProvider, ICpuPolicySessionRoot, IDisposable
{
    public const uint RaplPowerUnitMsr = 0x606;
    public const uint PackagePowerLimitMsr = 0x610;

    private readonly SemaphoreSlim operationLock = new(1, 1);
    // Raw (power unit, MSR 0x610) states. Ownership compares the unit exponent and the
    // controlled PL fields only, so a firmware change to an unrelated bit keeps it.
    private readonly DirectPowerLimitOwnership<(ulong Unit, ulong Value)> ownership = new(SameState, PartOf);
    private static readonly ulong[] ControlledFields = [0x7FFFUL, 1UL << 15, 0x7FFFUL << 32, 1UL << 47];

    public ValueTask<CpuPolicy?> ReadCpuPolicyAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!TryReadRegisters(out var unit, out var packageLimit))
        {
            return ValueTask.FromResult<CpuPolicy?>(null);
        }

        var limits = IntelRaplPowerLimitCodec.Decode(unit, packageLimit);
        return ValueTask.FromResult<CpuPolicy?>(limits.Locked
            ? null
            : new(limits.Pl1Watts, limits.Pl2Watts, null));
    }

    public async ValueTask<ControlResult> ApplyCpuPolicyAsync(
        CpuPolicy policy,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(policy);
        await operationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!msrAccess.IsAvailable)
            {
                return ControlResult.Rejected($"Intel RAPL 后端不可用：{msrAccess.AvailabilityReason}");
            }

            if (!TryReadRegisters(out var unit, out var original))
            {
                return ControlResult.Rejected("无法读取 Intel RAPL 功耗单位或包功耗限制寄存器。");
            }

            ulong target;
            try
            {
                target = IntelRaplPowerLimitCodec.Encode(unit, original, policy.Pl1Watts, policy.Pl2Watts);
            }
            catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
            {
                return ControlResult.Rejected(exception.Message);
            }

            var root = ownership.BeginWrite((unit, original));
            if (!msrAccess.TryWrite(PackagePowerLimitMsr, target))
            {
                RecordUnconfirmedWrite(root, unit, original, target);
                return ControlResult.Rejected("Intel RAPL 写入失败，实际功耗限制以回读为准。");
            }

            if (msrAccess.TryRead(PackagePowerLimitMsr, out var readback) &&
                SameControlledFields(target, readback))
            {
                var actual = IntelRaplPowerLimitCodec.Decode(unit, readback);
                if (actual.Pl1Watts == policy.Pl1Watts && actual.Pl2Watts == policy.Pl2Watts)
                {
                    ownership.Confirmed(root, (unit, original), (unit, readback));
                    return new(true, "Intel RAPL PL1/PL2 已写入并从 MSR 真实回读。");
                }

                return RestoreAfterFailedWrite(root, unit, original, target, "RAPL 回读值与目标功耗不一致");
            }

            return RestoreAfterFailedWrite(root, unit, original, target, "RAPL 写入后寄存器未收敛");
        }
        finally
        {
            operationLock.Release();
        }
    }

    public async ValueTask<CpuPolicyRestorePoint?> CaptureRestorePointAsync(CancellationToken cancellationToken)
    {
        await operationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!TryReadRegisters(out var unit, out var original)) return null;
            var limits = IntelRaplPowerLimitCodec.Decode(unit, original);
            if (limits.Locked) return null;
            var snapshot = ownership.Capture((unit, original));
            return new(new(limits.Pl1Watts, limits.Pl2Watts, null),
                token => RestoreCapturedAsync(unit, original, snapshot, token));
        }
        finally { operationLock.Release(); }
    }

    private async ValueTask<ControlResult> RestoreCapturedAsync(
        ulong unit,
        ulong original,
        DirectPowerLimitOwnership<(ulong Unit, ulong Value)>.Snapshot snapshot,
        CancellationToken token)
    {
        await operationLock.WaitAsync(token).ConfigureAwait(false);
        try
        {
            if (!TryReadRegisters(out var currentUnit, out var current) || (currentUnit & 0xFUL) != (unit & 0xFUL))
                return ControlResult.Rejected("Intel RAPL 恢复时无法确认原功耗单位或寄存器，未写入。");
            // Already there: nothing to write, and whoever put it there keeps it.
            if (SameControlledFields(original, current)) return new(true, "Intel RAPL 原始受控位未改变。");
            switch (ownership.Claim((currentUnit, current)))
            {
                case DirectPowerLimitClaim.Unconfirmed:
                    return ControlResult.Rejected("最近一次 Intel RAPL 写入未确认，已保留当前值，未恢复旧快照。");
                case not DirectPowerLimitClaim.Owned:
                    return ControlResult.Rejected("检测到外部 Intel RAPL 功耗修改，已保留当前值，未恢复旧快照。");
            }
            if (!ownership.MayRestore((unit, original), (currentUnit, current)))
                return ControlResult.Rejected("当前 Intel RAPL 写入不是从这个快照开始的，已保留当前值，未恢复旧快照。");
            if (IntelRaplPowerLimitCodec.Decode(currentUnit, current).Locked)
                return ControlResult.Rejected("Intel RAPL 寄存器现已锁定，未恢复旧快照。");

            // Restore the exact raw limits/enable bits; keep current unrelated fields.
            var target = RestoreControlledFields(original, current);
            if (msrAccess.TryWrite(PackagePowerLimitMsr, target) &&
                msrAccess.TryRead(PackagePowerLimitMsr, out var readback) && SameControlledFields(target, readback))
            {
                ownership.Restored(snapshot, (currentUnit, readback));
                return new(true, "Intel RAPL 原始功耗编码与使能位已恢复并回读确认。");
            }

            // Unreadable: the ownership stays as it was.
            if (msrAccess.TryRead(PackagePowerLimitMsr, out var after))
                ownership.RestoreFailed((currentUnit, current), (currentUnit, target), (currentUnit, after), snapshot);
            return ControlResult.Rejected("Intel RAPL 原始受控位恢复未确认。");
        }
        finally { operationLock.Release(); }
    }

    public async ValueTask<ControlResult> RestoreSessionRootAsync(CancellationToken cancellationToken)
    {
        await operationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!ownership.HasState) return new(true, "本次没有需要撤回的 Intel RAPL 功耗写入。");
            if (!TryReadRegisters(out var unit, out var current))
                return ControlResult.Rejected("退出时无法读取 Intel RAPL 寄存器，未恢复功耗限制。");
            switch (ownership.Claim((unit, current)))
            {
                case DirectPowerLimitClaim.ChangedElsewhere:
                    ownership.Clear();
                    return ControlResult.Rejected("Intel RAPL 功耗限制已被外部修改，保留当前值。");
                case DirectPowerLimitClaim.Unconfirmed:
                    return ControlResult.Rejected("最近一次 Intel RAPL 写入未确认，保留当前值。");
                case DirectPowerLimitClaim.None:
                    return new(true, "本次没有需要撤回的 Intel RAPL 功耗写入。");
            }

            ownership.TryGetRoot(out var root);
            if (SameControlledFields(root.Value, current))
            {
                ownership.Clear();
                return new(true, "Intel RAPL 已是本次首次写入前的功耗限制。");
            }
            if (IntelRaplPowerLimitCodec.Decode(unit, current).Locked)
                return ControlResult.Rejected("Intel RAPL 寄存器现已锁定，未恢复功耗限制。");

            var target = RestoreControlledFields(root.Value, current);
            if (msrAccess.TryWrite(PackagePowerLimitMsr, target) &&
                msrAccess.TryRead(PackagePowerLimitMsr, out var readback) && SameControlledFields(target, readback))
            {
                ownership.Clear();
                return new(true, "Intel RAPL 已恢复本次首次写入前的功耗限制并回读确认。");
            }

            if (msrAccess.TryRead(PackagePowerLimitMsr, out var after))
                ownership.RestoreFailed((unit, current), (unit, target), (unit, after), restoring: null);
            return ControlResult.Rejected("Intel RAPL 功耗限制恢复未确认。");
        }
        finally { operationLock.Release(); }
    }

    public async ValueTask<ControlResult> VerifyWriteAccessAsync(CancellationToken cancellationToken)
    {
        await operationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!TryReadRegisters(out var unit, out var original))
            {
                return ControlResult.Rejected(
                    $"Intel RAPL 真实读写探测不可用：{msrAccess.AvailabilityReason}");
            }

            if (IntelRaplPowerLimitCodec.Decode(unit, original).Locked)
            {
                return ControlResult.Rejected("Intel RAPL 包功耗寄存器已被固件锁定。");
            }

            if (!msrAccess.TryWrite(PackagePowerLimitMsr, original))
            {
                return ControlResult.Rejected("Intel RAPL 同值写入被驱动或固件拒绝。");
            }

            return msrAccess.TryRead(PackagePowerLimitMsr, out var readback) && readback == original
                ? new ControlResult(true, "Intel RAPL 已完成无状态变化的真实读写探测。")
                : ControlResult.Rejected("Intel RAPL 同值写入后的寄存器回读不一致。");
        }
        finally
        {
            operationLock.Release();
        }
    }

    public void Dispose()
    {
        msrAccess.Dispose();
        operationLock.Dispose();
    }

    private bool TryReadRegisters(out ulong unit, out ulong packageLimit)
    {
        unit = 0;
        packageLimit = 0;
        return msrAccess.IsAvailable &&
               msrAccess.TryRead(RaplPowerUnitMsr, out unit) &&
               msrAccess.TryRead(PackagePowerLimitMsr, out packageLimit);
    }

    private ControlResult RestoreAfterFailedWrite(
        (ulong Unit, ulong Value) root, ulong unit, ulong original, ulong requested, string failure)
    {
        if (!msrAccess.TryRead(PackagePowerLimitMsr, out var current) ||
            (!SameControlledFields(current, original) && !SameControlledFields(current, requested)))
        {
            RecordUnconfirmedWrite(root, unit, original, requested);
            return ControlResult.Rejected($"{failure}，当前值归属未确认，已保留外部变化。");
        }
        var target = RestoreControlledFields(original, current);
        var restored = SameControlledFields(original, current) ||
                       msrAccess.TryWrite(PackagePowerLimitMsr, target) &&
                       msrAccess.TryRead(PackagePowerLimitMsr, out var restoredValue) &&
                       SameControlledFields(restoredValue, target);
        // Fields put back to the original keep the ownership the register had before the write.
        if (!restored) RecordUnconfirmedWrite(root, unit, original, requested);
        return ControlResult.Rejected(restored
            ? $"{failure}，已恢复原始 RAPL 寄存器。"
            : $"{failure}，且原始 RAPL 寄存器恢复失败。");
    }

    // Unreadable: the ownership stays as it was, and the requested value is not taken as written.
    private void RecordUnconfirmedWrite((ulong Unit, ulong Value) root, ulong unit, ulong original, ulong requested)
    {
        if (msrAccess.TryRead(PackagePowerLimitMsr, out var after))
            ownership.WriteFailed(root, (unit, original), (unit, requested), (unit, after));
    }

    private static bool SameState((ulong Unit, ulong Value) left, (ulong Unit, ulong Value) right) =>
        (left.Unit & 0xFUL) == (right.Unit & 0xFUL) && SameControlledFields(left.Value, right.Value);

    private static bool PartOf(
        (ulong Unit, ulong Value) before, (ulong Unit, ulong Value) requested, (ulong Unit, ulong Value) after) =>
        (after.Unit & 0xFUL) == (before.Unit & 0xFUL) && (requested.Unit & 0xFUL) == (before.Unit & 0xFUL) &&
        ControlledFields.All(field =>
            (after.Value & field) == (before.Value & field) || (after.Value & field) == (requested.Value & field));

    private static ulong RestoreControlledFields(ulong original, ulong current) =>
        (current & IntelRaplPowerLimitCodec.PreservedBitsMask) | (original & ~IntelRaplPowerLimitCodec.PreservedBitsMask);

    private static bool SameControlledFields(ulong expected, ulong actual)
    {
        const ulong controlled = 0x7FFFUL | (1UL << 15) | (0x7FFFUL << 32) | (1UL << 47);
        return (expected & controlled) == (actual & controlled);
    }
}
