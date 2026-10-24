namespace RevoHelper.App.Tests.Presentation;

public sealed class WindowScaleLifecycleTests
{
    [Fact]
    public void Dragging_RecordsPositionButOnlyReflowsAfterDragCompletes()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "RevoHelper.App", "MainWindow.xaml.cs");
        var source = File.ReadAllText(path);
        var locationHandler = source[source.IndexOf("private void Window_LocationChanged", StringComparison.Ordinal)..source.IndexOf("protected override void OnDpiChanged", StringComparison.Ordinal)];
        Assert.Contains("preferredMainPanelLeft = Left + windowLayout.DrawerOffset", locationHandler);
        Assert.DoesNotContain("ApplyWindowGeometry();", locationHandler);
        var dragHandler = source[source.IndexOf("private void Window_MouseLeftButtonDown", StringComparison.Ordinal)..source.IndexOf("private async void Profile_Checked", StringComparison.Ordinal)];
        Assert.True(dragHandler.IndexOf("ApplyWindowGeometry();", StringComparison.Ordinal) >
            dragHandler.IndexOf("DragMove();", StringComparison.Ordinal));
    }
}
