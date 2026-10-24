using RevoHelper.Hardware.Windows.Display;

namespace RevoHelper.Hardware.Windows.Tests.Display;

public sealed class WindowsDisplayControllerTests
{
    private static FakeDisplayModeApi HighRefreshApi() => new(
        new("DISPLAY1", "Panel", 2560, 1600, 240),
        new[] { 60, 120, 240 }.Select(rate => new WindowsDisplayMode("DISPLAY1", "Panel", 2560, 1600, rate)).ToArray());

    [Fact]
    public async Task ManualApply_PersistsEvenWhenRequestedHighRateIsAlreadyActive()
    {
        var api = HighRefreshApi();
        api.TryApplyRefreshRate("DISPLAY1", 2560, 1600, 60, true);
        api.ExternalRate(240);
        var controller = new WindowsDisplayController(api);
        Assert.True((await controller.SetRefreshRateAsync(240, CancellationToken.None)).Succeeded);
        Assert.Equal(240, api.Saved.RefreshRateHz);
        Assert.True(api.PersistenceFlags.Last());
        api.ExternalRate(api.Saved.RefreshRateHz); // Next Windows session loads its saved mode.
        Assert.Equal(240, (await controller.ReadAsync(CancellationToken.None)).CurrentRefreshRateHz);
    }

