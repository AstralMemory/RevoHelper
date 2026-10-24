using System.Windows;

namespace RevoHelper.App.Presentation;

public sealed record WindowScaleLayout(
    double Left, double Top, double Width, double Height, double EffectiveScale,
    double DrawerOffset, double MainPanelLeft, bool IsCapped);

/// <summary>All sizes and positions are WPF device-independent pixels.</summary>
public static class WindowScaleGeometry
{
    public const double MainWidth = 580;
    public const double DrawerWidth = 360;
    public const double LogicalHeight = 550;
    public static IReadOnlyList<int> Options { get; } = Array.AsReadOnly(new[] { 70, 80, 90, 100, 110, 120, 130 });

    public static int NormalizePercent(int percent) => Options.Contains(percent) ? percent : 100;

    public static WindowScaleLayout Calculate(int requestedPercent, bool drawerOpen, Rect workArea, double mainPanelLeft, double top)
    {
        var logicalWidth = MainWidth + (drawerOpen ? DrawerWidth : 0);
        var requestedScale = NormalizePercent(requestedPercent) / 100d;
        var scale = Math.Min(requestedScale, Math.Min(workArea.Width / logicalWidth, workArea.Height / LogicalHeight));
        var width = logicalWidth * scale;
        var height = LogicalHeight * scale;
        var drawerOffset = drawerOpen ? DrawerWidth * scale : 0;
        var left = Math.Clamp(mainPanelLeft - drawerOffset, workArea.Left, Math.Max(workArea.Left, workArea.Right - width));
        top = Math.Clamp(top, workArea.Top, Math.Max(workArea.Top, workArea.Bottom - height));
        return new(left, top, width, height, scale, drawerOffset, left + drawerOffset, scale < requestedScale - 0.000001);
    }
}
