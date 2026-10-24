namespace RevoHelper.Core.Control;

public sealed class NullProcessorFrequencyLimitController : IProcessorFrequencyLimitController
{
    private ProcessorFrequencyLimitPolicy current = ProcessorFrequencyLimitPolicy.Uniform(0);

    public ValueTask<ProcessorFrequencyLimitPolicy> ReadAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(current);
    }

    public ValueTask<ControlResult> ApplyAsync(
        ProcessorFrequencyLimitPolicy policy,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        current = policy;
        return ValueTask.FromResult(new ControlResult(true, "未接入 Windows 频率控制器；仅保留本地状态。"));
    }
}