    [Fact]
    public async Task AutomaticLowRate_DoesNotPersistAndExitRestoresOriginalHighRate()
    {
        var api = HighRefreshApi();
        var controller = new WindowsDisplayController(api);
        await controller.SetTemporaryRefreshRateAsync(60, CancellationToken.None);
        Assert.Equal(60, api.ReadCurrent().RefreshRateHz);
        Assert.Equal(240, api.Saved.RefreshRateHz);
        Assert.True((await controller.StopAndRestoreAsync(CancellationToken.None)).Succeeded);
        Assert.Equal(240, api.ReadCurrent().RefreshRateHz);
        Assert.All(api.PersistenceFlags, persist => Assert.False(persist));
        Assert.False((await controller.SetTemporaryRefreshRateAsync(60, CancellationToken.None)).Succeeded);
        Assert.Equal(240, api.ReadCurrent().RefreshRateHz);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Restore_DoesNotOverwriteLaterManualOrExternalChoice(bool throughRevoHelper)
    {
        var api = HighRefreshApi();
        var controller = new WindowsDisplayController(api);
        await controller.SetTemporaryRefreshRateAsync(60, CancellationToken.None);
        if (throughRevoHelper) await controller.SetRefreshRateAsync(120, CancellationToken.None);
        else api.ExternalRate(120);
        await controller.RestoreTemporaryRefreshRateAsync(CancellationToken.None);
        Assert.Equal(120, api.ReadCurrent().RefreshRateHz);
    }

    [Fact]
    public async Task RepeatedAutomaticChanges_RestoreFirstBaselineNotLastTemporaryMode()
    {
        var api = HighRefreshApi();
        var controller = new WindowsDisplayController(api);
        await controller.SetTemporaryRefreshRateAsync(60, CancellationToken.None);
        await controller.SetTemporaryRefreshRateAsync(120, CancellationToken.None);
        await controller.RestoreTemporaryRefreshRateAsync(CancellationToken.None);
        Assert.Equal(240, api.ReadCurrent().RefreshRateHz);
        Assert.Equal(240, api.Saved.RefreshRateHz);
    }

    [Fact]
    public async Task RejectedAutomaticChange_DoesNotLosePreviousTemporaryOwnership()
    {
        var api = HighRefreshApi();
        var controller = new WindowsDisplayController(api);
        await controller.SetTemporaryRefreshRateAsync(60, CancellationToken.None);
        api.RejectNextApply = true;
        Assert.False((await controller.SetTemporaryRefreshRateAsync(120, CancellationToken.None)).Succeeded);
        await controller.RestoreTemporaryRefreshRateAsync(CancellationToken.None);
        Assert.Equal(240, api.ReadCurrent().RefreshRateHz);
        Assert.Equal(240, api.Saved.RefreshRateHz);
    }

    [Fact]
    public async Task Read_FiltersToCurrentResolutionAndSortsUniqueRefreshRates()
    {
        var api = new FakeDisplayModeApi(
            new("DISPLAY1", "内置显示器", 2560, 1600, 240),
            [
                new("DISPLAY1", "内置显示器", 1920, 1080, 60),
                new("DISPLAY1", "内置显示器", 2560, 1600, 300),
                new("DISPLAY1", "内置显示器", 2560, 1600, 60),
                new("DISPLAY1", "内置显示器", 2560, 1600, 240),
                new("DISPLAY1", "内置显示器", 2560, 1600, 240)
            ]);
        var controller = new WindowsDisplayController(api);

        var snapshot = await controller.ReadAsync(CancellationToken.None);

        Assert.Equal("内置显示器", snapshot.DisplayName);
        Assert.Equal(2560, snapshot.Width);
        Assert.Equal(1600, snapshot.Height);
        Assert.Equal(240, snapshot.CurrentRefreshRateHz);
        Assert.Equal([60, 240, 300], snapshot.AvailableRefreshRatesHz);
    }

    [Fact]
    public async Task SetRefreshRate_RejectsRateNotExposedByCurrentDisplayPath()
    {
        var api = new FakeDisplayModeApi(
            new("DISPLAY1", "内置显示器", 2560, 1600, 240),
            [new("DISPLAY1", "内置显示器", 2560, 1600, 60), new("DISPLAY1", "内置显示器", 2560, 1600, 240)]);
        var controller = new WindowsDisplayController(api);

        var result = await controller.SetRefreshRateAsync(300, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Null(api.AppliedRefreshRate);
    }

    [Fact]
    public async Task SetRefreshRate_AppliesSupportedRateAndRequiresReadback()
    {
        var api = new FakeDisplayModeApi(
            new("DISPLAY1", "内置显示器", 2560, 1600, 240),
            [new("DISPLAY1", "内置显示器", 2560, 1600, 60), new("DISPLAY1", "内置显示器", 2560, 1600, 240)]);
        var controller = new WindowsDisplayController(api);

        var result = await controller.SetRefreshRateAsync(60, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(60, api.AppliedRefreshRate);
    }

    [Fact]
    public async Task Read_UsesPhysicalActivePathRateAndReportsDesktopPathMismatch()
    {
        var api = new FakeDisplayModeApi(
            new("DISPLAY1", "内置显示器", 2560, 1600, 60),
            [new("DISPLAY1", "内置显示器", 2560, 1600, 60), new("DISPLAY1", "内置显示器", 2560, 1600, 240)],
            new(60, 240));
        var controller = new WindowsDisplayController(api);

        var snapshot = await controller.ReadAsync(CancellationToken.None);

        Assert.Equal(240, snapshot.CurrentRefreshRateHz);
        Assert.Equal(60, snapshot.DesktopRefreshRateHz);
        Assert.True(snapshot.RequiresRefreshRateSynchronization);
    }

    [Fact]
    public async Task SetRefreshRate_ReappliesPhysicalRateWhenDesktopPathIsOutOfSync()
    {
        var api = new FakeDisplayModeApi(
            new("DISPLAY1", "内置显示器", 2560, 1600, 60),
            [new("DISPLAY1", "内置显示器", 2560, 1600, 60), new("DISPLAY1", "内置显示器", 2560, 1600, 240)],
            new(60, 240));
        var controller = new WindowsDisplayController(api);

        var result = await controller.SetRefreshRateAsync(240, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(240, api.AppliedRefreshRate);
    }

    [Fact]
    public async Task SetRefreshRate_RejectsWhenDesktopPathRemainsOutOfSyncAfterApply()
    {
        var api = new FakeDisplayModeApi(
            new("DISPLAY1", "内置显示器", 2560, 1600, 60),
            [new("DISPLAY1", "内置显示器", 2560, 1600, 60), new("DISPLAY1", "内置显示器", 2560, 1600, 240)],
            new(60, 240),
            synchronizeOnApply: false);
        var controller = new WindowsDisplayController(api);

        var result = await controller.SetRefreshRateAsync(240, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains("桌面路径", result.Reason, StringComparison.Ordinal);
    }

    private sealed class FakeDisplayModeApi(
        WindowsDisplayMode current,
        IReadOnlyList<WindowsDisplayMode> modes,
        WindowsDisplayPathRefresh? activePath = null,
        bool synchronizeOnApply = true) : IWindowsDisplayModeApi
    {
        public int? AppliedRefreshRate { get; private set; }
        public bool RejectNextApply { get; set; }
        public WindowsDisplayMode Saved { get; private set; } = current;
        public List<bool> PersistenceFlags { get; } = [];
        public void ExternalRate(int rate) { currentMode = currentMode with { RefreshRateHz = rate }; activePath = new(rate, rate); }
        private WindowsDisplayMode currentMode = current;

        public WindowsDisplayMode ReadCurrent() => currentMode;
        public WindowsDisplayMode ReadSaved(string deviceName) => Saved;
        public IReadOnlyList<WindowsDisplayMode> EnumerateModes(string deviceName) => modes;
        public WindowsDisplayPathRefresh? ReadActivePathRefresh(string deviceName) => activePath;

        public bool TryApplyRefreshRate(string deviceName, int width, int height, int refreshRateHz, bool persist)
        {
            if (RejectNextApply) { RejectNextApply = false; return false; }
            PersistenceFlags.Add(persist);
            if (persist) Saved = currentMode with { RefreshRateHz = refreshRateHz };
            AppliedRefreshRate = refreshRateHz;
            if (synchronizeOnApply)
            {
                currentMode = currentMode with { RefreshRateHz = refreshRateHz };
                activePath = new(refreshRateHz, refreshRateHz);
            }
            return true;
        }
    }
}
