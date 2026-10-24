namespace RevoHelper.Core.Control;

public interface IPlatformModeController
{
    ValueTask<PlatformModeSelection?> ReadPlatformModeAsync(CancellationToken cancellationToken);
    ValueTask<ControlResult> SetPlatformModeAsync(PlatformModeSelection selection, CancellationToken cancellationToken);
}
