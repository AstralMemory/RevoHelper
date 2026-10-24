namespace RevoHelper.Core.Control;

public sealed record FanBoostState(bool Supported, bool Enabled);

public interface IFanBoostController
{
    ValueTask<FanBoostState> ReadAsync(CancellationToken cancellationToken);
    ValueTask<ControlResult> SetAsync(bool enabled, CancellationToken cancellationToken);
}
