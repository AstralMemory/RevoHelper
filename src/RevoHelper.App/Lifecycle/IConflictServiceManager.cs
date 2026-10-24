namespace RevoHelper.App.Lifecycle;

public interface IConflictServiceManager
{
    Task<IReadOnlyList<ConflictServiceStatus>> DetectAsync(CancellationToken cancellationToken);

    Task StopAsync(IReadOnlyList<string> serviceNames, CancellationToken cancellationToken);

    Task RestoreAsync(IReadOnlyList<string> serviceNames, CancellationToken cancellationToken);
}
