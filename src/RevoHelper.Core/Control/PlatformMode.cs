namespace RevoHelper.Core.Control;

public enum PlatformMode
{
    Office,
    Balanced,
    TurboQuiet,
    TurboHigh,
    Custom
}

public sealed record PlatformModeSelection(PlatformMode Mode, int ProfileIndex = 0)
{
    /// <summary>
    /// Read back from hardware: Turbo with neither the quiet nor the high strategy on. Display
    /// only. It stays out of equality, so switching, verification and the saved settings still
    /// treat basic Turbo as <see cref="PlatformMode.TurboQuiet"/>, exactly as before.
    /// </summary>
    public bool IsBasicTurbo { get; init; }

    public bool Equals(PlatformModeSelection? other) =>
        other is not null && Mode == other.Mode && ProfileIndex == other.ProfileIndex;

    public override int GetHashCode() => HashCode.Combine(Mode, ProfileIndex);
}

public enum ControlModule
{
    PlatformMode,
    CpuPolicy,
    GpuPolicy,
    FanCurve,
    Lighting,
    LiquidCooling
}
