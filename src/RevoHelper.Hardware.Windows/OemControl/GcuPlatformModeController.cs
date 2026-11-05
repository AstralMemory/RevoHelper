using System.Diagnostics;
using RevoHelper.Core.Control;

namespace RevoHelper.Hardware.Windows.OemControl;

public sealed class GcuPlatformModeController(
    IGcuPlatformModeChannel channel,
    Action<string, string>? diagnostic = null,
    TimeSpan? lateConvergenceWindow = null) : IPlatformModeController
{
    private static readonly TimeSpan LatePollInterval = TimeSpan.FromMilliseconds(500);
    private readonly TimeSpan lateWindow = lateConvergenceWindow ?? TimeSpan.FromSeconds(4);
    private readonly SemaphoreSlim operationLock = new(1, 1);

    private void Trace(string message)
    {
        try { diagnostic?.Invoke("platform-gcu", message); }
        catch (Exception) { /* Diagnostics must not change control outcomes. */ }
    }

    public async ValueTask<PlatformModeSelection?> ReadPlatformModeAsync(CancellationToken cancellationToken) =>
        GcuPlatformModeStatusMapper.Map(await channel.ReadStatusAsync(cancellationToken).ConfigureAwait(false));

    public ValueTask<ControlResult> SetPlatformModeAsync(
        PlatformModeSelection selection,
        CancellationToken cancellationToken) => SetPlatformModeCoreAsync(selection, true, cancellationToken);

    public ValueTask<ControlResult> SetBasicTurboModeAsync(int profileIndex, CancellationToken cancellationToken) =>
        SetPlatformModeCoreAsync(new(PlatformMode.TurboQuiet, profileIndex), false, cancellationToken);

    public async ValueTask<PlatformRestorePoint?> CaptureRestorePointAsync(CancellationToken cancellationToken)
    {
        var status = await channel.ReadStatusAsync(cancellationToken).ConfigureAwait(false);
        return GcuPlatformModeStatusMapper.Map(status) is { } selection
            ? new(selection, HasActiveTurboStrategy(status))
            : null;
    }

    public ValueTask<ControlResult> RestorePlatformModeAsync(
        PlatformRestorePoint point,
        CancellationToken cancellationToken) =>
        SetPlatformModeCoreAsync(point.Selection, point.RestoreStrategy, cancellationToken);

    private async ValueTask<ControlResult> SetPlatformModeCoreAsync(
        PlatformModeSelection selection,
        bool applyTurboStrategy,
        CancellationToken cancellationToken)
    {
        Trace($"stage=request target={selection.Mode}/{selection.ProfileIndex}");
        if (selection.Mode == PlatformMode.Custom && selection.ProfileIndex is < 1 or > 5)
        {
            Trace("stage=request result=rejected reason=自定义方案编号超出1-5");
            return ControlResult.Rejected("自定义方案编号必须位于 1–5。");
        }

        await operationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        PlatformModeSelection? baseline = null;
        var hasTurboStrategies = false;
        var restoreBaselineStrategy = false;
        try
        {
            Trace("stage=baseline-read result=begin");
            var status = await channel.ReadStatusAsync(cancellationToken).ConfigureAwait(false);
            baseline = GcuPlatformModeStatusMapper.Map(status);
            Trace($"stage=baseline-read result=success observed=({Describe(status)})");
            // A missing current-mode report is not a denial of a known mode
            // command. Keep it only as an optional, genuinely observed restore point.
            hasTurboStrategies = status.SupportsTurboStrategies;
            restoreBaselineStrategy = HasActiveTurboStrategy(status);
            if (selection.Mode == PlatformMode.TurboHigh && !hasTurboStrategies)
            {
                Trace("stage=request result=rejected reason=狂暴子策略未确认");
                return ControlResult.Rejected("当前 GCU 尚未确认支持狂暴模式的高能/静音子策略。");
            }

            var confirmed = await ApplyAndVerifyAsync(selection, applyTurboStrategy && hasTurboStrategies, cancellationToken)
                .ConfigureAwait(false);
            Trace($"stage=complete result=success target={selection.Mode}/{selection.ProfileIndex}");
            return new ControlResult(true, $"整机档位已应用并回读：{Label(selection.Mode, applyTurboStrategy && hasTurboStrategies)}")
            {
                ConfirmedPlatform = GcuPlatformModeStatusMapper.Map(confirmed)
            };
        }
        catch (TurboStrategyException exception)
        {
            Trace($"stage=strategy result=unconfirmed basicModeRetained={exception.ConfirmedPlatform.Mode}/{exception.ConfirmedPlatform.ProfileIndex} reason={exception.Message}");
            return new ControlResult(false, $"狂暴模式已保留，所选子策略未确认：{exception.Message}")
            {
                ConfirmedPlatform = exception.ConfirmedPlatform
            };
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            Trace($"stage=target result=exception type={exception.GetType().Name} reason={exception.Message}");
            if (baseline is null) return ControlResult.Rejected($"整机档位切换失败：{exception.Message}");
            try
            {
                Trace($"stage=rollback result=begin target={baseline.Mode}/{baseline.ProfileIndex}");
                // Basic Turbo (no sub-strategy on) maps to TurboQuiet; rolling back to it
                // must return to the mode without switching the quiet strategy on.
                await ApplyAndVerifyAsync(baseline, restoreBaselineStrategy, cancellationToken).ConfigureAwait(false);
                Trace($"stage=rollback result=success target={baseline.Mode}/{baseline.ProfileIndex}");
                return ControlResult.Rejected($"整机档位切换失败，已恢复原档位：{exception.Message}");
            }
            catch (Exception rollbackException) when (rollbackException is not OperationCanceledException)
            {
                Trace($"stage=rollback result=exception type={rollbackException.GetType().Name} reason={rollbackException.Message}");
                return ControlResult.Rejected(
                    $"整机档位切换失败，恢复原档位也失败：{exception.Message}；{rollbackException.Message}");
            }
        }
        finally
        {
            operationLock.Release();
        }
    }

    private async Task<GcuCpuPolicyStatus> ApplyAndVerifyAsync(
        PlatformModeSelection selection,
        bool hasTurboStrategies,
        CancellationToken cancellationToken)
    {
        var stage = "mode-write";
        try
        {
            Trace($"stage={stage} result=begin target={selection.Mode}/{selection.ProfileIndex}");
            await channel.WriteModeAsync(selection, cancellationToken).ConfigureAwait(false);
            Trace($"stage={stage} result=sent target={selection.Mode}/{selection.ProfileIndex}");
            stage = "mode-verify";
            GcuCpuPolicyStatus confirmed;
            try
            {
                confirmed = await WaitForAsync(status => ModeMatches(status, selection), "OperatingMode 未收敛", stage, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (ConvergenceTimeoutException)
            {
                stage = "mode-verify-late";
                confirmed = await WaitForLateModeAsync(selection, cancellationToken).ConfigureAwait(false)
                    ?? throw new InvalidOperationException("OperatingMode 未收敛");
            }
            stage = "companion-write";
            Trace($"stage={stage} result=begin target={selection.Mode}");
            await channel.WriteCompanionModeAsync(selection.Mode, cancellationToken).ConfigureAwait(false);
            Trace($"stage={stage} result=sent target={selection.Mode}");

            if (hasTurboStrategies && (selection.Mode is PlatformMode.TurboQuiet or PlatformMode.TurboHigh))
            {
                stage = "strategy-write";
                Trace($"stage={stage} result=begin target={selection.Mode}");
                await channel.WriteTurboStrategyAsync(selection.Mode, cancellationToken).ConfigureAwait(false);
                Trace($"stage={stage} result=sent target={selection.Mode}");
                stage = "strategy-verify";
                confirmed = await WaitForAsync(status => ModeMatches(status, selection) && TurboStrategyMatches(status, selection.Mode),
                    "狂暴子策略未收敛", stage, cancellationToken).ConfigureAwait(false);
            }
            else if (selection.Mode is PlatformMode.TurboQuiet or PlatformMode.TurboHigh)
            {
                // The main Turbo card selects only the basic mode. Observe which
                // sub-strategy firmware retained instead of silently requesting one.
                stage = "basic-mode-verify";
                confirmed = await WaitForAsync(status => ModeMatches(status, selection),
                    "OperatingMode 未收敛", stage, cancellationToken).ConfigureAwait(false);
            }
            return confirmed;
        }
        catch (Exception exception)
        {
            Trace($"stage={stage} result=exception type={exception.GetType().Name} reason={exception.Message}");
            if (exception is not OperationCanceledException && stage is "strategy-write" or "strategy-verify")
            {
                var confirmed = await TryReadConfirmedTurboAsync(selection, cancellationToken).ConfigureAwait(false);
                if (confirmed is not null) throw new TurboStrategyException(exception.Message, confirmed);
            }
            throw;
        }
    }

    private async Task<PlatformModeSelection?> TryReadConfirmedTurboAsync(
        PlatformModeSelection selection, CancellationToken cancellationToken)
    {
        try
        {
            var status = await channel.ReadStatusAsync(cancellationToken).ConfigureAwait(false);
            return ModeMatches(status, selection) ? GcuPlatformModeStatusMapper.Map(status) : null;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            Trace($"stage=strategy-retained-read result=exception reason={exception.Message}");
            return null;
        }
    }

    private sealed class TurboStrategyException(string message, PlatformModeSelection confirmedPlatform)
        : InvalidOperationException(message)
    {
        public PlatformModeSelection ConfirmedPlatform { get; } = confirmedPlatform;
    }

    private async Task<GcuCpuPolicyStatus> WaitForAsync(
        Func<GcuCpuPolicyStatus, bool> predicate,
        string failure,
        string stage,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 8; attempt++)
        {
            var status = await channel.ReadStatusAsync(cancellationToken).ConfigureAwait(false);
            if (predicate(status))
            {
                Trace($"stage={stage} result=success attempts={attempt + 1} observed=({Describe(status)})");
                return status;
            }
            if (attempt == 7)
                Trace($"stage={stage} result=unconfirmed attempts=8 observed=({Describe(status)})");
            if (attempt < 7) await Task.Delay(TimeSpan.FromMilliseconds(250), cancellationToken).ConfigureAwait(false);
        }
        throw new ConvergenceTimeoutException(failure);
    }

    /// <summary>
    /// A rollback sent right at the timeout would cancel a slow but valid transition.
    /// Keep watching first, and record when (or whether) the target actually arrived.
    /// </summary>
    private async Task<GcuCpuPolicyStatus?> WaitForLateModeAsync(
        PlatformModeSelection selection,
        CancellationToken cancellationToken)
    {
        var watch = Stopwatch.StartNew();
        GcuCpuPolicyStatus? last = null;
        while (watch.Elapsed < lateWindow)
        {
            await Task.Delay(LatePollInterval, cancellationToken).ConfigureAwait(false);
            last = await channel.ReadStatusAsync(cancellationToken).ConfigureAwait(false);
            if (ModeMatches(last, selection))
            {
                Trace($"stage=mode-verify-late result=success waitedMs={watch.ElapsedMilliseconds} observed=({Describe(last)})");
                return last;
            }
        }
        Trace($"stage=mode-verify-late result=unconfirmed waitedMs={watch.ElapsedMilliseconds} observed=({(last is null ? "none" : Describe(last))})");
        return null;
    }

    private sealed class ConvergenceTimeoutException(string message) : InvalidOperationException(message);

    private static bool HasActiveTurboStrategy(GcuCpuPolicyStatus status) =>
        status.OperatingMode == 2 && status.SupportsTurboStrategies && (status.Silent == 1 || status.Extreme == 1);

    private static bool ModeMatches(GcuCpuPolicyStatus status, PlatformModeSelection selection) =>
        status.OperatingMode == ModeNumber(selection.Mode) && selection.Mode switch
        {
            PlatformMode.Office => StockProfileMatches(status.OfficeProfileIndex, selection.ProfileIndex),
            PlatformMode.Balanced => StockProfileMatches(status.GamingProfileIndex, selection.ProfileIndex),
            PlatformMode.TurboQuiet or PlatformMode.TurboHigh => StockProfileMatches(status.TurboProfileIndex, selection.ProfileIndex),
            PlatformMode.Custom => status.CustomProfileIndex == selection.ProfileIndex - 1,
            _ => false
        };

    private static string Describe(GcuCpuPolicyStatus status) =>
        $"OperatingMode={Number(status.OperatingMode)},OfficeProfile={Number(status.OfficeProfileIndex)},GamingProfile={Number(status.GamingProfileIndex)},TurboProfile={Number(status.TurboProfileIndex)},CustomProfile={Number(status.CustomProfileIndex)},Silent={Number(status.Silent)},Extreme={Number(status.Extreme)}";

    private static string Number(int? value) => value?.ToString() ?? "null";

    // Firmware may omit the index for its sole/default stock profile. Explicit
    // non-default requests and explicit mismatches still require an exact match.
    private static bool StockProfileMatches(int? reported, int requested) =>
        reported == requested || (reported is null && requested == 0);

    private static bool TurboStrategyMatches(GcuCpuPolicyStatus status, PlatformMode mode) => mode switch
    {
        PlatformMode.TurboQuiet => status.Silent == 1 && status.Extreme != 1,
        PlatformMode.TurboHigh => status.Extreme == 1 && status.Silent != 1,
        _ => false
    };

    private static int ModeNumber(PlatformMode mode) => mode switch
    {
        PlatformMode.Office => 0,
        PlatformMode.Balanced => 1,
        PlatformMode.TurboQuiet or PlatformMode.TurboHigh => 2,
        PlatformMode.Custom => 3,
        _ => throw new ArgumentOutOfRangeException(nameof(mode))
    };

    private static string Label(PlatformMode mode, bool hasTurboStrategies) => mode switch
    {
        PlatformMode.Office => "办公模式",
        PlatformMode.Balanced => "平衡模式",
        PlatformMode.TurboQuiet => hasTurboStrategies ? "狂暴模式 · 静音" : "狂暴模式",
        PlatformMode.TurboHigh => "狂暴模式 · 高能",
        PlatformMode.Custom => "自定义模式",
        _ => mode.ToString()
    };
}
