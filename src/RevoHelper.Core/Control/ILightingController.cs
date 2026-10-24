namespace RevoHelper.Core.Control;

public enum LightingBehavior { Off, Static, PlatformModeColor }
public sealed record LightingPolicy(LightingBehavior Behavior, uint? Rgb = null);

public interface ILightingController
{
    ValueTask<LightingPolicy?> ReadLightingAsync(CancellationToken cancellationToken);
    ValueTask<ControlResult> ApplyLightingAsync(LightingPolicy policy, CancellationToken cancellationToken);
}
