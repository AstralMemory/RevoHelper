namespace RevoHelper.App.Tests.Lifecycle;

public sealed class StartupOrderingTests
{
    [Fact]
    public void App_RoutesPowerTransitionsThroughTheSessionChargeController()
    {
        var source = File.ReadAllText(Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..",
            "src", "RevoHelper.App", "App.xaml.cs")));
        Assert.Contains("sessionCharge = new SessionChargeController(experimentalCharge,", source);
        Assert.Contains("chargeController: sessionCharge", source);
        var start = source.IndexOf("private void SystemEvents_PowerModeChanged", StringComparison.Ordinal);
        var end = source.IndexOf("private async Task InitializePerformancePolicyWhenReadyAsync", start, StringComparison.Ordinal);
        var power = source[start..end];
        Assert.Contains("UpdateChargePowerAvailabilityAsync(e.Mode != PowerModes.Suspend", power);
        Assert.Contains("sessionCharge.SetPowerAvailableAsync(available, applicationLifetime.Token)", power);
        Assert.DoesNotContain("experimentalCharge?.RequestStop", power);
        Assert.Contains("sessionCharge?.Dispose();", source);
    }

    [Fact]
    public void App_PublishesBasicDiscoveryBeforeStartingOptionalDirectCpuProbe()
    {
        var source = File.ReadAllText(Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..",
            "src", "RevoHelper.App", "App.xaml.cs")));
        var start = source.IndexOf("private void StartDeferredHardwareDiscovery", StringComparison.Ordinal);
        var end = source.IndexOf("protected override void OnExit", start, StringComparison.Ordinal);
        var discovery = source[start..end];

        Assert.True(discovery.IndexOf("viewModel.UpdateDashboard", StringComparison.Ordinal) <
            discovery.IndexOf("StartDirectCpuBackendProbe", StringComparison.Ordinal),
            "Basic OEM discovery must be published before optional direct-backend work starts.");
        Assert.Contains("viewModel.CapabilityRecheckRequested +=", source, StringComparison.Ordinal);
        Assert.Contains("case OemControlSessionUpdate:\n", source.Replace("\r\n", "\n"), StringComparison.Ordinal);
        var reconnect = source.IndexOf("case OemControlSessionUpdate:", StringComparison.Ordinal);
        Assert.Contains("StartDeferredHardwareDiscovery(viewModel)", source[reconnect..source.IndexOf("break;", reconnect, StringComparison.Ordinal)], StringComparison.Ordinal);
    }

    [Fact]
    public void App_ShowsMainWindowBeforeStartingHardwareDiscovery()
    {
        var sourcePath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..",
            "src", "RevoHelper.App", "App.xaml.cs"));
        var source = File.ReadAllText(sourcePath);
        var showIndex = source.IndexOf("window.Show();", StringComparison.Ordinal);
        var discoveryIndex = source.IndexOf("StartDeferredHardwareDiscovery(viewModel);", StringComparison.Ordinal);

        Assert.True(showIndex >= 0, "Main window must be shown during startup.");
        Assert.True(discoveryIndex > showIndex,
            "Slow WMI and OEM profile discovery must begin only after the first window is visible.");
    }

    [Fact]
    public void App_DetectsConflictServicesAfterShowingTheMainWindow()
    {
        var sourcePath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..",
            "src", "RevoHelper.App", "App.xaml.cs"));
        var source = File.ReadAllText(sourcePath);
        var showIndex = source.IndexOf("window.Show();", StringComparison.Ordinal);
        var detectionIndex = source.IndexOf("StartConflictServiceDetection(viewModel);", StringComparison.Ordinal);

        Assert.True(detectionIndex > showIndex,
            "Service detection uses WMI and must not delay the first window render.");
        Assert.Contains("viewModel.RefreshConflictServicesAsync", source, StringComparison.Ordinal);
        Assert.Contains("new WindowsConflictServiceManager()", source, StringComparison.Ordinal);
    }

    [Fact]
    public void App_ControlConflictNotice_CannotCancelStartup()
    {
        var sourcePath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..",
            "src", "RevoHelper.App", "App.xaml.cs"));
        var source = File.ReadAllText(sourcePath);
        var start = source.IndexOf("var controlConflictDetector", StringComparison.Ordinal);
        var end = source.IndexOf("var gcuStatusSource", start, StringComparison.Ordinal);
        var conflictNotice = source[start..end];

        Assert.Contains("MessageBoxButton.OK", conflictNotice, StringComparison.Ordinal);
        Assert.DoesNotContain("MessageBoxButton.YesNo", conflictNotice, StringComparison.Ordinal);
        Assert.DoesNotContain("Shutdown();", conflictNotice, StringComparison.Ordinal);
    }

    [Fact]
    public void App_ControlConflictNotice_ExplainsCoexistenceWithoutRequestingOemShutdown()
    {
        var sourcePath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..",
            "src", "RevoHelper.App", "App.xaml.cs"));
        var source = File.ReadAllText(sourcePath);
        var start = source.IndexOf("var controlConflictDetector", StringComparison.Ordinal);
        var end = source.IndexOf("var gcuStatusSource", start, StringComparison.Ordinal);
        var conflictNotice = source[start..end];

        Assert.Contains("RevoHelper可以与官方控制台共存，不会停用或禁用它们", conflictNotice, StringComparison.Ordinal);
        Assert.DoesNotContain("主动让出", conflictNotice, StringComparison.Ordinal);
        Assert.DoesNotContain("请关闭官方前台", conflictNotice, StringComparison.Ordinal);
    }

    [Fact]
    public void App_ForwardsTypedLightingEventsToTheViewModel()
    {
        var sourcePath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..",
            "src", "RevoHelper.App", "App.xaml.cs"));
        var source = File.ReadAllText(sourcePath);

        Assert.Contains("case OemLightingZoneUpdate lighting:", source, StringComparison.Ordinal);
        Assert.Contains("viewModel.ApplyLightingZoneState(lighting.Zone, lighting.State);", source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void App_WindowsStartupLaunch_InitializesThenHidesToTray()
    {
        var sourcePath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..",
            "src", "RevoHelper.App", "App.xaml.cs"));
        var source = File.ReadAllText(sourcePath);

        Assert.Contains("StartupCommand.IsStartupLaunch(e.Args)", source, StringComparison.Ordinal);
        var showIndex = source.IndexOf("window.Show();", StringComparison.Ordinal);
        var hideIndex = source.IndexOf("window.Hide();", StringComparison.Ordinal);
        Assert.True(showIndex >= 0 && hideIndex > showIndex,
            "A login launch must initialize WPF and its tray icon before hiding the main window.");
    }

    [Fact]
    public void ExperimentalAppReadsStartupWithoutMigratingStablePortableEntry()
    {
        var sourcePath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..",
            "src", "RevoHelper.App", "App.xaml.cs"));
        var source = File.ReadAllText(sourcePath);
        var readIndex = source.IndexOf("startupRegistration.IsEnabled()", StringComparison.Ordinal);
        var viewModelIndex = source.IndexOf("var viewModel = new MainWindowViewModel", StringComparison.Ordinal);

        Assert.DoesNotContain("StartupRegistrationSynchronizer.Synchronize(startupRegistration)", source, StringComparison.Ordinal);
        Assert.True(readIndex >= 0 && readIndex < viewModelIndex,
            "Experimental startup must only read registration and must not retarget the stable executable.");
    }

    [Fact]
    public void App_AutoConnectsRememberedLiquidCoolingAfterStartingOemStatusAndOnResume()
    {
        var sourcePath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..",
            "src", "RevoHelper.App", "App.xaml.cs"));
        var source = File.ReadAllText(sourcePath);
        var statusIndex = source.IndexOf("StartOemStatus(viewModel);", StringComparison.Ordinal);
        var autoConnectIndex = source.IndexOf("viewModel.TryAutoConnectLiquidCoolingAsync", statusIndex,
            StringComparison.Ordinal);

        Assert.True(statusIndex >= 0 && autoConnectIndex > statusIndex,
            "Automatic water-cooling connection must wait until the shared GCU observer is running.");
        Assert.Contains("mainViewModel.TryAutoConnectLiquidCoolingAsync", source, StringComparison.Ordinal);
    }

    [Fact]
    public void App_AppliesAutomaticRefreshRateForTheCurrentPowerSourceAtStartup()
    {
        var sourcePath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..",
            "src", "RevoHelper.App", "App.xaml.cs"));
        var source = File.ReadAllText(sourcePath);
        var showIndex = source.IndexOf("window.Show();", StringComparison.Ordinal);
        var startupRefreshIndex = source.IndexOf(
            "ApplyAutomaticRefreshRateForCurrentPowerSourceAsync(viewModel)",
            StringComparison.Ordinal);

        Assert.True(startupRefreshIndex > showIndex,
            "The saved automatic refresh-rate policy must be reconciled after the window is initialized, " +
            "rather than waiting for a later AC/DC notification.");
    }

}
