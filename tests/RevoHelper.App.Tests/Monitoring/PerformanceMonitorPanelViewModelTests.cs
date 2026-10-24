using RevoHelper.App.Monitoring;
using RevoHelper.App.Settings;
using RevoHelper.Core.Monitoring;

namespace RevoHelper.App.Tests.Monitoring;

public sealed class PerformanceMonitorPanelViewModelTests
{
    [Fact]
    public void Defaults_AreAutomaticSmallAndHaveFiveSizes()
    {
        var panel = new PerformanceMonitorPanelViewModel();
        Assert.True(panel.IsAutomatic);
        Assert.False(panel.IsManual);
        Assert.Equal(new[] { "最小", "小", "中", "大", "最大" }, panel.SizeOptions);
        Assert.Equal(1, panel.SelectedSizePreset);
        Assert.True(panel.IsTopCenter);
    }

    [Fact]
    public void ApplyingControllerSettings_DoesNotSendUserMutationEvents()
    {
        var panel = new PerformanceMonitorPanelViewModel();
        var events = 0;
        panel.PreferencesChanged += _ => events++;
        panel.ApplyPreferences(new(Enabled: true, SizePreset: 4, IsPositionLocked: true));
        Assert.True(panel.IsEnabled);
        Assert.Equal(4, panel.SelectedSizePreset);
        Assert.Contains("🔒", panel.PositionLockText);
        Assert.Equal(0, events);
    }

    [Fact]
    public void UserPreferences_SendOnlyRelevantCurrentValues()
    {
        var panel = new PerformanceMonitorPanelViewModel();
        PerformanceMonitorSettings? captured = null;
        panel.PreferencesChanged += value => captured = value;
        panel.SelectPosition(MonitorPositionPreset.BottomRight);
        Assert.Equal(MonitorPositionPreset.BottomRight, captured!.PositionPreset);
        Assert.True(panel.IsBottomRight);
        panel.IsPositionLocked = true;
        Assert.True(captured.IsPositionLocked);
        panel.SelectedSizePreset = 0;
        Assert.Equal(0, captured.SizePreset);
    }

    [Fact]
    public void BottomCenterSelection_NotifiesNewRadioAndClearsItOnAnotherSelection()
    {
        var panel = new PerformanceMonitorPanelViewModel();
        var notifications = new List<string?>();
        var mutations = 0;
        panel.PropertyChanged += (_, args) => notifications.Add(args.PropertyName);
        panel.PreferencesChanged += _ => mutations++;

        panel.SelectPosition(MonitorPositionPreset.BottomCenter);

        Assert.True(panel.IsBottomCenter);
        Assert.False(panel.IsTopCenter);
        Assert.Contains(nameof(panel.IsBottomCenter), notifications);
        Assert.Equal(1, mutations);
        notifications.Clear();
        panel.ApplyPreferences(new(PositionPreset: MonitorPositionPreset.TopCenter));
        Assert.False(panel.IsBottomCenter);
        Assert.True(panel.IsTopCenter);
        Assert.Contains(nameof(panel.IsBottomCenter), notifications);
        Assert.Equal(1, mutations);
    }

    [Fact]
    public void ProgramList_IsOnlyExplicitlySuppliedRecords_AndReloadIsNotSelection()
    {
        var panel = new PerformanceMonitorPanelViewModel();
        var selected = 0;
        panel.ProgramSelectionChanged += _ => selected++;
        var now = DateTimeOffset.UtcNow;
        panel.ReplacePrograms([new("one", @"C:\Games\one.exe", "one.exe", now), new("two", @"C:\Games\two.exe", "two.exe", now)]);
        Assert.Equal(2, panel.Programs.Count);
        Assert.Equal(0, selected);
        panel.SelectedProgram = panel.Programs[1];
        Assert.Equal(1, selected);
        panel.ReplacePrograms(panel.Programs.ToArray(), "two");
        Assert.Equal("two", panel.SelectedProgram!.Id);
        Assert.Equal(1, selected);
    }

    [Fact]
    public void ManualModeWithoutProgram_IsHonestAndCannotLeakAnAutomaticTarget()
    {
        var panel = new PerformanceMonitorPanelViewModel { IsAutomatic = false };
        panel.ApplyFrameRateStatus(new(new(7, DateTimeOffset.UtcNow, "unrelated", "private chat"), 90,
            DateTimeOffset.UtcNow, FrameRateStatus.Available));
        Assert.Contains("请先添加", panel.StatusText);
        Assert.DoesNotContain("unrelated", panel.StatusText);
        Assert.DoesNotContain("private chat", panel.StatusText);
    }

    [Fact]
    public void AutomaticStatus_UsesProcessName_NotDocumentOrChatTitle()
    {
        var panel = new PerformanceMonitorPanelViewModel();
        panel.ApplyFrameRateStatus(new(new(7, DateTimeOffset.UtcNow, "javaw", "private document title"), 90,
            DateTimeOffset.UtcNow, FrameRateStatus.Available));
        Assert.Contains("javaw", panel.StatusText);
        Assert.DoesNotContain("private document title", panel.StatusText);
    }

    [Fact]
    public void AddingAndRemoving_OnlyEmitExplicitProgramActions()
    {
        var panel = new PerformanceMonitorPanelViewModel();
        string? added = null; string? removed = null;
        panel.ProgramAddRequested += value => added = value;
        panel.ProgramRemoveRequested += value => removed = value;
        panel.AddProgram(@"C:\Games\example.exe");
        panel.ReplacePrograms([new("one", added!, "example.exe", DateTimeOffset.UtcNow)]);
        panel.RemoveSelectedProgram();
        Assert.Equal(@"C:\Games\example.exe", added);
        Assert.Equal("one", removed);
    }
}
