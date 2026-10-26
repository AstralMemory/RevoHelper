using System.Runtime.ExceptionServices;
using RevoHelper.App.Monitoring;
using RevoHelper.App.Settings;
using RevoHelper.Core.Monitoring;

namespace RevoHelper.App.Tests.Monitoring;

public sealed class PerformanceMonitorControllerTests
{
    [Fact]
    public void ExplicitAdd_CreatesOnlyChosenRecord_AndPinsMatchingIdentityWithoutWindowEnumeration() => RunOnSta(() =>
    {
        using var context = new Context(new(741, DateTimeOffset.UtcNow.AddMinutes(-1), "user-added", "user-added.exe"));
        Assert.True(context.Panel.IsAutomatic);
        Assert.Empty(context.Panel.Programs);
        Assert.Equal(0, context.Resolver.Calls);
        Assert.Equal(0, context.Source.StartCalls);

        context.Panel.AddProgram(@"C:\Games\user-added.exe");

        Assert.False(context.Panel.IsAutomatic);
        var entry = Assert.Single(context.Panel.Programs);
        Assert.Equal(@"C:\Games\user-added.exe", entry.ExecutablePath);
        Assert.Equal(entry.Id, Assert.Single(context.Records.Load()).Id);
        Assert.NotNull(entry.LastSelectedAtUtc);
        Assert.Equal(741, context.Source.LastPinned!.ProcessId);
        Assert.Equal(context.Resolver.Result!.StartedAtUtc, context.Source.LastPinned.StartedAtUtc);
        Assert.Equal(new[] { @"C:\Games\user-added.exe" }, context.Resolver.RequestedPaths);
        Assert.Equal(0, context.Source.StartCalls); // Merely editing settings does not start FPS/ETW or a meter HWND.
    });

    [Fact]
    public void MissingManualProgram_PinsUnavailablePlaceholder_AndReturningToAutoClearsPinWithoutResolverScan() => RunOnSta(() =>
    {
        using var context = new Context(null);
        context.Panel.AddProgram(@"C:\Games\not-running.exe");

        Assert.False(context.Panel.IsAutomatic);
        Assert.Equal(-1, context.Source.LastPinned!.ProcessId);
        Assert.Equal("not-running.exe", context.Source.LastPinned.DisplayName);
        var lookupCalls = context.Resolver.Calls;

        context.Panel.IsAutomatic = true;

        Assert.Null(context.Source.LastPinned);
        Assert.Equal(lookupCalls, context.Resolver.Calls);
        Assert.Single(context.Records.Load());
        Assert.Equal(0, context.Source.StartCalls);
    });

    [Fact]
    public void RemoveSelectedProgram_RemovesOnlyRecord_AndDisposeStopsOnlyFakeSource() => RunOnSta(() =>
    {
        using var context = new Context(null);
        Directory.CreateDirectory(context.DirectoryPath);
        var executable = Path.Combine(context.DirectoryPath, "user-added.exe");
        File.WriteAllText(executable, "test marker; never executed");
        context.Panel.AddProgram(executable);

        context.Panel.RemoveSelectedProgram();

        Assert.Empty(context.Panel.Programs);
        Assert.Empty(context.Records.Load());
        Assert.Equal("test marker; never executed", File.ReadAllText(executable));
        Assert.Equal(-1, context.Source.LastPinned!.ProcessId);
        context.CloseController();
        Assert.Equal(1, context.Source.DisposeCalls);
        Assert.Equal(1, context.Source.StopCalls);
        Assert.Equal(0, context.Source.StartCalls);
    });

    [Fact]
    public void Restart_RestoresManualSelectionEvenWhenProgramIsNotRunning() => RunOnSta(() =>
    {
        using var first = new Context(null);
        first.Panel.AddProgram(@"C:\Games\first.exe");
        first.Panel.AddProgram(@"C:\Games\chosen.exe");
        var selectedId = first.Panel.SelectedProgram!.Id;
        Assert.Equal(-1, first.Source.LastPinned!.ProcessId);
        first.CloseController();
        // Simulate the saved enabled state without opening a real monitor HWND.
        var settings = new PerformanceMonitorSettingsStore(Path.Combine(first.DirectoryPath, "monitor.json"));
        settings.Save(settings.Load() with { Enabled = true });

        using var restarted = new Context(null, first.DirectoryPath);

        Assert.True(restarted.Panel.IsEnabled);
        Assert.True(restarted.Panel.IsManual);
        Assert.Equal(selectedId, restarted.Panel.SelectedProgram!.Id);
        Assert.Equal(@"C:\Games\chosen.exe", restarted.Panel.SelectedProgram.ExecutablePath);
        restarted.Panel.BackgroundOpacity = 0.75;
        Assert.False(settings.Load().IsAutomatic);
        Assert.Equal(selectedId, settings.Load().SelectedProgramId);
        Assert.Equal(0, restarted.Source.StartCalls);
        Assert.Equal(0, restarted.Resolver.Calls);
    });

