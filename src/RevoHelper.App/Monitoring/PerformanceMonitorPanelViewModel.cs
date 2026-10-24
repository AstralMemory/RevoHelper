using System.Collections.ObjectModel;
using System.ComponentModel;
using RevoHelper.App.Settings;
using RevoHelper.App.Presentation;
using RevoHelper.Core.Monitoring;

namespace RevoHelper.App.Monitoring;

public sealed class PerformanceMonitorPanelViewModel : INotifyPropertyChanged
{
    private PerformanceMonitorSettings preferences = new();
    private bool automatic = true;
    private MonitorProgramEntry? selectedProgram;
    private string statusText = "监控已关闭。";
    private bool replacingPrograms;

    public event PropertyChangedEventHandler? PropertyChanged;
    public event Action<PerformanceMonitorSettings>? PreferencesChanged;
    public event Action<bool>? SourceModeChanged;
    public event Action<MonitorProgramEntry?>? ProgramSelectionChanged;
    public event Action<string>? ProgramAddRequested;
    public event Action<string>? ProgramRemoveRequested;

    public IReadOnlyList<string> SizeOptions { get; } = ["最小", "小", "中", "大", "最大"];
    public ObservableCollection<MonitorProgramEntry> Programs { get; } = [];
    public bool HasPrograms => Programs.Count > 0;
    public bool HasSelectedProgram => selectedProgram is not null;
    public PerformanceMonitorSettings Preferences => preferences;
    public bool IsEnabled { get => preferences.Enabled; set { if (value != IsEnabled) Change(preferences with { Enabled = value }); } }
    public bool AlwaysOnTop { get => preferences.AlwaysOnTop; set { if (value != AlwaysOnTop) Change(preferences with { AlwaysOnTop = value }); } }
    public int SelectedSizePreset { get => preferences.SizePreset; set { if (value != SelectedSizePreset && value >= 0) Change(preferences with { SizePreset = value }); } }
    public double BackgroundOpacity { get => preferences.BackgroundOpacity; set { if (value != BackgroundOpacity) Change(preferences with { BackgroundOpacity = value }); } }
    public string BackgroundOpacityText => $"{preferences.BackgroundOpacity:P0}";
    public bool IsPositionLocked { get => preferences.IsPositionLocked; set { if (value != IsPositionLocked) Change(preferences with { IsPositionLocked = value }); } }
    public string PositionLockText => UiLanguageService.T(IsPositionLocked ? "🔒 已锁定" : "🔓 可拖动");
    public bool IsTopLeft => preferences.PositionPreset == MonitorPositionPreset.TopLeft;
    public bool IsTopCenter => preferences.PositionPreset == MonitorPositionPreset.TopCenter;
    public bool IsTopRight => preferences.PositionPreset == MonitorPositionPreset.TopRight;
    public bool IsBottomLeft => preferences.PositionPreset == MonitorPositionPreset.BottomLeft;
    public bool IsBottomRight => preferences.PositionPreset == MonitorPositionPreset.BottomRight;
    public bool IsBottomCenter => preferences.PositionPreset == MonitorPositionPreset.BottomCenter;
    public bool IsAutomatic
    {
        get => automatic;
        set
        {
            if (automatic == value) return;
            automatic = value; Notify(nameof(IsAutomatic)); Notify(nameof(IsManual));
            SourceModeChanged?.Invoke(value);
        }
    }
    public bool IsManual { get => !IsAutomatic; set { if (value) IsAutomatic = false; } }
    public MonitorProgramEntry? SelectedProgram
    {
        get => selectedProgram;
        set
        {
            if (replacingPrograms) return;
            if (selectedProgram?.Id == value?.Id) return;
            selectedProgram = value; Notify(nameof(SelectedProgram)); Notify(nameof(HasSelectedProgram));
            ProgramSelectionChanged?.Invoke(value);
        }
    }
    public string StatusText { get => UiLanguageService.T(statusText); private set { if (statusText == value) return; statusText = value; Notify(nameof(StatusText)); } }

    public void RefreshLanguage()
    {
        Notify(nameof(StatusText));
        Notify(nameof(PositionLockText));
    }

    public void SelectPosition(MonitorPositionPreset preset) => Change(preferences with { PositionPreset = preset });
    public void AddProgram(string executablePath) => ProgramAddRequested?.Invoke(executablePath);
    public void RemoveSelectedProgram() { if (selectedProgram is { } program) ProgramRemoveRequested?.Invoke(program.Id); }
    public void ReportStatus(string message) => StatusText = message;

    public void ApplyPreferences(PerformanceMonitorSettings value)
    {
        preferences = value.Normalize();
        foreach (var property in new[] { nameof(IsEnabled), nameof(AlwaysOnTop), nameof(SelectedSizePreset),
            nameof(BackgroundOpacity), nameof(BackgroundOpacityText), nameof(IsPositionLocked), nameof(PositionLockText),
            nameof(IsTopLeft), nameof(IsTopCenter), nameof(IsTopRight), nameof(IsBottomLeft), nameof(IsBottomRight),
            nameof(IsBottomCenter) }) Notify(property);
    }

    public void ReplacePrograms(IEnumerable<MonitorProgramEntry> entries, string? selectId = null)
    {
        var selection = selectId ?? selectedProgram?.Id;
        replacingPrograms = true;
        try
        {
            Programs.Clear(); foreach (var entry in entries) Programs.Add(entry);
            selectedProgram = Programs.FirstOrDefault(entry => entry.Id == selection) ?? Programs.FirstOrDefault();
        }
        finally { replacingPrograms = false; }
        Notify(nameof(SelectedProgram)); Notify(nameof(HasPrograms)); Notify(nameof(HasSelectedProgram));
    }

    public void ApplyFrameRateStatus(FrameRateSnapshot frame)
    {
        if (frame.Status == FrameRateStatus.Stopped) { StatusText = "监控已关闭。"; return; }
        if (!automatic && selectedProgram is null) { StatusText = "请先添加并选择一个程序；没有目标时显示 FPS —。"; return; }
        if (!automatic && frame.Status == FrameRateStatus.TargetExited)
        {
            StatusText = $"未运行：{selectedProgram!.DisplayName}\n启动后会自动匹配该程序。";
            return;
        }
        var target = frame.Target is { ProcessId: > 0 } process ? $"当前：{process.ProcessName}\n" : "";
        StatusText = target + (frame.Status switch
        {
            FrameRateStatus.Starting => "正在启动帧率采集…",
            FrameRateStatus.Available => "显示程序提交的帧率；插帧后的帧数可能不同。",
            FrameRateStatus.Unavailable => frame.Detail ?? "帧率暂不可用，硬件读数不受影响。",
            FrameRateStatus.TargetExited => "目标程序已退出，等待读取新的前台程序。",
            _ => "等待有效帧数据；暂时无法测量时显示 FPS —。"
        });
    }

    private void Change(PerformanceMonitorSettings value) { ApplyPreferences(value); PreferencesChanged?.Invoke(preferences); }
    private void Notify(string property) => PropertyChanged?.Invoke(this, new(property));
}
