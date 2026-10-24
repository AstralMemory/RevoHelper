namespace RevoHelper.Core.Control;

public enum GpuOutputMode
{
    Hybrid,
    Discrete,
    Integrated
}

public sealed record GpuOutputModeCapabilities(
    bool SupportsHybrid,
    bool SupportsDiscrete,
    bool SupportsIntegrated)
{
    public bool Supports(GpuOutputMode mode) => mode switch
    {
        GpuOutputMode.Hybrid => SupportsHybrid,
        GpuOutputMode.Discrete => SupportsDiscrete,
        GpuOutputMode.Integrated => SupportsIntegrated,
        _ => false
    };
}

public sealed record GpuOutputModeState(
    GpuOutputMode Mode,
    GpuOutputModeCapabilities Capabilities);
