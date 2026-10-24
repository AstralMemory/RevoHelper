namespace RevoHelper.Core.Fans;

public static class FanCurveValidator
{
    public static FanCurveValidationResult Validate(FanCurveTable table)
    {
        var errors = new List<string>();
        ValidateChannel("CPU", table.Cpu, errors);
        ValidateChannel("GPU", table.Gpu, errors);
        return new(errors.Count == 0, errors);
    }

    private static void ValidateChannel(
        string channel,
        IReadOnlyList<FanCurvePoint> points,
        ICollection<string> errors)
    {
        if (points.Count != 16)
        {
            errors.Add($"{channel} curve must contain exactly 16 points.");
        }

        for (var index = 0; index < points.Count; index++)
        {
            var point = points[index];
            if (point.UpTemperatureC is < 0 or > 255 ||
                point.DownTemperatureC is < 0 or > 255)
            {
                errors.Add($"{channel} point {index}: UpT and DownT must be in the 0-255 range.");
            }

            if (point.DutyPercent is < 0 or > 100)
            {
                errors.Add($"{channel} point {index}: duty must be in the 0-100 range.");
            }

            if (index > 0)
            {
                var previous = points[index - 1];
                if (point.UpTemperatureC < previous.UpTemperatureC)
                {
                    errors.Add($"{channel} UpT values must be nondecreasing.");
                }

                if (point.DownTemperatureC < previous.DownTemperatureC)
                {
                    errors.Add($"{channel} DownT values must be nondecreasing.");
                }

                if (point.DutyPercent < previous.DutyPercent)
                {
                    errors.Add($"{channel} duty values must be nondecreasing.");
                }
            }
        }

        if (points.Count == 0 || points[^1].DutyPercent != 100)
        {
            errors.Add($"{channel} terminal safety point must request 100% duty.");
        }
    }
}
