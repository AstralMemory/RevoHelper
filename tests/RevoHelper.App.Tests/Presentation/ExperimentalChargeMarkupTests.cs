using System.Xml.Linq;
using RevoHelper.App.Presentation;

namespace RevoHelper.App.Tests.Presentation;

public sealed class ExperimentalChargeMarkupTests
{
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

    [Fact]
    public void ChargeCardUsesOneWayToggleAndOnlyThreeUserCommittedLimitPositions()
    {
        var document = XDocument.Load(Locate("src", "RevoHelper.App", "MainWindow.xaml"));
        var card = Named(document, "ExperimentalChargeCard");
        Assert.Contains(Named(document, "BatteryDrawerContent").Descendants(), element => element == card);
        var slider = Named(document, "ExperimentalChargeLimitSlider");
        Assert.Single(card.Descendants(), element => element.Name.LocalName == "Slider");
        Assert.Equal("0", (string?)slider.Attribute("Minimum"));
        Assert.Equal("2", (string?)slider.Attribute("Maximum"));
        Assert.Equal("1", (string?)slider.Attribute("TickFrequency"));
        Assert.Equal("True", (string?)slider.Attribute("IsSnapToTickEnabled"));
        Assert.Contains("SelectedLimitIndex", (string?)slider.Attribute("Value"));
        Assert.Null(slider.Attribute("ValueChanged"));
        var ticks = Named(document, "ExperimentalChargeLimitTicks");
        Assert.Equal("TickBar", ticks.Name.LocalName);
        Assert.Equal("0", (string?)ticks.Attribute("Minimum"));
        Assert.Equal("2", (string?)ticks.Attribute("Maximum"));
        Assert.Equal("1", (string?)ticks.Attribute("TickFrequency"));
        Assert.Equal("5", (string?)ticks.Attribute("Height"));
        Assert.Equal("10", (string?)ticks.Attribute("ReservedSpace"));
        Assert.Equal("ExperimentalChargeLimit_Commit", (string?)slider.Attribute("PreviewMouseLeftButtonUp"));
        Assert.Equal("ExperimentalChargeLimit_KeyUp", (string?)slider.Attribute("KeyUp"));
        var toggle = Named(document, "ExperimentalChargeToggle");
        Assert.Equal("{Binding Experiments.Charge.CanToggle}", (string?)toggle.Attribute("IsEnabled"));
        Assert.Equal("{Binding Experiments.Charge.IsEnabled, Mode=OneWay}", (string?)toggle.Attribute("IsChecked"));
        var toggleStyle = Assert.Single(toggle.Descendants(), element => element.Name.LocalName == "Style");
        Assert.Equal("{StaticResource SettingsToggleStyle}", (string?)toggleStyle.Attribute("BasedOn"));
        var disabledTrigger = Assert.Single(toggleStyle.Descendants(), element => element.Name.LocalName == "Trigger" &&
            (string?)element.Attribute("Property") == "IsEnabled" && (string?)element.Attribute("Value") == "False");
        Assert.Contains(disabledTrigger.Elements(), element => element.Name.LocalName == "Setter" &&
            (string?)element.Attribute("Property") == "Opacity" && (string?)element.Attribute("Value") == "0.45");
        Assert.Equal("ExperimentalChargeToggle_Click", (string?)toggle.Attribute("Click"));
        Assert.Null(toggle.Attribute("ToolTip"));
        Assert.Equal("{Binding Experiments.Charge.CanUseNativeCareModes}",
            (string?)Named(document, "NativeBatteryCareModes").Attribute("IsEnabled"));
        Assert.Equal("Wrap", (string?)Named(document, "ExperimentalChargeRiskText").Attribute("TextWrapping"));
        Assert.Equal("Wrap", (string?)Named(document, "ExperimentalChargeStatusText").Attribute("TextWrapping"));
        Assert.Contains(Named(document, "ExperimentalChargeRiskText").Ancestors(), element => element.Name.LocalName == "ToolTip");
        Assert.DoesNotContain(Named(document, "ExperimentalChargeDelayText").Ancestors(), element => element.Name.LocalName == "ToolTip");
        Assert.Equal("#D9363E", (string?)Named(document, "ExperimentalChargeDelayText").Attribute("Foreground"));
        Assert.Null(Named(document, "ExperimentalChargeHelpIcon").Attribute("MouseDown"));
        Assert.Contains(Named(document, "PowerModeFollowHelpTooltip").Descendants(), element =>
            (string?)element.Attribute("Text") == "{Binding UiCopy.PowerModeFollowHint}");
        Assert.Contains(Named(document, "ExperimentalChargeNoLimitText").Descendants(), element =>
            element.Name.LocalName == "DataTrigger" && (string?)element.Attribute("Value") == "100");
        var handlers = File.ReadAllText(Locate("src", "RevoHelper.App", "MainWindow.ExperimentalCharge.cs"));
        Assert.Contains("if (!IsLoaded", handlers);
        Assert.Contains("CommitSelectedLimitAsync", handlers);
        Assert.Contains("SetEnabledAsync(enabled)", handlers);
    }

