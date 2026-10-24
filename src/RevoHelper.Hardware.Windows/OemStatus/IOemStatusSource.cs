namespace RevoHelper.Hardware.Windows.OemStatus;

public interface IOemStatusSource : IAsyncDisposable
{
    Task RunAsync(
        Func<OemStatusUpdate, ValueTask> onUpdate,
        Func<Exception, ValueTask> onError,
        CancellationToken cancellationToken);
}
