using System.IO;
using System.Net.Http;
using System.Xml.Linq;
using RevoHelper.App.Presentation;
using RevoHelper.App.Settings;
using RevoHelper.App.Updates;
using RevoHelper.Core.Dashboard;
using RevoHelper.Core.Hardware;

namespace RevoHelper.App.Tests.Presentation;

[Collection("Display language")]
public sealed class UpdateNotificationTests
{
    private static readonly DateTimeOffset Start = new(2026, 11, 7, 12, 0, 0, TimeSpan.Zero);
    private const string Newer = "v99.0.0";
    private const string Older = "v0.0.1";
    private static string Running => MainWindowViewModel.RunningReleaseVersion!.ToString();

    [Fact]
    public async Task WithoutASource_NothingRunsAndNothingShows()
    {
        var clock = new Clock(delaysBeforeStop: 1);
        var vm = Create(clock, new ApplicationSettings(), new MemorySettings(), source: null);

        await vm.RunAutomaticUpdateChecksAsync(clock.Stop.Token);

        Assert.Empty(clock.Delays);
        Assert.False(vm.CanCheckForUpdates);
        Assert.False(vm.ShowUpdateBadge);
        Assert.False(vm.ShowPossibleUpdateBadge);
    }

    [Fact]
    public async Task FirstCheck_WaitsTwoMinutesThenShowsTheNoticeAndRemembersIt()
    {
        var clock = new Clock(delaysBeforeStop: 2);
        var source = new FakeSource(Newer);
        var settings = new MemorySettings();
        var vm = Create(clock, new ApplicationSettings(), settings, source);
        var readsDuringFirstDelay = -1;
        clock.OnDelay = () => { if (readsDuringFirstDelay < 0) readsDuringFirstDelay = source.Reads; };

        await vm.RunAutomaticUpdateChecksAsync(clock.Stop.Token);

        Assert.Equal(0, readsDuringFirstDelay);
        Assert.Equal(TimeSpan.FromMinutes(2), clock.Delays[0]);
        Assert.Equal(1, source.Reads);
        Assert.True(vm.ShowUpdateBadge);
        Assert.Equal("发现新版本 V99.0.0", vm.UpdateBadgeText);
        Assert.Equal("发现新版本 V99.0.0", vm.UpdateCheckStatusText);
        Assert.Equal("V99.0.0", settings.Saved!.LatestReleaseTag);
        Assert.Equal(Start + TimeSpan.FromMinutes(2), settings.Saved.LastUpdateCheckUtc);
        Assert.Equal(Start + TimeSpan.FromMinutes(2), settings.Saved.LastSuccessfulUpdateCheckUtc);
    }

    [Fact]
    public async Task CheckedLessThanADayAgo_DoesNotGoOnlineButStillShowsTheSavedNotice()
    {
        var clock = new Clock(delaysBeforeStop: 2);
        var source = new FakeSource(Older);
        var lastCheck = Start - TimeSpan.FromHours(23);
        var vm = Create(clock, new ApplicationSettings(LastUpdateCheckUtc: lastCheck, LastSuccessfulUpdateCheckUtc: lastCheck, LatestReleaseTag: "V99.0.0"),
            new MemorySettings(), source);

        Assert.True(vm.ShowUpdateBadge);
        await vm.RunAutomaticUpdateChecksAsync(clock.Stop.Token);

        Assert.Equal(0, source.Reads);
        Assert.True(vm.ShowUpdateBadge);
    }

    [Theory]
    [InlineData(25)]
    [InlineData(-5)] // The clock moved back, so the saved time means nothing.
    public async Task ADayLaterOrAfterAClockChange_ChecksAgain(int hoursAgo)
    {
        var clock = new Clock(delaysBeforeStop: 2);
        var source = new FakeSource(Older);
        var lastCheck = Start - TimeSpan.FromHours(hoursAgo);
        var vm = Create(clock, new ApplicationSettings(LastUpdateCheckUtc: lastCheck, LastSuccessfulUpdateCheckUtc: lastCheck), new MemorySettings(), source);

        await vm.RunAutomaticUpdateChecksAsync(clock.Stop.Token);

        Assert.Equal(1, source.Reads);
    }

