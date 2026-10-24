namespace RevoHelper.Core.Control;

public enum ProcessorBoostMode : uint
{
    Disabled = 0,
    Enabled = 1,
    Aggressive = 2,
    EfficientEnabled = 3,
    EfficientAggressive = 4,
    AggressiveAtGuaranteed = 5,
    EfficientAggressiveAtGuaranteed = 6
}

public sealed record ProcessorBoostPolicy(
    ProcessorBoostMode AcMode,
    ProcessorBoostMode DcMode);

public interface IProcessorBoostController
{
    ValueTask<ProcessorBoostPolicy> ReadAsync(CancellationToken cancellationToken);
    ValueTask<ControlResult> ApplyAsync(ProcessorBoostPolicy policy, CancellationToken cancellationToken);
}
