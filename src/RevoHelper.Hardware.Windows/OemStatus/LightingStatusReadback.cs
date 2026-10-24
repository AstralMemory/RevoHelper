using RevoHelper.Core.Control;

namespace RevoHelper.Hardware.Windows.OemStatus;

public static class LightingStatusReadback
{
    public static async Task<string> WaitAsync(
        LightingZone zone, Task<string> response, Func<Task> requestStatus, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (zone is LightingZone.CoastLightbar or LightingZone.Logo)
        {
            // These zones may only acknowledge power/brightness on an explicit request.
            // Do not impose the keyboard's unsolicited-event grace period on each edit.
            if (!response.IsCompleted) await requestStatus().ConfigureAwait(false);
            return await response.WaitAsync(TimeSpan.FromSeconds(3), cancellationToken).ConfigureAwait(false);
        }
        try
        {
            return await response.WaitAsync(TimeSpan.FromMilliseconds(650), cancellationToken).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            if (!response.IsCompleted) await requestStatus().ConfigureAwait(false);
            return await response.WaitAsync(TimeSpan.FromSeconds(3), cancellationToken).ConfigureAwait(false);
        }
    }
}
