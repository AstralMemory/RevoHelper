using System.IO;
using System.Runtime.ExceptionServices;
using System.Windows.Controls;
using System.Windows.Media;
using System.Xml.Linq;
using RevoHelper.App.Presentation;

namespace RevoHelper.App.Tests.Presentation;

public sealed class TextClarityTests
{
    private static readonly XNamespace Presentation = "clr-namespace:RevoHelper.App.Presentation";

    [Fact]
    public void TextBelowTheSwitchGetsClearTypeUntilItIsDisabled() => RunOnSta(() =>
    {
        var text = new TextBlock { Text = "诊断报告" };
        var inner = new StackPanel { Children = { text } };
        var area = new ScrollViewer { Content = inner };
        Assert.Equal(ClearTypeHint.Auto, RenderOptions.GetClearTypeHint(text));

        TextClarity.SetEnabled(area, true);
        Assert.Equal(ClearTypeHint.Enabled, RenderOptions.GetClearTypeHint(text));

        // A disabled control is drawn faded in its own see-through layer: no ClearType there.
        inner.IsEnabled = false;
        Assert.Equal(ClearTypeHint.Auto, RenderOptions.GetClearTypeHint(text));
        inner.IsEnabled = true;
        Assert.Equal(ClearTypeHint.Enabled, RenderOptions.GetClearTypeHint(text));

        TextClarity.SetEnabled(inner, false);
        Assert.Equal(ClearTypeHint.Auto, RenderOptions.GetClearTypeHint(text));
    });

    [Fact]
    public void TextAddedLaterInheritsTheSwitch() => RunOnSta(() =>
    {
        var panel = new StackPanel();
        TextClarity.SetEnabled(panel, true);
        var text = new TextBlock { Text = "后加入" };

        panel.Children.Add(text);

        Assert.Equal(ClearTypeHint.Enabled, RenderOptions.GetClearTypeHint(text));
    });

    [Fact]
    public void EveryScrollingDrawerAndTheDeviceListTurnItOn()
    {
        var document = XDocument.Load(MainWindowXaml());
        var switchName = Presentation + "TextClarity.Enabled";
        var drawers = document.Descendants().Where(element => element.Name.LocalName == "ScrollViewer" &&
            ((string?)element.Attribute(XName.Get("Name", "http://schemas.microsoft.com/winfx/2006/xaml")))?.EndsWith("DrawerContent", StringComparison.Ordinal) == true).ToArray();

        Assert.True(drawers.Length >= 13);
        Assert.All(drawers, drawer => Assert.Equal("True", (string?)drawer.Attribute(switchName)));
        var deviceList = document.Descendants().Single(element => element.Name.LocalName == "ItemsControl" &&
            (string?)element.Attribute(XName.Get("Name", "http://schemas.microsoft.com/winfx/2006/xaml")) == "DeviceSensorsPanel");
        Assert.Equal("True", (string?)deviceList.Ancestors().First(element => element.Name.LocalName == "ScrollViewer").Attribute(switchName));
    }

    [Fact]
    public void AreasFadedByABindingAndDropDownsTurnItOff()
    {
        var document = XDocument.Load(MainWindowXaml());
        var switchName = Presentation + "TextClarity.Enabled";
        var xamlName = XName.Get("Name", "http://schemas.microsoft.com/winfx/2006/xaml");

        foreach (var name in new[] { "FanResponseControls", "FanCurveChartSurface", "FanPointEditors" })
            Assert.Equal("False", (string?)document.Descendants().Single(element => (string?)element.Attribute(xamlName) == name).Attribute(switchName));
        var dropDown = document.Descendants().Single(element => element.Name.LocalName == "Popup" && (string?)element.Attribute(xamlName) == "Popup");
        Assert.Equal("False", (string?)dropDown.Attribute(switchName));
        // Every element that fades through a binding has turned the switch off.
        Assert.All(document.Descendants().Where(element => ((string?)element.Attribute("Opacity"))?.StartsWith("{Binding", StringComparison.Ordinal) == true),
            element => Assert.Equal("False", (string?)element.Attribute(switchName)));
    }

    // Hover and press fade a control that stays enabled, so the TextBlock's own IsEnabled rule does
    // not cover them: the trigger that fades must turn the switch off for the same part.
    [Fact]
    public void EveryFadeThatIsNotADisabledStateTurnsItOff()
    {
        var document = XDocument.Load(MainWindowXaml());
        var fades = document.Descendants()
            .Where(trigger => trigger.Name.LocalName == "Trigger" &&
                              !((string?)trigger.Attribute("Property") == "IsEnabled" && (string?)trigger.Attribute("Value") == "False"))
            .SelectMany(trigger => trigger.Elements()
                .Where(setter => setter.Name.LocalName == "Setter" && (string?)setter.Attribute("Property") == "Opacity")
                .Select(setter => (Trigger: trigger, Target: (string?)setter.Attribute("TargetName"))))
            .ToArray();

        Assert.NotEmpty(fades);
        Assert.All(fades, fade => Assert.Contains(fade.Trigger.Elements(), setter =>
            setter.Name.LocalName == "Setter" && (string?)setter.Attribute("Property") == "presentation:TextClarity.Enabled" &&
            (string?)setter.Attribute("Value") == "False" && (string?)setter.Attribute("TargetName") == fade.Target));
    }

    private static string MainWindowXaml()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var path = Path.Combine(directory.FullName, "src", "RevoHelper.App", "MainWindow.xaml");
            if (File.Exists(path)) return path;
        }
        throw new FileNotFoundException("MainWindow.xaml not found above the test output folder.");
    }

    private static void RunOnSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() => { try { action(); } catch (Exception exception) { failure = exception; } });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(20)), "STA test timed out.");
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
