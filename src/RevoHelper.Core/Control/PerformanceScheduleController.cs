namespace RevoHelper.Core.Control;

public sealed class PerformanceScheduleController(
    IPlatformModeController platformController,
    ICpuPolicyController cpuController,
    IProcessorBoostController boostController,
    IProcessorFrequencyLimitController frequencyController)
{
    private readonly SemaphoreSlim operationLock = new(1, 1);

    public async ValueTask<PerformanceScheduleResult> ApplyAsync(
        PerformanceSchedule target,
        bool platformAlreadySelected,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);
        await operationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var baselinePlatform = await platformController.ReadPlatformModeAsync(cancellationToken).ConfigureAwait(false);
            var baselineBoost = await boostController.ReadAsync(cancellationToken).ConfigureAwait(false);
            var baselineCpu = target.Cpu is null
                ? null
                : await CpuPolicyRestorePoint.CaptureAsync(cpuController, cancellationToken).ConfigureAwait(false);
            var baselineFrequency = await frequencyController.ReadAsync(cancellationToken).ConfigureAwait(false);
            if (baselinePlatform is null)
            {
                return new(false, "无法读取当前整机模式，未执行调度。");
            }

            CpuPolicyRestorePoint? targetCpuBaseline = null;
            var targetPlatformSelected = platformAlreadySelected && baselinePlatform == target.Platform;
            try
            {
                // The official GCU client always selects the target custom profile
                // immediately before editing its PL fields. Repeat that selection even
                // when the UI already shows Custom, because the UI slot and the live
                // firmware slot can drift after an external/profile change.
                if (!platformAlreadySelected ||
                    baselinePlatform != target.Platform ||
                    target.Platform.Mode == PlatformMode.Custom)
                {
                    var platformResult = await platformController
                        .SetPlatformModeAsync(target.Platform, cancellationToken)
                        .ConfigureAwait(false);
                    if (!platformResult.Succeeded) throw new TransitionException(platformResult.Reason);
                    targetPlatformSelected = true;
                }
                else
                {
                    targetPlatformSelected = true;
                }

                if (target.Cpu is not null)
                {
                    targetCpuBaseline = await CpuPolicyRestorePoint
                        .CaptureAsync(cpuController, cancellationToken)
                        .ConfigureAwait(false)
                        ?? throw new TransitionException("无法读取目标档案的 CPU 功耗基线，未写入功耗限制。");
                }

                var boostResult = await boostController.ApplyAsync(target.Boost, cancellationToken).ConfigureAwait(false);
                if (!boostResult.Succeeded) throw new TransitionException(boostResult.Reason);

                if (target.Cpu is not null)
                {
                    var cpuResult = await cpuController.ApplyCpuPolicyAsync(target.Cpu, cancellationToken).ConfigureAwait(false);
                    if (!cpuResult.Succeeded) throw new TransitionException(cpuResult.Reason);

                }

                var frequencyResult = await frequencyController
                    .ApplyAsync(target.Frequency, cancellationToken)
                    .ConfigureAwait(false);
                if (!frequencyResult.Succeeded) throw new TransitionException(frequencyResult.Reason);

                var readback = await ReadbackAsync(target.Cpu is not null, cancellationToken).ConfigureAwait(false);
                if (readback.Platform != target.Platform || readback.Boost != target.Boost ||
                    readback.Frequency != target.Frequency ||
                    (target.Cpu is not null && readback.Cpu != target.Cpu))
                {
                    throw new TransitionException("最终回读与目标调度方案不一致。");
                }

                var successReason = target.Cpu is null
                    ? "整机模式、Boost 与 Windows 频率策略已应用并回读；OEM 预设 PL 未写入。"
                    : "整机模式、Boost、PL1/PL2 与 Windows 频率策略已应用并回读。";
                return new(true, successReason, readback);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                var rollback = await RollbackAsync(
                    new(baselinePlatform, baselineCpu?.Policy, baselineBoost, baselineFrequency),
                    baselineCpu,
                    target.Platform,
                    targetCpuBaseline,
                    targetPlatformSelected,
                    cancellationToken).ConfigureAwait(false);
                return rollback.Succeeded
                    ? new(false, $"调度失败，已恢复原整机模式、睿频策略和功耗：{exception.Message}")
                    : new(false, $"调度失败且恢复不完整：{exception.Message}；{rollback.Reason}");
            }
        }
        finally
        {
            operationLock.Release();
        }
    }

    private async ValueTask<PerformanceSchedule> ReadbackAsync(
        bool requireCpu,
        CancellationToken cancellationToken)
    {
        var platform = await platformController.ReadPlatformModeAsync(cancellationToken).ConfigureAwait(false)
            ?? throw new TransitionException("整机模式回读不可用。");
        var boost = await boostController.ReadAsync(cancellationToken).ConfigureAwait(false);
        var cpu = await cpuController.ReadCpuPolicyAsync(cancellationToken).ConfigureAwait(false);
        if (requireCpu && cpu is null)
        {
            throw new TransitionException("CPU 功耗回读不可用。");
        }
        var frequency = await frequencyController.ReadAsync(cancellationToken).ConfigureAwait(false);
        return new(platform, cpu, boost, frequency);
    }

    private async ValueTask<ControlResult> RollbackAsync(
        PerformanceSchedule baseline,
        CpuPolicyRestorePoint? baselineCpu,
        PlatformModeSelection targetPlatform,
        CpuPolicyRestorePoint? targetCpuBaseline,
        bool targetPlatformSelected,
        CancellationToken cancellationToken)
    {
        var failures = new List<string>();
        if (targetPlatformSelected && targetCpuBaseline is not null)
        {
            var targetSelectionRestored = await RestoreAsync(
                () => platformController.SetPlatformModeAsync(targetPlatform, cancellationToken),
                failures).ConfigureAwait(false);
            if (targetSelectionRestored)
            {
                await RestoreAsync(
                    () => targetCpuBaseline.RestoreAsync(cancellationToken),
                    failures).ConfigureAwait(false);
            }
        }

        var originalPlatformRestored = await RestoreAsync(
            () => platformController.SetPlatformModeAsync(baseline.Platform, cancellationToken),
            failures).ConfigureAwait(false);
        if (originalPlatformRestored && baselineCpu is not null)
        {
            await RestoreAsync(
                () => baselineCpu.RestoreAsync(cancellationToken),
                failures).ConfigureAwait(false);
        }
        await RestoreAsync(
            () => boostController.ApplyAsync(baseline.Boost, cancellationToken),
            failures).ConfigureAwait(false);
        await RestoreAsync(
            () => frequencyController.ApplyAsync(baseline.Frequency, cancellationToken),
            failures).ConfigureAwait(false);
        return failures.Count == 0
            ? new(true, "已恢复")
            : ControlResult.Rejected(string.Join("；", failures));
    }

    private static async ValueTask<bool> RestoreAsync(
        Func<ValueTask<ControlResult>> restore,
        List<string> failures)
    {
        try
        {
            var result = await restore().ConfigureAwait(false);
            if (!result.Succeeded) failures.Add(result.Reason);
            return result.Succeeded;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            failures.Add(exception.Message);
            return false;
        }
    }

    private sealed class TransitionException(string message) : Exception(message);
}
