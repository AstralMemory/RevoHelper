using RevoHelper.Core.Control;

namespace RevoHelper.Hardware.Windows.CpuPower;

public sealed class IntelRaplCpuPolicyController(IMsrAccess msrAccess)
    : IVerifiableCpuPolicyController, ICpuPolicyRestorePointProvider, IDisposable
{
    public const uint RaplPowerUnitMsr = 0x606;
    public const uint PackagePowerLimitMsr = 0x610;

    private readonly SemaphoreSlim operationLock = new(1, 1);
    private (ulong Unit, ulong Value)? lastRequested;

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

            lastRequested = (unit, target);
            if (!msrAccess.TryWrite(PackagePowerLimitMsr, target))
            {
                return ControlResult.Rejected("Intel RAPL 写入失败，未改变功耗限制。");
            }

            if (msrAccess.TryRead(PackagePowerLimitMsr, out var readback) &&
                SameControlledFields(target, readback))
            {
                var actual = IntelRaplPowerLimitCodec.Decode(unit, readback);
                return actual.Pl1Watts == policy.Pl1Watts && actual.Pl2Watts == policy.Pl2Watts
                    ? new(true, "Intel RAPL PL1/PL2 已写入并从 MSR 真实回读。")
                    : await RestoreAsync(original, target, "RAPL 回读值与目标功耗不一致").ConfigureAwait(false);
            }

            return await RestoreAsync(original, target, "RAPL 写入后寄存器未收敛").ConfigureAwait(false);
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
            return limits.Locked ? null : new(new(limits.Pl1Watts, limits.Pl2Watts, null),
                token => RestoreCapturedAsync(unit, original, token));
        }
        finally { operationLock.Release(); }
    }

    private async ValueTask<ControlResult> RestoreCapturedAsync(ulong unit, ulong original, CancellationToken token)
    {
        await operationLock.WaitAsync(token).ConfigureAwait(false);
        try
        {
            if (!TryReadRegisters(out var currentUnit, out var current) || (currentUnit & 0xFUL) != (unit & 0xFUL))
                return ControlResult.Rejected("Intel RAPL 恢复时无法确认原功耗单位或寄存器，未写入。");
            if (SameControlledFields(original, current)) return new(true, "Intel RAPL 原始受控位未改变。");
            if (lastRequested is not { } owned || (owned.Unit & 0xFUL) != (unit & 0xFUL) ||
                !SameControlledFields(owned.Value, current))
                return ControlResult.Rejected("检测到外部 Intel RAPL 功耗修改，已保留当前值，未恢复旧快照。");
            if (IntelRaplPowerLimitCodec.Decode(currentUnit, current).Locked)
                return ControlResult.Rejected("Intel RAPL 寄存器现已锁定，未恢复旧快照。");

            // Restore the exact raw limits/enable bits; keep current unrelated fields.
            var target = RestoreControlledFields(original, current);
            lastRequested = (unit, target);
            var restored = msrAccess.TryWrite(PackagePowerLimitMsr, target) &&
                msrAccess.TryRead(PackagePowerLimitMsr, out var readback) && SameControlledFields(target, readback);
            return restored ? new(true, "Intel RAPL 原始功耗编码与使能位已恢复并回读确认。")
                : ControlResult.Rejected("Intel RAPL 原始受控位恢复未确认。");
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

    private ValueTask<ControlResult> RestoreAsync(ulong original, ulong requested, string failure)
    {
        if (!msrAccess.TryRead(PackagePowerLimitMsr, out var current) ||
            (!SameControlledFields(current, original) && !SameControlledFields(current, requested)))
            return ValueTask.FromResult(ControlResult.Rejected($"{failure}，当前值归属未确认，已保留外部变化。"));
        var target = RestoreControlledFields(original, current);
        var restored = SameControlledFields(original, current) ||
                       msrAccess.TryWrite(PackagePowerLimitMsr, target) &&
                       msrAccess.TryRead(PackagePowerLimitMsr, out var restoredValue) &&
                       SameControlledFields(restoredValue, target);
        if (restored && lastRequested is { } owned) lastRequested = (owned.Unit, target);
        return ValueTask.FromResult(ControlResult.Rejected(restored
            ? $"{failure}，已恢复原始 RAPL 寄存器。"
            : $"{failure}，且原始 RAPL 寄存器恢复失败。"));
    }

    private static ulong RestoreControlledFields(ulong original, ulong current) =>
        (current & IntelRaplPowerLimitCodec.PreservedBitsMask) | (original & ~IntelRaplPowerLimitCodec.PreservedBitsMask);

    private static bool SameControlledFields(ulong expected, ulong actual)
    {
        const ulong controlled = 0x7FFFUL | (1UL << 15) | (0x7FFFUL << 32) | (1UL << 47);
        return (expected & controlled) == (actual & controlled);
    }
}
