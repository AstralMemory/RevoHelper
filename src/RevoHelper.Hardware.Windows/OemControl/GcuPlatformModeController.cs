using RevoHelper.Core.Control;

namespace RevoHelper.Hardware.Windows.OemControl;

public sealed class GcuPlatformModeController(IGcuPlatformModeChannel channel) : IPlatformModeController
{
    private readonly SemaphoreSlim operationLock = new(1, 1);

    public async ValueTask<PlatformModeSelection?> ReadPlatformModeAsync(CancellationToken cancellationToken) =>
        GcuPlatformModeStatusMapper.Map(await channel.ReadStatusAsync(cancellationToken).ConfigureAwait(false));

    public async ValueTask<ControlResult> SetPlatformModeAsync(
        PlatformModeSelection selection,
        CancellationToken cancellationToken)
    {
        if (selection.Mode == PlatformMode.Custom && selection.ProfileIndex is < 1 or > 5)
            return ControlResult.Rejected("自定义方案编号必须位于 1–5。");

        await operationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        PlatformModeSelection? baseline = null;
        try
        {
            baseline = GcuPlatformModeStatusMapper.Map(await channel.ReadStatusAsync(cancellationToken).ConfigureAwait(false));
            if (baseline is null) return ControlResult.Rejected("GCU 未返回可识别的当前整机档位。");

            await ApplyAndVerifyAsync(selection, cancellationToken).ConfigureAwait(false);
            return new ControlResult(true, $"整机档位已应用并回读：{Label(selection.Mode)}");
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            if (baseline is null) return ControlResult.Rejected($"整机档位切换失败：{exception.Message}");
            try
            {
                await ApplyAndVerifyAsync(baseline, cancellationToken).ConfigureAwait(false);
                return ControlResult.Rejected($"整机档位切换失败，已恢复原档位：{exception.Message}");
            }
            catch (Exception rollbackException) when (rollbackException is not OperationCanceledException)
            {
                return ControlResult.Rejected(
                    $"整机档位切换失败，恢复原档位也失败：{exception.Message}；{rollbackException.Message}");
            }
        }
        finally
        {
            operationLock.Release();
        }
    }

    private async Task ApplyAndVerifyAsync(
        PlatformModeSelection selection,
        CancellationToken cancellationToken)
    {
        await channel.WriteModeAsync(selection, cancellationToken).ConfigureAwait(false);
        await WaitForAsync(status => ModeMatches(status, selection), "OperatingMode 未收敛", cancellationToken)
            .ConfigureAwait(false);
        await channel.WriteCompanionModeAsync(selection.Mode, cancellationToken).ConfigureAwait(false);

        if (selection.Mode is PlatformMode.TurboQuiet or PlatformMode.TurboHigh)
        {
            await channel.WriteTurboStrategyAsync(selection.Mode, cancellationToken).ConfigureAwait(false);
            await WaitForAsync(status => TurboStrategyMatches(status, selection.Mode),
                "狂暴子策略未收敛", cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task WaitForAsync(
        Func<GcuCpuPolicyStatus, bool> predicate,
        string failure,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 8; attempt++)
        {
            var status = await channel.ReadStatusAsync(cancellationToken).ConfigureAwait(false);
            if (predicate(status)) return;
            if (attempt < 7) await Task.Delay(TimeSpan.FromMilliseconds(250), cancellationToken).ConfigureAwait(false);
        }
        throw new InvalidOperationException(failure);
    }

    private static bool ModeMatches(GcuCpuPolicyStatus status, PlatformModeSelection selection) =>
        status.OperatingMode == ModeNumber(selection.Mode) && selection.Mode switch
        {
            PlatformMode.Office => status.OfficeProfileIndex == selection.ProfileIndex,
            PlatformMode.Balanced => status.GamingProfileIndex == selection.ProfileIndex,
            PlatformMode.TurboQuiet or PlatformMode.TurboHigh => status.TurboProfileIndex == selection.ProfileIndex,
            PlatformMode.Custom => status.CustomProfileIndex == selection.ProfileIndex - 1,
            _ => false
        };

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

    private static string Label(PlatformMode mode) => mode switch
    {
        PlatformMode.Office => "办公模式",
        PlatformMode.Balanced => "平衡模式",
        PlatformMode.TurboQuiet => "狂暴模式 · 静音",
        PlatformMode.TurboHigh => "狂暴模式 · 高能",
        PlatformMode.Custom => "自定义模式",
        _ => mode.ToString()
    };
}
