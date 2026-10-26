namespace RevoHelper.App.Lifecycle;

/// <summary>Restore the dynamic overlay before the manual baseline. A failed or
/// timed-out outer restore must leave the inner recovery journal untouched.</summary>
public static class LayeredFrequencyRecovery
{
    public static async Task<bool> RunAsync(
        Func<CancellationToken, Task<bool>> restoreDynamic,
        Func<CancellationToken, Task<bool>> restoreManual,
        CancellationToken token)
    {
        if (!await restoreDynamic(token).WaitAsync(token).ConfigureAwait(false)) return false;
        token.ThrowIfCancellationRequested();
        return await restoreManual(token).WaitAsync(token).ConfigureAwait(false);
    }
}
