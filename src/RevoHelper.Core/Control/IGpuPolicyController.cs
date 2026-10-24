namespace RevoHelper.Core.Control;

public enum GpuOperatingMode { Hybrid, Discrete, Integrated, Automatic }
public sealed record GpuPolicy(GpuOperatingMode OperatingMode, int? TgpWatts);

public interface IGpuPolicyController
{
    ValueTask<GpuPolicy?> ReadGpuPolicyAsync(CancellationToken cancellationToken);
    ValueTask<ControlResult> ApplyGpuPolicyAsync(GpuPolicy policy, CancellationToken cancellationToken);
}