    [Fact]
    public async Task LeftRunningInTheTray_ChecksOncePerDay()
    {
        var clock = new Clock(delaysBeforeStop: 73); // Two minutes, then three days of hourly polls.
        var source = new FakeSource(Older);
        var vm = Create(clock, new ApplicationSettings(), new MemorySettings(), source);

        await vm.RunAutomaticUpdateChecksAsync(clock.Stop.Token);

        Assert.All(clock.Delays.Skip(1), delay => Assert.Equal(TimeSpan.FromHours(1), delay));
        Assert.Equal(3, source.Reads);
    }

    [Fact]
    public async Task UnreachableGitHub_IsRetriedEveryThreeHours()
    {
        var clock = new Clock(delaysBeforeStop: 25); // Two minutes, then one day of hourly polls.
        var source = new FakeSource(failure: new HttpRequestException("offline"));
        var vm = Create(clock, new ApplicationSettings(), new MemorySettings(), source);

        await vm.RunAutomaticUpdateChecksAsync(clock.Stop.Token);

        Assert.Equal(8, source.Reads);
    }

    [Fact]
    public async Task OnceARetrySucceeds_TheNextCheckIsADayLater()
    {
        var clock = new Clock(delaysBeforeStop: 30);
        var source = new FakeSource(Older, failure: new HttpRequestException("offline"), failuresBeforeSuccess: 2);
        var vm = Create(clock, new ApplicationSettings(), new MemorySettings(), source);

        await vm.RunAutomaticUpdateChecksAsync(clock.Stop.Token);

        Assert.Equal(3, source.Reads); // Fails at boot and three hours later, works at six, then waits a day.
        Assert.Equal("已是最新版本", vm.UpdateCheckStatusText);
    }

    [Fact]
    public async Task AutomaticChecksOff_StaysOfflineButAManualCheckStillWorks()
    {
        var clock = new Clock(delaysBeforeStop: 5);
        var source = new FakeSource(Newer);
        var settings = new MemorySettings();
        var vm = Create(clock, new ApplicationSettings(LatestReleaseTag: "V99.0.0"), settings, source);

        vm.CheckForUpdatesAutomatically = false;
        Assert.False(settings.Saved!.CheckForUpdatesAutomatically);
        Assert.False(vm.ShowUpdateBadge);

        await vm.RunAutomaticUpdateChecksAsync(clock.Stop.Token);
        Assert.Equal(0, source.Reads);

        await vm.CheckForUpdatesNowAsync();
        Assert.Equal(1, source.Reads);
        Assert.True(vm.ShowUpdateBadge); // The user asked, so the answer is shown.
        Assert.Equal("检查更新：发现新版本 V99.0.0", vm.StatusMessage);
    }

    [Fact]
    public async Task FailedCheck_IsQuietAndKeepsWhatWeKnew()
    {
        var clock = new Clock(delaysBeforeStop: 3);
        var source = new FakeSource(failure: new HttpRequestException("offline"));
        var settings = new MemorySettings();
        var logged = new List<string>();
        var vm = Create(clock, new ApplicationSettings(), settings, source, (_, message) => logged.Add(message));
        var status = vm.StatusMessage;

        await vm.RunAutomaticUpdateChecksAsync(clock.Stop.Token);

        Assert.Equal(1, source.Reads);
        Assert.False(vm.ShowUpdateBadge);
        Assert.Equal(status, vm.StatusMessage);
        Assert.Equal("检查失败，请稍后再试", vm.UpdateCheckStatusText);
        Assert.NotNull(settings.Saved!.LastUpdateCheckUtc);
        Assert.Null(settings.Saved.LastSuccessfulUpdateCheckUtc);
        Assert.Null(settings.Saved.LatestReleaseTag);
        Assert.Contains("failed HttpRequestException", logged);
    }

