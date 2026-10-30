using System.Xml.Linq;

namespace RevoHelper.App.Tests.Presentation;

public sealed class ReleasePresentationTests
{
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

    [Fact]
    public void PerformanceReadback_IsOptionalDiagnosticDetails()
    {
        var details = Named(LoadWindow(), "PerformanceReadbackDetails");
        Assert.Equal("Expander", details.Name.LocalName);
        Assert.Equal("False", (string?)details.Attribute("IsExpanded"));
        Assert.Equal("状态详情", (string?)details.Attribute("Header"));
        Assert.Single(details.Descendants(), e => (string?)e.Attribute("Text") == "{Binding PerformanceReadbackText}");
        Assert.DoesNotContain(details.Descendants(), e => e.Name.LocalName is "Button" or "Slider" or "ComboBox" or "CheckBox");
    }

    [Fact]
    public void PerformanceValidation_OnlyShowsInvalidDraftsInRed()
    {
        var notice = Named(LoadWindow(), "PerformanceValidationNotice");
        Assert.Equal("Border", notice.Name.LocalName);
        Assert.Null(notice.Attribute("Visibility"));
        var style = notice.Elements().Single(e => e.Name.LocalName == "Border.Style").Elements().Single();
        Assert.Contains(style.Elements(), e => (string?)e.Attribute("Property") == "Visibility" && (string?)e.Attribute("Value") == "Visible");
        var valid = style.Descendants().Single(e => e.Name.LocalName == "DataTrigger");
        Assert.Equal("{Binding IsPerformanceDraftValid}", (string?)valid.Attribute("Binding"));
        Assert.Equal("True", (string?)valid.Attribute("Value"));
        Assert.Contains(valid.Elements(), e => (string?)e.Attribute("Property") == "Visibility" && (string?)e.Attribute("Value") == "Collapsed");
        var message = notice.Elements().Single(e => e.Name.LocalName == "TextBlock");
        Assert.Equal("{Binding PerformanceValidationText}", (string?)message.Attribute("Text"));
        Assert.Equal("#D9363E", (string?)message.Attribute("Foreground"));
    }

    [Theory]
    [InlineData("OemPresetPowerLimitWarning", "可能无法调节超过机械革命官方预设的功耗 PL 值。例如平衡模式默认最高 85 W，该限制无法被突破，但可以向下调整。若想尝试自定义调整，请使用狂暴模式，或在自定义模式方案中细调。")]
    [InlineData("ProcessorFrequencyDelayWarning", "仅本次会话持有；退出自动恢复。生效可能延迟约 5–10 秒")]
    public void ImportantWarnings_KeepExactRedCopyOutsideDetails(string name, string expected)
    {
        var warning = Named(LoadWindow(), name);
        Assert.Equal(expected, (string?)warning.Attribute("Text"));
        Assert.Equal("#D9363E", (string?)warning.Attribute("Foreground"));
        Assert.Null(warning.Attribute("Visibility"));
        Assert.DoesNotContain(warning.Ancestors(), e => e.Name.LocalName == "Expander");
        if (name == "OemPresetPowerLimitWarning")
            Assert.Contains(warning.Ancestors(), e => (string?)e.Attribute(Xaml + "Name") == "CustomPowerLimitEditorPanel");
    }

