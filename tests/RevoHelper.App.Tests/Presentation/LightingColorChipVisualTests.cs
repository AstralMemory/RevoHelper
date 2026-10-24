using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Xml.Linq;
using RevoHelper.App.Presentation;

namespace RevoHelper.App.Tests.Presentation;

public sealed class LightingColorChipVisualTests
{
    [Theory]
    [InlineData("红色", "#FF0000", "Red #FF0000")]
    [InlineData("黄色", "#FFFF00", "Yellow #FFFF00")]
    public void RedAndYellowShortcuts_UsePureRgbRatherThanPastelUiAccents(string label, string hex, string english)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "RevoHelper.App", "MainWindow.xaml");
        var xaml = XDocument.Load(path);
        var chip = xaml.Descendants().Single(element => element.Name.LocalName == "Button" &&
            (string?)element.Attribute("Click") == "LightingColorChip_Click" &&
            (string?)element.Attribute("Tag") == hex);
        Assert.Equal(hex, (string?)chip.Attribute("Tag"));
        Assert.Equal(hex, (string?)chip.Attribute("Background"));
        var localizedMarkup = $"{{presentation:UiText '{label} {hex}'}}";
        Assert.Equal(localizedMarkup, (string?)chip.Attribute("ToolTip"));
        Assert.Equal(localizedMarkup, (string?)chip.Attribute("AutomationProperties.Name"));
        Assert.Equal(english, UiTextCatalog.Translate(label + " " + hex, "en-US"));
    }

    [Fact]
    public void ColorChips_KeepRgbFillWhenPressedOrDisabled()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "RevoHelper.App", "MainWindow.xaml");
        var xaml = XDocument.Load(path);
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        var style = xaml.Descendants().SingleOrDefault(element => (string?)element.Attribute(x + "Key") == "LightingColorChipStyle");
        Assert.NotNull(style);
        Assert.DoesNotContain(style.Descendants(), element => element.Name.LocalName == "Setter" &&
            (string?)element.Attribute("Property") == "Background");
        var chips = xaml.Descendants().Where(element => element.Name.LocalName == "Button" &&
            (string?)element.Attribute("Click") == "LightingColorChip_Click").ToArray();
        Assert.Equal(7, chips.Length);
        foreach (var chip in chips)
        {
            Assert.Equal((string?)chip.Attribute("Tag"), (string?)chip.Attribute("Background"));
            Assert.Equal("{StaticResource LightingColorChipStyle}", (string?)chip.Attribute("Style"));
        }
        var standaloneStyle = new XElement(style);
        standaloneStyle.Attribute(x + "Key")!.Remove();
        standaloneStyle.SetAttributeValue(XNamespace.Xmlns + "x", x.NamespaceName);
        RunOnSta(() =>
        {
            var actualStyle = (Style)XamlReader.Parse(standaloneStyle.ToString());
            var preview = new StackPanel { Background = Brushes.White };
            foreach (var name in new[] { "Normal", "Pressed", "Disabled" })
            {
                var row = new StackPanel { Orientation = Orientation.Horizontal };
                row.Children.Add(new TextBlock { Text = name, Width = 90, VerticalAlignment = VerticalAlignment.Center });
                foreach (var chip in chips)
                {
                    var button = new PressableButton
                    {
                        Style = actualStyle,
                        Background = (Brush)new BrushConverter().ConvertFromString((string)chip.Attribute("Tag")!)!,
                        Width = 60,
                        Height = 30
                    };
                    if (name == "Pressed") button.Press();
                    if (name == "Disabled") button.IsEnabled = false;
                    row.Children.Add(button);
                }
                preview.Children.Add(row);
            }
            preview.Measure(new Size(510, 90));
            preview.Arrange(new Rect(0, 0, 510, 90));
            preview.UpdateLayout();
            var bitmap = new RenderTargetBitmap(510, 90, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(preview);
            for (var row = 0; row < 3; row++)
                for (var column = 0; column < chips.Length; column++)
                {
                    var pixel = new byte[4];
                    bitmap.CopyPixels(new Int32Rect(120 + column * 60, 15 + row * 30, 1, 1), pixel, 4, 0);
                    var expected = (Color)ColorConverter.ConvertFromString((string)chips[column].Attribute("Tag")!);
                    Assert.Equal(expected, Color.FromArgb(pixel[3], pixel[2], pixel[1], pixel[0]));
                }
            if (Environment.GetEnvironmentVariable("REVOHELPER_TEST_VISUAL_DIR") is { Length: > 0 } output)
            {
                Directory.CreateDirectory(output);
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using var file = File.Create(Path.Combine(output, "lighting-color-chips-v20.png"));
                encoder.Save(file);
            }
            foreach (var chip in chips)
            {
                var rgb = (Color)ColorConverter.ConvertFromString((string)chip.Attribute("Tag")!);
                var button = new PressableButton { Style = actualStyle, Background = new SolidColorBrush(rgb), Width = 60, Height = 30 };
                button.Measure(new Size(60, 30));
                button.Arrange(new Rect(0, 0, 60, 30));
                button.ApplyTemplate();
                var swatch = Assert.IsType<Border>(button.Template.FindName("Swatch", button));
                Assert.Equal(rgb, Assert.IsType<SolidColorBrush>(swatch.Background).Color);
                button.Press();
                Assert.Equal(rgb, Assert.IsType<SolidColorBrush>(swatch.Background).Color);
                button.IsEnabled = false;
                Assert.Equal(rgb, Assert.IsType<SolidColorBrush>(swatch.Background).Color);
            }
        });
    }

    private sealed class PressableButton : Button
    {
        public void Press() => IsPressed = true;
    }

    private static void RunOnSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() => { try { action(); } catch (Exception exception) { failure = exception; } });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(15)), "WPF template test timed out.");
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
