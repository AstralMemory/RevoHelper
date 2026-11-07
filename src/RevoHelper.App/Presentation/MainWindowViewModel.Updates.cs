using System.IO;
using System.Reflection;
using RevoHelper.App.Updates;

namespace RevoHelper.App.Presentation;

/// <summary>
/// New-version notice. It only informs: no popup, no download, nothing forced. A failed check
/// stays quiet apart from the status on the About page.
/// </summary>
public sealed partial class MainWindowViewModel
{
    private static readonly TimeSpan UpdateCheckStartupDelay = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan UpdateCheckInterval = TimeSpan.FromHours(24);
    // Many users in China reach GitHub only some of the time, often not right after boot,
    // so a failed check is retried a few hours later instead of waiting a whole day.
    private static readonly TimeSpan UpdateCheckRetryInterval = TimeSpan.FromHours(3);
    // The app can sit in the tray for days. This only compares timestamps; the network is
    // used once a check is due.
    private static readonly TimeSpan UpdateCheckPollInterval = TimeSpan.FromHours(1);
    // Offline fallback: when GitHub has been unreachable this long, say once per version that
    // there may be a new release.
    private static readonly TimeSpan PossibleUpdateAfter = TimeSpan.FromDays(15);

    public static ReleaseVersion? RunningReleaseVersion { get; } = ReleaseVersion.TryParse(
        typeof(MainWindowViewModel).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion);

    private bool isCheckingForUpdates;
    private bool lastUpdateCheckFailed;
    private bool checkedForUpdatesManually;

    public bool CheckForUpdatesAutomatically
    {
        get => applicationSettings.CheckForUpdatesAutomatically;
        set
        {
            if (value == applicationSettings.CheckForUpdatesAutomatically) return;
            if (!TrySaveSettings(applicationSettings with { CheckForUpdatesAutomatically = value })) return;
            OnPropertyChanged();
            RefreshUpdatePresentation();
        }
    }

    private ReleaseVersion? LatestKnownRelease => ReleaseVersion.TryParse(applicationSettings.LatestReleaseTag);

    private ReleaseVersion? NewerRelease =>
        RunningReleaseVersion is { } running && LatestKnownRelease is { } latest && latest.IsNewerThan(running) ? latest : null;

    /// <summary>With automatic checks off, only a check the user asked for shows the notice.</summary>
    public bool ShowUpdateBadge => NewerRelease is not null && (CheckForUpdatesAutomatically || checkedForUpdatesManually);

    public string UpdateBadgeText => NewerRelease is { } release ? string.Format(UiCopy.UpdateAvailableFormat, release) : string.Empty;

    public bool ShowPossibleUpdateBadge
    {
        get
        {
            if (!CheckForUpdatesAutomatically || NewerRelease is not null || RunningReleaseVersion is not { } running) return false;
            var version = running.ToString();
            if (applicationSettings.PossibleUpdateNoticeReadVersion == version) return false;
            if (applicationSettings.VersionFirstSeen != version || applicationSettings.VersionFirstSeenUtc is not { } firstSeen) return false;
            var now = utcNow();
            if (now - firstSeen < PossibleUpdateAfter) return false;
            return applicationSettings.LastSuccessfulUpdateCheckUtc is not { } success || now - success >= PossibleUpdateAfter;
        }
    }

    public bool IsCheckingForUpdates => isCheckingForUpdates;

    public bool CanCheckForUpdates => latestReleaseSource is not null && !isCheckingForUpdates;

    public string UpdateCheckStatusText =>
        isCheckingForUpdates ? UiCopy.UpdateChecking
        : NewerRelease is { } release ? string.Format(UiCopy.UpdateAvailableFormat, release)
        : lastUpdateCheckFailed ? UiCopy.UpdateCheckFailed
        : LatestKnownRelease is not null ? UiCopy.UpdateUpToDate
        : UiCopy.UpdateNotChecked;

    public string ManualUpdateCheckTooltip => UpdateCheckStatusText == UiCopy.UpdateNotChecked
        ? UiCopy.ManualUpdateCheckTooltip
        : $"{UiCopy.ManualUpdateCheckTooltip} · {UpdateCheckStatusText}";

