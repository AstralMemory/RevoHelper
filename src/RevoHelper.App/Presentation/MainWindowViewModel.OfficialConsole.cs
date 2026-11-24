using RevoHelper.App.Lifecycle;

namespace RevoHelper.App.Presentation;

/// <summary>
/// The settings rows that exit the official control center's window and tray: once with the status
/// pill, or at every start while long-term exit is on. Its background service stays, since modes,
/// fans and lighting go through it.
/// </summary>
public sealed partial class MainWindowViewModel
{
    private readonly OfficialFrontendExit? officialFrontendExit;
    private CancellationTokenSource? officialConsoleWatchCancellation;
    private CancellationTokenSource? officialConsoleCloseCancellation;
    private bool closeStartedByLongTerm;
    private bool? officialFrontendRunning = false;

    /// <summary>Long-term exit: the official window and tray are closed at every RevoHelper start.</summary>
    public bool ExitOfficialConsole
    {
        get => applicationSettings.ExitOfficialConsole;
        set
        {
            if (value == applicationSettings.ExitOfficialConsole) return;
            if (!TrySaveSettings(applicationSettings with { ExitOfficialConsole = value })) return;
            OnPropertyChanged();
            if (value)
            {
                _ = ExitOfficialConsoleAsync(longTerm: true);
                return;
            }
            StopOfficialConsoleWatch();
            if (closeStartedByLongTerm) officialConsoleCloseCancellation?.Cancel();
            StatusMessage = "长期退出已关闭";
        }
    }

    /// <summary>
    /// Whether the official window or tray runs: read when settings open, when the window comes back
    /// to the front with settings open, and after each exit. Never on a timer.
    /// </summary>
    public bool IsOfficialFrontendRunning => officialFrontendRunning == true;

    private void SetOfficialFrontendRunning(bool? running)
    {
        if (officialFrontendRunning == running) return;
        officialFrontendRunning = running;
        OnPropertyChanged(nameof(IsOfficialFrontendRunning));
        OnOfficialConsoleExitButtonChanged();
    }

    /// <summary>The pill: red "正在运行 · 退出" exits once; grey "已退出" when nothing is left to exit.</summary>
    public string OfficialConsoleExitButtonText =>
        officialConsoleCloseCancellation is not null ? "正在退出…" : officialFrontendRunning switch
        {
            true => "正在运行 · 退出", false => "已退出", null => "状态未知 · 重查"
        };

    public bool CanExitOfficialConsoleNow =>
        officialFrontendExit is not null && officialFrontendRunning != false && officialConsoleCloseCancellation is null;

    /// <summary>The pill's click: closes the official window and tray this once.</summary>
    public Task ExitOfficialConsoleNowAsync()
    {
        if (officialFrontendRunning is null && !isShuttingDown)
        {
            RefreshOfficialConsoleStatus();
            StatusMessage = officialFrontendRunning.HasValue ? "已刷新官方控制台状态" : "未能确认官方控制台状态";
            return Task.CompletedTask;
        }
        return ExitOfficialConsoleAsync(longTerm: false);
    }

    /// <summary>
    /// For the window coming back to the front: someone who opened the official console meanwhile
    /// finds the pill red again. Only while settings show it, so other drawers cost nothing.
    /// </summary>
    public void RefreshOfficialConsoleStatusIfSettingsOpen()
    {
        if (ActiveDrawer == HomeDrawer.Settings) RefreshOfficialConsoleStatus();
    }

    /// <summary>
    /// For a start with Windows while long-term exit is on: the official service starts its tray after
    /// sign-in, later than RevoHelper, so it is looked for every 10 seconds for 3 minutes. A manual
    /// start only closes what runs then, before the conflict check.
    /// </summary>
    public void StartOfficialConsoleWatch(OfficialFrontendStartupWatch? startup = null)
    {
        if (isShuttingDown || officialFrontendExit is null || !ExitOfficialConsole) return;
        StopOfficialConsoleWatch();
        var cancellation = new CancellationTokenSource();
        officialConsoleWatchCancellation = cancellation;
        _ = WatchOfficialConsoleAsync(officialFrontendExit, cancellation, startup);
    }

    private async Task ExitOfficialConsoleAsync(bool longTerm)
    {
        if (isShuttingDown || officialFrontendExit is not { } exit ||
            officialConsoleCloseCancellation is { IsCancellationRequested: false }) return;
        using var cancellation = new CancellationTokenSource();
        officialConsoleCloseCancellation = cancellation;
        closeStartedByLongTerm = longTerm;
        OnOfficialConsoleExitButtonChanged();
        try
        {
            var token = cancellation.Token;
            var result = await Task.Run(() => exit.CloseNow(token), token);
            if (token.IsCancellationRequested) return;
            RefreshOfficialConsoleStatus();
            StatusMessage = result.HasUnknown || officialFrontendRunning is null ? "未能确认官方控制台状态"
                : IsOfficialFrontendRunning && result.Failed == 0 ? "官方控制台仍在运行"
                : OfficialConsoleExitMessage(result, longTerm);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (Exception)
        {
            if (!cancellation.IsCancellationRequested)
                StatusMessage = OfficialConsoleExitMessage(new(0, 1, false), longTerm);
        }
        finally
        {
            if (ReferenceEquals(officialConsoleCloseCancellation, cancellation))
            {
                officialConsoleCloseCancellation = null;
                closeStartedByLongTerm = false;
                // A termination already sent to Windows can finish just as the user switches off.
                if (cancellation.IsCancellationRequested && !isShuttingDown)
                    RefreshOfficialConsoleStatusIfSettingsOpen();
                OnOfficialConsoleExitButtonChanged();
            }
        }
    }

    private async Task WatchOfficialConsoleAsync(OfficialFrontendExit exit, CancellationTokenSource cancellation,
        OfficialFrontendStartupWatch? startup)
    {
        try
        {
            await exit.WatchAsync(result =>
            {
                // Settings read the status when they open; only a close or a failure changes it meanwhile.
                if (cancellation.IsCancellationRequested || result == OfficialFrontendExitResult.None) return;
                RefreshOfficialConsoleStatus();
            }, cancellation.Token, startup);
        }
        catch (OperationCanceledException) { }
        finally
        {
            if (ReferenceEquals(officialConsoleWatchCancellation, cancellation)) officialConsoleWatchCancellation = null;
            cancellation.Dispose();
        }
    }

    private void StopOfficialConsoleWatch()
    {
        officialConsoleWatchCancellation?.Cancel();
        officialConsoleWatchCancellation = null;
    }

    private void OnOfficialConsoleExitButtonChanged()
    {
        OnPropertyChanged(nameof(OfficialConsoleExitButtonText));
        OnPropertyChanged(nameof(CanExitOfficialConsoleNow));
    }

    // Short enough for the status line at the bottom of the window.
    private static string OfficialConsoleExitMessage(OfficialFrontendExitResult result, bool longTerm) =>
        (longTerm, result.Failed > 0, result.Closed > 0) switch
        {
            (false, true, _) => "没能退出官方控制台，可以再试一次",
            (false, false, true) => "已退出官方控制台，后台服务保留",
            (false, false, false) => "官方控制台没有在运行",
            (true, true, _) => "长期退出已打开，但这次没能退出",
            (true, false, true) => "长期退出已打开，官方控制台已退出",
            (true, false, false) => "长期退出已打开",
        };
}
