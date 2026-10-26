using System.IO;
using System.Windows;
using System.Windows.Threading;
using RevoHelper.App.Settings;
using RevoHelper.Core.Monitoring;
using RevoHelper.Core.Telemetry;
using RevoHelper.Hardware.Windows.Monitoring;

namespace RevoHelper.App.Monitoring;

public sealed class PerformanceMonitorController : IAsyncDisposable
{
    private readonly PerformanceMonitorSettingsStore store;
    private readonly MonitorProgramStore programs;
    private readonly PerformanceMonitorPanelViewModel panel;
    private readonly IManualFrameTargetResolver manualTargets;
    private readonly IProcessTargetProvider targets;
    private readonly IFrameRateSource frameSource;
    private readonly DispatcherTimer timer;
    private readonly SemaphoreSlim lifecycle = new(1, 1);
    private PerformanceMonitorSettings preferences;
    private PerformanceMonitorWindow? monitor;
    private HardwareTelemetrySnapshot? hardware;
    private CpuTemperatureSample? oemCpuTemperature;
    private ProcessTarget? manualTarget;
    private DateTimeOffset nextManualSearch;
    private bool closing;

    public event Action? SettingsRequested;

    public PerformanceMonitorController(string path, PerformanceMonitorPanelViewModel panel,
        IFrameRateSource? frameSource = null, IManualFrameTargetResolver? manualTargets = null,
        IProcessTargetProvider? targets = null)
    {
        this.panel = panel;
        store = new(path);
        preferences = store.Load();
        programs = new(Path.Combine(Path.GetDirectoryName(path) ?? "", "monitor-programs.json"));
        this.targets = targets ?? new WindowsProcessTargetProvider();
        this.frameSource = frameSource ?? new WindowsFrameRateSource(this.targets);
        this.manualTargets = manualTargets ?? new WindowsManualFrameTargetResolver();
        panel.ApplyPreferences(preferences);
        panel.ReplacePrograms(programs.Load(), preferences.SelectedProgramId);
        preferences = panel.Preferences;
        panel.PreferencesChanged += OnPreferencesChanged;
        panel.SourceModeChanged += OnSourceModeChanged;
        panel.ProgramSelectionChanged += OnProgramSelectionChanged;
        panel.ProgramAddRequested += AddProgram;
        panel.ProgramRemoveRequested += RemoveProgram;
        timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(500) };
        timer.Tick += (_, _) => Refresh();
    }

    public void Initialize() { if (preferences.Enabled) _ = SetEnabledAsync(true); }
    public void Publish(HardwareTelemetrySnapshot snapshot) => Volatile.Write(ref hardware, snapshot);
    public void PublishOemCpuTemperature(double? temperature)
    {
        if (temperature is double value && double.IsFinite(value) && value > 0)
            Volatile.Write(ref oemCpuTemperature, new(value, DateTimeOffset.UtcNow));
    }

    private void OnPreferencesChanged(PerformanceMonitorSettings requested)
    {
        if (closing) return;
        var before = preferences;
        Change(requested);
        if (before.SizePreset != preferences.SizePreset || before.PositionPreset != preferences.PositionPreset)
            monitor?.PlaceOnScreen(preferences);
        if (before.Enabled != preferences.Enabled) _ = SetEnabledAsync(preferences.Enabled);
    }

    private void Change(PerformanceMonitorSettings value)
    {
        preferences = value.Normalize();
        try { store.Save(preferences); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        { panel.ReportStatus("设置暂未保存：" + error.Message); }
        panel.ApplyPreferences(preferences);
        monitor?.ApplyPreferences(preferences);
    }

    private void OnSourceModeChanged(bool automatic)
    {
        Change(preferences with { IsAutomatic = automatic, SelectedProgramId = panel.SelectedProgram?.Id });
        if (automatic) { manualTarget = null; frameSource.SetPinnedTarget(null); }
        else { RememberSelection(); UpdateManualTarget(force: true); }
        Refresh();
    }

    private void OnProgramSelectionChanged(MonitorProgramEntry? selected)
    {
        Change(preferences with { SelectedProgramId = selected?.Id });
        if (panel.IsAutomatic) return;
        if (selected is not null) RememberSelection();
        UpdateManualTarget(force: true);
        Refresh();
    }

    private void RememberSelection()
    {
        if (panel.SelectedProgram is not { } selected) return;
        try
        {
            programs.MarkSelected(selected.Id, DateTimeOffset.UtcNow);
            panel.ReplacePrograms(programs.Load(), selected.Id);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        { panel.ReportStatus("程序记录暂未保存：" + error.Message); }
    }

    private void AddProgram(string executablePath)
    {
        try
        {
            var added = programs.Add(executablePath, DateTimeOffset.UtcNow);
            panel.ReplacePrograms(programs.Load(), added.Id);
            if (panel.IsAutomatic) panel.IsAutomatic = false;
            else
            {
                Change(preferences with { SelectedProgramId = added.Id });
                RememberSelection(); UpdateManualTarget(force: true); Refresh();
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        { panel.ReportStatus("未能添加程序：" + error.Message); }
    }

    private void RemoveProgram(string id)
    {
        try
        {
            programs.Remove(id);
            panel.ReplacePrograms(programs.Load());
            Change(preferences with { SelectedProgramId = panel.SelectedProgram?.Id });
            if (panel.IsManual) { UpdateManualTarget(force: true); Refresh(); }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException)
        { panel.ReportStatus("未能移除记录：" + error.Message); }
    }

    private void UpdateManualTarget(bool force = false)
    {
        if (panel.IsAutomatic) return;
        var now = DateTimeOffset.UtcNow;
        if (!force && now < nextManualSearch) return;
        nextManualSearch = now.AddSeconds(1);
        var selected = panel.SelectedProgram;
        if (selected is null)
        {
            manualTarget = new(-1, DateTimeOffset.MinValue, "未选择程序", "未选择程序");
        }
        else if (force || manualTarget is null || manualTarget.ProcessId <= 0 || !targets.IsAlive(manualTarget))
        {
            // Only explicitly added executable paths are resolved. Never build a list of unrelated windows.
            manualTarget = manualTargets.FindRunning(selected.ExecutablePath)
                ?? new(-1, DateTimeOffset.MinValue, Path.GetFileNameWithoutExtension(selected.ExecutablePath), selected.DisplayName);
        }
        frameSource.SetPinnedTarget(manualTarget);
    }

    private async Task SetEnabledAsync(bool enabled)
    {
        await lifecycle.WaitAsync();
        try
        {
            if (closing || preferences.Enabled != enabled) return;
            if (enabled)
            {
                if (monitor is null)
                {
                    monitor = new();
                    monitor.ApplyPreferences(preferences);
                    monitor.SetReadings(MonitorLineFormatter.Format(Volatile.Read(ref hardware), null));
                    monitor.SettingsRequested += () => SettingsRequested?.Invoke();
                    monitor.HideRequested += () => OnPreferencesChanged(preferences with { Enabled = false });
                    monitor.PositionSaved += (x, y) => Change(preferences with
                        { Left = x, Top = y, PositionPreset = MonitorPositionPreset.Custom });
                    var openedWindow = monitor;
                    monitor.Closed += (_, _) =>
                    {
                        if (!closing && ReferenceEquals(monitor, openedWindow))
                        {
                            monitor = null;
                            OnPreferencesChanged(preferences with { Enabled = false });
                        }
                    };
                    monitor.Show();
                    monitor.PlaceOnScreen(preferences);
                }
                UpdateManualTarget(force: true);
                timer.Start();
                await frameSource.StartAsync();
            }
            else
            {
                var closedWindow = monitor; monitor = null; closedWindow?.Close();
                await frameSource.StopAsync();
                timer.Stop();
            }
            Refresh();
        }
        catch (Exception error)
        { panel.ReportStatus("监控暂不可用：" + error.Message); }
        finally { lifecycle.Release(); }
    }

    private void Refresh()
    {
        if (closing) return;
        UpdateManualTarget();
        var fps = frameSource.GetSnapshot();
        var sample = Volatile.Read(ref hardware);
        if (sample is not null && DateTimeOffset.UtcNow - sample.CapturedAt > TimeSpan.FromSeconds(4)) sample = null;
        var temperature = Volatile.Read(ref oemCpuTemperature);
        var fallback = temperature is not null && DateTimeOffset.UtcNow - temperature.CapturedAt <= TimeSpan.FromSeconds(5)
            ? (double?)temperature.Value : null;
        monitor?.SetReadings(MonitorLineFormatter.Format(sample, fps.FramesPerSecond, fallback));
        panel.ApplyFrameRateStatus(fps);
    }

    public void CloseWindows()
    {
        closing = true; timer.Stop(); monitor?.Close(); monitor = null;
        panel.PreferencesChanged -= OnPreferencesChanged;
        panel.SourceModeChanged -= OnSourceModeChanged;
        panel.ProgramSelectionChanged -= OnProgramSelectionChanged;
        panel.ProgramAddRequested -= AddProgram;
        panel.ProgramRemoveRequested -= RemoveProgram;
    }
    public async ValueTask DisposeAsync()
    {
        await lifecycle.WaitAsync().ConfigureAwait(false);
        try { await frameSource.DisposeAsync().ConfigureAwait(false); }
        finally { lifecycle.Release(); }
    }

    private sealed record CpuTemperatureSample(double Value, DateTimeOffset CapturedAt);
}
