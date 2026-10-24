namespace RevoHelper.Core.Fans;

public sealed record FanCurvePoint(
    int Id,
    int UpTemperatureC,
    int DownTemperatureC,
    int DutyPercent);
