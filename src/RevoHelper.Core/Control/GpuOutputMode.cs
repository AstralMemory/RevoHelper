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

/// <summary>
/// <paramref name="Mode"/> is the saved setting. It takes effect only after a real restart, so
/// <paramref name="PanelOnDiscreteGpu"/> (which GPU drives the built-in panel now, null when
/// unknown) tells whether the setting is already live.
/// </summary>
public sealed record GpuOutputModeState(
    GpuOutputMode Mode,
    GpuOutputModeCapabilities Capabilities,
    bool? PanelOnDiscreteGpu = null)
{
    public bool IsPendingRestart =>
        PanelOnDiscreteGpu is { } discrete && discrete != (Mode == GpuOutputMode.Discrete);
}