    [Fact]
    public async Task RequestTimeout_CountsAsAFailureAndDoesNotEndTheLoop()
    {
        var clock = new Clock(delaysBeforeStop: 3);
        var source = new FakeSource(failure: new TaskCanceledException("timed out"));
        var vm = Create(clock, new ApplicationSettings(), new MemorySettings(), source);

        await vm.RunAutomaticUpdateChecksAsync(clock.Stop.Token);

        Assert.Equal(3, clock.Delays.Count);
        Assert.Equal("检查失败，请稍后再试", vm.UpdateCheckStatusText);
    }

    [Fact]
    public async Task FailureAfterAKnownNewerRelease_KeepsTheNotice()
    {
        var source = new FakeSource(failure: new HttpRequestException("offline"));
        var vm = Create(new Clock(1), new ApplicationSettings(LatestReleaseTag: "V99.0.0"), new MemorySettings(), source);

        await vm.CheckForUpdatesNowAsync();

        Assert.True(vm.ShowUpdateBadge);
        Assert.Equal("发现新版本 V99.0.0", vm.UpdateCheckStatusText);
    }

    [Fact]
    public async Task ManualCheck_ReportsTheResultOnTheHomeStatusLineAndInTheTooltip()
    {
        var vm = Create(new Clock(1), new ApplicationSettings(), new MemorySettings(), new FakeSource(Older));

        Assert.Equal("尚未检查", vm.UpdateCheckStatusText);
        Assert.Equal("手动检查更新", vm.ManualUpdateCheckTooltip);
        await vm.CheckForUpdatesNowAsync();

        Assert.False(vm.ShowUpdateBadge);
        Assert.Equal(string.Empty, vm.UpdateBadgeText);
        Assert.Equal("已是最新版本", vm.UpdateCheckStatusText);
        Assert.Equal("检查更新：已是最新版本", vm.StatusMessage);
        Assert.Equal("手动检查更新 · 已是最新版本", vm.ManualUpdateCheckTooltip);
    }

    [Fact]
    public async Task ManualCheckFailure_IsReportedBecauseTheUserAsked()
    {
        var vm = Create(new Clock(1), new ApplicationSettings(), new MemorySettings(), new FakeSource(failure: new HttpRequestException("offline")));

        await vm.CheckForUpdatesNowAsync();

        Assert.Equal("检查更新：检查失败，请稍后再试", vm.StatusMessage);
    }

    [Theory]
    [InlineData("<b>hello</b>")]
    [InlineData("https://example.com/")]
    public void DamagedSavedTag_IsIgnored(string tag)
    {
        var vm = Create(new Clock(1), new ApplicationSettings(LatestReleaseTag: tag), new MemorySettings(), new FakeSource(Older));

        Assert.False(vm.ShowUpdateBadge);
        Assert.Equal("尚未检查", vm.UpdateCheckStatusText);
    }

    [Theory]
    [InlineData(typeof(IOException))]
    [InlineData(typeof(UnauthorizedAccessException))]
    public async Task SettingsThatCannotBeSaved_StayQuietAndTheNoticeStillShows(Type failure)
    {
        var clock = new Clock(delaysBeforeStop: 2);
        var settings = new MemorySettings { SaveFailure = (Exception)Activator.CreateInstance(failure)! };
        var vm = Create(clock, new ApplicationSettings(), settings, new FakeSource(Newer));
        var status = vm.StatusMessage;

        await vm.RunAutomaticUpdateChecksAsync(clock.Stop.Token);

        Assert.True(vm.ShowUpdateBadge);
        Assert.Equal(status, vm.StatusMessage);
    }

    [Fact]
    public async Task SecondClickWhileChecking_DoesNotSendAnotherRequest()
    {
        var pending = new TaskCompletionSource<ReleaseVersion>();
        var source = new FakeSource(pending: pending);
        var vm = Create(new Clock(1), new ApplicationSettings(), new MemorySettings(), source);

        var first = vm.CheckForUpdatesNowAsync();
        Assert.False(vm.CanCheckForUpdates);
        Assert.True(vm.IsCheckingForUpdates);
        Assert.Equal("正在检查…", vm.UpdateCheckStatusText);
        await vm.CheckForUpdatesNowAsync();
        Assert.Equal(1, source.Reads);

        pending.SetResult(ReleaseVersion.TryParse(Newer)!);
        await first;
        Assert.True(vm.CanCheckForUpdates);
        Assert.False(vm.IsCheckingForUpdates);
    }

