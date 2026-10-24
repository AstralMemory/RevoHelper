using System.Windows;
using RevoHelper.App.Controls;

namespace RevoHelper.App.Tests.Presentation;

public sealed class FanCurveChartMathTests
{
    [Fact]
    public void Chart_ExposesBindableItemsAndAccentWithoutAnyHardwareSurface()
    {
        Assert.True(typeof(System.Windows.FrameworkElement).IsAssignableFrom(typeof(FanCurveChart)));
        Assert.NotNull(FanCurveChart.ItemsSourceProperty);
        Assert.NotNull(FanCurveChart.AccentBrushProperty);
        Assert.DoesNotContain(typeof(FanCurveChart).GetMethods(
            System.Reflection.BindingFlags.Public |
            System.Reflection.BindingFlags.Instance |
            System.Reflection.BindingFlags.DeclaredOnly), method =>
            method.Name.Contains("Apply", StringComparison.OrdinalIgnoreCase) ||
            method.Name.Contains("Hardware", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData(0, 0, 10, 110)]
    [InlineData(50, 25, 100, 85)]
    [InlineData(100, 100, 190, 10)]
    public void Project_MapsTemperatureAndDutyIntoPaddedPlot(
        double temperature,
        double duty,
        double expectedX,
        double expectedY)
    {
        var point = FanCurveChartMath.Project(
            temperature,
            duty,
            minimumTemperature: 0,
            maximumTemperature: 100,
            width: 200,
            height: 120,
            padding: 10);

        Assert.Equal(expectedX, point.X, 3);
        Assert.Equal(expectedY, point.Y, 3);
    }

    [Theory]
    [InlineData(10, 100)]
    [InlineData(60, 50)]
    [InlineData(110, 0)]
    [InlineData(-20, 100)]
    [InlineData(150, 0)]
    public void DutyFromY_MapsAndClampsPointerPosition(double y, int expected)
    {
        var duty = FanCurveChartMath.DutyFromY(y, height: 120, padding: 10);

        Assert.Equal(expected, duty);
    }

    [Theory]
    [InlineData(20, 40, 80, 40)]
    [InlineData(60, 40, 80, 60)]
    [InlineData(90, 40, 80, 80)]
    [InlineData(50, null, 80, 50)]
    [InlineData(90, 40, null, 90)]
    public void ClampDuty_KeepsDraggedPointBetweenAdjacentDuties(
        int proposed,
        int? previous,
        int? next,
        int expected)
    {
        Assert.Equal(expected, FanCurveChartMath.ClampDuty(proposed, previous, next));
    }

    [Theory]
    [InlineData(5, 0)]
    [InlineData(42, 0)]
    [InlineData(125, 1)]
    [InlineData(500, 2)]
    public void NearestPointIndex_UsesHorizontalDistanceAcrossEntirePlot(double pointerX, int expected)
    {
        var points = new[]
        {
            new Point(20, 100),
            new Point(110, 70),
            new Point(190, 25)
        };

        Assert.Equal(expected, FanCurveChartMath.NearestPointIndex(points, new Point(pointerX, 0)));
    }

    [Theory]
    [InlineData(0, 85, 0, 20, 40, 60, 80, 100)]
    [InlineData(48, 96, 50, 60, 70, 80, 90, 100)]
    public void CreateTemperatureTicks_RoundsToReadableTenDegreeScale(
        double minimum,
        double maximum,
        double first,
        double second,
        double third,
        double fourth,
        double fifth,
        double last)
    {
        Assert.Equal(
            new[] { first, second, third, fourth, fifth, last },
            FanCurveChartMath.CreateTemperatureTicks(minimum, maximum, divisions: 5));
    }

    [Fact]
    public void CreateTemperatureTicks_FromEmptySourceReturnsNoTicks()
    {
        Assert.Empty(FanCurveChartMath.CreateTemperatureTicks(Array.Empty<double>(), divisions: 5));
    }

    [Fact]
    public void CreateTemperatureTicks_WithLaptopDisplayFloorStartsAtFortyDegrees()
    {
        var ticks = FanCurveChartMath.CreateTemperatureTicks(
            new[] { 0d, 48d, 52d, 56d, 60d, 64d, 68d, 72d, 76d, 80d, 85d },
            divisions: 5,
            minimumVisibleTemperature: 40);

        Assert.Equal(new[] { 40d, 50d, 60d, 70d, 80d, 90d }, ticks);
    }
}
