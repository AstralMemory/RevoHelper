using RevoHelper.Core.Control;
using RevoHelper.Core.Dashboard;

namespace RevoHelper.App.Presentation;

// Everything that happens after a performance mode is clicked or changes externally.
// Flow: queue -> prepare -> mode (with or without the schedule) -> recheck frequency.
public sealed partial class MainWindowViewModel
{
    public async Task<bool> InitializePlatformModeAsync(CancellationToken cancellationToken)
    {
        if (platformModeController is null || isApplyingPlatformMode) return false;
        var requestRevision = Volatile.Read(ref platformRequestRevision);
        try
        {
            var live = await platformModeController.ReadPlatformModeAsync(cancellationToken);
            if (live is null || isApplyingPlatformMode ||
                requestRevision != Volatile.Read(ref platformRequestRevision)) return false;
            ConfirmPlatformSelection(live);
            ApplyLivePlatformSelection(live);
            return true;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            StatusMessage = $"整机档位读取失败 · {exception.Message}";
            return false;
        }
    }

    public async Task<bool> ApplyPlatformProfileAsync(string profileId, CancellationToken cancellationToken)
    {
        if (platformModeController is null) return false;
        var selection = profileId switch
        {
            "office" => new PlatformModeSelection(PlatformMode.Office),
            "balanced" => new PlatformModeSelection(PlatformMode.Balanced),
            "turbo" => new PlatformModeSelection(PlatformMode.TurboQuiet),
            "custom" => new PlatformModeSelection(PlatformMode.Custom, SelectedCustomProfileIndex),
            _ => null
        };
        if (selection is null) return false;
        return await ApplyPlatformSelectionAsync(selection, cancellationToken, basicTurbo: profileId == "turbo");
    }

    public Task<bool> ApplyTurboStrategyAsync(TurboStrategy strategy, CancellationToken cancellationToken) =>
        ApplyPlatformSelectionAsync(
            new(strategy is TurboStrategy.HighEfficiency ? PlatformMode.TurboHigh : PlatformMode.TurboQuiet),
            cancellationToken);

    public Task<bool> ApplyCustomProfileAsync(int profileIndex, CancellationToken cancellationToken)
    {
        if (profileIndex is < 1 or > 3) return Task.FromResult(false);
        return ApplyPlatformSelectionAsync(new(PlatformMode.Custom, profileIndex), cancellationToken);
    }

    public Task<bool> ApplyPlatformSelectionFromTrayAsync(
        PlatformModeSelection selection,
        CancellationToken cancellationToken) =>
        ApplyPlatformSelectionAsync(selection, cancellationToken);

    private async Task<bool> ApplyPlatformSelectionAsync(
        PlatformModeSelection selection,
        CancellationToken cancellationToken,
        bool basicTurbo = false)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (isShuttingDown || platformModeController is null) return false;
        if (!CanAttemptPlatformMode(selection.Mode))
        {
            StatusMessage = "当前控制接口尚未确认支持该狂暴子策略；未发送指令。";
            return false;
        }