    [Fact]
    public void Restart_RestoresAutomaticModeAndSelectionAfterSwitchingBackFromManual() => RunOnSta(() =>
    {
        using var first = new Context(null);
        first.Panel.AddProgram(@"C:\Games\first.exe");
        first.Panel.AddProgram(@"C:\Games\chosen.exe");
        var selectedId = first.Panel.SelectedProgram!.Id;
        first.Panel.IsAutomatic = true;
        first.CloseController();

        using var restarted = new Context(null, first.DirectoryPath);

        Assert.True(restarted.Panel.IsAutomatic);
        Assert.Equal(selectedId, restarted.Panel.SelectedProgram!.Id);
        Assert.Equal(0, restarted.Source.StartCalls);
    });

    [Fact]
    public void RemovingManualSelection_PersistsRemainingSelectionWithoutChangingMode() => RunOnSta(() =>
    {
        using var first = new Context(null);
        first.Panel.AddProgram(@"C:\Games\first.exe");
        first.Panel.AddProgram(@"C:\Games\remove.exe");
        first.Panel.RemoveSelectedProgram();
        first.CloseController();

        using var restarted = new Context(null, first.DirectoryPath);

        Assert.True(restarted.Panel.IsManual);
        Assert.Equal(@"C:\Games\first.exe", restarted.Panel.SelectedProgram!.ExecutablePath);
        restarted.Panel.RemoveSelectedProgram();
        Assert.True(restarted.Panel.IsManual);
        Assert.Null(restarted.Panel.SelectedProgram);
        Assert.Null(new PerformanceMonitorSettingsStore(Path.Combine(first.DirectoryPath, "monitor.json")).Load().SelectedProgramId);
        Assert.Equal(-1, restarted.Source.LastPinned!.ProcessId);
    });

    private static void RunOnSta(Action assertion)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { assertion(); }
            catch (Exception exception) { failure = exception; }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(10)), "Fake monitor-controller test did not complete.");
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private sealed class Context : IDisposable
    {
        private readonly PerformanceMonitorController controller;
        private bool closed;
        public string DirectoryPath { get; }
        public PerformanceMonitorPanelViewModel Panel { get; } = new();
        public FakeSource Source { get; } = new();
        public FakeResolver Resolver { get; }
        public MonitorProgramStore Records { get; }

        public Context(ProcessTarget? result, string? directoryPath = null)
        {
            DirectoryPath = directoryPath ?? Path.Combine(Path.GetTempPath(), "RevoHelper.MonitorController.Tests." + Guid.NewGuid().ToString("N"));
            Resolver = new(result);
            Records = new(Path.Combine(DirectoryPath, "monitor-programs.json"));
            controller = new(Path.Combine(DirectoryPath, "monitor.json"), Panel, Source, Resolver, new NoWindowEnumerationTargets());
        }

        public void CloseController()
        {
            if (closed) return;
            controller.CloseWindows();
            controller.DisposeAsync().AsTask().GetAwaiter().GetResult();
            closed = true;
        }

        public void Dispose()
        {
            CloseController();
            if (Directory.Exists(DirectoryPath)) Directory.Delete(DirectoryPath, recursive: true);
        }
    }

    private sealed class FakeSource : IFrameRateSource
    {
        public ProcessTarget? LastPinned { get; private set; }
        public int StartCalls { get; private set; }
        public int StopCalls { get; private set; }
        public int DisposeCalls { get; private set; }
        public Task StartAsync(CancellationToken cancellationToken = default) { StartCalls++; return Task.CompletedTask; }
        public Task StopAsync(CancellationToken cancellationToken = default) { StopCalls++; return Task.CompletedTask; }
        public void SetPinnedTarget(ProcessTarget? target) => LastPinned = target;
        public FrameRateSnapshot GetSnapshot() => new(LastPinned, null, DateTimeOffset.UtcNow, FrameRateStatus.Stopped);
        public async ValueTask DisposeAsync() { DisposeCalls++; await StopAsync(); }
    }

    private sealed class FakeResolver(ProcessTarget? result) : IManualFrameTargetResolver
    {
        public ProcessTarget? Result { get; } = result;
        public int Calls => RequestedPaths.Count;
        public List<string> RequestedPaths { get; } = [];
        public ProcessTarget? FindRunning(string executablePath) { RequestedPaths.Add(executablePath); return Result; }
    }

    private sealed class NoWindowEnumerationTargets : IProcessTargetProvider
    {
        public ProcessTarget? GetForegroundTarget() => throw new InvalidOperationException("Controller must let the FPS source follow foreground directly.");
        public bool IsAlive(ProcessTarget target) => target.ProcessId > 0;
        public IReadOnlyList<ProcessTarget> GetWindowTargets() => throw new InvalidOperationException("Manual sources must never enumerate unrelated windows.");
    }
}
