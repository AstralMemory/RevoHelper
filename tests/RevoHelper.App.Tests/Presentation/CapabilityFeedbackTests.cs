using RevoHelper.App.Presentation;
using RevoHelper.App.Settings;
using RevoHelper.Core.Dashboard;
using RevoHelper.Core.Discovery;
using RevoHelper.Core.Hardware;
using RevoHelper.Core.Control;
using System.Xml.Linq;

namespace RevoHelper.App.Tests.Presentation;

public sealed class CapabilityFeedbackTests
{
    [Fact]
    public void RediscoveryPreservesLocalPowerDraftsAndOperationFeedback()
    {
        var vm = new MainWindowViewModel(HomeDashboardFactory.Create(new("MECHREVO", "Test", "", "CPU", ""), []));
        vm.SelectProfile("custom");
        var initial = new CpuPowerLimitProfile("custom:1", "Custom", "Custom", 55, true, 75, true, true);
        vm.ApplyCpuPowerLimitProfiles([initial]);
        vm.SelectedPl1Watts = 45;
        var before = vm.StatusMessage;
        vm.ApplyCpuPowerLimitProfiles([initial with { Pl1Watts = 60 }], preserveDrafts: true);
        Assert.Equal(45, vm.SelectedPl1Watts);
        Assert.Equal(before, vm.StatusMessage);
        vm.ApplyCpuPowerLimitProfiles([], preserveDrafts: true);
        Assert.Equal(45, vm.SelectedPl1Watts);
    }

    [Fact]
    public void ManualRecheckIsSingleFlightAndCanBeRequestedAgainAfterCompletion()
    {
        var vm = new MainWindowViewModel(HomeDashboardFactory.Create(new("MECHREVO", "Test", "", "CPU", ""), []));
        var requests = 0;
        vm.CapabilityRecheckRequested += () => requests++;
        vm.RequestCapabilityRecheck();
        vm.RequestCapabilityRecheck();
        Assert.Equal(1, requests);
        Assert.True(vm.IsCapabilityDetectionBusy);
        Assert.False(vm.CanRecheckCapabilities);
        vm.SetCapabilityDetectionBusy(false);
        vm.RequestCapabilityRecheck();
        Assert.Equal(2, requests);
    }

    [Fact]
    public void MissingTargetModeAndRediscoveryDoNotDisableManualBaseCommands()
    {
        var identity = new MachineIdentity("MECHREVO", "Test", "", "CPU", "");
        var vm = new MainWindowViewModel(HomeDashboardFactory.Create(identity, []), new(),
            new MemorySettings(), new NoStartup(), platformModeController: new AvailablePlatform());
        vm.ApplyRuntimeCapabilities(RuntimeCapabilityFactory.Create(identity,
            new OemControlInventory([], true, "test"),
            OemRuntimeEvidence.None with { SupportedPlatformModes = new HashSet<PlatformMode> { PlatformMode.Office } }, []));
        Assert.True(vm.CanUseOfficeMode);
        Assert.True(vm.CanUseBalancedMode);
        Assert.Equal(vm.BalancedModeDescription, vm.BalancedModeTooltip);
        vm.SetCapabilityDetectionBusy(true);
        Assert.True(vm.CanUseOfficeMode);
        Assert.True(vm.CanUseBalancedMode);
    }

    [Fact]
    public void DisabledModeTooltipDoesNotReplaceCardSubtitleAndManualRecheckIsWired()
    {
        var xaml = XDocument.Load(Locate("src", "RevoHelper.App", "MainWindow.xaml"));
        var name = XName.Get("Name", "http://schemas.microsoft.com/winfx/2006/xaml");
        foreach (var control in new[] { "OfficeProfileRadio", "BalancedProfileRadio", "TurboProfileRadio", "CustomProfileRadio" })
        {
            var element = xaml.Descendants().Single(item => (string?)item.Attribute(name) == control);
            Assert.Contains("ModeTooltip", (string?)element.Attribute("ToolTip"));
            Assert.NotNull(element.Attribute("AutomationProperties.HelpText"));
        }
        var style = xaml.Descendants().Single(item =>
            (string?)item.Attribute(XName.Get("Key", "http://schemas.microsoft.com/winfx/2006/xaml")) == "ProfileRadioStyle");
        Assert.Contains(style.Elements(), item => (string?)item.Attribute("Property") == "ToolTipService.ShowOnDisabled" && (string?)item.Attribute("Value") == "True");
        var recheck = xaml.Descendants().Single(item => (string?)item.Attribute(name) == "CapabilityRecheckButton");
        var settings = xaml.Descendants().Single(item => (string?)item.Attribute(name) == "SettingsDrawerContent");
        var homeStatus = xaml.Descendants().Single(item => (string?)item.Attribute(name) == "HomeStatusBlock");
        Assert.Equal("CapabilityRecheck_Click", (string?)recheck.Attribute("Click"));
        Assert.Contains(settings.Descendants(), item => ReferenceEquals(item, recheck));
        Assert.DoesNotContain(homeStatus.Descendants(), item => ReferenceEquals(item, recheck));
    }

    [Theory]
    [InlineData("UNKNOWN", true)]
    [InlineData("MECHREVO", false)]
    public void MissingFanReadbackIsNotMisreportedAsAnIdentityOrInstallationFailure(string manufacturer, bool chain)
    {
        var identity = new MachineIdentity(manufacturer, "Test", "", "CPU", "");
        var vm = new MainWindowViewModel(HomeDashboardFactory.Create(identity, []));
        vm.ApplyRuntimeCapabilities(RuntimeCapabilityFactory.Create(identity,
            new OemControlInventory([], chain, "test"), OemRuntimeEvidence.None, []));

        Assert.Contains("运行时证据", vm.FanAvailabilityReason);
    }

    [Fact]
    public void FanEvidenceAllowsViewingWithoutEnablingEditsOrUnconfirmedModes()
    {
        var identity = new MachineIdentity("MECHREVO", "Test", "", "CPU", "");
        var vm = new MainWindowViewModel(HomeDashboardFactory.Create(identity, []));
        vm.ApplyRuntimeCapabilities(RuntimeCapabilityFactory.Create(identity,
            new OemControlInventory([], true, "test"),
            OemRuntimeEvidence.None with { HasFanCurves = true }, []));

        Assert.True(vm.CanOpenFanAdjustment);
        Assert.False(vm.CanEditFanSettings);
        Assert.False(vm.CanUseCustomMode);
    }

    private static string Locate(params string[] path)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine([directory.FullName, .. path]);
            if (File.Exists(candidate)) return candidate;
        }
        throw new FileNotFoundException(Path.Combine(path));
    }

    private sealed class AvailablePlatform : IPlatformModeController
    {
        public ValueTask<PlatformModeSelection?> ReadPlatformModeAsync(CancellationToken token) =>
            ValueTask.FromResult<PlatformModeSelection?>(new(PlatformMode.Balanced));
        public ValueTask<ControlResult> SetPlatformModeAsync(PlatformModeSelection selection, CancellationToken token) =>
            ValueTask.FromResult(new ControlResult(true, "ok"));
    }

    private sealed class MemorySettings : IApplicationSettingsStore
    {
        public ApplicationSettings Load() => new();
        public void Save(ApplicationSettings settings) { }
    }

    private sealed class NoStartup : IStartupRegistration
    {
        public bool IsEnabled() => false;
        public void SetEnabled(bool enabled) { }
    }
}
