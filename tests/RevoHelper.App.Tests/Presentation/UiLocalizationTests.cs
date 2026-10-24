using System.Globalization;
using System.Runtime.ExceptionServices;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Threading;
using System.Xml.Linq;
using RevoHelper.App.Presentation;
using RevoHelper.App.Settings;
using RevoHelper.Core.Dashboard;
using RevoHelper.Core.Hardware;
using RevoHelper.Core.Monitoring;

namespace RevoHelper.App.Tests.Presentation;

[CollectionDefinition("Display language", DisableParallelization = true)]
public sealed class DisplayLanguageCollection;

[Collection("Display language")]
public sealed class UiLocalizationTests
{
    [Fact]
    public void EveryStaticLocalizedLiteralHasEnglishCopy_AndNoRawDisplayChineseRemains()
    {
        var document = XDocument.Load(LocateMainWindow());
        var literals = new List<string>();
        foreach (var element in document.Descendants())
        foreach (var attribute in element.Attributes())
        {
            var display = attribute.Name.LocalName is "Text" or "Content" or "Header" or "ToolTip" or "AutomationProperties.Name" ||
                          attribute.Name.LocalName == "Value" && (string?)element.Attribute("Property") is "Text" or "Content" or "Header" or "ToolTip";
            if (!display) continue;
            var value = attribute.Value;
            var match = Regex.Match(value, @"^\{presentation:UiText '(.*)'\}$");
            if (match.Success)
            {
                literals.Add(match.Groups[1].Value);
                Assert.True(UiTextCatalog.HasTranslation(match.Groups[1].Value), "Missing English UI copy: " + match.Groups[1].Value);
            }
            else if (value.Any(character => character is >= '\u4e00' and <= '\u9fff'))
            {
                Assert.StartsWith("{presentation:UiText Value=", value);
            }
        }
        Assert.True(literals.Distinct().Count() >= 220, "The full drawer UI, not only the home page, must be localized.");
    }

    [Theory]
    [InlineData("10 秒", "10 s")]
    [InlineData("2 分钟", "2 min")]
    [InlineData("当前：狂暴模式 · 静音", "Current: Turbo · Quiet")]
    [InlineData("只读传感器 · 更新于 18:30:20", "Read-only sensors · Updated 18:30:20")]
    [InlineData("状态：未连接", "Status: Disconnected")]
    [InlineData("24 核 · 24 线程", "24 cores · 24 threads")]
    [InlineData("24 GB 显存", "24 GB VRAM")]
    [InlineData("512 MB 显存", "512 MB VRAM")]
    public void NormalDynamicText_TranslatesWithoutChangingValues(string chinese, string english)
    {
        Assert.Equal(english, UiTextCatalog.Translate(chinese, "en-US"));
        Assert.Equal(chinese, UiTextCatalog.Translate(chinese, "zh-CN"));
    }

    [Fact]
    public void UserProgramNamesAndUnknownNativeErrors_AreNotRewritten()
    {
        const string fileName = "我的原神.exe";
        Assert.Equal(fileName, UiTextCatalog.Translate(fileName, "en-US"));
        Assert.Equal("Not running: 我的原神.exe\nIt will be matched when it starts.",
            UiTextCatalog.Translate("未运行：我的原神.exe\n启动后会自动匹配该程序。", "en-US"));
        const string nativeError = "设备返回未知原生错误（错误码 0x1234）";
        Assert.Equal(nativeError, UiTextCatalog.Translate(nativeError, "en-US"));
    }

    [Fact]
    public void DisplayConverter_PreservesStringFormatAndUnits()
    {
        Assert.Equal("150 W", new UiTextConverter("{0} W").Convert([150, "en-US"], typeof(string), null!, CultureInfo.InvariantCulture));
        Assert.Equal("3292 MHz", new UiTextConverter("{}{0} MHz").Convert([3292, "en-US"], typeof(string), null!, CultureInfo.InvariantCulture));
        Assert.Equal("Added: 2026-10-24 18:30", new UiTextConverter("添加：{0}").Convert(["2026-10-24 18:30", "en-US"], typeof(string), null!, CultureInfo.InvariantCulture));
    }

