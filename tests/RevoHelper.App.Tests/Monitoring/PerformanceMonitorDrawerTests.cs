using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using System.Xml.Linq;
using RevoHelper.App.Presentation;
using RevoHelper.Core.Dashboard;
using RevoHelper.Core.Hardware;

namespace RevoHelper.App.Tests.Monitoring;

public sealed class PerformanceMonitorDrawerTests
{
    [Fact]
    public void Snapshot2_MonitorSettingsUseExistingDrawerAndKeepExplicitProgramActions()
    {
        var document = XDocument.Load(Locate("src", "RevoHelper.App", "MainWindow.xaml"));
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        XElement Named(string name) => document.Descendants().Single(element => (string?)element.Attribute(x + "Name") == name);
        var drawer = Named("PerformanceMonitorDrawerContent");
        Assert.Equal("{Binding Monitor}", (string?)drawer.Attribute("DataContext"));
        Assert.Equal("Auto", (string?)drawer.Attribute("VerticalScrollBarVisibility"));
        Assert.Equal("Disabled", (string?)drawer.Attribute("HorizontalScrollBarVisibility"));
        var positions = Named("MonitorPositionPresets").Descendants().Where(element => element.Name.LocalName == "RadioButton").ToArray();
        Assert.Equal(new[] { "TopLeft", "TopCenter", "TopRight", "BottomLeft", "BottomCenter", "BottomRight" },
            positions.Select(element => (string?)element.Attribute("Tag")));
        Assert.All(positions, element => Assert.Equal("MonitorPosition_Click", (string?)element.Attribute("Click")));
        Assert.Equal("{Binding Programs}", (string?)Named("MonitorProgramsList").Attribute("ItemsSource"));
        Assert.Equal("AddMonitorProgram_Click", (string?)Named("AddMonitorProgramButton").Attribute("Click"));
        Assert.Equal("RemoveMonitorProgram_Click", (string?)Named("RemoveMonitorProgramButton").Attribute("Click"));
        Assert.Contains(Named("MonitorEntryButton").Descendants(), element => (string?)element.Attribute("Text") == "|");
        Assert.Equal("Center", (string?)Named("MonitorEntryTitle").Attribute("VerticalAlignment"));
    }

    [Theory]
    [InlineData(70)]
    [InlineData(100)]
    [InlineData(130)]
    public void Snapshot2_MonitorDrawerControlsRemainReachableAtSupportedScales(int percent)
    {
        RunOnSta(() =>
        {
            var vm = new MainWindowViewModel(HomeDashboardFactory.Create(
                new MachineIdentity("MECHREVO", "Offline", "Preview", "CPU", "test"), []));
            var window = new MainWindow(vm) { AllowApplicationClose = true };
            try
            {
                window.ShowPerformanceMonitorSettings();
                Assert.Equal(HomeDrawer.PerformanceMonitor, vm.ActiveDrawer);
                Assert.Equal("性能监控（外置监控小横条）", vm.DrawerTitle);
                vm.WindowScalePercent = percent;
                var viewport = Assert.IsType<Viewbox>(window.Content);
                window.Content = null;
                viewport.DataContext = vm;
                var size = new Size(940 * percent / 100d, 550 * percent / 100d);
                viewport.Measure(size);
                viewport.Arrange(new Rect(size));
                Flush(viewport);
                var scroll = Assert.IsType<ScrollViewer>(window.FindName("PerformanceMonitorDrawerContent"));
                Assert.Equal(Visibility.Visible, scroll.Visibility);
                Assert.Same(vm.Monitor, scroll.DataContext);
                var sizes = Assert.IsType<ComboBox>(window.FindName("MonitorSizeSelector"));
                Assert.Equal(5, sizes.Items.Count);
                Assert.Equal(Visibility.Visible, Assert.IsAssignableFrom<FrameworkElement>(window.FindName("PerformanceMonitorDrawerHeaderIcon")).Visibility);
                Assert.Equal(Visibility.Collapsed, Assert.IsAssignableFrom<FrameworkElement>(window.FindName("PerformanceDrawerContent")).Visibility);
                Assert.True(vm.Monitor.IsAutomatic);
                Assert.True(Assert.IsType<RadioButton>(window.FindName("MonitorAutomaticSource")).IsChecked);
                Assert.False(Assert.IsType<RadioButton>(window.FindName("MonitorManualSource")).IsChecked);

                foreach (var name in new[]
                {
                    "MonitorEnabledToggle", "MonitorTopmostToggle", "MonitorSizeSelector", "MonitorOpacitySlider",
                    "MonitorPositionLockToggle", "MonitorTopLeftPosition", "MonitorTopCenterPosition", "MonitorTopRightPosition",
                    "MonitorBottomLeftPosition", "MonitorBottomCenterPosition", "MonitorBottomRightPosition", "MonitorAutomaticSource", "MonitorManualSource"
                })
                    AssertReachable(Assert.IsAssignableFrom<FrameworkElement>(window.FindName(name)), scroll, viewport);

                vm.Monitor.IsManual = true;
                Flush(viewport);
                Assert.Equal(Visibility.Visible, Assert.IsType<StackPanel>(window.FindName("MonitorManualProgramsSection")).Visibility);
                foreach (var name in new[] { "AddMonitorProgramButton", "RemoveMonitorProgramButton", "MonitorProgramsEmptyText", "MonitorStatusText" })
                    AssertReachable(Assert.IsAssignableFrom<FrameworkElement>(window.FindName(name)), scroll, viewport);
                Assert.False(Assert.IsType<Button>(window.FindName("RemoveMonitorProgramButton")).IsEnabled);
                var footer = Assert.IsType<TextBlock>(window.FindName("SettingsVersionFooter"));
                var scrollBounds = scroll.TransformToAncestor(viewport).TransformBounds(new Rect(scroll.RenderSize));
                Assert.Equal(Visibility.Collapsed, footer.Visibility);
                Assert.True(scrollBounds.Bottom <= size.Height + 1, "Monitor settings must stay inside the scaled window after removing the duplicate footer.");
            }
            finally { window.Close(); }
        });
    }

    private static void AssertReachable(FrameworkElement control, ScrollViewer scroll, Viewbox viewport)
    {
        control.BringIntoView();
        Flush(viewport);
        var visible = scroll.TransformToAncestor(viewport).TransformBounds(new Rect(scroll.RenderSize));
        var bounds = control.TransformToAncestor(viewport).TransformBounds(new Rect(control.RenderSize));
        Assert.True(control.ActualWidth > 0 && control.ActualHeight > 0, $"{control.Name} must have visible space.");
        Assert.True(bounds.Left >= visible.Left - 1 && bounds.Right <= visible.Right + 1,
            $"{control.Name} must fit the monitor drawer width.");
        Assert.True(bounds.Top >= visible.Top - 1 && bounds.Bottom <= visible.Bottom + 1,
            $"{control.Name} must remain reachable by scrolling.");
    }

    private static void Flush(FrameworkElement content)
    {
        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Background);
        content.UpdateLayout();
    }

    private static void RunOnSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() => { try { action(); } catch (Exception error) { failure = error; } });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(15)), "Offline monitor layout timed out.");
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }

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