    [Fact]
    public void AdvancedPerformance_UsesOneCompactReasonAndKeepsDynamicFeedbackVisible()
    {
        var panel = Named(LoadWindow(), "CustomAdvancedPerformancePanel");
        Assert.DoesNotContain(panel.Descendants(), e => (string?)e.Attribute("Text") == "{Binding AdvancedPerformanceDescription}");
        Assert.Contains(panel.Descendants(), e => (string?)e.Attribute("ToolTip") == "{Binding AdvancedPerformanceDescription}");
        var reason = Named(panel, "PresetAdvancedPerformanceLockNotice");
        Assert.Equal("仅在自定义模式可调整。", (string?)reason.Descendants().Single(e => e.Name.LocalName == "TextBlock").Attribute("Text"));
        Assert.Equal("0,4", (string?)reason.Attribute("Padding"));
        var status = panel.Descendants().Single(e => (string?)e.Attribute("Text") == "{Binding CustomPerformanceStatusText}");
        Assert.DoesNotContain(status.Ancestors(), e => e.Name.LocalName is "Expander" or "ToolTip");
        Assert.DoesNotContain(status.AncestorsAndSelf().TakeWhile(e => e != panel), e => e.Attribute("Visibility") is not null || e.Elements().Any(c => c.Name.LocalName.EndsWith(".Style", StringComparison.Ordinal)));
        Assert.Equal("{Binding CanEditAdvancedPerformance}", (string?)Named(panel, "CustomCpuAdvancedCard").Attribute("IsEnabled"));
        Assert.Equal("{Binding CanEditAdvancedPerformance}", (string?)Named(panel, "CustomGpuAdvancedCard").Attribute("IsEnabled"));
    }

