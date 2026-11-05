using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Xml.Linq;
using RevoHelper.App.Presentation;
using RevoHelper.App.Settings;
using RevoHelper.Core.Control;
using RevoHelper.Core.Dashboard;
using RevoHelper.Core.Hardware;

namespace RevoHelper.App.Tests.Presentation;

[Collection("Display language")]
public sealed class PerformanceModeRetryTests
{
    [Theory]
    [InlineData("OfficeProfileRadio", "Profile_Click")]
    [InlineData("BalancedProfileRadio", "Profile_Click")]
    [InlineData("TurboProfileRadio", "Profile_Click")]
    [InlineData("CustomProfileRadio", "Profile_Click")]
    [InlineData("TurboQuietRadio", "TurboStrategy_Click")]
    [InlineData("TurboHighRadio", "TurboStrategy_Click")]
    [InlineData("CustomProfileOneRadio", "CustomProfile_Click")]
    [InlineData("CustomProfileTwoRadio", "CustomProfile_Click")]
    [InlineData("CustomProfileThreeRadio", "CustomProfile_Click")]
    public void RealModeControls_UseOnlyExplicitClickForRequests(string name, string handler)
    {
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
        var window = XDocument.Load(Locate("src", "RevoHelper.App", "MainWindow.xaml"));
        var radio = window.Descendants().Single(element => (string?)element.Attribute(xaml + "Name") == name);

        Assert.Equal(handler, (string?)radio.Attribute("Click"));
        Assert.Null(radio.Attribute("Checked"));
        Assert.Null(radio.Attribute("PreviewMouseDown"));
        Assert.Null(radio.Attribute("PreviewMouseLeftButtonDown"));
    }

    [Fact]
    public void AlreadySelectedRadio_CanRequestSameModeTwiceWithoutCheckedChanges()
    {
        RunOnSta(() =>
        {
            var platform = new RecordingPlatform();
            var vm = CreateViewModel(platform);
            vm.InitializePlatformModeAsync(default).GetAwaiter().GetResult();
            var radio = new RadioButton { IsChecked = true };
            var checkedEvents = 0;
            radio.Checked += (_, _) => checkedEvents++;
            radio.Click += (_, _) => Assert.True(vm.ApplyPlatformProfileAsync("office", default).GetAwaiter().GetResult());

            // Memory-only routed user actions: no HWND, real App host or hardware.
            radio.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent, radio));
            radio.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent, radio));

            Assert.True(radio.IsChecked);
            Assert.Equal(0, checkedEvents);
            Assert.Equal([new PlatformModeSelection(PlatformMode.Office), new(PlatformMode.Office)], platform.Writes);
        });
    }

    [Fact]
    public void ProgrammaticProfileSynchronization_DoesNotRequestAnyModeWrite()
    {
        RunOnSta(() =>
        {
            var platform = new RecordingPlatform();
            var vm = CreateViewModel(platform);
            var window = new MainWindow(vm) { AllowApplicationClose = true };
            try
            {
                // The actual MainWindow synchronizer runs on these VM changes.
                // No Show()/HWND is needed, and no application startup code runs.
                vm.SelectProfile("office");
                Assert.True(((RadioButton)window.FindName("OfficeProfileRadio")).IsChecked);
                vm.SelectProfile("balanced");
                Assert.True(((RadioButton)window.FindName("BalancedProfileRadio")).IsChecked);
                vm.SelectTurboStrategy(TurboStrategy.HighEfficiency);
                Assert.True(((RadioButton)window.FindName("TurboHighRadio")).IsChecked);
                vm.SelectCustomProfile(2);
                Assert.True(((RadioButton)window.FindName("CustomProfileTwoRadio")).IsChecked);
                Assert.Empty(platform.Writes);
            }
            finally { window.Close(); }
        });
    }

    private static MainWindowViewModel CreateViewModel(RecordingPlatform platform) => new(
        HomeDashboardFactory.Create(new MachineIdentity("MECHREVO", "offline", "offline", "CPU", ""), []),
        new(), new MemorySettings(), new NoStartup(), platformModeController: platform);

    private static void RunOnSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception exception) { failure = exception; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(15)), "Offline mode-event test timed out.");
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private static string Locate(params string[] parts)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine([directory.FullName, .. parts]);
            if (File.Exists(candidate)) return candidate;
        }
        throw new FileNotFoundException(Path.Combine(parts));
    }

    private sealed class RecordingPlatform : IPlatformModeController
    {
        private PlatformModeSelection current = new(PlatformMode.Office);
        public List<PlatformModeSelection> Writes { get; } = [];
        public ValueTask<PlatformModeSelection?> ReadPlatformModeAsync(CancellationToken token) => ValueTask.FromResult<PlatformModeSelection?>(current);
        public ValueTask<ControlResult> SetPlatformModeAsync(PlatformModeSelection selection, CancellationToken token)
        {
            Writes.Add(selection);
            current = selection;
            return ValueTask.FromResult(new ControlResult(true, "offline confirmed"));
        }
    }

    private sealed class MemorySettings : IApplicationSettingsStore
    {
        public ApplicationSettings Load() => new();
        public void Save(ApplicationSettings settings) { }
    }

    private sealed class NoStartup : IStartupRegistration
    {
        public bool IsEnabled() => false;
        public void SetEnabled(bool enabled) => throw new InvalidOperationException("No startup changes in offline retry tests.");
    }
}
