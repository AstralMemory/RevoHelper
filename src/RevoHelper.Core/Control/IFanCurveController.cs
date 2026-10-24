namespace RevoHelper.Core.Control;

public sealed record FanCurvePolicy(string CurveId);

public interface IFanCurveController
{
    ValueTask<FanCurvePolicy?> ReadFanCurveAsync(CancellationToken cancellationToken);
    ValueTask<ControlResult> ApplyFanCurveAsync(FanCurvePolicy policy, CancellationToken cancellationToken);
}