    [Fact]
    public void MarkupBinding_ChangesLanguageBothWaysWithoutRecreatingControl() => RunOnSta(() =>
    {
        UiLanguageService.Current.SetLanguage("zh-CN");
        try
        {
            var text = (TextBlock)XamlReader.Parse("""
                <TextBlock xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                           xmlns:p="clr-namespace:RevoHelper.App.Presentation;assembly=RevoHelper"
                           Text="{p:UiText '底部居中'}" />
                """);
            Pump();
            Assert.Equal("底部居中", text.Text);
            UiLanguageService.Current.SetLanguage("en-US"); Pump();
            Assert.Equal("Bottom center", text.Text);
            UiLanguageService.Current.SetLanguage("zh-CN"); Pump();
            Assert.Equal("底部居中", text.Text);
        }
        finally { UiLanguageService.Current.SetLanguage("zh-CN"); }
    });

    [Fact]
    public void ViewModelLanguageAndThemeChanges_UpdateDisplayWithoutTouchingMonitorSettings()
    {
        var settings = new MemorySettings();
        var vm = new MainWindowViewModel(HomeDashboardFactory.Create(
            new MachineIdentity("MECHREVO", "Offline", "Preview", "CPU", "test"), []),
            new ApplicationSettings(), settings, new NoStartup());
        try
        {
            vm.OpenPerformanceMonitor();
            vm.SelectedUiLanguageCode = "en-US";
            Assert.Equal("Performance monitor (floating bar)", vm.DrawerTitle);
            Assert.Equal("Monitor is off.", vm.Monitor.StatusText);
            Assert.Equal("en-US", settings.Saved!.UiLanguageCode);
            Assert.False(vm.Monitor.IsEnabled);
            vm.SelectedUiThemeCode = "dark";
            Assert.Equal("dark", settings.Saved.UiThemeCode);
            Assert.Equal("en-US", settings.Saved.UiLanguageCode);
            vm.SelectedUiLanguageCode = "zh-CN";
            Assert.Equal("性能监控（外置监控小横条）", vm.DrawerTitle);
            Assert.Equal("监控已关闭。", vm.Monitor.StatusText);
        }
        finally { UiLanguageService.Current.SetLanguage("zh-CN"); }
    }

    [Fact]
    public void EditableBindingsAndUserFileNames_AreNotWrappedInTranslation()
    {
        var document = XDocument.Load(LocateMainWindow());
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        var colorTextBox = document.Descendants().Single(element => (string?)element.Attribute(x + "Name") == "LightingColorHexBox");
        Assert.StartsWith("{Binding ", (string?)colorTextBox.Attribute("Text"));
        Assert.All(document.Descendants().Where(element => element.Name.LocalName == "TextBox" && element.Attribute("Text") is not null),
            element => Assert.DoesNotContain("UiText", element.Attribute("Text")!.Value));
        Assert.Contains(document.Descendants(), element => (string?)element.Attribute("Text") == "{Binding DisplayName}");
    }

    private static string LocateMainWindow()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var path = Path.Combine(directory.FullName, "src", "RevoHelper.App", "MainWindow.xaml");
            if (File.Exists(path)) return path;
        }
        throw new FileNotFoundException("MainWindow.xaml not found.");
    }
    private static void Pump() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Background);
    private static void RunOnSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() => { try { action(); } catch (Exception exception) { failure = exception; } }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(10)));
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }
    private sealed class MemorySettings : IApplicationSettingsStore
    {
        public ApplicationSettings? Saved { get; private set; }
        public ApplicationSettings Load() => new();
        public void Save(ApplicationSettings settings) => Saved = settings;
    }
    private sealed class NoStartup : IStartupRegistration
    {
        public bool IsEnabled() => false;
        public void SetEnabled(bool enabled) => throw new InvalidOperationException("No startup writes allowed.");
    }
}
