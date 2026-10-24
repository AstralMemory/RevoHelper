using RevoHelper.Core.Control;

namespace RevoHelper.Hardware.Windows.PowerPolicy;

public sealed class WindowsProcessorBoostController : IProcessorBoostController
{
    private readonly IPowerProfileApi api;
    private readonly IProcessorBoostCompatibilityLeaseStore leaseStore;
    private readonly SemaphoreSlim gate = new(1, 1);

    public WindowsProcessorBoostController(IPowerProfileApi api) : this(api, new MemoryLeaseStore()) { }

    public WindowsProcessorBoostController(IPowerProfileApi api, IProcessorBoostCompatibilityLeaseStore leaseStore)
    {
        this.api = api;
        this.leaseStore = leaseStore;
    }
    public static readonly Guid ProcessorSubgroup =
        Guid.Parse("54533251-82be-4824-96c1-47b60b740d00");

    public static readonly Guid BoostModeSetting =
        Guid.Parse("be337238-0d82-4146-a960-4f3749d470c7");

    // Some autonomous HWP/CPPC firmware acknowledges PERFBOOSTMODE=Disabled but
    // keeps requesting boost. Windows' maximum-performance setting is used as
    // the documented compatibility guard for that mode.
    public static readonly Guid MaximumProcessorStateSetting =
        Guid.Parse("bc5038f7-23e0-4960-96da-33abaf5935ec");

