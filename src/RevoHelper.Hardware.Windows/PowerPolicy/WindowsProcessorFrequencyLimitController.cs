using RevoHelper.Core.Control;

namespace RevoHelper.Hardware.Windows.PowerPolicy;

public sealed class WindowsProcessorFrequencyLimitController : IProcessorFrequencyLimitController
{
    private readonly IPowerProfileApi api;
    private readonly IProcessorFrequencyLimitLeaseStore leaseStore;
    private readonly SemaphoreSlim gate = new(1, 1);

    public static readonly Guid MaximumFrequencySetting =
        Guid.Parse("75b0ae3f-bce0-45a7-8c89-c9611c25e100");
    public static readonly Guid MaximumFrequencyClass1Setting =
        Guid.Parse("75b0ae3f-bce0-45a7-8c89-c9611c25e101");
    public static readonly Guid MaximumFrequencyClass2Setting =
        Guid.Parse("75b0ae3f-bce0-45a7-8c89-c9611c25e102");

    public WindowsProcessorFrequencyLimitController(IPowerProfileApi api)
        : this(api, new MemoryLeaseStore())
    {
    }

    public WindowsProcessorFrequencyLimitController(
        IPowerProfileApi api,
        IProcessorFrequencyLimitLeaseStore leaseStore)
    {
        this.api = api;
        this.leaseStore = leaseStore;
    }

