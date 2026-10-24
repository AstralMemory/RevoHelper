using System.Windows;

namespace RevoHelper.App.Controls;

public static class FanCurveChartMath
{
    public static Point Project(
        double temperature,
        double duty,
        double minimumTemperature,
        double maximumTemperature,
        double width,
        double height,
        double padding)
    {
        var usableWidth = Math.Max(0, width - (padding * 2));
        var usableHeight = Math.Max(0, height - (padding * 2));
        var temperatureRange = Math.Max(1, maximumTemperature - minimumTemperature);
        var xRatio = Math.Clamp((temperature - minimumTemperature) / temperatureRange, 0, 1);
        var yRatio = 1 - Math.Clamp(duty / 100d, 0, 1);
        return new(
            padding + (xRatio * usableWidth),
            padding + (yRatio * usableHeight));
    }

    public static int DutyFromY(double y, double height, double padding)
    {
        var usableHeight = Math.Max(1, height - (padding * 2));
        var ratio = 1 - ((y - padding) / usableHeight);
        return (int)Math.Round(Math.Clamp(ratio, 0, 1) * 100, MidpointRounding.AwayFromZero);
    }

    public static int ClampDuty(int proposed, int? previousDuty, int? nextDuty)
    {
        var minimum = Math.Clamp(previousDuty ?? 0, 0, 100);
        var maximum = Math.Clamp(nextDuty ?? 100, minimum, 100);
        return Math.Clamp(proposed, minimum, maximum);
    }

    public static int NearestPointIndex(IReadOnlyList<Point> projectedPoints, Point pointer)
    {
        if (projectedPoints.Count == 0)
        {
            return -1;
        }

        return projectedPoints
            .Select((point, index) => (index, distance: Math.Abs(point.X - pointer.X)))
            .OrderBy(candidate => candidate.distance)
            .ThenBy(candidate => candidate.index)
            .First()
            .index;
    }

    public static IReadOnlyList<double> CreateTemperatureTicks(
        IEnumerable<double> temperatures,
        int divisions)
    {
        var values = temperatures
            .Where(double.IsFinite)
            .ToArray();
        return values.Length == 0
            ? []
            : CreateTemperatureTicks(values.Min(), values.Max(), divisions, minimumVisibleTemperature: null);
    }

    public static IReadOnlyList<double> CreateTemperatureTicks(
        IEnumerable<double> temperatures,
        int divisions,
        double minimumVisibleTemperature)
    {
        var values = temperatures
            .Where(double.IsFinite)
            .ToArray();
        return values.Length == 0
            ? []
            : CreateTemperatureTicks(values.Min(), values.Max(), divisions, minimumVisibleTemperature);
    }

    public static IReadOnlyList<double> CreateTemperatureTicks(
        double minimumTemperature,
        double maximumTemperature,
        int divisions)
    {
        return CreateTemperatureTicks(
            valuesMinimum: minimumTemperature,
            valuesMaximum: maximumTemperature,
            divisions: divisions,
            minimumVisibleTemperature: null);
    }

    private static IReadOnlyList<double> CreateTemperatureTicks(
        double valuesMinimum,
        double valuesMaximum,
        int divisions,
        double? minimumVisibleTemperature)
    {
        if (divisions < 1 || !double.IsFinite(valuesMinimum) || !double.IsFinite(valuesMaximum))
        {
            return [];
        }

        var lower = Math.Ceiling(Math.Min(valuesMinimum, valuesMaximum) / 10d) * 10d;
        if (minimumVisibleTemperature is { } floor && double.IsFinite(floor))
        {
            lower = Math.Max(lower, Math.Ceiling(floor / 10d) * 10d);
        }

        var upper = Math.Ceiling(Math.Max(valuesMinimum, valuesMaximum) / 10d) * 10d;
        if (upper <= lower)
        {
            upper = lower + 10d;
        }

        var step = Math.Max(10d, Math.Ceiling(((upper - lower) / divisions) / 10d) * 10d);
        upper = lower + (step * divisions);

        return Enumerable.Range(0, divisions + 1)
            .Select(index => lower + ((upper - lower) * index / divisions))
            .ToArray();
    }
}