    public ValueTask<ProcessorBoostPolicy> ReadAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var scheme = api.GetActiveScheme();
        var ac = ToMode(api.ReadAcValue(scheme, ProcessorSubgroup, BoostModeSetting));
        var dc = ToMode(api.ReadDcValue(scheme, ProcessorSubgroup, BoostModeSetting));
        return ValueTask.FromResult(new ProcessorBoostPolicy(ac, dc));
    }

    public async ValueTask<ControlResult> ApplyAsync(
        ProcessorBoostPolicy policy,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!IsValid(policy.AcMode) || !IsValid(policy.DcMode))
        {
            return ControlResult.Rejected("睿频策略索引必须位于 0–6。");
        }

        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var lease = await leaseStore.LoadAsync(cancellationToken).ConfigureAwait(false);
            ValidateLease(lease);
            var scheme = api.GetActiveScheme();
            if (lease is not null && lease.Scheme != scheme)
            {
                var recovery = await RecoverLeaseAsync(lease, cancellationToken).ConfigureAwait(false);
                if (!recovery.Succeeded) return recovery;
                lease = null;
            }

            var before = ReadState(scheme);
            // Another writer's changes become the new baseline. Never raise an
            // unowned cap, including a user-selected 99% cap.
            var originalAc = lease is not null && before.AcMaximum == lease.ExpectedAcMaximum
                ? lease.OriginalAcMaximum : before.AcMaximum;
            var originalDc = lease is not null && before.DcMaximum == lease.ExpectedDcMaximum
                ? lease.OriginalDcMaximum : before.DcMaximum;
            var target = new State((uint)policy.AcMode, (uint)policy.DcMode,
                policy.AcMode == ProcessorBoostMode.Disabled ? Math.Min(originalAc, 99u) : originalAc,
                policy.DcMode == ProcessorBoostMode.Disabled ? Math.Min(originalDc, 99u) : originalDc);
            var pending = new ProcessorBoostCompatibilityLease(1, scheme, originalAc, originalDc,
                target.AcMaximum, target.DcMaximum);
            if (target.AcMaximum != before.AcMaximum || target.DcMaximum != before.DcMaximum || lease is not null)
                await leaseStore.SaveAsync(pending, cancellationToken).ConfigureAwait(false);
            try
            {
                Write(scheme, target, before);
                api.SetActiveScheme(scheme);
                if (ReadState(scheme) != target) throw new InvalidDataException("写入后回读不一致。");
                if (target.AcMaximum == originalAc && target.DcMaximum == originalDc)
                    await leaseStore.ClearAsync(CancellationToken.None).ConfigureAwait(false);
                return new(true, "Windows 睿频策略已应用；保留用户已有的处理器上限。");
            }
            catch (Exception exception)
            {
                try
                {
                    var current = ReadState(scheme);
                    var restored = RestoreOwned(current, target, before);
                    Write(scheme, restored, current);
                    api.SetActiveScheme(scheme);
                    if (ReadState(scheme) != restored) throw new InvalidDataException("恢复后回读不一致。");
                    if (lease is null) await leaseStore.ClearAsync(CancellationToken.None).ConfigureAwait(false);
                    else await leaseStore.SaveAsync(lease, CancellationToken.None).ConfigureAwait(false);
                    return ControlResult.Rejected($"Windows 睿频策略应用失败，已恢复原值：{exception.Message}");
                }
                catch (Exception rollbackException)
                {
                    // Partial rollback can leave either the previous or target cap.
                    // Keep ownership of either value for next-start recovery.
                    try
                    {
                        var current = ReadState(scheme);
                        pending = pending with
                        {
                            OriginalAcMaximum = current.AcMaximum == before.AcMaximum || current.AcMaximum == target.AcMaximum
                                ? originalAc : current.AcMaximum,
                            OriginalDcMaximum = current.DcMaximum == before.DcMaximum || current.DcMaximum == target.DcMaximum
                                ? originalDc : current.DcMaximum,
                            ExpectedAcMaximum = current.AcMaximum,
                            ExpectedDcMaximum = current.DcMaximum
                        };
                        await leaseStore.SaveAsync(pending, CancellationToken.None).ConfigureAwait(false);
                    }
                    catch (Exception) { /* Keep write-ahead recovery information. */ }
                    return ControlResult.Rejected(
                        $"Windows 睿频策略应用及恢复均失败：{exception.Message}；{rollbackException.Message}");
                }
            }
        }
        catch (Exception exception)
        {
            return ControlResult.Rejected($"Windows 睿频策略应用失败：{exception.Message}");
        }
        finally { gate.Release(); }
    }

    public async ValueTask<ControlResult> RecoverAsync(CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var lease = await leaseStore.LoadAsync(cancellationToken).ConfigureAwait(false);
            ValidateLease(lease);
            return lease is null ? new(true, "没有待恢复的处理器兼容限制。")
                : await RecoverLeaseAsync(lease, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            return ControlResult.Rejected($"处理器兼容限制恢复失败：{exception.Message}");
        }
        finally { gate.Release(); }
    }

    private async ValueTask<ControlResult> RecoverLeaseAsync(
        ProcessorBoostCompatibilityLease lease, CancellationToken cancellationToken)
    {
        var currentAc = ReadAc(lease.Scheme, MaximumProcessorStateSetting);
        var currentDc = ReadDc(lease.Scheme, MaximumProcessorStateSetting);
        var targetAc = currentAc == lease.ExpectedAcMaximum ? lease.OriginalAcMaximum : currentAc;
        var targetDc = currentDc == lease.ExpectedDcMaximum ? lease.OriginalDcMaximum : currentDc;
        if (targetAc != currentAc) WriteAc(lease.Scheme, MaximumProcessorStateSetting, targetAc);
        if (targetDc != currentDc) WriteDc(lease.Scheme, MaximumProcessorStateSetting, targetDc);
        if ((targetAc != currentAc || targetDc != currentDc) && api.GetActiveScheme() == lease.Scheme)
            api.SetActiveScheme(lease.Scheme);
        if (ReadAc(lease.Scheme, MaximumProcessorStateSetting) != targetAc ||
            ReadDc(lease.Scheme, MaximumProcessorStateSetting) != targetDc)
            return ControlResult.Rejected("处理器兼容限制恢复后回读不一致，保留恢复记录。");
        await leaseStore.ClearAsync(cancellationToken).ConfigureAwait(false);
        return new(true, "已恢复临时处理器兼容限制；外部修改保持不变。");
    }

    private State ReadState(Guid scheme) => new(ReadAc(scheme, BoostModeSetting), ReadDc(scheme, BoostModeSetting),
        ReadAc(scheme, MaximumProcessorStateSetting), ReadDc(scheme, MaximumProcessorStateSetting));
    private void Write(Guid scheme, State target, State current)
    {
        WriteAc(scheme, BoostModeSetting, target.AcBoost);
        WriteDc(scheme, BoostModeSetting, target.DcBoost);
        if (target.AcMaximum != current.AcMaximum) WriteAc(scheme, MaximumProcessorStateSetting, target.AcMaximum);
        if (target.DcMaximum != current.DcMaximum) WriteDc(scheme, MaximumProcessorStateSetting, target.DcMaximum);
    }
    private static State RestoreOwned(State current, State expected, State original) => new(
        current.AcBoost == expected.AcBoost ? original.AcBoost : current.AcBoost,
        current.DcBoost == expected.DcBoost ? original.DcBoost : current.DcBoost,
        current.AcMaximum == expected.AcMaximum ? original.AcMaximum : current.AcMaximum,
        current.DcMaximum == expected.DcMaximum ? original.DcMaximum : current.DcMaximum);
    private static void ValidateLease(ProcessorBoostCompatibilityLease? lease)
    {
        if (lease is not null && (lease.Version != 1 || lease.Scheme == Guid.Empty ||
            lease.OriginalAcMaximum > 100 || lease.OriginalDcMaximum > 100 ||
            lease.ExpectedAcMaximum > 100 || lease.ExpectedDcMaximum > 100))
            throw new InvalidDataException("处理器兼容限制恢复记录无效。");
    }
    private uint ReadAc(Guid scheme, Guid setting) => api.ReadAcValue(scheme, ProcessorSubgroup, setting);
    private uint ReadDc(Guid scheme, Guid setting) => api.ReadDcValue(scheme, ProcessorSubgroup, setting);
    private void WriteAc(Guid scheme, Guid setting, uint value) => api.WriteAcValue(scheme, ProcessorSubgroup, setting, value);
    private void WriteDc(Guid scheme, Guid setting, uint value) => api.WriteDcValue(scheme, ProcessorSubgroup, setting, value);
    private sealed record State(uint AcBoost, uint DcBoost, uint AcMaximum, uint DcMaximum);
    private sealed class MemoryLeaseStore : IProcessorBoostCompatibilityLeaseStore
    {
        private ProcessorBoostCompatibilityLease? lease;
        public ValueTask<ProcessorBoostCompatibilityLease?> LoadAsync(CancellationToken token) => ValueTask.FromResult(lease);
        public ValueTask SaveAsync(ProcessorBoostCompatibilityLease value, CancellationToken token)
        { lease = value; return ValueTask.CompletedTask; }
        public ValueTask ClearAsync(CancellationToken token)
        { lease = null; return ValueTask.CompletedTask; }
    }

    private static ProcessorBoostMode ToMode(uint value) => value <= 6
        ? (ProcessorBoostMode)value
        : throw new InvalidDataException($"Windows 返回未知的睿频策略索引：{value}。");

    private static bool IsValid(ProcessorBoostMode value) => (uint)value <= 6;
}
