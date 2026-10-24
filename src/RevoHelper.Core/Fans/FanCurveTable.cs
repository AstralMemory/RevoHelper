namespace RevoHelper.Core.Fans;

public sealed record FanCurveTable(
    string Name,
    bool IndependentCpuGpuControl,
    IReadOnlyList<FanCurvePoint> Cpu,
    IReadOnlyList<FanCurvePoint> Gpu);

public sealed record FanCurveValidationResult(
    bool IsValid,
    IReadOnlyList<string> Errors);
