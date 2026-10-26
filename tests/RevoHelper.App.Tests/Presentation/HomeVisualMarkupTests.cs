using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using RevoHelper.App.Presentation;

namespace RevoHelper.App.Tests.Presentation;

public sealed class HomeVisualMarkupTests
{
    [Fact]
    public void September28_GpuSuperPerformanceControlsAndSliderStepsMatchTheContract()
    {
        var document = LoadMainWindowXaml();
        Assert.Equal("{Binding CanEditGpuPower}", (string?)FindNamedElement(document, "GpuManualPowerPanel").Attribute("IsEnabled"));
        Assert.Equal("{Binding CanEditGpuClockOffsets}", (string?)FindNamedElement(document, "GpuClockOffsetsPanel").Attribute("IsEnabled"));
        Assert.Contains("开启后可调整 TGP、Dynamic Boost 与频率偏移", FindNamedElement(document, "GpuManualPowerPanel").Parent!.ToString(), StringComparison.Ordinal);
        foreach (var name in new[] { "PerformancePl1Slider", "PerformancePl2Slider", "CustomCpuPl4Slider", "GpuTgpSlider", "GpuDynamicBoostSlider" })
        {
            var slider = FindNamedElement(document, name);
            Assert.Equal("5", (string?)slider.Attribute("SmallChange"));
            Assert.Equal("5", (string?)slider.Attribute("LargeChange"));
        }
        foreach (var name in new[] { "GpuCoreOffsetSlider", "GpuMemoryOffsetSlider" })
        {
            var slider = FindNamedElement(document, name);
            Assert.Equal("20", (string?)slider.Attribute("SmallChange"));
            Assert.Equal("20", (string?)slider.Attribute("LargeChange"));
            Assert.Equal("20", (string?)slider.Attribute("TickFrequency"));
            Assert.NotNull(slider.Attribute("Tag"));
        }
        var source = File.ReadAllText(LocateRepositoryFile("src", "RevoHelper.App", "MainWindow.xaml.cs"));
        Assert.DoesNotContain("ViewModel.ApplyCustomPerformanceAsync", source);
        Assert.Contains("ViewModel.ApplyCustomPerformanceSettingAsync", source);
    }

    [Fact]
    public void GpuOffsetEditorsUseDedicatedCommandsAndRedGuidanceWithShortDynamicRules()
    {
        var document = LoadMainWindowXaml();
        foreach (var name in new[] { "GpuCoreOffsetSlider", "GpuMemoryOffsetSlider" })
        {
            var slider = FindNamedElement(document, name);
            Assert.Equal("GpuClockOffsetSlider_ValueChanged", (string?)slider.Attribute("ValueChanged"));
            Assert.Contains("{Binding", (string?)slider.Attribute("Maximum"));
        }
        foreach (var name in new[] { "GpuCoreOffsetWarning", "GpuMemoryOffsetWarning" })
            Assert.Equal("#D9363E", (string?)FindNamedElement(document, name).Attribute("Foreground"));
        var rules = (string?)FindNamedElement(document, "DynamicPowerRulesText").Attribute("Text");
        Assert.InRange(rules!.Length, 1, 30);
    }