    [Fact]
    public async Task AppExit_EndsTheLoopWithoutRecordingAFailure()
    {
        using var exit = new CancellationTokenSource();
        var settings = new MemorySettings();
        var source = new FakeSource(onRead: exit.Cancel, failure: new OperationCanceledException(exit.Token));
        var vm = Create(new Clock(5), new ApplicationSettings(VersionFirstSeen: Running, VersionFirstSeenUtc: Start), settings, source);

        await vm.RunAutomaticUpdateChecksAsync(exit.Token);

        Assert.Equal(1, source.Reads);
        Assert.Null(settings.Saved);
        Assert.Equal("尚未检查", vm.UpdateCheckStatusText);
    }

    [Fact]
    public async Task OfflineReminder_StartsCountingWhenThisVersionFirstRuns()
    {
        var clock = new Clock(delaysBeforeStop: 1);
        var settings = new MemorySettings();
        var vm = Create(clock, new ApplicationSettings(VersionFirstSeen: "V0.0.1", VersionFirstSeenUtc: Start - TimeSpan.FromDays(90)), settings,
            new FakeSource(failure: new HttpRequestException("offline")));
        Assert.False(vm.ShowPossibleUpdateBadge);

        await vm.RunAutomaticUpdateChecksAsync(clock.Stop.Token);

        Assert.Equal(Running, settings.Saved!.VersionFirstSeen);
        Assert.Equal(Start, settings.Saved.VersionFirstSeenUtc);
        Assert.False(vm.ShowPossibleUpdateBadge);
    }

    [Fact]
    public async Task OfflineReminder_AppearsAfterFifteenDaysWithoutReachingGitHub()
    {
        var clock = new Clock(delaysBeforeStop: 2 + 15 * 24);
        var vm = Create(clock, new ApplicationSettings(), new MemorySettings(), new FakeSource(failure: new HttpRequestException("offline")));
        var shownAt = new List<int>();
        vm.PropertyChanged += (_, change) =>
        {
            if (change.PropertyName == nameof(vm.ShowPossibleUpdateBadge) && vm.ShowPossibleUpdateBadge) shownAt.Add(clock.Delays.Count);
        };

        await vm.RunAutomaticUpdateChecksAsync(clock.Stop.Token);

        Assert.True(vm.ShowPossibleUpdateBadge);
        Assert.False(vm.ShowUpdateBadge);
        Assert.True(shownAt.Min() > 15 * 24, "The reminder must not appear before fifteen days.");
    }

    [Fact]
    public void OfflineReminder_IsReadOncePerVersion()
    {
        var settings = new MemorySettings();
        var vm = Create(new Clock(1), PossibleUpdateSettings(), settings, new FakeSource(Older));
        Assert.True(vm.ShowPossibleUpdateBadge);

        vm.DismissPossibleUpdateNotice();

        Assert.False(vm.ShowPossibleUpdateBadge);
        Assert.Equal(Running, settings.Saved!.PossibleUpdateNoticeReadVersion);
        var next = Create(new Clock(1), PossibleUpdateSettings() with { PossibleUpdateNoticeReadVersion = "V0.0.1" }, new MemorySettings(), new FakeSource(Older));
        Assert.True(next.ShowPossibleUpdateBadge); // Read for an older version only.
    }

    [Fact]
    public void OfflineReminder_StaysAwayWhenItWouldBeWrongOrUnwanted()
    {
        ApplicationSettings[] cases =
        [
            PossibleUpdateSettings() with { VersionFirstSeenUtc = Start - TimeSpan.FromDays(14) },
            PossibleUpdateSettings() with { LastSuccessfulUpdateCheckUtc = Start - TimeSpan.FromDays(3) },
            PossibleUpdateSettings() with { CheckForUpdatesAutomatically = false },
            PossibleUpdateSettings() with { PossibleUpdateNoticeReadVersion = Running },
            PossibleUpdateSettings() with { LatestReleaseTag = "V99.0.0" }, // The real notice shows instead.
        ];
        foreach (var settings in cases)
            Assert.False(Create(new Clock(1), settings, new MemorySettings(), new FakeSource(Older)).ShowPossibleUpdateBadge);
        Assert.True(Create(new Clock(1), cases[^1], new MemorySettings(), new FakeSource(Older)).ShowUpdateBadge);
    }

