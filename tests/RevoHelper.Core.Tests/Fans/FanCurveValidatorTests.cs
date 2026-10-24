using RevoHelper.Core.Fans;

namespace RevoHelper.Core.Tests.Fans;

public sealed class FanCurveValidatorTests
{
    [Fact]
    public void Validate_AcceptsSafeOrderedSixteenPointTable()
    {
        var result = FanCurveValidator.Validate(Table(Points()));

        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void Validate_ReportsEveryUnsafeShape()
    {
        var points = Points().ToArray();
        points[4] = points[4] with { UpTemperatureC = 49, DownTemperatureC = 10, DutyPercent = 120 };
        points[8] = points[8] with { DutyPercent = 10 };
        points[^1] = points[^1] with { DutyPercent = 90 };

        var result = FanCurveValidator.Validate(Table(points[..15]));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Contains("16", StringComparison.Ordinal));
        Assert.Contains(result.Errors, error => error.Contains("DownT", StringComparison.Ordinal));
        Assert.Contains(result.Errors, error => error.Contains("0-100", StringComparison.Ordinal));
        Assert.Contains(result.Errors, error => error.Contains("nondecreasing", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Errors, error => error.Contains("100%", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_AcceptsOemThresholdPairsAnd255Sentinels()
    {
        var points = Enumerable.Range(0, 16)
            .Select(index => new FanCurvePoint(
                index,
                index switch { 0 => 0, >= 11 => 255, _ => 44 + index * 4 },
                index >= 10 ? 255 : 46 + index * 4,
                index >= 10 ? 100 : index * 10))
            .ToArray();

        var result = FanCurveValidator.Validate(Table(points));

        Assert.True(result.IsValid);
    }

    private static FanCurveTable Table(IReadOnlyList<FanCurvePoint> points) =>
        new("M4T2", false, points, points);

    internal static IReadOnlyList<FanCurvePoint> Points() =>
        Enumerable.Range(0, 16)
            .Select(index => new FanCurvePoint(
                index,
                Math.Min(255, 45 + index * 3),
                Math.Min(255, 42 + index * 3),
                index == 15 ? 100 : Math.Min(90, index * 6)))
            .ToArray();
}