    private bool IsUpdateCheckDue
    {
        get
        {
            if (!CheckForUpdatesAutomatically) return false;
            var now = utcNow();
            // A time in the future means the clock moved back, so it no longer counts.
            static bool Within(DateTimeOffset? time, DateTimeOffset now, TimeSpan span) =>
                time is { } at && now >= at && now - at < span;
            return !Within(applicationSettings.LastSuccessfulUpdateCheckUtc, now, UpdateCheckInterval) &&
                !Within(applicationSettings.LastUpdateCheckUtc, now, UpdateCheckRetryInterval);
        }
    }

    public async Task RunAutomaticUpdateChecksAsync(CancellationToken cancellationToken)
    {
        if (latestReleaseSource is null) return;
        RememberRunningVersion();
        try
        {
            await verificationDelay(UpdateCheckStartupDelay, cancellationToken);
            while (true)
            {
                if (IsUpdateCheckDue) await RunUpdateCheckAsync(cancellationToken);
                else RefreshUpdatePresentation(); // The offline reminder depends on the date.
                await verificationDelay(UpdateCheckPollInterval, cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    /// <summary>The user asked, so this ignores both the automatic setting and the schedule.</summary>
    public async Task CheckForUpdatesNowAsync()
    {
        if (latestReleaseSource is null || isCheckingForUpdates) return;
        checkedForUpdatesManually = true;
        await RunUpdateCheckAsync(CancellationToken.None);
        StatusMessage = string.Format(UiCopy.UpdateStatusMessageFormat, UpdateCheckStatusText);
    }

    /// <summary>Opening the offline reminder counts as reading it; it does not return for this version.</summary>
    public void DismissPossibleUpdateNotice()
    {
        if (RunningReleaseVersion is not { } running) return;
        applicationSettings = applicationSettings with { PossibleUpdateNoticeReadVersion = running.ToString() };
        SaveUpdateStateQuietly();
        RefreshUpdatePresentation();
    }

    private async Task RunUpdateCheckAsync(CancellationToken cancellationToken)
    {
        if (latestReleaseSource is null || isCheckingForUpdates) return;
        isCheckingForUpdates = true;
        RefreshUpdatePresentation();
        ReleaseVersion? latest = null;
        try
        {
            latest = await latestReleaseSource.ReadLatestAsync(cancellationToken);
            diagnostic?.Invoke("update-check", $"latest={latest} running={RunningReleaseVersion}");
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            // GitHub is not reachable from every network. This is routine, not an app problem.
            diagnostic?.Invoke("update-check", $"failed {exception.GetType().Name}");
        }
        finally
        {
            isCheckingForUpdates = false;
        }

        lastUpdateCheckFailed = latest is null;
        var now = utcNow();
        applicationSettings = applicationSettings with
        {
            LastUpdateCheckUtc = now,
            LastSuccessfulUpdateCheckUtc = latest is null ? applicationSettings.LastSuccessfulUpdateCheckUtc : now,
            LatestReleaseTag = latest?.ToString() ?? applicationSettings.LatestReleaseTag
        };
        SaveUpdateStateQuietly();
        RefreshUpdatePresentation();
    }

    /// <summary>The 15 days of the offline reminder start when this version first runs.</summary>
    private void RememberRunningVersion()
    {
        if (RunningReleaseVersion?.ToString() is not { } version || applicationSettings.VersionFirstSeen == version) return;
        applicationSettings = applicationSettings with { VersionFirstSeen = version, VersionFirstSeenUtc = utcNow() };
        SaveUpdateStateQuietly();
    }

    // Kept in memory even if it cannot be saved: background bookkeeping must not put
    // "settings could not be saved" on the home screen.
    private void SaveUpdateStateQuietly()
    {
        try
        {
            settingsStore.Save(applicationSettings);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            diagnostic?.Invoke("update-check", $"not saved {exception.GetType().Name}");
        }
    }

    private void RefreshUpdatePresentation()
    {
        OnPropertyChanged(nameof(ShowUpdateBadge));
        OnPropertyChanged(nameof(UpdateBadgeText));
        OnPropertyChanged(nameof(ShowPossibleUpdateBadge));
        OnPropertyChanged(nameof(IsCheckingForUpdates));
        OnPropertyChanged(nameof(CanCheckForUpdates));
        OnPropertyChanged(nameof(UpdateCheckStatusText));
        OnPropertyChanged(nameof(ManualUpdateCheckTooltip));
    }
}
