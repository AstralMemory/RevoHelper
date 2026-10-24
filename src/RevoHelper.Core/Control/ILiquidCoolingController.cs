namespace RevoHelper.Core.Control;

public enum LiquidCoolingStrategy { Quieter, Balanced, Performance }
public sealed record LiquidCoolingPolicy(LiquidCoolingStrategy Strategy);

public interface ILiquidCoolingController
{
    ValueTask<LiquidCoolingPolicy?> ReadLiquidCoolingAsync(CancellationToken cancellationToken);
    ValueTask<ControlResult> ApplyLiquidCoolingAsync(LiquidCoolingPolicy policy, CancellationToken cancellationToken);
}