    [Fact]
    public async Task ChangingLanguage_RetranslatesTheNotice()
    {
        var vm = Create(new Clock(1), new ApplicationSettings(), new MemorySettings(), new FakeSource(Newer));
        await vm.CheckForUpdatesNowAsync();
        var changed = new List<string?>();
        vm.PropertyChanged += (_, change) => changed.Add(change.PropertyName);
        try
        {
            vm.SelectedUiLanguageCode = "en-US";

            Assert.Equal("New version V99.0.0", vm.UpdateBadgeText);
            Assert.Equal("Check for updates · New version V99.0.0", vm.ManualUpdateCheckTooltip);
            Assert.Contains(nameof(vm.UpdateBadgeText), changed);
            Assert.Contains(nameof(vm.UpdateCheckStatusText), changed);
            Assert.Contains(nameof(vm.ManualUpdateCheckTooltip), changed);
        }
        finally { UiLanguageService.Current.SetLanguage("zh-CN"); }
    }

    [Fact]
    public void Settings_DefaultToOnForUpgradingUsersAndSurviveARoundTrip()
    {
        var path = Path.Combine(Path.GetTempPath(), $"revohelper-update-settings-{Guid.NewGuid():N}.json");
        try
        {
            var store = new JsonApplicationSettingsStore(path);
            File.WriteAllText(path, """{"UiLanguageCode":"zh-CN"}""");
            Assert.True(store.Load().CheckForUpdatesAutomatically);

            var expected = new ApplicationSettings(CheckForUpdatesAutomatically: false, LastUpdateCheckUtc: Start, LatestReleaseTag: "V1.0.5",
                LastSuccessfulUpdateCheckUtc: Start, VersionFirstSeen: "V1.0.4", VersionFirstSeenUtc: Start, PossibleUpdateNoticeReadVersion: "V1.0.4");
            store.Save(expected);
            var loaded = store.Load();
            Assert.False(loaded.CheckForUpdatesAutomatically);
            Assert.Equal(Start, loaded.LastUpdateCheckUtc);
            Assert.Equal(Start, loaded.LastSuccessfulUpdateCheckUtc);
            Assert.Equal("V1.0.5", loaded.LatestReleaseTag);
            Assert.Equal("V1.0.4", loaded.VersionFirstSeen);
            Assert.Equal(Start, loaded.VersionFirstSeenUtc);
            Assert.Equal("V1.0.4", loaded.PossibleUpdateNoticeReadVersion);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void HomeNotices_OpenTheAboutPageWithGitHubAndMirrorDownloads()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "RevoHelper.sln"))) directory = directory.Parent;
        Assert.NotNull(directory);
        var document = XDocument.Load(Path.Combine(directory.FullName, "src", "RevoHelper.App", "MainWindow.xaml"));
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        XElement Named(string name) => document.Descendants().Single(element => (string?)element.Attribute(x + "Name") == name);

        // Users who cannot reach GitHub must not be sent straight to a page that never loads.
        Assert.Equal("UpdateBadge_Click", (string?)Named("UpdateAvailableBadge").Attribute("Click"));
        Assert.Null(Named("UpdateAvailableBadge").Attribute("Tag"));
        Assert.Equal("PossibleUpdateBadge_Click", (string?)Named("PossibleUpdateBadge").Attribute("Click"));
        Assert.Null(Named("PossibleUpdateBadge").Attribute("Tag"));
        Assert.Equal("CheckForUpdates_Click", (string?)Named("ManualUpdateCheckButton").Attribute("Click"));
        Assert.Equal("mirror", (string?)Named("MirrorDownloadButton").Attribute("Tag"));
        Assert.Equal("mirror-backup", (string?)Named("MirrorBackupDownloadButton").Attribute("Tag"));
        Assert.Equal("https://github.com/AstralMemory/RevoHelper/releases", AnnouncementCopy.LinkFor("downloads"));

        var codeBehind = File.ReadAllText(Path.Combine(directory.FullName, "src", "RevoHelper.App", "MainWindow.xaml.cs"));
        Assert.Contains("private void UpdateBadge_Click(object sender, RoutedEventArgs e) => Announcements_Click(sender, e);", codeBehind, StringComparison.Ordinal);
        Assert.Contains("ViewModel.DismissPossibleUpdateNotice();\n        Announcements_Click(sender, e);", codeBehind.Replace("\r\n", "\n"), StringComparison.Ordinal);
    }

    [Fact]
    public void UpdateCheck_NeverTalksToTheDownloadMirrors()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "RevoHelper.sln"))) directory = directory.Parent;
        Assert.NotNull(directory);
        foreach (var file in Directory.EnumerateFiles(Path.Combine(directory.FullName, "src", "RevoHelper.App", "Updates"), "*.cs"))
        {
            var source = File.ReadAllText(file);
            Assert.DoesNotContain("ghfast", source, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("gh-proxy", source, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Mirror", source, StringComparison.Ordinal);
        }
    }

    internal static ApplicationSettings PossibleUpdateSettings() => new(
        VersionFirstSeen: Running, VersionFirstSeenUtc: Start - TimeSpan.FromDays(16), LastUpdateCheckUtc: Start - TimeSpan.FromHours(1));

    private static MainWindowViewModel Create(Clock clock, ApplicationSettings settings, IApplicationSettingsStore store,
        ILatestReleaseSource? source, Action<string, string>? diagnostic = null) =>
        new(HomeDashboardFactory.Create(
                new MachineIdentity("MECHREVO", "YAOSHI Series", "Offline update test", "Core Ultra 9 275HX", "test", 24, 24),
                ["Intel Graphics"]),
            settings, store, new NoStartup(),
            verificationDelay: clock.DelayAsync, utcNow: () => clock.Now, diagnostic: diagnostic, latestReleaseSource: source);

    /// <summary>Time only moves through delays, and the app "exits" after the given number of them.</summary>
    private sealed class Clock(int delaysBeforeStop)
    {
        public DateTimeOffset Now { get; private set; } = Start;
        public List<TimeSpan> Delays { get; } = [];
        public CancellationTokenSource Stop { get; } = new();
        public Action? OnDelay { get; set; }

        public Task DelayAsync(TimeSpan delay, CancellationToken token)
        {
            OnDelay?.Invoke();
            Delays.Add(delay);
            Now += delay;
            if (Delays.Count >= delaysBeforeStop) Stop.Cancel();
            token.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }
    }

    private sealed class FakeSource(string? tag = null, Exception? failure = null,
        TaskCompletionSource<ReleaseVersion>? pending = null, Action? onRead = null, int failuresBeforeSuccess = int.MaxValue) : ILatestReleaseSource
    {
        public int Reads { get; private set; }

        public Task<ReleaseVersion> ReadLatestAsync(CancellationToken cancellationToken)
        {
            Reads++;
            onRead?.Invoke();
            if (pending is not null) return pending.Task;
            return failure is not null && Reads <= failuresBeforeSuccess
                ? Task.FromException<ReleaseVersion>(failure)
                : Task.FromResult(ReleaseVersion.TryParse(tag)!);
        }
    }

    private sealed class MemorySettings : IApplicationSettingsStore
    {
        public ApplicationSettings? Saved { get; private set; }
        public Exception? SaveFailure { get; init; }
        public ApplicationSettings Load() => new();

        public void Save(ApplicationSettings settings)
        {
            if (SaveFailure is not null) throw SaveFailure;
            Saved = settings;
        }
    }

    private sealed class NoStartup : IStartupRegistration
    {
        public bool IsEnabled() => false;
        public void SetEnabled(bool enabled) => throw new InvalidOperationException("No startup changes in update tests.");
    }
}