    [Fact]
    public void MoreProfilesDrawerFocusesOnCrossModeDynamicSavingWithoutDuplicateCustomProfiles()
    {
        var document = LoadMainWindowXaml();
        var drawer = FindNamedElement(document, "MoreProfilesDrawerContent");
        var markup = drawer.ToString();

        Assert.DoesNotContain("自定义档案", markup, StringComparison.Ordinal);
        Assert.DoesNotContain("MoreCustomProfile_Click", markup, StringComparison.Ordinal);
        Assert.Contains("适用于所有性能档位", markup, StringComparison.Ordinal);
        Assert.Contains("最近 10 分钟", markup, StringComparison.Ordinal);
        Assert.Contains("{Binding Experiments.DynamicProgress}", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void LightingCommands_AllUseZoneScopedVersionsAndCaptureMasterPowerBeforeWaiting()
    {
        var source = File.ReadAllText(LocateRepositoryFile("src", "RevoHelper.App", "MainWindow.xaml.cs"));
        Assert.DoesNotContain("Volatile.Read(ref lightingAutoApplyVersion)", source, StringComparison.Ordinal);
        Assert.Contains("lightingMutationVersions.IsCurrent(zone, version)", source, StringComparison.Ordinal);
        Assert.Contains("ViewModel.CaptureKeyboardLightingPower(enabled)", source, StringComparison.Ordinal);
        var marker = FindNamedElement(LoadMainWindowXaml(), "LightingBuildLabel");
        Assert.Equal("分区即时编辑", (string?)marker.Attribute("Text"));
    }

    [Fact]
    public void PerformanceModeStyles_ShowAnExplicitDisabledState()
    {
        var document = LoadMainWindowXaml();

        foreach (var key in new[] { "ProfileRadioStyle", "StrategyRadioStyle" })
        {
            var style = document.Descendants().Single(element =>
                element.Name.LocalName == "Style" &&
                (string?)element.Attribute(XamlNamespace + "Key") == key);
            var disabledTrigger = style.Descendants().SingleOrDefault(element =>
                element.Name.LocalName == "Trigger" &&
                (string?)element.Attribute("Property") == "IsEnabled" &&
                (string?)element.Attribute("Value") == "False");

            Assert.NotNull(disabledTrigger);
            Assert.Contains(disabledTrigger!.Descendants(), element =>
                element.Name.LocalName == "Setter" &&
                (string?)element.Attribute("Property") == "Opacity");
            Assert.Contains(disabledTrigger.Descendants(), element =>
                element.Name.LocalName == "Setter" &&
                (string?)element.Attribute("Property") == "Cursor" &&
                (string?)element.Attribute("Value") == "Arrow");
        }
    }

    [Fact]
    public void GpuOutputDrawer_ProvidesThreeCapabilityGatedModesAndExplicitApply()
    {
        var document = LoadMainWindowXaml();

        Assert.NotNull(document.Descendants().SingleOrDefault(element =>
            (string?)element.Attribute(XName.Get("Name", "http://schemas.microsoft.com/winfx/2006/xaml")) == "GpuOutputDrawerContent"));
        Assert.Contains("SupportsHybridGpuMode", document.ToString(), StringComparison.Ordinal);
        Assert.Contains("SupportsDiscreteGpuMode", document.ToString(), StringComparison.Ordinal);
        Assert.Contains("SupportsIntegratedGpuMode", document.ToString(), StringComparison.Ordinal);
        Assert.Contains("ApplyGpuOutputMode_Click", document.ToString(), StringComparison.Ordinal);
        Assert.Contains("不会自动重启", document.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void DisplayTabsAreCapabilityGatedInsteadOfShowingUnsupportedMiniLedControls()
    {
        var document = LoadMainWindowXaml();
        var tabs = FindNamedElement(document, "DisplaySettingsTabs");
        var colorTab = FindNamedElement(tabs, "DisplayColorCalibrationTab");
        var featureTab = FindNamedElement(tabs, "DisplayFeatureTab");
        var refreshTab = FindNamedElement(tabs, "DisplayRefreshRateTab");

        Assert.Equal("{Binding DisplaySettingsTabCount}", (string?)tabs.Attribute("Columns"));
        Assert.Contains("SupportsDisplayColorManagement", colorTab.ToString(), StringComparison.Ordinal);
        Assert.Contains("SupportsMiniLedLocalDimming", featureTab.ToString(), StringComparison.Ordinal);
        Assert.Contains("SupportsDisplayRefreshRate", refreshTab.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void OptionalSystemAndPowerRowsUseRuntimeCapabilityVisibility()
    {
        var document = LoadMainWindowXaml();
        var expectedBindings = new Dictionary<string, string>
        {
            ["TouchpadControlCard"] = "SupportsTouchpadToggle",
            ["WebcamControlCard"] = "SupportsWebcamToggle",
            ["CopilotControlRow"] = "SupportsCopilotKeyToggle",
            ["AcAutoBootControlRow"] = "SupportsAcAutoBoot"
        };

        foreach (var (name, binding) in expectedBindings)
            Assert.Contains(binding, FindNamedElement(document, name).ToString(), StringComparison.Ordinal);
    }

    private static readonly XNamespace XamlNamespace = "http://schemas.microsoft.com/winfx/2006/xaml";

    [Fact]
    public void Radar_KeepsAxisLabelsAndProvidesDashedReferenceGuides()
    {
        var document = LoadMainWindowXaml();
        var radar = FindNamedElement(document, "RadarCanvas");
        var title = FindNamedElement(document, "RadarTitle");

        var labels = radar
            .Descendants()
            .Where(element => element.Name.LocalName == "TextBlock")
            .Select(element => (string?)element.Attribute("Text"))
            .OfType<string>()
            .ToArray();

        Assert.Equal([
            "{Binding UiCopy.NoiseLabel}",
            "{Binding UiCopy.PerformanceLabel}",
            "{Binding UiCopy.TemperatureLabel}"
        ], labels);
        Assert.Equal("Left", (string?)title.Attribute("HorizontalAlignment"));
        Assert.NotNull(FindNamedElement(radar, "RadarGuideInner"));
        Assert.NotNull(FindNamedElement(radar, "RadarGuideMiddle"));
        Assert.All(
            [FindNamedElement(radar, "RadarGuideInner"), FindNamedElement(radar, "RadarGuideMiddle")],
            guide => Assert.False(string.IsNullOrWhiteSpace((string?)guide.Attribute("StrokeDashArray"))));
        Assert.All(
            ["RadarAxisNoise", "RadarAxisPerformance", "RadarAxisTemperature"],
            name => Assert.False(string.IsNullOrWhiteSpace(
                (string?)FindNamedElement(radar, name).Attribute("StrokeDashArray"))));
    }

    [Fact]
    public void FanEntry_UsesCenteredFanSeparatorAndTextWithoutPurpleCurve()
    {
        var document = LoadMainWindowXaml();
        var content = FindNamedElement(document, "FanEntryContent");
        var icon = FindNamedElement(content, "FanEntryIcon");

        Assert.Equal("Center", (string?)content.Attribute("HorizontalAlignment"));
        Assert.Equal(["|", "{Binding UiCopy.FanAdjustmentTitle}"], content
            .Elements()
            .Where(element => element.Name.LocalName == "TextBlock")
            .Select(element => (string?)element.Attribute("Text"))
            .OfType<string>()
            .ToArray());
        Assert.DoesNotContain(icon.Descendants(), element =>
            element.Name.LocalName == "Path" &&
            string.Equals((string?)element.Attribute("Stroke"), "#8B3DDB", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void FanChannelBindings_AreOneWayBecauseSelectionPropertiesAreReadOnly()
    {
        var document = LoadMainWindowXaml();

        foreach (var name in new[] { "CpuFanRadio", "GpuFanRadio" })
        {
            var binding = (string?)FindNamedElement(document, name).Attribute("IsChecked");
            Assert.Contains("Mode=OneWay", binding, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void DeviceRows_UseCompactHeightAndSpacing()
    {
        var document = LoadMainWindowXaml();
        var row = FindNamedElement(document, "DeviceRowBorder");

        Assert.Equal("50", (string?)row.Attribute("Height"));
        Assert.Equal("0,0,0,4", (string?)row.Attribute("Margin"));
    }

    [Fact]
    public void HomeCopy_UsesRevisedPerformanceModeWordingAndMutedCardDescriptions()
    {
        var document = LoadMainWindowXaml();

        Assert.Contains(document.Descendants(), element =>
            element.Name.LocalName == "TextBlock" &&
            (string?)element.Attribute("Text") == "{Binding UiCopy.HomeTitle}");
        Assert.Contains(document.Descendants(), element =>
            element.Name.LocalName == "RadioButton" &&
            (string?)element.Attribute("Tag") == "balanced" &&
            (string?)element.Attribute("ToolTip") == "{Binding BalancedModeTooltip}" &&
            (string?)element.Attribute("AutomationProperties.HelpText") == "{Binding BalancedModeDescription}");

        var description = FindNamedElement(document, "ProfileDescription");
        Assert.Equal("{DynamicResource ProfileDescriptionBrush}", (string?)description.Attribute("Foreground"));
    }

    [Fact]
    public void HomeProfileArea_UsesCompactCustomPlanPillsAndKeepsEditingInsideCustomMode()
    {
        var document = LoadMainWindowXaml();
        var text = string.Concat(document.DescendantNodes().OfType<XText>().Select(node => node.Value));
        var customEntry = FindNamedElement(document, "CustomProfileSelector");
        var descriptions = document.Descendants()
            .Where(element => element.Name.LocalName == "TextBlock" &&
                              (string?)element.Attribute(XamlNamespace + "Name") == "ProfileDescription")
            .ToArray();

        Assert.DoesNotContain("四个档位 · 狂暴包含两种策略", text, StringComparison.Ordinal);
        Assert.Equal("StackPanel", customEntry.Name.LocalName);
        Assert.Equal("Horizontal", (string?)customEntry.Attribute("Orientation"));
        var planPills = customEntry.Elements()
            .Where(element => element.Name.LocalName == "RadioButton")
            .ToArray();
        Assert.Equal(3, planPills.Length);
        Assert.Equal(["1", "2", "3"], planPills
            .Select(element => (string?)element.Attribute("Content"))
            .OfType<string>()
            .ToArray());
        Assert.All(planPills, pill =>
            Assert.Equal("{StaticResource CustomPlanRadioStyle}", (string?)pill.Attribute("Style")));
        var customProfile = document.Descendants().Single(element =>
            element.Name.LocalName == "RadioButton" && (string?)element.Attribute("Tag") == "custom");
        Assert.Equal("{Binding CustomModeTooltip}", (string?)customProfile.Attribute("ToolTip"));
        Assert.Equal("{Binding UiCopy.CustomDescription}", (string?)customProfile.Attribute("AutomationProperties.HelpText"));
        Assert.DoesNotContain(document.Descendants(), element =>
            (string?)element.Attribute(XName.Get("Name", "http://schemas.microsoft.com/winfx/2006/xaml")) ==
            "CustomProfileSettingsButton");
        Assert.DoesNotContain(document.Descendants(), element =>
            element.Name.LocalName == "TextBlock" && (string?)element.Attribute("Text") == "方案设置");
        var codeBehind = File.ReadAllText(LocateRepositoryFile("src", "RevoHelper.App", "MainWindow.xaml.cs"));
        Assert.Contains("OpenPerformanceAsync", codeBehind, StringComparison.Ordinal);
        Assert.DoesNotContain("CustomProfileSettings_Click", codeBehind, StringComparison.Ordinal);
        Assert.DoesNotContain("PreviewQuickAction", codeBehind, StringComparison.Ordinal);
        Assert.NotEmpty(descriptions);
    }

    [Fact]
    public void HomeProfileDescriptionsUseDedicatedLighterMutedBrush()
    {
        var document = LoadMainWindowXaml();
        var descriptionBrush = document.Descendants().Single(element =>
            element.Name.LocalName == "SolidColorBrush" &&
            element.Attributes().Any(attribute =>
                attribute.Name.LocalName == "Key" && attribute.Value == "ProfileDescriptionBrush"));

        Assert.Equal("#9AA4B2", (string?)descriptionBrush.Attribute("Color"));
    }

    [Fact]
    public void RadarHelp_ShowsTradeoffExplanationNextToReferenceTitle()
    {
        var document = LoadMainWindowXaml();
        var title = FindNamedElement(document, "RadarTitle");
        var help = FindNamedElement(document, "RadarHelp");
        var tooltipText = FindNamedElement(help, "RadarHelpTooltipText");
        var tooltip = (string?)tooltipText.Attribute("Text");

        Assert.Equal("{Binding UiCopy.ProfileTendencyTitle}", (string?)title.Attribute("Text"));
        Assert.Equal("Wrap", (string?)tooltipText.Attribute("TextWrapping"));
        Assert.Contains("不可能三角", tooltip, StringComparison.Ordinal);
        Assert.Contains("水冷", tooltip, StringComparison.Ordinal);
        Assert.Contains("风压式散热器", tooltip, StringComparison.Ordinal);
        Assert.Contains("仅供大致参考", tooltip, StringComparison.Ordinal);
        Assert.Contains("风扇曲线上微调", tooltip, StringComparison.Ordinal);
    }

    [Fact]
    public void RadarHelp_IsCompactAndRadarGraphicIsShiftedSlightlyRight()
    {
        var document = LoadMainWindowXaml();
        var help = FindNamedElement(document, "RadarHelp");
        var radar = FindNamedElement(document, "RadarCanvas");

        Assert.Equal("11", (string?)help.Attribute("Width"));
        Assert.Equal("11", (string?)help.Attribute("Height"));
        Assert.Equal("34,2,0,0", (string?)radar.Attribute("Margin"));
    }

    [Fact]
    public void HomeQuickControls_ExposeInteractiveCoreAndSecondaryEntryCards()
    {
        var document = LoadMainWindowXaml();
        var panel = FindNamedElement(document, "HomeQuickControlsPanel");
        var expectedNames = new[]
        {
            "GpuOutputQuickEntry", "LightingQuickEntry", "WaterCoolingQuickEntry", "KeyboardQuickEntry",
            "BatteryHealthQuickEntry", "DisplayColorQuickEntry", "OtherQuickEntry"
        };

        Assert.All(expectedNames, name =>
        {
            var entry = FindNamedElement(panel, name);
            Assert.Null(entry.Attribute("IsHitTestVisible"));
            Assert.Equal("QuickAction_Click", (string?)entry.Attribute("Click"));
            Assert.Equal("{StaticResource QuickActionCardStyle}", (string?)entry.Attribute("Style"));
            var content = entry.Elements().Single();
            Assert.Equal("Grid", content.Name.LocalName);
        });

        foreach (var name in expectedNames)
        {
            Assert.Equal("0", (string?)FindNamedElement(panel, name).Attribute("Grid.Row"));
        }

        Assert.All(expectedNames, name =>
            Assert.Equal("1", (string?)FindNamedElement(panel, name).Attribute("Grid.ColumnSpan")));

        var labels = panel.Descendants()
            .Where(element => element.Name.LocalName == "TextBlock")
            .Select(element => (string?)element.Attribute("Text"))
            .OfType<string>()
            .ToArray();
        Assert.Contains("{Binding UiCopy.GpuOutputTitle}", labels);
        Assert.Contains("{Binding UiCopy.LightingTitle}", labels);
        Assert.Contains("{Binding UiCopy.LiquidCoolingTitle}", labels);
        Assert.Contains("{Binding UiCopy.KeyboardTitle}", labels);
        Assert.Contains("{Binding UiCopy.BatteryTitle}", labels);
        Assert.Contains("{Binding UiCopy.DisplayTitle}", labels);
        Assert.Contains("{Binding UiCopy.OtherTitle}", labels);
        Assert.Equal("电源", (string?)FindNamedElement(panel, "BatteryHealthQuickEntry").Attribute("ToolTip"));
        Assert.All(
            new[] { "GpuOutputQuickTitle", "LightingQuickTitle", "WaterCoolingQuickTitle", "KeyboardQuickTitle", "BatteryHealthQuickTitle", "DisplayColorQuickTitle" },
            name =>
            {
                var title = FindNamedElement(panel, name);
                Assert.Equal("{DynamicResource TextBrush}", (string?)title.Attribute("Foreground"));
                Assert.Equal("SemiBold", (string?)title.Attribute("FontWeight"));
            });

        var row = panel.Descendants().First(element => element.Name.LocalName == "RowDefinition");
        Assert.True(double.Parse((string?)row.Attribute("Height") ?? "0") >= 64);
        var waterIcon = FindNamedElement(panel, "WaterCoolingQuickIcon");
        Assert.Equal("Path", waterIcon.Name.LocalName);
    }

    [Fact]
    public void QuickControlDrawers_ExposeFiveWorkingPanelsAndHandlers()
    {
        var document = LoadMainWindowXaml();
        var codeBehind = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "RevoHelper.App", "MainWindow.xaml.cs"));

        Assert.All(
            new[] { "LightingDrawerContent", "LiquidCoolingDrawerContent", "KeyboardDrawerContent", "BatteryDrawerContent", "DisplayColorDrawerContent" },
            name => Assert.Equal("Collapsed", (string?)FindNamedElement(document, name).Attribute("Visibility")));
        Assert.Contains("OpenLightingAsync", codeBehind, StringComparison.Ordinal);
        Assert.Contains("OpenLiquidCoolingAsync", codeBehind, StringComparison.Ordinal);
        Assert.Contains("OpenKeyboardControlsAsync", codeBehind, StringComparison.Ordinal);
        Assert.Contains("OpenBatteryAsync", codeBehind, StringComparison.Ordinal);
        Assert.Contains("OpenDisplayAsync", codeBehind, StringComparison.Ordinal);
        Assert.Contains("KeyboardLighting_Click", codeBehind, StringComparison.Ordinal);
        Assert.DoesNotContain("KeyboardLighting_Checked", codeBehind, StringComparison.Ordinal);
        Assert.Contains("LiquidFan_Click", codeBehind, StringComparison.Ordinal);
        Assert.Contains("BatteryMode_Click", codeBehind, StringComparison.Ordinal);
        Assert.Contains("DisplayColor_Click", codeBehind, StringComparison.Ordinal);
    }

    [Fact]
    public void LightingDrawer_ExposesCompleteWorkingEditorInsteadOfSingleToggle()
    {
        var document = LoadMainWindowXaml();
        var drawer = FindNamedElement(document, "LightingDrawerContent");
        var text = string.Join(" ", drawer.Descendants().Select(element =>
            (string?)element.Attribute("Text") ?? (string?)element.Attribute("Content")));
        var codeBehind = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "RevoHelper.App", "MainWindow.xaml.cs"));

        foreach (var name in new[]
                 {
                     "LightingKeyboardTab", "LightingCoastTab", "LightingLogoTab", "LightingWaterTab",
                     "LightingEffectCombo", "LightingColorHexBox", "LightingBrightnessSlider", "LightingSpeedSlider",
                     "LightingZonePowerToggle", "LightingSleepTimerSelector", "KeyboardLightingProfilesSection",
                     "KeyboardLightingProfileActionsSection", "SaveLightingProfileButton", "ImportLightingProfileButton",
                     "ExportLightingProfileButton"
                 })
            FindNamedElement(drawer, name);
        foreach (var label in new[]
                 {
                     "键盘灯光方案", "机器最初始默认背光", "耀世流光", "冰海呼吸", "静夜微光",
                     "键盘与海岸灯带自动熄灯（秒级档位）",
                     "保存为我的方案"
                 })
            Assert.Contains(label, text, StringComparison.Ordinal);
        Assert.Contains("官方协议与状态回读均使用秒值",
            (string?)FindNamedElement(drawer, "LightingSleepTimerSelector").Attribute("ToolTip"), StringComparison.Ordinal);
        Assert.DoesNotContain("多区联动", text, StringComparison.Ordinal);
        Assert.Contains("LightingZoneTab_Click", codeBehind, StringComparison.Ordinal);
        Assert.Contains("LightingZonePower_Click", codeBehind, StringComparison.Ordinal);
        Assert.Contains("LightingEffect_SelectionChanged", codeBehind, StringComparison.Ordinal);
        Assert.Contains("LightingSlider_ValueChanged", codeBehind, StringComparison.Ordinal);
        Assert.DoesNotContain("ApplyLighting_Click", codeBehind, StringComparison.Ordinal);
        Assert.Contains("LightingSleepTimer_SelectionChanged", codeBehind, StringComparison.Ordinal);
        Assert.Contains("ImportLightingProfile_Click", codeBehind, StringComparison.Ordinal);
        Assert.DoesNotContain("if (!IsLoaded || !ViewModel.CanApplyLighting) return;", codeBehind, StringComparison.Ordinal);
        Assert.Contains("comboBox.IsDropDownOpen", codeBehind, StringComparison.Ordinal);
        Assert.Contains("{Binding LightingEditorApplyHint}", drawer.ToString(), StringComparison.Ordinal);
        Assert.Contains("var profileIndex = ViewModel.SelectedCustomProfileIndex;", codeBehind, StringComparison.Ordinal);
        Assert.Contains("profileIndex == ViewModel.SelectedCustomProfileIndex", codeBehind, StringComparison.Ordinal);
        Assert.DoesNotContain("if (!IsLoaded || !ViewModel.IsPerformanceApplyEnabled) return;", codeBehind, StringComparison.Ordinal);
        Assert.DoesNotContain("if (!IsLoaded || !ViewModel.CanEditFanSettings) return;", codeBehind, StringComparison.Ordinal);
    }

    [Fact]
    public void LightingDrawer_UserComboSelectionsCommitNewValueBeforeAutoApply()
    {
        var codeBehind = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "RevoHelper.App", "MainWindow.xaml.cs"));
        var effectHandler = SliceMethod(codeBehind, "private void LightingEffect_SelectionChanged", "private void LightingDirection_SelectionChanged");
        var directionHandler = SliceMethod(codeBehind, "private void LightingDirection_SelectionChanged", "private void LightingSlider_ValueChanged");
        var sleepHandler = SliceMethod(codeBehind, "private void LightingSleepTimer_SelectionChanged", "private async Task ApplyLatestLightingSleepTimerAsync");

        AssertAssignmentPrecedesApply(effectHandler, "ViewModel.SelectedLightingEffect = effect;", "QueueLightingAutoApply();");
        AssertAssignmentPrecedesApply(directionHandler, "ViewModel.SelectedLightingDirection = direction;", "QueueLightingAutoApply();");
        AssertAssignmentPrecedesApply(sleepHandler, "ViewModel.LightingSleepSeconds = seconds;", "ApplyLatestLightingSleepTimerAsync");
    }

    [Fact]
    public void LightingDrawer_FocusedComboSelectionStillCountsAfterPopupCloses()
    {
        var codeBehind = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "RevoHelper.App", "MainWindow.xaml.cs"));
        var effectHandler = SliceMethod(codeBehind, "private void LightingEffect_SelectionChanged", "private void LightingDirection_SelectionChanged");
        var directionHandler = SliceMethod(codeBehind, "private void LightingDirection_SelectionChanged", "private void LightingSlider_ValueChanged");
        var sleepHandler = SliceMethod(codeBehind, "private void LightingSleepTimer_SelectionChanged", "private async Task ApplyLatestLightingSleepTimerAsync");

        Assert.Contains("IsUserLightingComboBoxSelection(sender)", effectHandler, StringComparison.Ordinal);
        Assert.Contains("IsUserLightingComboBoxSelection(sender)", directionHandler, StringComparison.Ordinal);
        Assert.Contains("IsUserLightingComboBoxSelection(sender)", sleepHandler, StringComparison.Ordinal);
        Assert.Contains("comboBox.IsDropDownOpen || comboBox.IsKeyboardFocusWithin", codeBehind, StringComparison.Ordinal);
        Assert.Contains("!ViewModel.IsSynchronizingLightingEditor", codeBehind, StringComparison.Ordinal);
    }

    private static string SliceMethod(string source, string startMarker, string endMarker)
    {
        var start = source.IndexOf(startMarker, StringComparison.Ordinal);
        var end = source.IndexOf(endMarker, start + startMarker.Length, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start, $"Unable to locate method slice {startMarker}.");
        return source[start..end];
    }

    [Fact]
    public void LightingDrawer_QueuesCapturedValuesPerZoneInsteadOfRereadingLiveEditor()
    {
        var source = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "RevoHelper.App", "MainWindow.xaml.cs"));
        var queue = SliceMethod(source, "private void QueueLightingAutoApply", "private void LightingSleepTimer_SelectionChanged");
        Assert.Contains("ViewModel.CaptureLightingEditor()", queue);
        Assert.Contains("pendingLightingEdits", queue);
        Assert.Contains("ViewModel.ApplyLightingConfigurationAsync(request.Configuration", queue);
        Assert.DoesNotContain("ViewModel.ApplySelectedLightingZoneAsync", queue);
        Assert.DoesNotContain("zone == ViewModel.SelectedLightingZone", queue);
        var slider = SliceMethod(source, "private void LightingSlider_ValueChanged", "private void QueueLightingAutoApply");
        Assert.Contains("!ViewModel.IsSynchronizingLightingEditor", slider);
        Assert.Contains("LightingColorHexBox.IsKeyboardFocusWithin", source);
    }

    private static void AssertAssignmentPrecedesApply(string method, string assignment, string apply)
    {
        var assignmentIndex = method.IndexOf(assignment, StringComparison.Ordinal);
        var applyIndex = method.IndexOf(apply, StringComparison.Ordinal);
        Assert.True(assignmentIndex >= 0, $"Missing explicit selection commit: {assignment}");
        Assert.True(applyIndex > assignmentIndex, $"{assignment} must occur before {apply}.");
    }

    [Fact]
    public void LightingDrawer_ActionButtonsUseCompactTypography()
    {
        var drawer = FindNamedElement(LoadMainWindowXaml(), "LightingDrawerContent");
        foreach (var name in new[]
                 {
                     "SaveLightingProfileButton",
                     "ExportLightingProfileButton", "ImportLightingProfileButton"
                 })
        {
            var button = FindNamedElement(drawer, name);
            Assert.True(double.TryParse((string?)button.Attribute("FontSize"), out var fontSize));
            Assert.InRange(fontSize, 8.0, 10.0);
        }
    }

    [Fact]
    public void LightingDrawer_ShowsOnlyTheColorSurfaceThatMatchesTheSelectedEffect()
    {
        var drawer = FindNamedElement(LoadMainWindowXaml(), "LightingDrawerContent");
        var colorEditor = FindNamedElement(drawer, "LightingColorEditor");
        var firmwarePalette = FindNamedElement(drawer, "FirmwareLightingPalettePreview");
        var oemNotice = FindNamedElement(drawer, "OemDefaultLightingNotice");
        var waterNotice = FindNamedElement(drawer, "WaterDynamicColorNotice");

        Assert.Null((string?)colorEditor.Attribute("IsEnabled"));
        Assert.Contains("CanEditLightingColor", colorEditor.ToString(), StringComparison.Ordinal);
        Assert.Contains("IsFirmwarePaletteLightingEffectSelected", firmwarePalette.ToString(), StringComparison.Ordinal);
        Assert.Equal(7, firmwarePalette.Descendants().Count(element =>
            element.Name.LocalName == "Border" &&
            ((string?)element.Attribute("Tag"))?.StartsWith("#", StringComparison.Ordinal) == true));
        Assert.Contains("IsOemDefaultLightingEffectSelected", oemNotice.ToString(), StringComparison.Ordinal);
        Assert.Contains("不会应用下方自选参数", (string?)oemNotice.Attribute("Text"), StringComparison.Ordinal);
        Assert.Equal("此水冷灯效使用固定颜色，不能自选配色。",
            (string?)waterNotice.Attribute("Text"));
    }

    [Fact]
    public void LightingDrawer_HidesIrrelevantParametersInsteadOfRenderingDisabledControls()
    {
        var drawer = FindNamedElement(LoadMainWindowXaml(), "LightingDrawerContent");
        var parameters = FindNamedElement(drawer, "LightingEffectParameterEditor");
        var direction = FindNamedElement(drawer, "LightingDirectionEditor");

        Assert.Contains("IsOemDefaultLightingEffectSelected", parameters.ToString(), StringComparison.Ordinal);
        Assert.Contains("CanSelectLightingDirection", direction.ToString(), StringComparison.Ordinal);
        Assert.Null((string?)direction.Attribute("IsEnabled"));
    }

    [Fact]
    public void QuickControlCheckBoxes_BindReadOnlyViewModelStateOneWay()
    {
        var document = LoadMainWindowXaml();
        var checkBoxes = document.Descendants().Where(element => element.Name.LocalName == "CheckBox").ToArray();

        Assert.All(
            new[] { "IsKeyboardLightingEnabled", "IsWinKeyEnabled", "IsFnKeyEnabled", "IsOsdEnabled" },
            propertyName =>
            {
                var checkBox = Assert.Single(
                    checkBoxes,
                    element => ((string?)element.Attribute("IsChecked"))?.Contains(propertyName, StringComparison.Ordinal) == true);
                Assert.Contains("Mode=OneWay", (string?)checkBox.Attribute("IsChecked"), StringComparison.Ordinal);
            });
    }

    [Fact]
    public void KeyboardDrawer_RestoresAllOfficialSystemAndKeyControls()
    {
        var document = LoadMainWindowXaml();
        var drawer = FindNamedElement(document, "KeyboardDrawerContent");
        var text = string.Join(" ", drawer.Descendants().Select(element =>
            (string?)element.Attribute("Text") ?? (string?)element.Attribute("Content")));

        foreach (var label in new[] { "Wi-Fi 开关", "蓝牙开关", "触摸板开关", "网络摄像头开关", "Win 键", "OSD 开关", "Fn 功能键", "Copilot 键" })
            Assert.Contains(label, text, StringComparison.Ordinal);
        AssertDrawerContentUsesSymmetricStretchLayout(FindNamedElement(document, "KeyboardControlsStack"));
    }

    [Fact]
    public void KeyboardAndPowerDrawers_RenderLabelsOutsideToggleOnlyTemplates()
    {
        var document = LoadMainWindowXaml();
        var toggleStyle = document.Descendants().Single(element =>
            element.Name.LocalName == "Style" &&
            (string?)element.Attribute(XamlNamespace + "Key") == "SettingsToggleStyle");
        Assert.DoesNotContain(toggleStyle.Descendants(), element => element.Name.LocalName == "ContentPresenter");

        foreach (var name in new[]
                 {
                     "WifiSettingLabel", "BluetoothSettingLabel", "TouchpadSettingLabel", "WebcamSettingLabel",
                     "WinKeySettingLabel", "OsdSettingLabel", "FnSettingLabel", "CopilotSettingLabel",
                     "UsbChargingSettingLabel", "AcAutoBootSettingLabel", "HighPerformanceSettingLabel", "DeepSleepSettingLabel"
                 })
            Assert.Equal("TextBlock", FindNamedElement(document, name).Name.LocalName);

        var keyboard = FindNamedElement(document, "KeyboardDrawerContent");
        var power = FindNamedElement(document, "BatteryDrawerContent");
        Assert.All(keyboard.Descendants().Concat(power.Descendants()).Where(element =>
                element.Name.LocalName == "CheckBox" &&
                ((string?)element.Attribute("Style"))?.Contains("SettingsToggleStyle", StringComparison.Ordinal) == true),
            toggle => Assert.Null((string?)toggle.Attribute("Content")));
        Assert.DoesNotContain(power.Descendants(), element =>
            (string?)element.Attribute(XamlNamespace + "Name") == "ApplyBatteryChargeLimitButton");
    }

    [Fact]
    public void PowerDrawer_CoversOfficialFeaturesWithoutExposingUnsupportedChargeCeiling()
    {
        var document = LoadMainWindowXaml();
        var drawer = FindNamedElement(document, "BatteryDrawerContent");
        var text = string.Join(" ", drawer.Descendants().Select(element =>
            (string?)element.Attribute("Text") ?? (string?)element.Attribute("Content")));

        foreach (var label in new[] { "关机下使用 USB 供电", "来电自动开机", "Windows 电源模式跟随", "深度睡眠", "长效模式", "均衡模式", "工作站模式" })
            Assert.Contains(label, text, StringComparison.Ordinal);
        Assert.DoesNotContain("充电上限", text, StringComparison.Ordinal);
        Assert.DoesNotContain(drawer.Descendants(), element =>
            (string?)element.Attribute(XamlNamespace + "Name") is "BatteryChargeLimitCard" or "BatteryChargeLimitSlider");
        AssertDrawerContentUsesSymmetricStretchLayout(FindNamedElement(document, "PowerControlsStack"));
    }

    [Fact]
    public void SettingsDrawer_DistinguishesOfficialConsoleFromIntelPlatformServices()
    {
        var document = LoadMainWindowXaml();
        var panel = FindNamedElement(document, "ConflictServicesPanel");
        var text = string.Join(" ", panel.Descendants().Select(element =>
            (string?)element.Attribute("Text") ?? (string?)element.Attribute("Content")));

        Assert.NotNull(FindNamedElement(panel, "OfficialConsoleConflictCard"));
        Assert.Contains("CCUWinUI", text, StringComparison.Ordinal);
        // Snapshot 4 replaces duplicate coexistence paragraphs with one concrete reminder.
        Assert.Contains("RevoHelper 可以与官方控制台共存。请保留 GCUBridge、GCUService 和 OSD 热键后台运行。", text, StringComparison.Ordinal);
        Assert.DoesNotContain("主动让出", text, StringComparison.Ordinal);
        Assert.Contains("GCUBridge", text, StringComparison.Ordinal);
        Assert.Contains("GCUService", text, StringComparison.Ordinal);
        Assert.Contains("请保留", text, StringComparison.Ordinal);
        Assert.Contains("后台运行", text, StringComparison.Ordinal);
        Assert.DoesNotContain("只关闭官方前台", text, StringComparison.Ordinal);
    }

    [Fact]
    public void LiquidCoolingDrawer_CoversOfficialDeviceMonitorAndThermalStrategySurface()
    {
        var document = LoadMainWindowXaml();
        var drawer = FindNamedElement(document, "LiquidCoolingDrawerContent");
        var drawerText = string.Join(" ", drawer.Descendants().Select(element => (string?)element.Attribute("Text") ?? (string?)element.Attribute("Content")));

        Assert.Equal("Grid", FindNamedElement(document, "LiquidCoolingMonitorSection").Name.LocalName);
        Assert.Equal("Border", FindNamedElement(document, "LiquidCoolingDeviceSection").Name.LocalName);
        Assert.Equal("Border", FindNamedElement(document, "LiquidCoolingStrategySection").Name.LocalName);
        Assert.Contains("风扇状态", drawerText, StringComparison.Ordinal);
        Assert.Contains("水泵状态", drawerText, StringComparison.Ordinal);
        Assert.Contains("蓝牙设备", drawerText, StringComparison.Ordinal);
        Assert.Contains("连接", drawerText, StringComparison.Ordinal);
        Assert.Contains("断开", drawerText, StringComparison.Ordinal);
        Assert.Contains("注水模式", drawerText, StringComparison.Ordinal);
        Assert.Contains("重设", drawerText, StringComparison.Ordinal);
        Assert.Contains("登录后与睡眠唤醒时自动连接上次设备", drawerText, StringComparison.Ordinal);
        Assert.Equal("CheckBox", FindNamedElement(document, "LiquidCoolingAutoConnectToggle").Name.LocalName);

        var strategies = drawer.Descendants()
            .Where(element => element.Name.LocalName == "RadioButton" && (string?)element.Attribute("GroupName") == "LiquidCoolingStrategy")
            .ToArray();
        Assert.Equal(new[] { "更安静", "安静", "均衡", "最大", "智慧控制" },
            strategies.Select(strategy => (string?)strategy.Attribute("Content")));
        Assert.Equal(new[] { "0", "1", "2", "3", "4" },
            strategies.Select(strategy => (string?)strategy.Attribute("Tag")));

        var codeBehind = File.ReadAllText(LocateRepositoryFile("src", "RevoHelper.App", "MainWindow.xaml.cs"));
        Assert.Contains("ScanLiquidCooling_Click", codeBehind, StringComparison.Ordinal);
        Assert.Contains("ConnectLiquidCooling_Click", codeBehind, StringComparison.Ordinal);
        Assert.Contains("DisconnectLiquidCooling_Click", codeBehind, StringComparison.Ordinal);
        Assert.Contains("PrimeLiquidCooling_Click", codeBehind, StringComparison.Ordinal);
        Assert.Contains("ResetLiquidCooling_Click", codeBehind, StringComparison.Ordinal);
        Assert.Contains("LiquidCoolingAutoConnect_Click", codeBehind, StringComparison.Ordinal);
    }

    [Fact]
    public void LiquidCoolingDrawer_DoesNotExposePumpNoiseOrWaterTankLightingControls()
    {
        var document = LoadMainWindowXaml();
        var drawer = FindNamedElement(document, "LiquidCoolingDrawerContent");

        Assert.DoesNotContain(drawer.Descendants(), element => (string?)element.Attribute("Click") == "LiquidPump_Click");
        Assert.DoesNotContain("水箱灯", drawer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void LiquidCoolingDrawer_UsesAlignedTypographyAndDedicatedStrategyStyle()
    {
        var document = LoadMainWindowXaml();
        var content = FindNamedElement(document, "LiquidCoolingContentStack");
        var fanValue = FindNamedElement(document, "LiquidCoolingFanValueText");
        var pumpValue = FindNamedElement(document, "LiquidCoolingPumpValueText");
        var drawer = FindNamedElement(document, "LiquidCoolingDrawerContent");

        Assert.Equal("7,6,7,10", (string?)content.Attribute("Margin"));
        Assert.Equal("14", (string?)fanValue.Attribute("FontSize"));
        Assert.Equal("14", (string?)pumpValue.Attribute("FontSize"));
        Assert.All(
            drawer.Descendants().Where(element => element.Name.LocalName == "RadioButton" &&
                (string?)element.Attribute("GroupName") == "LiquidCoolingStrategy"),
            strategy => Assert.Equal("{StaticResource LiquidCoolingStrategyRadioStyle}", (string?)strategy.Attribute("Style")));
        Assert.Equal("扫描", (string?)FindNamedElement(document, "LiquidCoolingScanButton").Attribute("Content"));
    }

    [Fact]
    public void LiquidCoolingMonitor_UsesCenteredVectorFanAndStateSpecificPumpIcons()
    {
        var document = LoadMainWindowXaml();
        var fanIndicator = FindNamedElement(document, "LiquidCoolingFanIndicator");
        var fanIcon = FindNamedElement(document, "LiquidCoolingFanIcon");
        var pumpIndicator = FindNamedElement(document, "LiquidCoolingPumpIndicator");
        var runningIcon = FindNamedElement(document, "LiquidCoolingPumpRunningIcon");
        var warningIcon = FindNamedElement(document, "LiquidCoolingPumpWarningIcon");

        Assert.Equal("Center", (string?)fanIndicator.Attribute("HorizontalAlignment"));
        Assert.Equal("Center", (string?)fanIndicator.Attribute("VerticalAlignment"));
        Assert.Equal("Viewbox", fanIcon.Name.LocalName);
        Assert.Equal("Center", (string?)pumpIndicator.Attribute("HorizontalAlignment"));
        Assert.Equal("Center", (string?)pumpIndicator.Attribute("VerticalAlignment"));
        Assert.Equal("Viewbox", runningIcon.Name.LocalName);
        Assert.Equal("Viewbox", warningIcon.Name.LocalName);
        Assert.Contains("LiquidCoolingPumpStatusText", pumpIndicator.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("❋", fanIndicator.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("↻", pumpIndicator.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void LiquidCoolingDrawer_UsesCenteredOemLabelsAndWaterDropIdentity()
    {
        var document = LoadMainWindowXaml();
        var drawer = FindNamedElement(document, "LiquidCoolingDrawerContent");
        var drawerMarkup = drawer.ToString();
        var codeBehind = File.ReadAllText(LocateRepositoryFile("src", "RevoHelper.App", "MainWindow.xaml.cs"));

        Assert.DoesNotContain("监视器", drawerMarkup, StringComparison.Ordinal);
        Assert.Contains("水冷箱风扇状态", drawerMarkup, StringComparison.Ordinal);
        Assert.Contains("散热策略档位调节", drawerMarkup, StringComparison.Ordinal);
        Assert.DoesNotContain("仅调节水冷风扇噪音与散热倾向", drawerMarkup, StringComparison.Ordinal);
        Assert.Equal("Viewbox", FindNamedElement(document, "LiquidCoolingDrawerHeaderIcon").Name.LocalName);
        Assert.Equal("{StaticResource ModernComboBoxStyle}",
            (string?)FindNamedElement(document, "LiquidCoolingDeviceSelector").Attribute("Style"));
        Assert.Contains("liquidCoolingRefreshTimer", codeBehind, StringComparison.Ordinal);
        Assert.Contains("RefreshLiquidCoolingStatusAsync", codeBehind, StringComparison.Ordinal);
    }

    [Fact]
    public void DisplayDrawer_ContainsThreeCenteredFunctionalPages()
    {
        var document = LoadMainWindowXaml();
        var drawer = FindNamedElement(document, "DisplayColorDrawerContent");
        var markup = drawer.ToString();

        Assert.Equal("{Binding UiCopy.DisplayTitle}", (string?)FindNamedElement(document, "DisplayColorQuickTitle").Attribute("Text"));
        Assert.Equal("显示", (string?)FindNamedElement(document, "DisplayColorQuickEntry").Attribute("Tag"));
        Assert.Contains("色彩校正", markup, StringComparison.Ordinal);
        Assert.Contains("显示功能", markup, StringComparison.Ordinal);
        Assert.Contains("刷新率", markup, StringComparison.Ordinal);
        Assert.Contains("Mini LED 局部调光", markup, StringComparison.Ordinal);
        Assert.Contains("当前显示器", markup, StringComparison.Ordinal);
        Assert.Contains("选择刷新率 (Hz)", markup, StringComparison.Ordinal);
        Assert.Equal("UniformGrid", FindNamedElement(document, "DisplaySettingsTabs").Name.LocalName);
        Assert.NotNull(FindNamedElement(document, "DisplayColorCalibrationPage"));
        Assert.NotNull(FindNamedElement(document, "DisplayFeaturePage"));
        Assert.NotNull(FindNamedElement(document, "DisplayRefreshRatePage"));
        Assert.Equal("Viewbox", FindNamedElement(document, "DisplayDrawerHeaderIcon").Name.LocalName);
        var refreshSelector = FindNamedElement(document, "DisplayRefreshRateSelector");
        Assert.Equal("{Binding DisplayRefreshRates}", (string?)refreshSelector.Attribute("ItemsSource"));
        Assert.Equal("{Binding SelectedDisplayRefreshRateHz, Mode=OneWay}", (string?)refreshSelector.Attribute("SelectedItem"));
        Assert.Equal("{}{0} Hz", (string?)refreshSelector.Attribute("ItemStringFormat"));
        Assert.Null(refreshSelector.Attribute("DisplayMemberPath"));
        Assert.Null(refreshSelector.Attribute("SelectedValuePath"));
        var colorArtwork = FindNamedElement(document, "DisplayColorArtwork");
        Assert.Equal("Border", colorArtwork.Name.LocalName);
        Assert.Contains("LinearGradientBrush", colorArtwork.ToString(), StringComparison.Ordinal);
        Assert.Contains("StartPoint=\"0,1\"", colorArtwork.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void HomeBrand_UsesSingleBoldRAndSlightlyLowerModeTintedBetaV1Badge()
    {
        var document = LoadMainWindowXaml();
        var logo = FindNamedElement(document, "HomeBrandLogo");
        var badge = FindNamedElement(document, "ReleaseStatusBadge");
        var status = FindNamedElement(document, "ReleaseStatusText");

        Assert.Equal("R", (string?)logo.Attribute("Text"));
        Assert.Equal("Bold", (string?)logo.Attribute("FontWeight"));
        Assert.Equal("Center", (string?)logo.Attribute("HorizontalAlignment"));
        Assert.Equal("Center", (string?)logo.Attribute("VerticalAlignment"));
        Assert.Equal("0,-1,0,1", (string?)logo.Attribute("Margin"));
        Assert.Equal("{Binding ReleaseVersionText}", (string?)status.Attribute("Text"));
        Assert.Equal("Center", (string?)status.Attribute("HorizontalAlignment"));
        Assert.Equal("Center", (string?)status.Attribute("VerticalAlignment"));
        Assert.Equal("Center", (string?)badge.Attribute("VerticalAlignment"));
        Assert.Equal("9,4,0,0", (string?)badge.Attribute("Margin"));
        Assert.Equal("72", (string?)badge.Attribute("MinWidth"));
        Assert.Equal("10", (string?)status.Attribute("FontSize"));
        Assert.Equal("{Binding PerformanceAccentSurfaceColor}", (string?)badge.Attribute("Background"));
        Assert.Equal("{Binding PerformanceAccentColor}", (string?)status.Attribute("Foreground"));
    }

    [Fact]
    public void ApplicationLogoGenerator_UsesSingleBoldRWithoutReticleLines()
    {
        var script = File.ReadAllText(LocateRepositoryFile("tools", "Generate-AppIcon.ps1"));
        var svg = File.ReadAllText(LocateRepositoryFile("src", "RevoHelper.App", "Assets", "RevoHelperLogo.svg"));

        Assert.DoesNotContain("DrawLine", script, StringComparison.Ordinal);
        Assert.Contains("[Drawing.FontStyle]::Bold", script, StringComparison.Ordinal);
        Assert.DoesNotContain("<path", svg, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("font-weight=\"700\"", svg, StringComparison.Ordinal);
        Assert.Contains("#FFFFFF", svg, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("#00A9D6", svg, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("[Drawing.Color]::White", script, StringComparison.Ordinal);
        Assert.Contains("#00A9D6", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("$size * 0.015", script, StringComparison.Ordinal);
        Assert.Contains("y=\"182\"", svg, StringComparison.Ordinal);
    }

    [Fact]
    public void DrawerHeaders_KeepEveryFeatureIconAtTheSameLeftOrigin()
    {
        var document = LoadMainWindowXaml();

        foreach (var name in new[]
                 {
                     "FanDrawerHeaderIcon", "PerformanceDrawerHeaderIcon", "SettingsDrawerHeaderIcon",
                     "GpuOutputDrawerHeaderIcon", "LightingDrawerHeaderIcon", "KeyboardDrawerHeaderIcon", "BatteryDrawerHeaderIcon",
                     "LiquidCoolingDrawerHeaderIcon", "DisplayDrawerHeaderIcon"
                 })
            Assert.Equal("Left", (string?)FindNamedElement(document, name).Attribute("HorizontalAlignment"));
    }

    private static void AssertDrawerContentUsesSymmetricStretchLayout(XElement stack)
    {
        Assert.Null(stack.Attribute("Width"));
        Assert.Null(stack.Attribute("HorizontalAlignment"));
        var margin = ((string?)stack.Attribute("Margin") ?? string.Empty).Split(',');
        Assert.Equal(4, margin.Length);
        Assert.Equal(margin[0], margin[2]);
    }

    [Fact]
    public void HomeWindow_IsSlightlyNarrowerAndHidesDuplicateFootersWhileAnnouncementsKeepVersion()
    {
        var document = LoadMainWindowXaml();
        var window = document.Root!;
        Assert.Equal("580", (string?)window.Attribute("Width"));
        Assert.Equal("{Binding AppVersionText}", (string?)FindNamedElement(document, "HomeVersionFooter").Attribute("Text"));
        Assert.Equal("Collapsed", (string?)FindNamedElement(document, "HomeVersionFooter").Attribute("Visibility"));
        Assert.Contains("免费使用", File.ReadAllText(LocateRepositoryFile("src", "RevoHelper.App", "Presentation", "LocalizedUiCopy.cs")), StringComparison.Ordinal);
        var footer = FindNamedElement(document, "SettingsVersionFooter");
        Assert.Equal("{Binding AppVersionText}", (string?)footer.Attribute("Text"));
        Assert.Equal("0,6,10,3", (string?)footer.Attribute("Margin"));
        Assert.Equal("Collapsed", (string?)footer.Attribute("Visibility"));
        Assert.Equal("{Binding Announcements.Version}",
            (string?)FindNamedElement(document, "AnnouncementVersionText").Attribute("Text"));
    }

    [Fact]
    public void Footer_PutsMonitorBeforeDynamicPerformanceAndFanEntries()
    {
        var document = LoadMainWindowXaml();
        var monitor = FindNamedElement(document, "MonitorEntryButton");
        var performance = FindNamedElement(document, "PerformanceEntryButton");
        var fan = FindNamedElement(document, "FanEntryButton");

        Assert.Equal("1", (string?)monitor.Attribute("Grid.Column"));
        Assert.Equal("2", (string?)performance.Attribute("Grid.Column"));
        Assert.Equal("3", (string?)fan.Attribute("Grid.Column"));
        Assert.Equal("PerformanceMonitor_Click", (string?)monitor.Attribute("Click"));
        Assert.Equal("{Binding UiCopy.MonitorEntryTitle}",
            (string?)FindNamedElement(monitor, "MonitorEntryTitle").Attribute("Text"));
        Assert.Equal("性能监控", LocalizedUiCopy.For("zh-CN").MonitorEntryTitle);
        Assert.Equal("{Binding FanAccentSurfaceColor}", (string?)performance.Attribute("Background"));
        Assert.Equal("{Binding FanAccentColor}", (string?)performance.Attribute("BorderBrush"));
        Assert.Equal("Performance_Click", (string?)performance.Attribute("Click"));
    }

    [Fact]
    public void PerformanceDrawer_ExposesLivePowerAndBoostControls()
    {
        var document = LoadMainWindowXaml();
        var drawer = FindNamedElement(document, "PerformanceDrawerContent");
        var text = string.Concat(drawer.Descendants().Select(element => (string?)element.Attribute("Text")));

        Assert.Contains("PL1", text, StringComparison.Ordinal);
        Assert.Contains("PL2", text, StringComparison.Ordinal);
        var details = FindNamedElement(drawer, "PerformanceReadbackDetails");
        Assert.Equal("False", (string?)details.Attribute("IsExpanded"));
        Assert.Contains(details.Descendants(), element =>
            (string?)element.Attribute("Text") == "{Binding PerformanceReadbackText}");
        Assert.NotNull(FindNamedElement(drawer, "PerformancePl1Editor"));
        Assert.NotNull(FindNamedElement(drawer, "PerformancePl2Editor"));
        Assert.NotNull(FindNamedElement(drawer, "CustomPowerLimitEditorPanel"));
        Assert.NotNull(FindNamedElement(drawer, "FirmwareManagedPowerLimitNotice"));
        var boost = FindNamedElement(drawer, "ProcessorBoostModeSelector");
        Assert.Equal("{Binding ProcessorBoostModes}", (string?)boost.Attribute("ItemsSource"));
        Assert.Equal(
            "{Binding SelectedProcessorBoostMode, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}",
            (string?)boost.Attribute("SelectedValue"));
        Assert.DoesNotContain(drawer.Descendants(), element =>
            (string?)element.Attribute("Click") == "ApplyPerformancePolicy_Click");
        Assert.Equal("PerformanceBoost_SelectionChanged", (string?)boost.Attribute("SelectionChanged"));
        Assert.Contains("调整后自动应用", text, StringComparison.Ordinal);
    }

    [Fact]
    public void PerformanceDrawer_WarnsThatOemPresetPowerCanOnlyBeReducedReliably()
    {
        var document = LoadMainWindowXaml();
        var drawer = FindNamedElement(document, "PerformanceDrawerContent");
        var warning = FindNamedElement(drawer, "OemPresetPowerLimitWarning");
        var pl1 = FindNamedElement(drawer, "PerformancePl1Editor");
        var descendants = drawer.Descendants().ToList();

        Assert.Equal("#D9363E", (string?)warning.Attribute("Foreground"));
        Assert.Equal("Wrap", (string?)warning.Attribute("TextWrapping"));
        Assert.Contains("机械革命官方预设", (string?)warning.Attribute("Text"), StringComparison.Ordinal);
        Assert.Contains("平衡模式默认最高 85 W", (string?)warning.Attribute("Text"), StringComparison.Ordinal);
        Assert.Contains("可以向下调整", (string?)warning.Attribute("Text"), StringComparison.Ordinal);
        Assert.Contains("狂暴模式", (string?)warning.Attribute("Text"), StringComparison.Ordinal);
        Assert.Contains("自定义模式方案", (string?)warning.Attribute("Text"), StringComparison.Ordinal);
        Assert.True(descendants.IndexOf(warning) < descendants.IndexOf(pl1));
    }

    [Fact]
    public void FanEditor_UsesFourCompactColumnsAndCelsiusLabels()
    {
        var document = LoadMainWindowXaml();
        var fanContent = FindNamedElement(document, "FanDrawerContent");
        var uniformGrid = fanContent.Descendants().Single(element => element.Name.LocalName == "UniformGrid");
        Assert.Equal("4", (string?)uniformGrid.Attribute("Columns"));
        var pointLabel = fanContent.Descendants().Single(element =>
            element.Name.LocalName == "TextBlock" &&
            (string?)element.Attribute("Text") == "{Binding TemperatureLabel}");
        Assert.Equal("{Binding TemperatureLabel}", (string?)pointLabel.Attribute("Text"));
    }

    [Fact]
    public void GpuBadge_UsesGpuTitleWithIntegratedOrDiscreteSubtitle()
    {
        var document = LoadMainWindowXaml();
        var badge = FindNamedElement(document, "DeviceBadge");
        var label = FindNamedElement(badge, "DeviceLabel");
        var kind = FindNamedElement(badge, "GpuKindLabel");

        Assert.Equal("GPU", (string?)label.Attribute("Text"));
        Assert.Equal("Collapsed", (string?)kind.Attribute("Visibility"));

        var triggerValues = document.Descendants()
            .Where(element => element.Name.LocalName == "Setter" &&
                              (string?)element.Attribute("TargetName") == "GpuKindLabel")
            .Select(element => (string?)element.Attribute("Value"))
            .OfType<string>()
            .ToArray();
        Assert.Contains("独立", triggerValues);
        Assert.Contains("集成", triggerValues);
    }

    [Fact]
    public void CloseButtons_UseCenteredVectorIconsInsteadOfFontGlyphs()
    {
        var document = LoadMainWindowXaml();

        foreach (var name in new[] { "MainCloseButton", "DrawerCloseButton" })
        {
            var button = FindNamedElement(document, name);
            Assert.Null(button.Attribute("Content"));
            var icon = button.Descendants().Single(element =>
                (string?)element.Attribute(XamlNamespace + "Name") == $"{name}Icon");
            Assert.Equal("Center", (string?)icon.Attribute("HorizontalAlignment"));
            Assert.Equal("Center", (string?)icon.Attribute("VerticalAlignment"));
        }
    }

    [Fact]
    public void FanEditor_RemovesIndependentPresetsAndUsesClearOemDefaultAction()
    {
        var document = LoadMainWindowXaml();
        var text = string.Concat(document.DescendantNodes().OfType<XText>().Select(node => node.Value));
        var restore = FindNamedElement(document, "RestoreOemFanCurveButton");

        Assert.DoesNotContain("本地预设", text, StringComparison.Ordinal);
        Assert.DoesNotContain(document.Descendants(), element =>
            ((string?)element.Attribute("Click")) is "QuietFanPreset_Click" or
                "BalancedFanPreset_Click" or "CoolingFanPreset_Click");
        Assert.Equal("恢复该档位原厂默认曲线", (string?)restore.Attribute("Content"));
    }

    [Fact]
    public void PerformanceCards_UseFullModeNames()
    {
        var document = LoadMainWindowXaml();
        var labels = document.Descendants()
            .Where(element => element.Name.LocalName == "RadioButton" &&
                              (string?)element.Attribute("GroupName") == "PerformanceProfile")
            .Select(element => (string?)element.Attribute("Content"))
            .OfType<string>()
            .ToArray();

        Assert.Equal([
            "{Binding OfficeModeTitle}",
            "{Binding BalancedModeTitle}",
            "{Binding UiCopy.TurboTitle}",
            "{Binding UiCopy.CustomTitle}"
        ], labels);
    }

    [Fact]
    public void FanDrawer_BindsCurrentModeBadgeAndTintsReferenceSurface()
    {
        var document = LoadMainWindowXaml();
        var titleRow = FindNamedElement(document, "FanDrawerTitleRow");
        var badge = FindNamedElement(document, "CurrentFanProfileBadge");
        var badgeText = FindNamedElement(badge, "CurrentFanProfileText");
        var surface = FindNamedElement(document, "FanCurveChartSurface");

        Assert.Equal("Horizontal", (string?)titleRow.Attribute("Orientation"));
        Assert.Same(titleRow, badge.Parent);
        Assert.Equal("{Binding FanAccentSurfaceColor}", (string?)badge.Attribute("Background"));
        Assert.Equal("{Binding FanAccentColor}", (string?)badge.Attribute("BorderBrush"));
        Assert.Equal("{Binding CurrentFanProfileLabel}", (string?)badgeText.Attribute("Text"));
        Assert.Equal("{Binding FanAccentSurfaceColor}", (string?)surface.Attribute("Background"));
    }

    [Fact]
    public void FanDrawer_UsesDraggableChartAndKeepsNumericInputs()
    {
        var document = LoadMainWindowXaml();
        var chart = FindNamedElement(document, "FanCurveChart");
        var fanContent = FindNamedElement(document, "FanDrawerContent");

        Assert.Equal("{Binding FanPoints}", (string?)chart.Attribute("ItemsSource"));
        Assert.Equal("{Binding FanAccentColor}", (string?)chart.Attribute("AccentBrush"));
        Assert.Contains(fanContent.Descendants(), element =>
            element.Name.LocalName == "TextBox" &&
            ((string?)element.Attribute("Text"))?.Contains("DutyPercent", StringComparison.Ordinal) == true);
    }

    [Fact]
    public void FanDrawer_ProvidesWiderEditingSurfaceAndRoomForAxisLabels()
    {
        var document = LoadMainWindowXaml();
        var drawer = FindNamedElement(document, "DrawerPanel");
        var chart = FindNamedElement(document, "FanCurveChartSurface");

        Assert.Equal("360", (string?)drawer.Attribute("MinWidth"));
        Assert.True(double.Parse((string?)chart.Attribute("Height") ?? "0") >= 160);
    }

    [Fact]
    public void FanDrawer_PresentsResponseControlsBeforeChannelSelectionAndCurveTable()
    {
        var document = LoadMainWindowXaml();
        var fanContent = FindNamedElement(document, "FanDrawerContent");
        var descendants = fanContent.Descendants().ToList();
        var responseIndex = descendants.IndexOf(FindNamedElement(fanContent, "FanResponseControls"));
        var chartIndex = descendants.IndexOf(FindNamedElement(fanContent, "FanCurveChartSurface"));
        var channelIndex = descendants.IndexOf(FindNamedElement(fanContent, "CpuFanRadio"));
        var editorsIndex = descendants.IndexOf(FindNamedElement(fanContent, "FanPointEditors"));

        Assert.True(chartIndex >= 0);
        Assert.True(responseIndex >= 0 && responseIndex < channelIndex);
        Assert.True(channelIndex < chartIndex && chartIndex < editorsIndex);
    }

    [Fact]
    public void FanDrawer_PutsIndependentOutputBeforeSensitivityAndKeepsSensitivityReadable()
    {
        var document = LoadMainWindowXaml();
        var panel = FindNamedElement(document, "FanResponseControls");
        var independentLabel = FindNamedElement(panel, "FanIndependentOutputLabel");
        var sensitivityLabel = FindNamedElement(panel, "FanTransitionSensitivityLabel");
        var sensitivityValue = FindNamedElement(panel, "FanTransitionSensitivityValue");

        Assert.Equal("0", (string?)independentLabel.Attribute("Grid.Row"));
        Assert.Equal("1", (string?)sensitivityLabel.Attribute("Grid.Row"));
        Assert.Equal("2", (string?)sensitivityValue.Parent?.Attribute("Grid.Row"));
        Assert.Equal("Wrap", (string?)sensitivityLabel.Attribute("TextWrapping"));
    }

    [Fact]
    public void FanDrawer_ChannelControlsAndHeaderUseCurrentModeAccent()
    {
        var document = LoadMainWindowXaml();
        var fanIcon = FindNamedElement(document, "FanDrawerHeaderIcon");

        foreach (var name in new[] { "CpuFanRadio", "GpuFanRadio" })
        {
            var radio = FindNamedElement(document, name);
            Assert.Equal("{StaticResource FanChannelRadioStyle}", (string?)radio.Attribute("Style"));
            Assert.Equal("{Binding FanAccentColor}", (string?)radio.Attribute("BorderBrush"));
            Assert.Null((string?)radio.Attribute("Foreground"));
        }

        Assert.Contains(fanIcon.Descendants(), element =>
            element.Name.LocalName == "Path" &&
            (string?)element.Attribute("Fill") == "{Binding FanAccentColor}");
    }

    [Fact]
    public void FanDrawer_ChannelControlsKeepReadableTextWhenSelected()
    {
        var document = LoadMainWindowXaml();
        var style = document.Descendants().Single(element =>
            element.Name.LocalName == "Style" &&
            element.Attributes().Any(attribute =>
                attribute.Name.LocalName == "Key" && attribute.Value == "FanChannelRadioStyle"));

        Assert.Contains(style.Descendants(), element =>
            element.Name.LocalName == "Setter" &&
            (string?)element.Attribute("Property") == "Foreground" &&
            (string?)element.Attribute("Value") == "White");
    }

    [Fact]
    public void FanDrawer_BindsResponseControlsForImmediateApplyAndKeepsExplicitRestore()
    {
        var document = LoadMainWindowXaml();
        var panel = FindNamedElement(document, "FanResponseControls");
        var sensitivityToggle = FindNamedElement(panel, "FanTransitionSensitivityToggle");
        var sensitivitySlider = FindNamedElement(panel, "FanTransitionSensitivitySlider");
        var independentToggle = FindNamedElement(panel, "FanIndependentOutputToggle");
        var restore = FindNamedElement(document, "RestoreOemFanCurveButton");

        var panelText = string.Concat(panel.Descendants().Select(element => (string?)element.Attribute("Text")));
        Assert.Contains("风扇转换延迟", panelText, StringComparison.Ordinal);
        Assert.Contains("独立输出", panelText, StringComparison.Ordinal);
        Assert.Equal("{Binding IsFanTransitionSensitivityEnabled, Mode=TwoWay}", (string?)sensitivityToggle.Attribute("IsChecked"));
        Assert.Equal("{Binding IsFanTransitionSensitivityEnabled}", (string?)sensitivitySlider.Attribute("IsEnabled"));
        Assert.Equal("{Binding FanTransitionSensitivity, Mode=TwoWay}", (string?)sensitivitySlider.Attribute("Value"));
        Assert.Equal("0", (string?)sensitivitySlider.Attribute("Minimum"));
        Assert.Equal("5000", (string?)sensitivitySlider.Attribute("Maximum"));
        Assert.Equal("100", (string?)sensitivitySlider.Attribute("TickFrequency"));
        Assert.Equal("100", (string?)sensitivitySlider.Attribute("SmallChange"));
        Assert.Equal("100", (string?)sensitivitySlider.Attribute("LargeChange"));
        Assert.Equal("True", (string?)sensitivitySlider.Attribute("IsSnapToTickEnabled"));
        Assert.Contains("0.5%", (string?)sensitivitySlider.Attribute("ToolTip"), StringComparison.Ordinal);
        Assert.Contains("5000 ms", (string?)sensitivitySlider.Attribute("ToolTip"), StringComparison.Ordinal);
        Assert.Equal("{Binding IsFanIndependentOutputEnabled, Mode=TwoWay}", (string?)independentToggle.Attribute("IsChecked"));
        Assert.Equal("FanResponseToggle_Click", (string?)independentToggle.Attribute("Click"));
        Assert.Equal("FanResponseToggle_Click", (string?)sensitivityToggle.Attribute("Click"));
        Assert.Equal("FanResponseSlider_ValueChanged", (string?)sensitivitySlider.Attribute("ValueChanged"));
        Assert.DoesNotContain(document.Descendants(), element =>
            (string?)element.Attribute("Click") == "ApplyFanCurve_Click");
        Assert.Equal("RestoreOemFanCurve_Click", (string?)restore.Attribute("Click"));
        Assert.Equal("{Binding FanEditingAvailabilityText}",
            (string?)FindNamedElement(document, "FanEditingAvailabilityText").Attribute("Text"));
    }

    [Fact]
    public void FanDrawer_DisablesFirmwareLockedPresetEditingAndLabelsDelayInMilliseconds()
    {
        var document = LoadMainWindowXaml();
        var chart = FindNamedElement(document, "FanCurveChartSurface");
        var response = FindNamedElement(document, "FanResponseControls");
        var delayValue = FindNamedElement(response, "FanTransitionSensitivityValue");
        var availability = FindNamedElement(document, "FanEditingAvailabilityText");

        Assert.Equal("{Binding CanEditFanSettings}", (string?)chart.Attribute("IsEnabled"));
        Assert.Equal("{Binding CanEditFanSettings}", (string?)response.Attribute("IsEnabled"));
        Assert.Equal("{Binding FanTransitionSensitivityText}", (string?)delayValue.Attribute("Text"));
        Assert.Equal("{Binding FanEditingAvailabilityText}", (string?)availability.Attribute("Text"));
    }

    [Fact]
    public void FanDrawer_TintsReadOnlyReferenceUsingCurrentProfileColors()
    {
        var document = LoadMainWindowXaml();
        var chart = FindNamedElement(document, "FanCurveChartSurface");
        var heading = FindNamedElement(document, "FanCurveModeHeading");

        Assert.Equal("{Binding FanAccentSurfaceColor}", (string?)chart.Attribute("Background"));
        Assert.Equal("{Binding FanCurveSurfaceOpacity}", (string?)chart.Attribute("Opacity"));
        Assert.Equal("{Binding FanCurveModeHeading}", (string?)heading.Attribute("Text"));
    }

    [Fact]
    public void PerformanceDrawer_BoostSelectionCommitsImmediatelyToViewModel()
    {
        var document = LoadMainWindowXaml();
        var selector = FindNamedElement(document, "ProcessorBoostModeSelector");

        Assert.Equal(
            "{Binding SelectedProcessorBoostMode, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}",
            (string?)selector.Attribute("SelectedValue"));
    }

    [Fact]
    public void PerformanceFrequencyLimit_UsesSupportedRangeAndWarnsAboutDelayedEffect()
    {
        var document = LoadMainWindowXaml();
        var slider = FindNamedElement(document, "ProcessorFrequencyLimitSlider");
        var warning = FindNamedElement(document, "ProcessorFrequencyDelayWarning");

        Assert.Equal("1000", (string?)slider.Attribute("Minimum"));
        Assert.Equal("6000", (string?)slider.Attribute("Maximum"));
        Assert.Contains("5–10 秒", (string?)warning.Attribute("Text"), StringComparison.Ordinal);
        Assert.Equal("#D9363E", (string?)warning.Attribute("Foreground"));
        Assert.True(double.Parse((string?)warning.Attribute("FontSize") ?? "99") < 9);
    }

    [Fact]
    public void CustomPerformancePanel_IsCustomOnlyAndUsesImmediateApplySurface()
    {
        var document = LoadMainWindowXaml();
        var panel = FindNamedElement(document, "CustomAdvancedPerformancePanel");
        var text = string.Join(" ", panel.Descendants().Select(element =>
            (string?)element.Attribute("Text") ?? (string?)element.Attribute("Content")));

        foreach (var label in new[]
                 {
                     "CPU 温度墙", "瞬时高负载功耗墙 (PL4)", "CPU 高级性能菜单",
                     "GPU 超性能开关", "Configurable TGP Target", "Dynamic Boost",
                     "核心频率偏移", "显存频率偏移"
                 })
            Assert.Contains(label, text, StringComparison.Ordinal);
        Assert.Contains(panel.Descendants(), element =>
            (string?)element.Attribute("IsEnabled") == "{Binding CanEditAdvancedPerformance}");
        Assert.DoesNotContain(panel.Descendants(), element =>
            (string?)element.Attribute("Click") == "ApplyCustomPerformance_Click");
        Assert.Contains("仅在自定义模式", text, StringComparison.Ordinal);
    }

    [Fact]
    public void SettingsDrawer_OffersSelectionStopAndRestoreForPossibleConflictServices()
    {
        var document = LoadMainWindowXaml();
        var panel = FindNamedElement(document, "ConflictServicesPanel");
        var list = FindNamedElement(panel, "ConflictServicesList");
        var stopButton = FindNamedElement(panel, "StopConflictServicesButton");
        var restoreButton = FindNamedElement(panel, "RestoreConflictServicesButton");

        Assert.Equal("{Binding ConflictServices}", (string?)list.Attribute("ItemsSource"));
        Assert.Contains(list.Descendants(), element =>
            element.Name.LocalName == "CheckBox" &&
            (string?)element.Attribute("IsChecked") == "{Binding IsSelected, Mode=TwoWay}");
        Assert.Equal("{Binding CanStopSelectedConflictServices}", (string?)stopButton.Attribute("IsEnabled"));
        Assert.Equal("StopSelectedConflictServices_Click", (string?)stopButton.Attribute("Click"));
        Assert.Equal("{Binding CanRestoreSelectedConflictServices}", (string?)restoreButton.Attribute("IsEnabled"));
        Assert.Equal("RestoreSelectedConflictServices_Click", (string?)restoreButton.Attribute("Click"));
        Assert.Contains("可能冲突服务检测", document.ToString(), StringComparison.Ordinal);
        Assert.Contains("不会修改开机启动类型", document.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void FinalPolish_RadarHelpLeadsWithPracticalGuidanceAndBalancedPerformanceIsRetracted()
    {
        var document = LoadMainWindowXaml();
        var tooltip = (string?)FindNamedElement(document, "RadarHelpTooltipText").Attribute("Text");
        var codeBehind = File.ReadAllText(LocateRepositoryFile("src", "RevoHelper.App", "MainWindow.xaml.cs"));

        Assert.StartsWith("仅供大致参考。用户可在风扇曲线上微调", tooltip, StringComparison.Ordinal);
        Assert.Contains("tradeoff.Performance - 8", codeBehind, StringComparison.Ordinal);
    }

    [Fact]
    public void FinalPolish_QuickEntriesUseFeatureSpecificColoredIconsAndHeaders()
    {
        var document = LoadMainWindowXaml();
        var expectedIconSurfaces = new Dictionary<string, string>
        {
            ["GpuOutputQuickIconTile"] = "#EAF4FF",
            ["LightingQuickIconTile"] = "#F2ECFF",
            ["WaterCoolingQuickIconTile"] = "#E8F5FF",
            ["KeyboardQuickIconTile"] = "#EEF0FF",
            ["BatteryQuickIconTile"] = "#FFF2E5",
            ["DisplayQuickIconTile"] = "#F0EEFF",
            ["OtherQuickIconTile"] = "#EAF4FF"
        };

        foreach (var (name, background) in expectedIconSurfaces)
            Assert.Equal(background, (string?)FindNamedElement(document, name).Attribute("Background"));

        foreach (var name in new[] { "LightingDrawerHeaderIcon", "KeyboardDrawerHeaderIcon", "BatteryDrawerHeaderIcon" })
            Assert.NotNull(FindNamedElement(document, name));

        var codeBehind = File.ReadAllText(LocateRepositoryFile("src", "RevoHelper.App", "MainWindow.xaml.cs"));
        Assert.DoesNotContain("HomeDrawer.Lighting or\r\n            HomeDrawer.Keyboard or HomeDrawer.Battery", codeBehind, StringComparison.Ordinal);
    }

    [Fact]
    public void FinalPolish_SelectorsUseOneRoundedFocusNeutralStyle()
    {
        var document = LoadMainWindowXaml();
        var style = document.Descendants().Single(element =>
            element.Name.LocalName == "Style" &&
            (string?)element.Attribute(XamlNamespace + "Key") == "ModernComboBoxStyle");

        Assert.Contains(style.Elements(), setter =>
            setter.Name.LocalName == "Setter" &&
            (string?)setter.Attribute("Property") == "FocusVisualStyle" &&
            (string?)setter.Attribute("Value") == "{x:Null}");
        Assert.Contains(style.Descendants(), border =>
            border.Name.LocalName == "Border" && (string?)border.Attribute("CornerRadius") == "9");

        foreach (var name in new[]
                 {
                     "ProcessorBoostModeSelector", "LightingEffectCombo", "LightingDirectionCombo",
                     "LightingSleepTimerSelector", "LiquidCoolingDeviceSelector", "DeepSleepMinuteSelector",
                     "DisplayRefreshRateSelector", "ShutdownMinuteSelector", "WindowScaleSelector"
                 })
            Assert.Equal("{StaticResource ModernComboBoxStyle}",
                (string?)FindNamedElement(document, name).Attribute("Style"));
    }

    [Fact]
    public void FinalPolish_ComboBoxesUseReadableType()
    {
        var document = LoadMainWindowXaml();
        var comboStyle = document.Descendants().Single(element =>
            element.Name.LocalName == "Style" &&
            (string?)element.Attribute(XamlNamespace + "Key") == "ModernComboBoxStyle");
        var itemStyle = document.Descendants().Single(element =>
            element.Name.LocalName == "Style" &&
            (string?)element.Attribute(XamlNamespace + "Key") == "LiquidCoolingDeviceComboItemStyle");

        Assert.Contains(comboStyle.Elements(), setter =>
            setter.Name.LocalName == "Setter" &&
            (string?)setter.Attribute("Property") == "FontSize" &&
            (string?)setter.Attribute("Value") == "10");
        Assert.Contains(itemStyle.Elements(), setter =>
            setter.Name.LocalName == "Setter" &&
            (string?)setter.Attribute("Property") == "FontSize" &&
            (string?)setter.Attribute("Value") == "10");

        foreach (var name in new[]
                 {
                     "ProcessorBoostModeSelector", "LightingEffectCombo", "LightingDirectionCombo",
                     "LightingSleepTimerSelector", "LiquidCoolingDeviceSelector", "DeepSleepMinuteSelector",
                     "DisplayRefreshRateSelector", "ShutdownMinuteSelector", "WindowScaleSelector"
                 })
        {
            var localFontSize = (string?)FindNamedElement(document, name).Attribute("FontSize");
            Assert.True(localFontSize is null or "10", $"{name} overrides the shared readable font size with {localFontSize}.");
        }
    }

    [Fact]
    public void FinalPolish_ComboBoxSelectionUsesDisplayMemberTemplateSelector()
    {
        var document = LoadMainWindowXaml();
        var comboStyle = document.Descendants().Single(element =>
            element.Name.LocalName == "Style" &&
            (string?)element.Attribute(XamlNamespace + "Key") == "ModernComboBoxStyle");
        var selectionPresenter = comboStyle.Descendants().Single(element =>
            element.Name.LocalName == "ContentPresenter" &&
            (string?)element.Attribute("Content") == "{TemplateBinding SelectionBoxItem}");

        Assert.Equal("{TemplateBinding ItemTemplateSelector}",
            (string?)selectionPresenter.Attribute("ContentTemplateSelector"));
    }

    [Fact]
    public void PartialEnglish_PrimaryHomeAndSettingsCopyUseLocalizedBindings()
    {
        var document = LoadMainWindowXaml();

        Assert.Equal("{Binding UiCopy.HomeTitle}",
            (string?)FindNamedElement(document, "HomeTitle").Attribute("Text"));
        Assert.Equal("{Binding OfficeModeTitle}",
            (string?)FindNamedElement(document, "OfficeProfileRadio").Attribute("Content"));
        Assert.Equal("{Binding UiCopy.GpuOutputTitle}",
            (string?)FindNamedElement(document, "GpuOutputQuickTitle").Attribute("Text"));
        Assert.Equal("{Binding UiCopy.PerformanceAdjustmentTitle}",
            (string?)FindNamedElement(document, "PerformanceEntryTitle").Attribute("Text"));

        var language = FindNamedElement(document, "UiLanguageSelector");
        Assert.Equal("{Binding UiLanguageOptions}", (string?)language.Attribute("ItemsSource"));
        Assert.Equal("{Binding SelectedUiLanguageCode, Mode=TwoWay}",
            (string?)language.Attribute("SelectedValue"));
        Assert.Equal("Label", (string?)language.Attribute("DisplayMemberPath"));
        Assert.Equal("Code", (string?)language.Attribute("SelectedValuePath"));
    }

    [Fact]
    public void FinalPolish_ComboBoxesUseTransparentDropDownToggle()
    {
        var document = LoadMainWindowXaml();
        var comboStyle = document.Descendants().Single(element =>
            element.Name.LocalName == "Style" &&
            (string?)element.Attribute(XamlNamespace + "Key") == "ModernComboBoxStyle");

        var dropDownToggle = comboStyle.Descendants().Single(element =>
            element.Name.LocalName == "ToggleButton");
        var toggleTemplate = dropDownToggle.Elements().Single(element =>
            element.Name.LocalName == "ToggleButton.Template");
        Assert.Contains(toggleTemplate.Descendants(), element =>
            element.Name.LocalName == "Border" &&
            (string?)element.Attribute("Background") == "Transparent");
    }

    [Fact]
    public void FinalPolish_ComboBoxCandidatesUseNeutralFeedbackColors()
    {
        var document = LoadMainWindowXaml();
        var itemStyle = document.Descendants().Single(element =>
            element.Name.LocalName == "Style" &&
            (string?)element.Attribute(XamlNamespace + "Key") == "LiquidCoolingDeviceComboItemStyle");
        var setterValues = itemStyle.Descendants()
            .Where(element => element.Name.LocalName == "Setter")
            .Select(element => (string?)element.Attribute("Value"))
            .ToArray();

        Assert.Contains("#F3F6FA", setterValues);
        Assert.Contains("#E9EEF5", setterValues);
        Assert.DoesNotContain("#EAF4FF", setterValues);
        Assert.DoesNotContain("#DCEEFF", setterValues);
    }

    [Fact]
    public void FinalPolish_KeyboardCardsAreCompactEnoughForSixRowsPerViewport()
    {
        var document = LoadMainWindowXaml();

        foreach (var name in new[] { "WifiControlCard", "BluetoothControlCard", "TouchpadControlCard", "WebcamControlCard" })
        {
            var card = FindNamedElement(document, name);
            Assert.Equal("54", (string?)card.Attribute("Height"));
            Assert.Equal("8", (string?)card.Attribute("Padding"));
        }

        Assert.Equal("0,0,0,5", (string?)FindNamedElement(document, "WifiControlCard").Attribute("Margin"));
    }

    [Fact]
    public void FinalPolish_LightingColorEditorUsesRoundedFocusNeutralChrome()
    {
        var document = LoadMainWindowXaml();
        var editor = FindNamedElement(document, "LightingColorHexBox");
        var style = document.Descendants().Single(element =>
            element.Name.LocalName == "Style" &&
            (string?)element.Attribute(XamlNamespace + "Key") == "ModernTextBoxStyle");

        Assert.Equal("{StaticResource ModernTextBoxStyle}", (string?)editor.Attribute("Style"));
        Assert.Contains(style.Elements(), setter =>
            setter.Name.LocalName == "Setter" &&
            (string?)setter.Attribute("Property") == "FocusVisualStyle" &&
            (string?)setter.Attribute("Value") == "{x:Null}");
        Assert.Contains(style.Descendants(), border =>
            border.Name.LocalName == "Border" && (string?)border.Attribute("CornerRadius") == "8");
    }

    [Fact]
    public void FinalPolish_OtherAndLiquidCoolingTypographyKeepAQuietHierarchy()
    {
        var document = LoadMainWindowXaml();
        var other = FindNamedElement(document, "OtherDrawerContent");
        var schedule = FindNamedElement(other, "ScheduleShutdownButton");
        var cancel = FindNamedElement(other, "CancelShutdownButton");
        var countdown = FindNamedElement(other, "ShutdownCountdownText");
        var pump = FindNamedElement(document, "LiquidCoolingPumpValueText");

        Assert.Equal("9", (string?)schedule.Attribute("FontSize"));
        Assert.Equal("9", (string?)cancel.Attribute("FontSize"));
        Assert.Contains(countdown.Descendants(), setter =>
            setter.Name.LocalName == "Setter" &&
            (string?)setter.Attribute("Property") == "FontSize" &&
            (string?)setter.Attribute("Value") == "16");
        Assert.True(double.Parse((string?)pump.Attribute("FontSize") ?? "99") <= 15);
    }

    [Fact]
    public void PostReview_ChargeLimitImplementationRemainsInSourceButNotInPublishedUi()
    {
        var document = LoadMainWindowXaml();
        var drawer = FindNamedElement(document, "BatteryDrawerContent");
        Assert.DoesNotContain("BatteryChargeLimit", drawer.ToString(), StringComparison.Ordinal);

        var controller = File.ReadAllText(LocateRepositoryFile(
            "src", "RevoHelper.Hardware.Windows", "OemControl", "GcuQuickControlsController.cs"));
        Assert.Contains("SetBatteryChargeLimitAsync", controller, StringComparison.Ordinal);
        Assert.Contains("BatteryChargingLimit", controller, StringComparison.Ordinal);
    }

    [Fact]
    public void PostReview_ReadOnlyFanAreaIsMutedAsOneUnit()
    {
        var document = LoadMainWindowXaml();

        foreach (var name in new[] { "FanCurveChartSurface", "FanPointEditors", "FanResponseControls" })
            Assert.Equal("{Binding FanCurveSurfaceOpacity}",
                (string?)FindNamedElement(document, name).Attribute("Opacity"));
    }

    [Fact]
    public void PostReview_HomeSecondaryEntriesHaveDescriptionsAndFooterIsQuieter()
    {
        var document = LoadMainWindowXaml();
        var block = FindNamedElement(document, "HomeStatusBlock");

        Assert.Equal("{Binding UiCopy.BatterySubtitle}", (string?)FindNamedElement(document, "BatteryHealthQuickSubtitle").Attribute("Text"));
        Assert.Equal("{Binding UiCopy.DisplaySubtitle}", (string?)FindNamedElement(document, "DisplayColorQuickSubtitle").Attribute("Text"));
        Assert.Equal("Bottom", (string?)block.Attribute("VerticalAlignment"));
        Assert.Equal("0,0,0,3", (string?)block.Attribute("Margin"));
        Assert.Equal("#8795A8", (string?)FindNamedElement(document, "HomeStatusText").Attribute("Foreground"));
        Assert.Equal("#AAB4C2", (string?)FindNamedElement(document, "HomeVersionFooter").Attribute("Foreground"));
    }

    [Fact]
    public void PostReview_ProductLogoUsesRoundedTileAndTransparentAssetCorners()
    {
        var document = LoadMainWindowXaml();
        var script = File.ReadAllText(LocateRepositoryFile("tools", "Generate-AppIcon.ps1"));

        Assert.Equal("11", (string?)FindNamedElement(document, "HomeBrandLogoTile").Attribute("CornerRadius"));
        Assert.Contains("Color]::Transparent", script, StringComparison.Ordinal);
        Assert.Contains("AddArc", script, StringComparison.Ordinal);
        Assert.Contains("FillPath", script, StringComparison.Ordinal);
    }

    private static XDocument LoadMainWindowXaml()
    {
        var document = XDocument.Load(LocateRepositoryFile("src", "RevoHelper.App", "MainWindow.xaml"));
        // These historical structural contracts inspect copy, binding paths, base typography
        // and light-mode neutral colors. Snapshot 3's runtime translation/font growth/theme
        // are checked on real WPF elements by the dedicated localization and visual tests.
        // Unwrap only the actual presentation extensions and registered neutral aliases:
        // unknown markup/resources stay untouched and therefore cannot satisfy old literals.
        foreach (var attribute in document.Descendants().Attributes())
            attribute.Value = LogicalPresentationValue(attribute.Value);
        return document;
    }

    [Theory]
    [InlineData("{presentation:UiText '测试'}", "测试")]
    [InlineData("{presentation:UiText Value={Binding Label}}", "{Binding Label}")]
    [InlineData("{presentation:UiText Value={Binding Label, Mode=OneWay}}", "{Binding Label, Mode=OneWay}")]
    [InlineData("{presentation:UiFont 9.2}", "9.2")]
    [InlineData("{DynamicResource Neutral8795A8Brush}", "#8795A8")]
    [InlineData("{presentation:UiText Value={Binding Label}", "{presentation:UiText Value={Binding Label}")]
    [InlineData("{presentation:UiText Value={Unknown Label}}", "{presentation:UiText Value={Unknown Label}}")]
    [InlineData("{other:UiText '测试'}", "{other:UiText '测试'}")]
    [InlineData("{presentation:UiFont invalid}", "{presentation:UiFont invalid}")]
    [InlineData("{DynamicResource NeutralNOTREGISTEREDBrush}", "{DynamicResource NeutralNOTREGISTEREDBrush}")]
    [InlineData("{DynamicResource CpuBrush}", "{DynamicResource CpuBrush}")]
    [InlineData("{StaticResource Neutral8795A8Brush}", "{StaticResource Neutral8795A8Brush}")]
    public void LogicalMarkupView_OnlyUnwrapsKnownExactPresentationForms(string raw, string expected) =>
        Assert.Equal(expected, LogicalPresentationValue(raw));

    private static string LogicalPresentationValue(string value)
    {
        const string literalPrefix = "{presentation:UiText '";
        if (value.StartsWith(literalPrefix, StringComparison.Ordinal) && value.EndsWith("'}", StringComparison.Ordinal))
        {
            var literal = value[literalPrefix.Length..^2];
            if (!literal.Contains('\'')) return literal;
        }

        const string bindingPrefix = "{presentation:UiText Value=";
        if (value.StartsWith(bindingPrefix, StringComparison.Ordinal) && value.EndsWith("}}", StringComparison.Ordinal))
        {
            var binding = value[bindingPrefix.Length..^1];
            if ((binding == "{Binding}" || binding.StartsWith("{Binding ", StringComparison.Ordinal)) &&
                BalancedBraces(binding)) return binding;
        }

        var font = Regex.Match(value, @"^\{presentation:UiFont (?<size>\d+(?:\.\d+)?)\}$", RegexOptions.CultureInvariant);
        if (font.Success && double.TryParse(font.Groups["size"].Value, NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture, out var size) && double.IsFinite(size))
            return font.Groups["size"].Value;

        foreach (var (color, key) in AppTheme.NeutralBrushKeys)
            if (value == $"{{DynamicResource {key}}}") return color;
        return value;
    }

    private static bool BalancedBraces(string value)
    {
        var depth = 0;
        foreach (var character in value)
        {
            if (character == '{') depth++;
            else if (character == '}' && --depth < 0) return false;
        }
        return depth == 0;
    }

    private static string LocateRepositoryFile(params string[] relativePath)
    {
        foreach (var start in new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() })
        {
            var directory = new DirectoryInfo(start);
            while (directory is not null)
            {
                var candidate = Path.Combine([directory.FullName, .. relativePath]);
                if (File.Exists(candidate))
                {
                    return candidate;
                }

                directory = directory.Parent;
            }
        }

        throw new FileNotFoundException($"Could not locate {Path.Combine(relativePath)} from the test workspace.");
    }

    private static XElement FindNamedElement(XContainer container, string name) =>
        container.Descendants().Single(element => (string?)element.Attribute(XamlNamespace + "Name") == name);
}
