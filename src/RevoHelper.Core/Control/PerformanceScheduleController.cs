namespace RevoHelper.Core.Control;

public sealed class PerformanceScheduleController(
    IPlatformModeController platformController,
    ICpuPolicyController cpuController,
    IProcessorBoostController boostController,
    IProcessorFrequencyLimitController frequencyController,
    Action<string, string>? diagnostic = null)
{
    private readonly SemaphoreSlim operationLock = new(1, 1);

    private void Trace(string message)
    {
        try { diagnostic?.Invoke("performance-schedule", message); }
        catch (Exception) { /* Diagnostics must not change control outcomes. */ }
    }

    public async ValueTask<PerformanceScheduleResult> ApplyAsync(
        PerformanceSchedule target,
        bool platformAlreadySelected,
        CancellationToken cancellationToken,
        bool retainSelectedPlatformOnFailure = false)
    {
        ArgumentNullException.ThrowIfNull(target);
        await operationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        var stage = "baseline-platform-read";
        try
        {
            Trace($"stage=request target=({Describe(target)}) platformAlreadySelected={platformAlreadySelected}");
            var baselineRestorePoint = await platformController.CaptureRestorePointAsync(cancellationToken).ConfigureAwait(false);
            var baselinePlatform = baselineRestorePoint?.Selection;
            Trace($"stage={stage} result=success actual={Mode(baselinePlatform)} restoreStrategy={baselineRestorePoint?.RestoreStrategy}");
            stage = "baseline-boost-read";
            var baselineBoost = await boostController.ReadAsync(cancellationToken).ConfigureAwait(false);
            Trace($"stage={stage} result=success actual={baselineBoost.AcMode}/{baselineBoost.DcMode}");
            stage = "baseline-cpu-read";
            var baselineCpu = target.Cpu is null
                ? null
                : await CpuPolicyRestorePoint.CaptureAsync(cpuController, cancellationToken).ConfigureAwait(false);
            Trace($"stage={stage} result={(target.Cpu is null ? "skipped" : "complete")} actual={Cpu(baselineCpu?.Policy)}");
            stage = "baseline-frequency-read";
            var baselineFrequency = await frequencyController.ReadAsync(cancellationToken).ConfigureAwait(false);
            Trace($"stage={stage} result=success actual=({Frequency(baselineFrequency)})");
            if (baselineRestorePoint is null || baselinePlatform is null)
            {
                Trace("stage=baseline result=rejected reason=当前整机模式不可读");
                return new(false, "无法读取当前整机模式，未执行调度。");
            }
            if (retainSelectedPlatformOnFailure && baselinePlatform != target.Platform)
            {
                Trace($"stage=baseline result=rejected reason=已确认档位发生变化 actual={Mode(baselinePlatform)}");
                return new(false, "整机档位确认后已发生变化，未应用后续策略。") { ConfirmedPlatform = baselinePlatform };
            }

            var cpuWriteAttempted = false;
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
                    stage = "platform-apply";
                    Trace($"stage={stage} result=begin target={Mode(target.Platform)}");
                    var platformResult = await platformController
                        .SetPlatformModeAsync(target.Platform, cancellationToken)
                        .ConfigureAwait(false);
                    Trace($"stage={stage} result={(platformResult.Succeeded ? "success" : "rejected")} reason={platformResult.Reason}");
                    if (!platformResult.Succeeded)
                    {
                        if (platformResult.ConfirmedPlatform is { } confirmedPlatform)
                        {
                            // A rejected optional sub-strategy must not undo a
                            // basic mode already confirmed by the platform. No
                            // associated policy has been written at this stage.
                            Trace($"stage=platform-apply result=partial actual={Mode(confirmedPlatform)}");
                            return new(false, platformResult.Reason) { ConfirmedPlatform = confirmedPlatform };
                        }
                        throw new TransitionException(platformResult.Reason);
                    }
                    targetPlatformSelected = true;
                }
                else
                {
                    targetPlatformSelected = true;
                    Trace($"stage=platform-apply result=skipped actual={Mode(baselinePlatform)}");
                }

                if (target.Cpu is not null)
                {
                    stage = "target-cpu-baseline-read";
                    targetCpuBaseline = await CpuPolicyRestorePoint
                        .CaptureAsync(cpuController, cancellationToken)
                        .ConfigureAwait(false)
                        ?? throw new TransitionException("无法读取目标档案的 CPU 功耗基线，未写入功耗限制。");
                    Trace($"stage={stage} result=success actual={Cpu(targetCpuBaseline.Policy)}");
                }

                stage = "boost-apply";
                Trace($"stage={stage} result=begin target={target.Boost.AcMode}/{target.Boost.DcMode}");
                var boostResult = await boostController.ApplyAsync(target.Boost, cancellationToken).ConfigureAwait(false);
                Trace($"stage={stage} result={(boostResult.Succeeded ? "success" : "rejected")} reason={boostResult.Reason}");
                if (!boostResult.Succeeded) throw new TransitionException(boostResult.Reason);

                if (target.Cpu is not null)
                {
                    stage = "cpu-apply";
                    Trace($"stage={stage} result=begin target={Cpu(target.Cpu)}");
                    cpuWriteAttempted = true;
                    var cpuResult = await cpuController.ApplyCpuPolicyAsync(target.Cpu, cancellationToken).ConfigureAwait(false);
                    Trace($"stage={stage} result={(cpuResult.Succeeded ? "success" : "rejected")} reason={cpuResult.Reason}");
                    if (!cpuResult.Succeeded) throw new TransitionException(cpuResult.Reason);

                }

                stage = "frequency-apply";
                Trace($"stage={stage} result=begin target=({Frequency(target.Frequency)})");
                var frequencyResult = await frequencyController
                    .ApplyAsync(target.Frequency, cancellationToken)
                    .ConfigureAwait(false);
                Trace($"stage={stage} result={(frequencyResult.Succeeded ? "success" : "rejected")} target=({Frequency(target.Frequency)}) reason={frequencyResult.Reason}");
                if (!frequencyResult.Succeeded) throw new TransitionException(frequencyResult.Reason);

                stage = "final-readback";
                Trace($"stage={stage} result=begin");
                var readback = await ReadbackAsync(target.Cpu is not null, cancellationToken).ConfigureAwait(false);
                Trace($"stage={stage} result=observed actual=({Describe(readback)})");
                if (readback.Platform != target.Platform || readback.Boost != target.Boost ||
                    readback.Frequency != target.Frequency ||
                    (target.Cpu is not null && readback.Cpu != target.Cpu))
                {
                    Trace($"stage={stage} result=unconfirmed");
                    throw new TransitionException("最终回读与目标调度方案不一致。");
                }

                var successReason = target.Cpu is null
                    ? "整机模式、Boost 与 Windows 频率策略已应用并回读；OEM 预设 PL 未写入。"
                    : "整机模式、Boost、PL1/PL2 与 Windows 频率策略已应用并回读。";
                Trace("stage=complete result=success");
                return new(true, successReason, readback) { ConfirmedPlatform = readback.Platform };
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                Trace($"stage={stage} result=exception type={exception.GetType().Name} reason={exception.Message}");
                if (retainSelectedPlatformOnFailure)
                {
                    // The baselines were read after the caller's mode switch, so they hold the
                    // previous profile's Boost and frequency. Writing them back would pin those
                    // onto the new mode; the caller reconciles from the actual mode instead.
                    stage = "retain-platform";
                    var retained = await RetainSelectedPlatformAsync(
                        target.Platform,
                        cpuWriteAttempted ? targetCpuBaseline : null,
                        exception.Message,
                        cancellationToken).ConfigureAwait(false);
                    Trace($"stage={stage} result=retained actual={Mode(retained.ConfirmedPlatform)} reason={retained.Reason}");
                    return retained;
                }
                stage = "rollback";
                Trace($"stage={stage} result=begin original={Mode(baselinePlatform)} targetWasSelected={targetPlatformSelected}");
                var rollback = await RollbackAsync(
                    new(baselinePlatform, baselineCpu?.Policy, baselineBoost, baselineFrequency),
                    baselineRestorePoint,
                    baselineCpu,
                    target.Platform,
                    targetCpuBaseline,
                    targetPlatformSelected,
                    cancellationToken).ConfigureAwait(false);
                Trace($"stage={stage} result={(rollback.Succeeded ? "success" : "rejected")} reason={rollback.Reason}");
                return rollback.Succeeded
                    ? new(false, $"调度失败，已恢复原整机模式、睿频策略和功耗：{exception.Message}")
                    : new(false, $"调度失败且恢复不完整：{exception.Message}；{rollback.Reason}");
            }
        }
        catch (Exception exception)
        {
            Trace($"stage={stage} result=exception type={exception.GetType().Name} reason={exception.Message}");
            throw;
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

    private async ValueTask<PerformanceScheduleResult> RetainSelectedPlatformAsync(
        PlatformModeSelection target,
        CpuPolicyRestorePoint? writtenCpuBaseline,
        string failure,
        CancellationToken cancellationToken)
    {
        var failures = new List<string>();
        if (writtenCpuBaseline is not null)
        {
            await RestoreAsync(() => writtenCpuBaseline.RestoreAsync(cancellationToken), failures).ConfigureAwait(false);
        }

        PlatformModeSelection? actual = null;
        try { actual = await platformController.ReadPlatformModeAsync(cancellationToken).ConfigureAwait(false); }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            failures.Add($"整机模式回读失败：{exception.Message}");
        }

        var suffix = failures.Count == 0 ? "" : $"；{string.Join("；", failures)}";
        return new(false, $"已保留当前整机档位，后续策略未完全应用：{failure}{suffix}")
        {
            ConfirmedPlatform = actual ?? target
        };
    }

    private async ValueTask<ControlResult> RollbackAsync(
        PerformanceSchedule baseline,
        PlatformRestorePoint baselineRestorePoint,
        CpuPolicyRestorePoint? baselineCpu,
        PlatformModeSelection targetPlatform,
        CpuPolicyRestorePoint? targetCpuBaseline,
        bool targetPlatformSelected,
        CancellationToken cancellationToken)
    {
        var failures = new List<string>();
        if (targetPlatformSelected && targetCpuBaseline is not null)
        {
            // Reselecting the same mode for a PL restore must not add a sub-strategy either.
            var targetSelectionRestored = await RestoreAsync(
                () => targetPlatform == baselineRestorePoint.Selection
                    ? platformController.RestorePlatformModeAsync(baselineRestorePoint, cancellationToken)
                    : platformController.SetPlatformModeAsync(targetPlatform, cancellationToken),
                failures).ConfigureAwait(false);
            if (targetSelectionRestored)
            {
                await RestoreAsync(
                    () => targetCpuBaseline.RestoreAsync(cancellationToken),
                    failures).ConfigureAwait(false);
            }
        }

        ControlResult? platformRestore = null;
        var originalPlatformRestored = await RestoreAsync(
            async () => platformRestore = await platformController
                .RestorePlatformModeAsync(baselineRestorePoint, cancellationToken).ConfigureAwait(false),
            failures).ConfigureAwait(false);
        // The mode can return while a strategy written by this attempt stays on. There is
        // no known command to clear it, so report the difference instead of claiming success.
        if (originalPlatformRestored && platformRestore?.ConfirmedPlatform is { } restoredPlatform &&
            restoredPlatform != baselineRestorePoint.Selection)
        {
            failures.Add($"整机模式已恢复，但狂暴子策略未回到原状态（当前回读 {Mode(restoredPlatform)}）。");
        }
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

    private static string Mode(PlatformModeSelection? value) =>
        value is null ? "null" : $"{value.Mode}/{value.ProfileIndex}";

    private static string Cpu(CpuPolicy? value) =>
        value is null ? "null" : $"PL1={value.Pl1Watts},PL2={value.Pl2Watts},MaxMHz={value.MaxFrequencyMhz}";

    private static string Frequency(ProcessorFrequencyLimitPolicy value) =>
        $"AC={value.AcMaximumMhz},DC={value.DcMaximumMhz},AC1={value.AcClass1MaximumMhz},DC1={value.DcClass1MaximumMhz},AC2={value.AcClass2MaximumMhz},DC2={value.DcClass2MaximumMhz}";

    private static string Describe(PerformanceSchedule value) =>
        $"platform={Mode(value.Platform)} cpu=({Cpu(value.Cpu)}) boost={value.Boost.AcMode}/{value.Boost.DcMode} frequency=({Frequency(value.Frequency)})";
}
