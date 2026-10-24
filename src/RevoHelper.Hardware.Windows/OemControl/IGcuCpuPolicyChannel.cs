namespace RevoHelper.Hardware.Windows.OemControl;

public interface IGcuCpuPolicyChannel
{
    ValueTask<GcuCpuPolicyStatus> ReadStatusAsync(CancellationToken cancellationToken);
    ValueTask WritePl1Async(int watts, CancellationToken cancellationToken);
    ValueTask WritePl2Async(int watts, CancellationToken cancellationToken);
}