    [Fact]
    public void BothLanguagesExplainTheSameConcreteEffectsOnlyInHoverNotes()
    {
        foreach (var copy in new[] { LocalizedUiCopy.Chinese, LocalizedUiCopy.English })
        {
            Assert.NotEmpty(copy.ExperimentalChargeDefault);
            Assert.NotEmpty(copy.ExperimentalChargeConfirmQuestion);
            Assert.DoesNotContain("MRO24", copy.ExperimentalChargeRisk);
            Assert.DoesNotContain("1.25", copy.ExperimentalChargeRisk);
        }
        Assert.Contains("已在机械革命耀世实测", LocalizedUiCopy.Chinese.ExperimentalChargeRisk);
        Assert.Contains("不按品牌或型号限制", LocalizedUiCopy.Chinese.ExperimentalChargeRisk);
        Assert.Contains("接口和原值检查", LocalizedUiCopy.Chinese.ExperimentalChargeRisk);
        Assert.Contains("Tested on MECHREVO YAOSHI", LocalizedUiCopy.English.ExperimentalChargeRisk);
        Assert.Contains("no brand or model restriction", LocalizedUiCopy.English.ExperimentalChargeRisk);
        Assert.Contains("interface and original-value checks", LocalizedUiCopy.English.ExperimentalChargeRisk);
        foreach (var word in new[] { "风扇", "功耗", "按键", "自动保存", "最小化", "隐藏到托盘继续", "拔电", "睡眠", "真正退出", "不能保证" })
            Assert.Contains(word, LocalizedUiCopy.Chinese.ExperimentalChargeRisk);
        foreach (var word in new[] { "fans", "power", "keys", "automatically save", "Minimizing", "hiding to the tray keeps", "Unplugging", "sleeping", "Actual exit", "not all side effects" })
            Assert.Contains(word, LocalizedUiCopy.English.ExperimentalChargeRisk);
        Assert.Equal("切换或应用可能需要15–30秒。", LocalizedUiCopy.Chinese.ExperimentalChargeDelay);
        Assert.Equal("Switching or applying may take 15–30 seconds.", LocalizedUiCopy.English.ExperimentalChargeDelay);
        Assert.DoesNotContain("请稍", LocalizedUiCopy.Chinese.ExperimentalChargeDelay);
        Assert.DoesNotContain("Please wait", LocalizedUiCopy.English.ExperimentalChargeDelay);
        Assert.Equal("不限制，按原生策略充电", LocalizedUiCopy.Chinese.ExperimentalChargeNoLimit);
        Assert.DoesNotContain("本机验证", LocalizedUiCopy.Chinese.ExperimentalChargeRisk);
        Assert.Equal("Experimental charge limiting is not connected; off by default.",
            UiTextCatalog.Translate("实验性限充未连接；默认关闭。", "en-US"));
        Assert.Equal("Restoring experimental charge limiting failed: unknown native detail",
            UiTextCatalog.Translate("恢复实验性限充失败：unknown native detail", "en-US"));
    }

