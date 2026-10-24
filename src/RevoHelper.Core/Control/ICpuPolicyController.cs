namespace RevoHelper.Core.Control;

public sealed record CpuPolicy(int Pl1Watts, int Pl2Watts, int? MaxFrequencyMhz);

public interface ICpuPolicyController
{
    ValueTask<CpuPolicy?> ReadCpuPolicyAsync(CancellationToken cancellationToken);
    ValueTask<ControlResult> ApplyCpuPolicyAsync(CpuPolicy policy, CancellationToken cancellationToken);
}
