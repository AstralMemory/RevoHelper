namespace RevoHelper.Hardware.Windows.OemControl;

public interface IGcuCustomPerformanceChannel
{
    ValueTask<string> ReadCustomPerformanceStatusAsync(CancellationToken cancellationToken);
    ValueTask WriteCustomPerformanceAsync(string payload, CancellationToken cancellationToken);
}
