using System.Windows;
using RevoHelper.App.Monitoring;
using RevoHelper.App.Settings;

namespace RevoHelper.App.Tests.Monitoring;

public sealed class MonitorPlacementTests
{
    [Theory]
    [InlineData(MonitorPositionPreset.TopLeft, 100, 50)]
    [InlineData(MonitorPositionPreset.TopCenter, 400, 50)]
    [InlineData(MonitorPositionPreset.TopRight, 700, 50)]
    [InlineData(MonitorPositionPreset.BottomLeft, 100, 610)]
    [InlineData(MonitorPositionPreset.BottomRight, 700, 610)]
    [InlineData(MonitorPositionPreset.BottomCenter, 400, 610)]
    public void Presets_AreFlushWithWorkAreaAndMeasuredMeterSize(MonitorPositionPreset preset, double x, double y)
    {
        var point = MonitorPlacement.Calculate(new(100, 50, 800, 600), new(200, 40), preset, null);
        Assert.Equal(new Point(x, y), point);
    }

    [Fact]
    public void NegativeOrigin_IsNotForcedOntoPrimaryDisplay()
    {
        var point = MonitorPlacement.Calculate(new(-1920, -200, 1920, 1080), new(1000, 35),
            MonitorPositionPreset.BottomRight, null);
        Assert.Equal(new Point(-1000, 845), point);
    }

    [Fact]
    public void CustomPosition_IsClampedToWorkAreaWithoutForcingPresetMargin()
    {
        var point = MonitorPlacement.Calculate(new(-1920, -200, 1920, 1080), new(1000, 35),
            MonitorPositionPreset.Custom, new(-5000, 2000));
        Assert.Equal(new Point(-1920, 845), point);
    }

    [Fact]
    public void OversizedMeter_KeepsItsTopLeftReachable()
    {
        var point = MonitorPlacement.Calculate(new(-600, -100, 500, 300), new(1000, 500),
            MonitorPositionPreset.BottomRight, null);
        Assert.Equal(new Point(-600, -100), point);
    }

    [Fact]
    public void NarrowRemainingSpace_DoesNotInvertClampBounds()
    {
        var point = MonitorPlacement.Calculate(new(0, 0, 105, 45), new(100, 40),
            MonitorPositionPreset.TopLeft, null, margin: 8);
        Assert.Equal(new Point(2.5, 2.5), point);
    }

    [Fact]
    public void InvalidCustomCoordinates_FallBackToTopCenter()
    {
        var point = MonitorPlacement.Calculate(new(0, 0, 800, 600), new(200, 40),
            MonitorPositionPreset.Custom, new(double.NaN, double.PositiveInfinity));
        Assert.Equal(new Point(300, 0), point);
    }

    [Fact]
    public void BottomCenter_UsesNegativeDisplayCoordinates()
    {
        var point = MonitorPlacement.Calculate(new(-1920, -200, 1920, 1080), new(1000, 35),
            MonitorPositionPreset.BottomCenter, null);
        Assert.Equal(new Point(-1460, 845), point);
    }

    [Fact]
    public void BottomCenter_LeavesTaskbarAreaOutsideTheWorkAreaUntouched()
    {
        var point = MonitorPlacement.Calculate(new(0, 0, 1920, 1040), new(1000, 40),
            MonitorPositionPreset.BottomCenter, null);
        Assert.Equal(new Point(460, 1000), point);
    }

    [Fact]
    public void BottomCenter_OversizedMeterStillKeepsTopLeftReachable()
    {
        var point = MonitorPlacement.Calculate(new(-600, -100, 500, 300), new(1000, 500),
            MonitorPositionPreset.BottomCenter, null);
        Assert.Equal(new Point(-600, -100), point);
    }

    [Fact]
    public void EmptyAreaAndUnknownSize_ReturnFiniteCoordinates()
    {
        var point = MonitorPlacement.Calculate(Rect.Empty, Size.Empty, MonitorPositionPreset.TopRight, null);
        Assert.True(double.IsFinite(point.X));
        Assert.True(double.IsFinite(point.Y));
    }
}
