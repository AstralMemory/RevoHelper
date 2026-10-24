using System.Windows;
using RevoHelper.App.Settings;

namespace RevoHelper.App.Monitoring;

public static class MonitorPlacement
{
    public static Point Calculate(Rect workArea, Size meterSize, MonitorPositionPreset preset,
        Point? customPosition, double margin = 0)
    {
        var left = double.IsFinite(workArea.Left) ? workArea.Left : 0;
        var top = double.IsFinite(workArea.Top) ? workArea.Top : 0;
        var width = NonNegative(workArea.Width);
        var height = NonNegative(workArea.Height);
        var roomX = Math.Max(0, width - NonNegative(meterSize.Width));
        var roomY = Math.Max(0, height - NonNegative(meterSize.Height));
        if (preset == MonitorPositionPreset.Custom && customPosition is Point custom &&
            double.IsFinite(custom.X) && double.IsFinite(custom.Y))
            return new(Math.Clamp(custom.X, left, left + roomX), Math.Clamp(custom.Y, top, top + roomY));

        var insetX = Math.Min(NonNegative(margin), roomX / 2);
        var insetY = Math.Min(NonNegative(margin), roomY / 2);
        var minimumX = left + insetX;
        var maximumX = left + roomX - insetX;
        var minimumY = top + insetY;
        var maximumY = top + roomY - insetY;
        return preset switch
        {
            MonitorPositionPreset.TopLeft => new(minimumX, minimumY),
            MonitorPositionPreset.TopRight => new(maximumX, minimumY),
            MonitorPositionPreset.BottomLeft => new(minimumX, maximumY),
            MonitorPositionPreset.BottomRight => new(maximumX, maximumY),
            MonitorPositionPreset.BottomCenter => new(left + roomX / 2, maximumY),
            _ => new(left + roomX / 2, minimumY)
        };
    }

    private static double NonNegative(double value) => double.IsFinite(value) ? Math.Max(0, value) : 0;
}
