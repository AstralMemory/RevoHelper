using System.Xml.Linq;

namespace RevoHelper.App.Tests.Presentation;

public sealed class CustomPerformanceCommitMarkupTests
{
    [Fact]
    public void GpuSlidersCommitAtTheEndOfTheGestureInsteadOfDuringEveryTick()
    {
        var path = Path.Combine(FindRoot(), "src", "RevoHelper.App", "MainWindow.xaml");
        var document = XDocument.Load(path);
        var sliders = document.Descendants().Where(element => element.Name.LocalName == "Slider" &&
            ((string?)element.Attribute("Tag"))?.StartsWith("Gpu", StringComparison.Ordinal) == true).ToArray();

        Assert.Equal(4, sliders.Length);
        var style = document.Descendants().Single(element => element.Name.LocalName == "Style" &&
            (string?)element.Attribute("TargetType") == "Slider");
        var events = style.Elements().Where(element => element.Name.LocalName == "EventSetter").ToArray();
        Assert.Contains(events, element => (string?)element.Attribute("Event") == "PreviewMouseLeftButtonUp" &&
            (string?)element.Attribute("Handler") == "CustomPerformanceSlider_MouseReleased");
        Assert.Contains(events, element => (string?)element.Attribute("Event") == "LostMouseCapture" &&
            (string?)element.Attribute("Handler") == "CustomPerformanceSlider_CaptureLost");
        Assert.Contains(events, element => (string?)element.Attribute("Event") == "PreviewKeyUp" &&
            (string?)element.Attribute("Handler") == "CustomPerformanceSlider_KeyReleased");
    }

    [Fact]
    public void MouseDraggingDoesNotScheduleATimedWrite()
    {
        var source = File.ReadAllText(Path.Combine(FindRoot(), "src", "RevoHelper.App", "MainWindow.xaml.cs"));
        var begin = source.IndexOf("private void CustomPerformanceSlider_ValueChanged", StringComparison.Ordinal);
        var end = source.IndexOf("private readonly HashSet<Slider>", begin, StringComparison.Ordinal);
        var handler = source[begin..end];

        Assert.Contains("pendingDraggedCustomSliders.Add(slider)", handler, StringComparison.Ordinal);
        Assert.Contains("Mouse.LeftButton == MouseButtonState.Pressed", handler, StringComparison.Ordinal);
        Assert.DoesNotContain("slider.IsMouseCaptureWithin || slider.IsKeyboardFocusWithin", handler, StringComparison.Ordinal);
        Assert.DoesNotContain("QueueCustomPerformanceAutoApply", handler, StringComparison.Ordinal);
    }

    private static string FindRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "RevoHelper.sln"))) return directory.FullName;
        throw new InvalidOperationException("Cannot find the source root.");
    }
}