    public ValueTask<ProcessorFrequencyLimitPolicy> ReadAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var scheme = api.GetActiveScheme();
        return ValueTask.FromResult(Read(scheme));
    }

    public async ValueTask<ControlResult> ApplyAsync(
        ProcessorFrequencyLimitPolicy policy,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!Values(policy).All(IsValid))
        {
            return ControlResult.Rejected("最大处理器频率必须为 0（不限制）或 100–64000 MHz。");
        }

        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var lease = await leaseStore.LoadAsync(cancellationToken).ConfigureAwait(false);
            var scheme = api.GetActiveScheme();
            if (lease is not null && lease.Scheme != scheme)
            {
                var recovery = await RecoverLeaseAsync(lease, cancellationToken).ConfigureAwait(false);
                if (!recovery.Succeeded)
                {
                    return recovery;
                }

                lease = null;
            }

            var previous = Read(scheme);
            if (lease is not null && previous != lease.Expected)
            {
                var recovery = await RecoverLeaseAsync(lease, cancellationToken).ConfigureAwait(false);
                if (!recovery.Succeeded)
                {
                    return recovery;
                }

                lease = null;
                previous = Read(scheme);
            }

            var priorLease = lease;
            if (!IsDisabled(policy) || priorLease is not null || policy != previous)
            {
                lease = new ProcessorFrequencyLimitLease(
                    1,
                    scheme,
                    priorLease?.Original ?? previous,
                    policy);
                await leaseStore.SaveAsync(lease, cancellationToken).ConfigureAwait(false);
            }

            try
            {
                Write(scheme, policy);
                api.SetActiveScheme(scheme);
                var readback = Read(scheme);
                if (readback != policy)
                {
                    throw new InvalidDataException(
                        $"写入后回读不一致：AC {readback.AcMaximumMhz} / DC {readback.DcMaximumMhz} MHz。");
                }

                return new ControlResult(
                    true,
                    $"Windows 已接受并回读 AC {readback.AcMaximumMhz} / DC {readback.DcMaximumMhz} MHz。");
            }
            catch (Exception exception)
            {
                var rollback = RestoreAfterFailure(scheme, previous, exception);
                if (rollback.Restored)
                {
                    if (priorLease is null)
                    {
                        await leaseStore.ClearAsync(CancellationToken.None).ConfigureAwait(false);
                    }
                    else
                    {
                        await leaseStore.SaveAsync(priorLease, CancellationToken.None).ConfigureAwait(false);
                    }
                }
                else
                {
                    // A failed rollback can leave a mix of the previous and new
                    // limits. Keep both as owned fields so next-start recovery
                    // does not silently abandon the previous session limit.
                    try
                    {
                        var current = Read(scheme);
                        var currentValues = Values(current);
                        var previousValues = Values(previous);
                        var targetValues = Values(policy);
                        var originalValues = Values(priorLease?.Original ?? previous);
                        for (var index = 0; index < currentValues.Length; index++)
                        {
                            if (currentValues[index] != previousValues[index] &&
                                currentValues[index] != targetValues[index])
                                originalValues[index] = currentValues[index]; // Preserve an external change.
                        }
                        await leaseStore.SaveAsync(new ProcessorFrequencyLimitLease(1, scheme,
                            new(originalValues[0], originalValues[1], originalValues[2],
                                originalValues[3], originalValues[4], originalValues[5]), current),
                            CancellationToken.None).ConfigureAwait(false);
                    }
                    catch (Exception) { /* Keep the write-ahead lease if recovery evidence is unavailable. */ }
                }

                return rollback.Result;
            }
        }
        catch (Exception exception)
        {
            return ControlResult.Rejected($"最大处理器频率读取或写入失败：{exception.Message}");
        }
        finally
        {
            gate.Release();
        }
    }

    public async ValueTask<ControlResult> RecoverAsync(CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var lease = await leaseStore.LoadAsync(cancellationToken).ConfigureAwait(false);
            return lease is null
                ? new ControlResult(true, "没有待恢复的处理器频率限制。")
                : await RecoverLeaseAsync(lease, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            return ControlResult.Rejected($"最大处理器频率恢复失败：{exception.Message}");
        }
        finally
        {
            gate.Release();
        }
    }

    private async ValueTask<ControlResult> RecoverLeaseAsync(
        ProcessorFrequencyLimitLease lease,
        CancellationToken cancellationToken)
    {
        try
        {
            var current = Read(lease.Scheme);
            var restored = RestoreOwnedFields(current, lease);
            if (restored != current)
            {
                Write(lease.Scheme, restored);
                if (api.GetActiveScheme() == lease.Scheme)
                {
                    api.SetActiveScheme(lease.Scheme);
                }

                var readback = Read(lease.Scheme);
                if (!OwnedFieldsWereRestored(readback, current, lease))
                {
                    throw new InvalidDataException("恢复后回读不一致。");
                }
            }

            await leaseStore.ClearAsync(cancellationToken).ConfigureAwait(false);
            return new ControlResult(true, "已恢复 RevoHelper 会话前的处理器频率设置。");
        }
        catch (Exception exception)
        {
            return ControlResult.Rejected($"最大处理器频率恢复失败：{exception.Message}");
        }
    }

    private ProcessorFrequencyLimitPolicy Read(Guid scheme) => new(
        ReadAc(scheme, MaximumFrequencySetting),
        ReadDc(scheme, MaximumFrequencySetting),
        ReadAc(scheme, MaximumFrequencyClass1Setting),
        ReadDc(scheme, MaximumFrequencyClass1Setting),
        ReadAc(scheme, MaximumFrequencyClass2Setting),
        ReadDc(scheme, MaximumFrequencyClass2Setting));

    private void Write(Guid scheme, ProcessorFrequencyLimitPolicy policy)
    {
        WriteAc(scheme, MaximumFrequencySetting, policy.AcMaximumMhz);
        WriteDc(scheme, MaximumFrequencySetting, policy.DcMaximumMhz);
        WriteAc(scheme, MaximumFrequencyClass1Setting, policy.AcClass1MaximumMhz);
        WriteDc(scheme, MaximumFrequencyClass1Setting, policy.DcClass1MaximumMhz);
        WriteAc(scheme, MaximumFrequencyClass2Setting, policy.AcClass2MaximumMhz);
        WriteDc(scheme, MaximumFrequencyClass2Setting, policy.DcClass2MaximumMhz);
    }

    private (ControlResult Result, bool Restored) RestoreAfterFailure(
        Guid scheme,
        ProcessorFrequencyLimitPolicy previous,
        Exception failure)
    {
        try
        {
            Write(scheme, previous);
            api.SetActiveScheme(scheme);
            if (Read(scheme) != previous)
            {
                throw new InvalidDataException("恢复后回读不一致。");
            }
            return (ControlResult.Rejected(
                $"最大处理器频率应用失败，已恢复原值：{failure.Message}"), true);
        }
        catch (Exception rollbackFailure)
        {
            return (ControlResult.Rejected(
                $"最大处理器频率应用及恢复均失败：{failure.Message}；{rollbackFailure.Message}"), false);
        }
    }

    private static bool IsValid(uint value) => value is 0 or >= 100 and <= 64000;

    private static bool IsDisabled(ProcessorFrequencyLimitPolicy policy) =>
        Values(policy).All(value => value == 0);

    private static ProcessorFrequencyLimitPolicy RestoreOwnedFields(
        ProcessorFrequencyLimitPolicy current,
        ProcessorFrequencyLimitLease lease) => new(
            current.AcMaximumMhz == lease.Expected.AcMaximumMhz ? lease.Original.AcMaximumMhz : current.AcMaximumMhz,
            current.DcMaximumMhz == lease.Expected.DcMaximumMhz ? lease.Original.DcMaximumMhz : current.DcMaximumMhz,
            current.AcClass1MaximumMhz == lease.Expected.AcClass1MaximumMhz ? lease.Original.AcClass1MaximumMhz : current.AcClass1MaximumMhz,
            current.DcClass1MaximumMhz == lease.Expected.DcClass1MaximumMhz ? lease.Original.DcClass1MaximumMhz : current.DcClass1MaximumMhz,
            current.AcClass2MaximumMhz == lease.Expected.AcClass2MaximumMhz ? lease.Original.AcClass2MaximumMhz : current.AcClass2MaximumMhz,
            current.DcClass2MaximumMhz == lease.Expected.DcClass2MaximumMhz ? lease.Original.DcClass2MaximumMhz : current.DcClass2MaximumMhz);

    private static bool OwnedFieldsWereRestored(
        ProcessorFrequencyLimitPolicy readback,
        ProcessorFrequencyLimitPolicy before,
        ProcessorFrequencyLimitLease lease)
    {
        var expected = RestoreOwnedFields(before, lease);
        return readback == expected;
    }

    private uint ReadAc(Guid scheme, Guid setting) => api.ReadAcValue(
        scheme, WindowsProcessorBoostController.ProcessorSubgroup, setting);

    private uint ReadDc(Guid scheme, Guid setting) => api.ReadDcValue(
        scheme, WindowsProcessorBoostController.ProcessorSubgroup, setting);

    private void WriteAc(Guid scheme, Guid setting, uint value) => api.WriteAcValue(
        scheme, WindowsProcessorBoostController.ProcessorSubgroup, setting, value);

    private void WriteDc(Guid scheme, Guid setting, uint value) => api.WriteDcValue(
        scheme, WindowsProcessorBoostController.ProcessorSubgroup, setting, value);

    private static uint[] Values(ProcessorFrequencyLimitPolicy policy) =>
    [
        policy.AcMaximumMhz,
        policy.DcMaximumMhz,
        policy.AcClass1MaximumMhz,
        policy.DcClass1MaximumMhz,
        policy.AcClass2MaximumMhz,
        policy.DcClass2MaximumMhz
    ];

    private sealed class MemoryLeaseStore : IProcessorFrequencyLimitLeaseStore
    {
        private ProcessorFrequencyLimitLease? lease;

        public ValueTask<ProcessorFrequencyLimitLease?> LoadAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult(lease);

        public ValueTask SaveAsync(
            ProcessorFrequencyLimitLease value,
            CancellationToken cancellationToken)
        {
            lease = value;
            return ValueTask.CompletedTask;
        }

        public ValueTask ClearAsync(CancellationToken cancellationToken)
        {
            lease = null;
            return ValueTask.CompletedTask;
        }
    }
}
