namespace RevoHelper.Core.Control;

public sealed record ProfileComposition(
    string Name,
    PlatformMode? PlatformMode,
    CpuPolicy? Cpu,
    GpuPolicy? Gpu,
    FanCurvePolicy? Fan,
    LightingPolicy? Lighting,
    LiquidCoolingPolicy? LiquidCooling);