    [Fact]
    public void ChargeToggleDirectlyAppliesWithoutAnyConfirmationDialog()
    {
        var source = File.ReadAllText(Locate("src", "RevoHelper.App", "MainWindow.ExperimentalCharge.cs"));
        Assert.Contains("await charge.SetEnabledAsync(enabled);", source);
        Assert.DoesNotContain("MessageBox", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ShowDialog", source, StringComparison.Ordinal);
        Assert.DoesNotContain("chargeRiskConfirmed", source, StringComparison.Ordinal);
    }

    [Fact]
    public void BackendStaticStatusCopyHasFaithfulEnglishDisplayTranslations()
    {
        foreach (var message in new[]
        {
            "默认关闭；仅手动启用。本功能会覆盖平台状态。",
            "发现未确认恢复的实验会话。请先使用恢复按钮，不自动启用。",
            "正在启动独立监护并检查控制接口。", "正在检查控制接口与原值；尚未确认生效。",
            "正在停止实验性限充并恢复原值。", "正在核对旧会话并恢复原值。",
            "没有需要恢复的实验会话；保持关闭。", "实验未启用，原值未改动。",
            "实验性限充运行中：已达上限，当前未充电。", "实验性限充运行中：低于上限，正在充电。",
            "实验性限充运行中：等待充电状态确认。", "工作进程已尝试恢复，监护正在复核。",
            "实验性限充已停止；平台、阈值和缓存观察已恢复，不能排除全部持久化副作用。",
            "恢复尚未确认。请勿再次启用，保留诊断日志并使用恢复按钮。",
            "恢复尚未确认；独立监护可能仍在处理，请勿再次启用。",
            "独立监护已退出，但恢复尚未确认。请使用恢复按钮。", "状态监测异常，已请求停止；请核对恢复。",
            "启用未完成，已请求恢复；请检查状态或使用恢复按钮。"
        })
        {
            Assert.True(UiTextCatalog.HasTranslation(message), message);
            var english = UiTextCatalog.Translate(message, "en-US");
            Assert.DoesNotContain(english, character => character is >= '\u4e00' and <= '\u9fff');
            Assert.Equal(message, UiTextCatalog.Translate(message, "zh-CN"));
        }
        Assert.Equal("Applying charge limiting; effectiveness has not been confirmed.",
            UiTextCatalog.Translate("正在应用限充，尚未确认生效。", "en-US"));
        Assert.Equal("Enabling experimental charge limiting failed: unknown native detail",
            UiTextCatalog.Translate("启用实验性限充失败：unknown native detail", "en-US"));
    }

    [Fact]
    public void CurrentChargeAndWaterRecoveryCopyTranslateWithoutRewritingUnknownNativeErrors()
    {
        foreach (var message in new[]
        {
            "限充未连接；默认关闭。",
            "正在应用限充，尚未确认生效。",
            "已开启：100% 不限制，按原生策略充电；未保持平台覆盖。",
            "限充已关闭；100% 透传模式没有平台覆盖需要恢复。",
            "正在停止限充并恢复原值。", "恢复未确认；请先使用恢复按钮，不继续启用或换档。",
            "限充已关闭，已有实验限制已恢复；按原生策略充电。",
            "水冷意外断开；10分钟内每3秒尝试重连同一设备。", "水冷已自动重连。",
            "水冷重连等待已超过10分钟；请手动连接。", "水冷重连已取消。", "水冷自动连接已取消。"
        })
        {
            Assert.True(UiTextCatalog.HasTranslation(message), message);
            Assert.DoesNotContain(UiTextCatalog.Translate(message, "en-US"), character => character is >= '\u4e00' and <= '\u9fff');
            Assert.Equal(message, UiTextCatalog.Translate(message, "zh-CN"));
        }
        Assert.Equal("Stopping charge limiting failed: WMI 原始错误 E123",
            UiTextCatalog.Translate("停止限充失败：WMI 原始错误 E123", "en-US"));
        Assert.Equal("Restoring charge limiting failed: WMI 原始错误 E123",
            UiTextCatalog.Translate("恢复限充失败：WMI 原始错误 E123", "en-US"));
        Assert.Equal("Charge-limit operation failed: A pending recovery record exists; no new charge-limit setting will be enabled.",
            UiTextCatalog.Translate("限充操作失败：存在待恢复记录，不启用新的充电档位。", "en-US"));
    }

    [Fact]
    public void SessionPauseAndResumeStatusesTranslateWhileNativeErrorDetailsStayUntouched()
    {
        foreach (var message in new[]
        {
            "请先确认原值恢复，不接管待恢复的限充会话。",
            "限充仍在恢复，请等待完成后再选择档位。",
            "请接通电源后手动启用限充；未启用的会话不会自动开始。",
            "限充启用未确认，保持关闭；不会自动重试，请检查恢复状态。",
            "启用前电源状态已变化；未启用新的限充会话。",
            "底层未确认目标限充会话已启用。",
            "已请求关闭限充，正在确认原值恢复。",
            "电源已断开，限充未启用。",
            "无法核对电源状态，保持关闭；不会自动重启限充。",
            "旧限充会话恢复尚未确认，不并行启用新会话。",
            "限充开关保持开启；正在安全暂停并恢复原值，完成后等待电源，不代表正在限制充电。",
            "限充开关保持开启；已安全暂停，等待电源稳定后恢复，并非正在限制充电。",
            "限充开关保持开启；已安全暂停，等待重新接通电源，并非正在限制充电。",
            "程序退出，限充已关闭并请求恢复原值。",
            "供电状态处理未完成，停止限充并保留恢复记录。"
        })
        {
            Assert.True(UiTextCatalog.HasTranslation(message), message);
            Assert.DoesNotContain(UiTextCatalog.Translate(message, "en-US"), character => character is >= '\u4e00' and <= '\u9fff');
            Assert.Equal(message, UiTextCatalog.Translate(message, "zh-CN"));
        }
        const string native = "WMI 原始错误 E123\nunaltered native detail";
        foreach (var prefix in new[]
        {
            "电源暂停后的恢复未确认；不会自动重启限充。",
            "限充自动恢复未确认，保持关闭且不自动重试。",
            "限充已安全停止，不自动重启。"
        })
        {
            var english = UiTextCatalog.Translate(prefix + native, "en-US");
            Assert.NotEqual(prefix + native, english);
            Assert.EndsWith(native, english);
        }
        Assert.Equal("Charge limiting stopped safely and will not restart automatically. AC is disconnected; charge limiting is not enabled.",
            UiTextCatalog.Translate("限充已安全停止，不自动重启。电源已断开，限充未启用。", "en-US"));
    }

    private static XElement Named(XDocument document, string name) =>
        document.Descendants().Single(element => (string?)element.Attribute(Xaml + "Name") == name);

    private static string Locate(params string[] parts)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var path = Path.Combine([directory.FullName, .. parts]);
            if (File.Exists(path)) return path;
        }
        throw new FileNotFoundException(string.Join('/', parts));
    }
}