        var previousPresentation = confirmedPlatformSelection ?? CurrentPlatformSelection();
        var requestRevision = Interlocked.Increment(ref platformRequestRevision);
        TraceControl("performance-request", $"request={requestRevision} target={selection.Mode}:{selection.ProfileIndex} confirmed={confirmedPlatformSelection?.Mode} chargePending={Experiments.Charge.RecoveryPending} dynamicPending={Experiments.HasRecoveryPending}");
        Interlocked.Increment(ref platformObservationRevision);
        isApplyingPlatformMode = true;
        OnPropertyChanged(nameof(IsPlatformSwitchPending));
        // A request may first reconcile an orphaned session. Keep the previous
        // selection and do not advertise a GCU switch before preparation succeeds.
        StatusMessage = Experiments.Charge.RecoveryPending
            ? "正在核对并恢复上次控制状态，尚未发送档位指令…"
            : "正在准备切换整机档位…";
        NotifyCustomPerformanceAvailability();
        RefreshPerformanceProperties();
        // An older request's post-switch wait holds the gate and its result would be discarded.
        Interlocked.Exchange(ref newerPlatformRequest, new()).Cancel();
        var gateHeld = false;
        try
        {
            await performanceTransitionGate.WaitAsync(cancellationToken);
            gateHeld = true;
            // A newer click supersedes requests that have not started writing yet.
            if (isShuttingDown || requestRevision != Volatile.Read(ref platformRequestRevision)) return false;
            TraceControl("performance-request", $"request={requestRevision} gate-acquired");
            var succeeded = await ApplyPlatformSelectionCoreAsync(selection, requestRevision, cancellationToken, basicTurbo);
            if (!succeeded && requestRevision == Volatile.Read(ref platformRequestRevision))
                await RestorePlatformPresentationAsync(requestRevision, previousPresentation);
            return succeeded;
        }
        catch (OperationCanceledException)
        {
            // Cancellation while queued must first let the in-flight switch finish;
            // otherwise an old confirmation could arrive after the fallback was shown.
            if (!gateHeld)
            {
                await performanceTransitionGate.WaitAsync(CancellationToken.None);
                gateHeld = true;
            }
            if (requestRevision == Volatile.Read(ref platformRequestRevision))
            {
                StatusMessage = "档位切换已取消，正在同步实际档位。";
                await RestorePlatformPresentationAsync(requestRevision, previousPresentation);
            }
            throw;
        }
        finally
        {
            TraceControl("performance-request", $"request={requestRevision} finished confirmed={confirmedPlatformSelection?.Mode}:{confirmedPlatformSelection?.ProfileIndex} latest={requestRevision == Volatile.Read(ref platformRequestRevision)} message={StatusMessage}");
            if (requestRevision == Volatile.Read(ref platformRequestRevision))
            {
                isApplyingPlatformMode = false;
                OnPropertyChanged(nameof(IsPlatformSwitchPending));
                NotifyCustomPerformanceAvailability();
                RefreshPerformanceProperties();
            }
            if (gateHeld) performanceTransitionGate.Release();
        }
    }

    private async Task<bool> ApplyPlatformSelectionCoreAsync(
        PlatformModeSelection selection,
        long requestRevision,
        CancellationToken cancellationToken,
        bool basicTurbo)
    {
        if (platformModeController is null) return false;
        bool IsLatestRequest() => requestRevision == Volatile.Read(ref platformRequestRevision);
        var previousConfirmed = confirmedPlatformSelection;
        PlatformModeSelection? retainedBasicTurbo = null;
        try
        {
            if (!await PauseDynamicForManualControlAsync(cancellationToken))
            {
                TraceControl("performance-request", $"request={requestRevision} prepare-blocked reason={StatusMessage} dynamic={Experiments.DynamicMessage}");
                return false;
            }
            TraceControl("performance-request", $"request={requestRevision} prepared");
            if (!IsLatestRequest() || isShuttingDown) return false;
            if (!CanAttemptPlatformMode(selection.Mode))
            {
                StatusMessage = "当前控制接口尚未确认支持该狂暴子策略；未发送指令。";
                return false;
            }
            cancellationToken.ThrowIfCancellationRequested();
            Volatile.Write(ref platformCommandRevision, requestRevision);
            OnPropertyChanged(nameof(IsPlatformSwitchPending));
            // Optimistic presentation is allowed only after preparation. Hardware
            // confirmation remains separate, and failures still read back/restore it.
            ApplyLivePlatformSelection(selection);
            StatusMessage = "正在切换整机档位并等待 GCU 回读…";
            // An explicit typed mode command can be verified without knowing the
            // starting mode or a capability list. Keep an unobserved target out
            // of the combined PL/Boost/frequency scheduler and its prerequisites.
            var modeOnly = scheduleController is null || !SupportsPlatformMode(selection.Mode) ||
                await platformModeController.ReadPlatformModeAsync(cancellationToken) is null;
            TraceControl("performance-request", $"request={requestRevision} route={(modeOnly ? "mode-only" : "schedule")}");
            ControlResult? basicResult = null;
            if (basicTurbo)
            {
                basicResult = await platformModeController.SetBasicTurboModeAsync(selection.ProfileIndex, cancellationToken);
                TraceControl("performance-request", $"request={requestRevision} basic-turbo={basicResult.Succeeded} reason={basicResult.Reason}");
                if (basicResult.ConfirmedPlatform is { } confirmed)
                {
                    ConfirmPlatformSelection(confirmed);
                    if (IsLatestRequest()) ApplyLivePlatformSelection(confirmed);
                }
                if (!basicResult.Succeeded)
                {
                    if (IsLatestRequest()) StatusMessage = basicResult.Reason;
                    return false;
                }
                // The base command can preserve either firmware Turbo strategy. Use
                // its actual mode for the saved Boost/frequency policy, and do not
                // turn a base-card click into a subsequent strategy write.
                selection = basicResult.ConfirmedPlatform ??
                    await platformModeController.ReadPlatformModeAsync(cancellationToken) ?? selection;
                if (selection.Mode is not (PlatformMode.TurboQuiet or PlatformMode.TurboHigh))
                {
                    if (IsLatestRequest()) StatusMessage = "基础狂暴档位未确认，未应用后续策略。";
                    return false;
                }
                ConfirmPlatformSelection(selection);
                retainedBasicTurbo = selection;
                if (IsLatestRequest()) ApplyLivePlatformSelection(selection);
                if (!IsLatestRequest() || isShuttingDown) return true;
            }
            if (modeOnly)
            {
                var modeResult = basicResult ?? await platformModeController.SetPlatformModeAsync(selection, cancellationToken);
                TraceControl("performance-request", $"request={requestRevision} mode-result={modeResult.Succeeded} reason={modeResult.Reason}");
                if (modeResult.ConfirmedPlatform is { } confirmed)
                {
                    ConfirmPlatformSelection(confirmed);
                    if (IsLatestRequest()) ApplyLivePlatformSelection(confirmed);
                }
                if (!modeResult.Succeeded)
                {
                    await ReconcileRetainedPlatformAsync(modeResult.ConfirmedPlatform, modeResult.Reason,
                        requestRevision, cancellationToken);
                    return false;
                }
                ConfirmPlatformSelection(selection);
                modeOnlyPlatformSelection = selection;
                if (IsLatestRequest()) StatusMessage = modeResult.Reason;
                // Runs even when superseded: the next request only compares against this
                // mode, so a cap left by the previous profile has to be removed here.
                if (!ShouldSyncFrequencyAfterModeOnly(previousConfirmed, selection)) return true;
                var modeOnlySettlement = await TrySettleFrequencyAsync(
                    CreateFrequencyPolicy(selection), requestRevision, cancellationToken);
                TraceControl("performance-request", $"request={requestRevision} mode-only-frequency={modeOnlySettlement.Succeeded} reason={modeOnlySettlement.Reason}");
                if (!IsLatestRequest()) return true;
                if (!modeOnlySettlement.Succeeded)
                    StatusMessage = $"{modeResult.Reason} · {modeOnlySettlement.Reason}";
                return modeOnlySettlement.Succeeded;
            }

            var schedule = CreateSchedule(selection);
            var result = await scheduleController!.ApplyAsync(
                schedule, platformAlreadySelected: basicTurbo, cancellationToken,
                retainSelectedPlatformOnFailure: basicTurbo);
            TraceControl("performance-request", $"request={requestRevision} schedule-result={result.Succeeded} reason={result.Reason}");
            if (!result.Succeeded)
            {
                await ReconcileRetainedPlatformAsync(result.ConfirmedPlatform ?? retainedBasicTurbo, result.Reason,
                    requestRevision, cancellationToken);
                return false;
            }
            modeOnlyPlatformSelection = null;
            ConfirmPlatformSelection(result.Readback?.Platform ?? selection);
            if (!IsLatestRequest()) return true;
            UpdatePerformanceReadback(result.Readback);
            StatusMessage = "档位已切换，正在复查 Windows 频率策略…";
            var frequencySettlement = await SettleFrequencyAfterPlatformSwitchAsync(
                schedule.Frequency, requestRevision, cancellationToken);
            TraceControl("performance-request", $"request={requestRevision} frequency-settlement={frequencySettlement.Succeeded} reason={frequencySettlement.Reason}");
            if (!IsLatestRequest()) return true;
            SavePerformanceProfile(selection);
            StatusMessage = frequencySettlement.Succeeded
                ? frequencySettlement.Reason
                : $"{result.Reason} · {frequencySettlement.Reason}";
            return frequencySettlement.Succeeded;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            TraceControl("performance-request", $"request={requestRevision} exception={exception.GetType().Name}: {exception.Message}");
            if (retainedBasicTurbo is not null)
            {
                // The basic mode was already confirmed, so an unrelated policy read
                // failure must not leave the previous profile's frequency cap behind.
                await ReconcileRetainedPlatformAsync(retainedBasicTurbo,
                    $"已保留当前整机档位，后续策略未完全应用：{exception.Message}",
                    requestRevision, cancellationToken);
                return false;
            }
            if (IsLatestRequest()) StatusMessage = $"整机档位切换异常 · {exception.Message}";
            return false;
        }
    }

    private async Task ReconcileRetainedPlatformAsync(PlatformModeSelection? actual, string reason,
        long requestRevision, CancellationToken cancellationToken)
    {
        if (actual is not null)
        {
            ConfirmPlatformSelection(actual);
            if (isShuttingDown) return;
            if (requestRevision == Volatile.Read(ref platformRequestRevision)) ApplyLivePlatformSelection(actual);
            // Keep the failed strategy's PL/Boost draft out of this path, while
            // removing a prior Office cap or restoring the actual mode's saved cap.
            var frequencyResult = await TrySettleFrequencyAsync(
                CreateFrequencyPolicy(actual), requestRevision, cancellationToken);
            if (!frequencyResult.Succeeded) reason += $" · {frequencyResult.Reason}";
        }
        if (requestRevision == Volatile.Read(ref platformRequestRevision)) StatusMessage = reason;
    }

    /// <summary>
    /// Writes the frequency now and rechecks it afterwards, like the schedule route, so a
    /// newer click that ends the recheck wait can never skip the write itself.
    /// </summary>
    private async Task<ControlResult> TrySettleFrequencyAsync(
        ProcessorFrequencyLimitPolicy expected,
        long requestRevision,
        CancellationToken cancellationToken)
    {
        try
        {
            if (frequencyLimitController is not null &&
                await frequencyLimitController.ReadAsync(cancellationToken) != expected)
            {
                var written = await frequencyLimitController.ApplyAsync(expected, cancellationToken);
                if (!written.Succeeded) return written;
            }
            return await SettleFrequencyAfterPlatformSwitchAsync(expected, requestRevision, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return ControlResult.Rejected($"频率策略同步失败：{exception.Message}");
        }
    }

    /// <summary>
    /// A mode-only switch still must not leave a cap this app applied for the previous
    /// profile. Without a saved setting on either side it touches nothing, so an unknown
    /// or externally set Windows value is never replaced by a default.
    /// </summary>
    private bool ShouldSyncFrequencyAfterModeOnly(PlatformModeSelection? previous, PlatformModeSelection target) =>
        frequencyLimitController is not null &&
        (frequencyConfiguredByProfile.GetValueOrDefault(PerformanceProfileKey(target)) ||
         previous is not null && previous != target &&
         CreateFrequencyPolicy(previous) != ProcessorFrequencyLimitPolicy.Uniform(0));

    private void ConfirmPlatformSelection(PlatformModeSelection selection)
    {
        if (confirmedPlatformSelection == selection) return;
        confirmedPlatformSelection = selection;
        OnPropertyChanged(nameof(ConfirmedPlatformSelection));
    }

    private async Task RestorePlatformPresentationAsync(long requestRevision, PlatformModeSelection fallback)
    {
        var actual = confirmedPlatformSelection ?? fallback;
        var readSucceeded = false;
        try
        {
            using var readTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            var live = await platformModeController!.ReadPlatformModeAsync(readTimeout.Token);
            if (live is not null)
            {
                actual = live;
                readSucceeded = true;
            }
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // A failed read must not keep the optimistic target displayed as confirmed.
        }
        if (requestRevision != Volatile.Read(ref platformRequestRevision)) return;
        if (readSucceeded) ConfirmPlatformSelection(actual);
        ApplyLivePlatformSelection(actual);
        if (!readSucceeded) StatusMessage = $"{StatusMessage} · 实际档位暂时无法回读，已恢复上次显示。";
    }

    public async Task<bool> ObservePlatformModeAsync(
        PlatformModeSelection selection,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (isApplyingPlatformMode || isApplyingPerformancePolicy)
        {
            return false;
        }

        if (modeOnlyPlatformSelection is { } modeOnly)
        {
            if (modeOnly == selection)
            {
                // Status echoes/reconnects must not turn a verified mode-only
                // request into an unsolicited application of old/default limits.
                ConfirmPlatformSelection(selection);
                ApplyLivePlatformSelection(selection);
                return true;
            }
            // A genuinely different externally selected mode follows the existing
            // per-profile frequency reconciliation path.
            modeOnlyPlatformSelection = null;
        }

        // Repeated notifications for the same mode share the existing settle window.
        if (pendingFrequencyObservation == selection) return true;

        var observationRevision = Interlocked.Increment(ref platformObservationRevision);
        ConfirmPlatformSelection(selection);
        ApplyLivePlatformSelection(selection);
        if (frequencyLimitController is null)
        {
            StatusMessage = "已同步显示当前 OEM 档位；未向硬件追加写入。";
            return true;
        }

        pendingFrequencyObservation = selection;
        pendingFrequencyObservationRevision = observationRevision;
        var gateHeld = false;

        try
        {
            StatusMessage = "已同步当前 OEM 档位，正在核对对应的 Windows 频率策略…";
            await verificationDelay(TimeSpan.FromSeconds(2), cancellationToken);
            await performanceTransitionGate.WaitAsync(cancellationToken);
            gateHeld = true;
            if (observationRevision != Volatile.Read(ref platformObservationRevision))
            {
                return true;
            }

            var check = await RecheckFrequencyAsync(
                CreateFrequencyPolicy(selection),
                () => observationRevision != Volatile.Read(ref platformObservationRevision),
                delay => verificationDelay(delay, cancellationToken),
                cancellationToken);
            switch (check.Outcome)
            {
                case FrequencyRecheckOutcome.Matched:
                    StatusMessage = "OEM 档位与对应的 Windows 频率策略已同步。";
                    return true;
                case FrequencyRecheckOutcome.Reapplied:
                    StatusMessage = "检测到上一档位遗留的频率限制，已按当前 OEM 档位恢复。";
                    return true;
                case FrequencyRecheckOutcome.Failed:
                    StatusMessage = $"已同步 OEM 档位，但频率策略恢复失败：{check.Detail}";
                    return false;
                default:
                    return true;
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            StatusMessage = $"已同步 OEM 档位，但核对频率策略时发生异常：{exception.Message}";
            return false;
        }
        finally
        {
            if (gateHeld) performanceTransitionGate.Release();
            if (pendingFrequencyObservationRevision == observationRevision)
                pendingFrequencyObservation = null;
        }
    }

    public async Task<bool> SynchronizeControlSessionAsync(CancellationToken cancellationToken)
    {
        if (platformModeController is null || isApplyingPlatformMode) return false;
        var requestRevision = Volatile.Read(ref platformRequestRevision);
        try
        {
            var live = await platformModeController.ReadPlatformModeAsync(cancellationToken);
            if (live is null || isApplyingPlatformMode ||
                requestRevision != Volatile.Read(ref platformRequestRevision)) return false;
            return await ObservePlatformModeAsync(live, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            StatusMessage = $"控制会话恢复同步失败 · {exception.Message}";
            return false;
        }
    }

    private void ApplyLivePlatformSelection(PlatformModeSelection selection)
    {
        if (selection.Mode is PlatformMode.Custom && selection.ProfileIndex is >= 1 and <= 3)
        {
            if (selectedCustomProfileIndex != selection.ProfileIndex)
                InvalidateGpuRangesForProfileChange();
            selectedCustomProfileIndex = selection.ProfileIndex;
            OnPropertyChanged(nameof(SelectedCustomProfileIndex));
        }

        if (selection.Mode is PlatformMode.TurboQuiet or PlatformMode.TurboHigh)
        {
            SelectedTurboStrategy = selection.Mode is PlatformMode.TurboHigh
                ? TurboStrategy.HighEfficiency
                : TurboStrategy.Quiet;
        }

        var basicTurbo = selection.IsBasicTurbo && selection.Mode is PlatformMode.TurboQuiet;
        if (isBasicTurboActive != basicTurbo)
        {
            isBasicTurboActive = basicTurbo;
            OnPropertyChanged(nameof(IsBasicTurboActive));
            OnPropertyChanged(nameof(CurrentFanProfileLabel));
        }

        ShowProfile(selection.Mode switch
        {
            PlatformMode.Office => "office",
            PlatformMode.Balanced => "balanced",
            PlatformMode.TurboQuiet or PlatformMode.TurboHigh => "turbo",
            PlatformMode.Custom => "custom",
            _ => selectedProfileId
        });
    }

    private PerformanceSchedule CreateSchedule(PlatformModeSelection selection)
    {
        var key = PerformanceProfileKey(selection);
        var draft = GetPowerDraft(key);
        var boost = boostDraftsByProfile.TryGetValue(key, out var savedBoost)
            ? savedBoost
            : ProcessorBoostMode.Aggressive;
        return new(
            selection,
            CanEditPowerLimitsFor(selection)
                ? new CpuPolicy(draft.Pl1Watts, draft.Pl2Watts, null)
                : null,
            new ProcessorBoostPolicy(boost, boost),
            CreateFrequencyPolicy(key));
    }

    private ProcessorFrequencyLimitPolicy CreateFrequencyPolicy(PlatformModeSelection selection) =>
        CreateFrequencyPolicy(PerformanceProfileKey(selection));

    private static string PerformanceProfileKey(PlatformModeSelection selection) => selection.Mode switch
    {
        PlatformMode.Office => "office:1",
        PlatformMode.Balanced => "balanced:1",
        PlatformMode.TurboQuiet => "turbo:Quiet",
        PlatformMode.TurboHigh => "turbo:HighEfficiency",
        PlatformMode.Custom => $"custom:{Math.Clamp(selection.ProfileIndex, 1, 3)}",
        _ => throw new ArgumentOutOfRangeException(nameof(selection))
    };

    private ProcessorFrequencyLimitPolicy CreateFrequencyPolicy(string key) =>
        ProcessorFrequencyLimitPolicy.Uniform((uint)(
            frequencyConfiguredByProfile.GetValueOrDefault(key)
                ? Math.Max(0, frequencyDraftsByProfile.GetValueOrDefault(key))
                : 0));

    private PlatformModeSelection CurrentPlatformSelection() => SelectedProfileId switch
    {
        "office" => new(PlatformMode.Office),
        "balanced" => new(PlatformMode.Balanced),
        "turbo" => new(SelectedTurboStrategy is TurboStrategy.HighEfficiency
            ? PlatformMode.TurboHigh
            : PlatformMode.TurboQuiet),
        "custom" => new(PlatformMode.Custom, SelectedCustomProfileIndex),
        _ => throw new InvalidOperationException($"未知性能档位：{SelectedProfileId}")
    };

    /// <summary>Ends early when a newer click arrives. Only waits end early, never hardware writes.</summary>
    private async Task WaitUnlessSupersededAsync(
        TimeSpan delay,
        long requestRevision,
        CancellationToken cancellationToken)
    {
        // Read the signal first: a request numbered after this check cancels the signal read here.
        var newerRequest = Volatile.Read(ref newerPlatformRequest);
        if (requestRevision != Volatile.Read(ref platformRequestRevision)) return;
        using var wait = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, newerRequest.Token);
        try
        {
            await verificationDelay(delay, wait.Token);
        }
        catch (OperationCanceledException) when (
            newerRequest.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
        }
    }

    private async Task<ControlResult> SettleFrequencyAfterPlatformSwitchAsync(
        ProcessorFrequencyLimitPolicy expected,
        long requestRevision,
        CancellationToken cancellationToken)
    {
        if (frequencyLimitController is null)
        {
            return new(true, "整机档位已应用并回读。");
        }

        await WaitUnlessSupersededAsync(TimeSpan.FromSeconds(2), requestRevision, cancellationToken);
        var check = await RecheckFrequencyAsync(
            expected,
            () => requestRevision != Volatile.Read(ref platformRequestRevision),
            delay => WaitUnlessSupersededAsync(delay, requestRevision, cancellationToken),
            cancellationToken);
        return check.Outcome switch
        {
            FrequencyRecheckOutcome.Superseded => new(true, "已有更新的档位请求，跳过旧档位复查。"),
            FrequencyRecheckOutcome.Matched => new(true, "整机档位与 Windows 频率策略已应用，并在延迟复查后保持一致。"),
            FrequencyRecheckOutcome.Reapplied => new(true, "检测到 OEM 延迟覆盖，已重新应用 Windows 频率策略并回读确认。"),
            _ => ControlResult.Rejected($"检测到 OEM 延迟覆盖，但恢复失败：{check.Detail}")
        };
    }

    private enum FrequencyRecheckOutcome { Superseded, Matched, Reapplied, Failed }

    /// <summary>
    /// The one recheck after any mode change: compare, rewrite if something overwrote the
    /// value, wait, then confirm. A rewrite only counts once a readback confirms it.
    /// </summary>
    private async Task<(FrequencyRecheckOutcome Outcome, string Detail)> RecheckFrequencyAsync(
        ProcessorFrequencyLimitPolicy expected,
        Func<bool> isSuperseded,
        Func<TimeSpan, Task> wait,
        CancellationToken cancellationToken)
    {
        if (isSuperseded()) return (FrequencyRecheckOutcome.Superseded, "");
        var settled = await frequencyLimitController!.ReadAsync(cancellationToken);
        if (isSuperseded()) return (FrequencyRecheckOutcome.Superseded, "");
        ProcessorFrequencyLimitStatusText = FormatFrequencyReadback(settled);
        if (settled == expected) return (FrequencyRecheckOutcome.Matched, "");

        var reapplied = await frequencyLimitController.ApplyAsync(expected, cancellationToken);
        if (!reapplied.Succeeded) return (FrequencyRecheckOutcome.Failed, reapplied.Reason);

        await wait(TimeSpan.FromSeconds(1));
        if (isSuperseded()) return (FrequencyRecheckOutcome.Superseded, "");
        var confirmed = await frequencyLimitController.ReadAsync(cancellationToken);
        if (isSuperseded()) return (FrequencyRecheckOutcome.Superseded, "");
        ProcessorFrequencyLimitStatusText = FormatFrequencyReadback(confirmed);
        return confirmed == expected
            ? (FrequencyRecheckOutcome.Reapplied, "")
            : (FrequencyRecheckOutcome.Failed,
                $"频率仍被覆盖：期望 {FormatFrequencyReadback(expected)}，实际 {FormatFrequencyReadback(confirmed)}");
    }
}