    [Fact]
    public void DrawerCopy_DeduplicatesAutoApplyAndMovesProtocolToLocalHints()
    {
        var window = LoadWindow();
        var performance = Named(window, "PerformanceDrawerContent");
        Assert.Single(performance.Descendants(), e => (string?)e.Attribute("Text") == "调整后自动应用");
        Assert.DoesNotContain(performance.Descendants(), e => ((string?)e.Attribute("Text") ?? "").Contains("连续拖动", StringComparison.Ordinal));
        Assert.Equal("分区即时编辑", (string?)Named(window, "LightingBuildLabel").Attribute("Text"));
        var sleep = Named(window, "LightingSleepTimerSelector");
        Assert.Contains("秒值", (string?)sleep.Attribute("ToolTip"), StringComparison.Ordinal);
        Assert.DoesNotContain(window.Descendants(), e => ((string?)e.Attribute("Text") ?? "").StartsWith("官方协议与状态回读", StringComparison.Ordinal));
        Assert.DoesNotContain(performance.Descendants(), e =>
            ((string?)e.Attribute("Text") ?? "").Contains("充电上限", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("CustomPerformanceStatusText")]
    [InlineData("ProcessorFrequencyLimitStatusText")]
    [InlineData("LightingStatusText")]
    [InlineData("LiquidCoolingStatusText")]
    [InlineData("BatteryStatusText")]
    [InlineData("FanStatusText")]
    [InlineData("GpuOutputStatusText")]
    [InlineData("DisplayColorStatusText")]
    [InlineData("ConflictServiceOperationStatusText")]
    public void OperationFeedback_IsNotHiddenInDetails(string binding)
    {
        var status = LoadWindow().Descendants().Single(e => (string?)e.Attribute("Text") == "{Binding " + binding + "}");
        Assert.Null(status.Attribute("Visibility"));
        Assert.DoesNotContain(status.Ancestors(), e => e.Name.LocalName is "Expander" or "ToolTip");
    }

    [Fact]
    public void ReleaseBranding_UsesUndatedVersionAndHidesTheDuplicateHomeFooter()
    {
        var window = LoadWindow();
        Assert.Equal("{Binding ReleaseBadgeText}", (string?)Named(window, "ReleaseStatusText").Attribute("Text"));
        Assert.Equal("{Binding AppVersionText}", (string?)Named(window, "SettingsVersionFooter").Attribute("Text"));
        Assert.Equal("{Binding AppVersionText}", (string?)Named(window, "HomeVersionFooter").Attribute("Text"));
        Assert.Equal("Collapsed", (string?)Named(window, "HomeVersionFooter").Attribute("Visibility"));
        var source = File.ReadAllText(Locate("src", "RevoHelper.App", "Presentation", "MainWindowViewModel.cs"));
        Assert.Contains("public string ReleaseVersionText => \"V1.0.2\";", source, StringComparison.Ordinal);
        Assert.Contains("public string ReleaseBadgeText => \"V1.0.2\";", source, StringComparison.Ordinal);
        Assert.Contains("public string AppVersionText => $\"RevoHelper {ReleaseVersionText} · {UiCopy.OpenSourceAndFree}\";", source, StringComparison.Ordinal);
        foreach (var old in new[] { "BETA · V1", "Beta v1 ·", "Beta v0.0.1", "界面预览 v23" })
        {
            Assert.DoesNotContain(old, window.ToString(), StringComparison.Ordinal);
            Assert.DoesNotContain(old, source, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void HomeVersionBadge_UsesACompactBackgroundWithoutShrinkingTheVersionText()
    {
        var window = LoadWindow();
        var badge = Named(window, "ReleaseStatusBadge");
        Assert.Equal("72", (string?)badge.Attribute("MinWidth"));
        Assert.Equal("22", (string?)badge.Attribute("Height"));
        Assert.Equal("8,2", (string?)badge.Attribute("Padding"));
        Assert.Equal("{presentation:UiFont 10}", (string?)Named(window, "ReleaseStatusText").Attribute("FontSize"));
        Assert.Equal("{Binding ReleaseBadgeText}", (string?)Named(window, "ReleaseStatusText").Attribute("Text"));
    }

    [Fact]
    public void HomeSwitchDelayNotice_IsAboveReadOnlySensorStatusAndOnlyAppearsWhileSwitching()
    {
        var window = LoadWindow();
        var notice = Named(window, "PlatformSwitchDelayNotice");
        var sensorStatus = Named(window, "HomeTelemetryStatusText");
        Assert.Same(sensorStatus.Parent, notice.Parent);
        var siblings = notice.Parent!.Elements().ToList();
        Assert.True(siblings.IndexOf(notice) < siblings.IndexOf(sensorStatus));
        Assert.Equal("可能有1-2秒的切换延迟", (string?)notice.Attribute("Text"));
        Assert.Equal("#D9363E", (string?)notice.Attribute("Foreground"));
        Assert.Equal("16", (string?)notice.Attribute("Height"));
        var style = notice.Elements().Single(e => e.Name.LocalName == "TextBlock.Style").Elements().Single();
        Assert.Contains(style.Elements(), e => (string?)e.Attribute("Property") == "Visibility" && (string?)e.Attribute("Value") == "Collapsed");
        var pending = style.Descendants().Single(e => e.Name.LocalName == "DataTrigger");
        Assert.Equal("{Binding IsPlatformSwitchPending}", (string?)pending.Attribute("Binding"));
        Assert.Equal("True", (string?)pending.Attribute("Value"));
        Assert.Contains(pending.Elements(), e => (string?)e.Attribute("Property") == "Visibility" && (string?)e.Attribute("Value") == "Visible");
        Assert.Equal("Profile switching may take 1–2 seconds.",
            RevoHelper.App.Presentation.UiTextCatalog.Translate("可能有1-2秒的切换延迟", "en-US"));
    }

    [Fact]
    public void GpuControls_OmitTheExtraPowerExplanationAndDriverCeilingCard()
    {
        var window = LoadWindow();
        Assert.DoesNotContain(window.Descendants(), e => (string?)e.Attribute(Xaml + "Name") == "GpuPowerFeedbackCard");
        Assert.DoesNotContain(window.Descendants(), e => (string?)e.Attribute("Text") == "{Binding GpuPowerStatusText}");
        Assert.DoesNotContain(window.Descendants(), e => ((string?)e.Attribute("Text") ?? "").Contains("松开滑块后应用", StringComparison.Ordinal));
        Assert.Contains(window.Descendants(), e => (string?)e.Attribute("Text") == "{Binding CustomPerformanceStatusText}");
    }

    [Fact]
    public void ReleaseMetadata_Uses102Stable()
    {
        var project = XDocument.Load(Locate("src", "RevoHelper.App", "RevoHelper.App.csproj"));
        Assert.Equal("1.0.2", project.Descendants("Version").Single().Value);
        Assert.Equal("1.0.2.0", project.Descendants("AssemblyVersion").Single().Value);
        Assert.Equal("1.0.2.5", project.Descendants("FileVersion").Single().Value);
        Assert.Equal("1.0.2+20261030", project.Descendants("InformationalVersion").Single().Value);
        Assert.Empty(project.Descendants("Content"));
    }

    [Fact]
    public void SharedBuildMetadata_UsesTheSameReleaseForCurrentAssemblies()
    {
        var defaults = XDocument.Load(Locate("Directory.Build.props"));
        Assert.Equal("1.0.2", defaults.Descendants("Version").Single().Value);
        Assert.Equal("1.0.2+20261030", defaults.Descendants("InformationalVersion").Single().Value);
        Assert.Equal("Astral Memory", defaults.Descendants("Authors").Single().Value);
    }

    [Fact]
    public void SecondaryDrawers_KeepShortUserCopyInsteadOfImplementationDescriptions()
    {
        var window = LoadWindow();
        foreach (var verbose in new[] { "控制器转接", "OEM 状态接口", "刷新率由 Windows 当前显示路径实时枚举", "系统启动项会回读确认", "Mini LED 局部调光技术的运用" })
            Assert.DoesNotContain(window.Descendants(), e => ((string?)e.Attribute("Text") ?? "").Contains(verbose, StringComparison.Ordinal));
        Assert.Equal("{Binding FanCurveModeHeading}", (string?)Named(window, "FanCurveModeHeading").Attribute("Text"));
        Assert.Contains("SupportsMiniLedLocalDimming", Named(window, "DisplayFeatureTab").ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void GpuRestartWarning_PreservesSaveWorkAndManualRestartProminently()
    {
        var warning = Named(LoadWindow(), "GpuOutputRestartWarning");
        Assert.Equal("切换显卡输出后需重启电脑。请先保存工作；RevoHelper 不会自动重启。", (string?)warning.Attribute("Text"));
        Assert.Equal("#D9363E", (string?)warning.Attribute("Foreground"));
        Assert.Null(warning.Attribute("Visibility"));
        Assert.DoesNotContain(warning.Ancestors(), e => e.Name.LocalName is "Expander" or "ToolTip");
    }

    [Fact]
    public void HomeFooter_LongStatusRemainsVisibleAndFullTextIsAvailableOnHover()
    {
        var window = LoadWindow();
        foreach (var item in new[] { ("HomeStatusText", "{Binding StatusMessage}"), ("HomeVersionFooter", "{Binding AppVersionText}") })
        {
            var text = Named(window, item.Item1);
            Assert.Equal(item.Item2, (string?)text.Attribute("Text"));
            Assert.Equal(item.Item2, (string?)text.Attribute("ToolTip"));
            Assert.Equal("CharacterEllipsis", (string?)text.Attribute("TextTrimming"));
            Assert.Equal("0,0,6,0", (string?)text.Attribute("Margin"));
        }
    }

    private static XDocument LoadWindow()
    {
        var window = XDocument.Load(Locate("src", "RevoHelper.App", "MainWindow.xaml"));
        // Existing safety-copy checks inspect the original display text/binding.
        // Localization wraps these values without changing their control semantics.
        foreach (var attribute in window.Descendants().Attributes())
        {
            var value = attribute.Value;
            const string bindingPrefix = "{presentation:UiText Value=";
            const string textPrefix = "{presentation:UiText '";
            if (value.StartsWith(bindingPrefix, StringComparison.Ordinal))
                attribute.Value = value[bindingPrefix.Length..^1];
            else if (value.StartsWith(textPrefix, StringComparison.Ordinal))
                attribute.Value = value[textPrefix.Length..^2];
        }
        return window;
    }
    private static XElement Named(XContainer container, string name) => container.Descendants().Single(e => (string?)e.Attribute(Xaml + "Name") == name);

    private static string Locate(params string[] path)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine([directory.FullName, .. path]);
            if (File.Exists(candidate)) return candidate;
        }
        throw new FileNotFoundException(Path.Combine(path));
    }
}
