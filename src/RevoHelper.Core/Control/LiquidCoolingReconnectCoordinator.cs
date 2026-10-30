namespace RevoHelper.Core.Control;

public sealed record LiquidCoolingReconnectResult(bool Attempted, bool Connected, LiquidCoolingState? State = null);

/// <summary>
/// Session-only recovery for a previously confirmed device. It never discovers or
/// selects devices. The caller serializes its callbacks with manual device actions.
/// A command acknowledgement is not a confirmed connection.
/// </summary>
public sealed class LiquidCoolingReconnectCoordinator(Func<DateTimeOffset>? utcNow = null)
{
    public static readonly TimeSpan RetryInterval = TimeSpan.FromSeconds(3);
    public static readonly TimeSpan MemoryDuration = TimeSpan.FromMinutes(10);
    private readonly Func<DateTimeOffset> now = utcNow ?? (() => DateTimeOffset.UtcNow);
    private readonly object stateGate = new();
    private readonly SemaphoreSlim singleAttempt = new(1, 1);
    private string? rememberedDevice;
    private bool wasConnected;
    private bool suppressed;
    private DateTimeOffset? deadline;
    private DateTimeOffset? nextAttempt;
    private long revision;
    private CancellationTokenSource? activeAttempt;

    public DateTimeOffset? Deadline { get { lock (stateGate) { ExpireCore(); return deadline; } } }

    public void Observe(LiquidCoolingState? state)
    {
        if (state is null) return; // An unavailable read is not a disconnect.
        lock (stateGate)
        {
            if (suppressed) return;
            if (state.IsPriming) { CancelCore(true); return; }
            if (state.Connected && !string.IsNullOrWhiteSpace(state.SelectedDeviceId))
            {
                rememberedDevice = state.SelectedDeviceId;
                wasConnected = true; deadline = null; nextAttempt = null;
                return;
            }
            if (rememberedDevice is null) return;
            // A powered-off device can temporarily lose its reported ID. Keep
            // the remembered deadline, but never select/connect until it returns.
            if (!string.IsNullOrWhiteSpace(state.SelectedDeviceId)
                && !string.Equals(rememberedDevice, state.SelectedDeviceId, StringComparison.OrdinalIgnoreCase))
            { CancelCore(true); return; }
            if (wasConnected)
            {
                wasConnected = false;
                deadline = now() + MemoryDuration;
                nextAttempt = now() + RetryInterval;
            }
            ExpireCore();
        }
    }

    // Only an explicitly confirmed new manual/startup connection may undo a
    // manual-disconnect/selection/priming suppression, never a stale status poll.
    public void RememberConnected(LiquidCoolingState? state)
    {
        if (state is not { Connected: true, IsPriming: false } || string.IsNullOrWhiteSpace(state.SelectedDeviceId)) return;
        lock (stateGate) { suppressed = false; }
        Observe(state);
    }

    public void Cancel()
    {
        lock (stateGate) CancelCore(true);
    }

    public async Task<LiquidCoolingReconnectResult> TryReconnectAsync(
        Func<CancellationToken, ValueTask<LiquidCoolingState?>> readState,
        Func<string, CancellationToken, ValueTask<ControlResult>> connectSameDevice,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(readState);
        ArgumentNullException.ThrowIfNull(connectSameDevice);
        if (!await singleAttempt.WaitAsync(0, cancellationToken).ConfigureAwait(false)) return new(false, false);
        CancellationTokenSource? attempt = null;
        try
        {
            string device; long version;
            lock (stateGate)
            {
                ExpireCore();
                if (suppressed || deadline is null || rememberedDevice is null || now() < nextAttempt) return new(false, false);
                device = rememberedDevice; version = revision;
                nextAttempt = now() + RetryInterval;
                attempt = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                activeAttempt = attempt;
            }
            var state = await readState(attempt.Token).ConfigureAwait(false);
            if (!IsCurrent(version)) return new(false, false);
            Observe(state);
            if (state?.Connected == true) return new(false, true, state);
            if (state is not { CanControl: true, IsPriming: false }
                || !string.Equals(state.SelectedDeviceId, device, StringComparison.OrdinalIgnoreCase)
                || !IsCurrent(version)) return new(false, false, state);
            attempt.Token.ThrowIfCancellationRequested();
            await connectSameDevice(device, attempt.Token).ConfigureAwait(false);
            if (!IsCurrent(version)) return new(true, false);
            var confirmed = await readState(attempt.Token).ConfigureAwait(false);
            if (!IsCurrent(version)) return new(true, false);
            Observe(confirmed);
            return new(true, confirmed?.Connected == true
                && string.Equals(confirmed.SelectedDeviceId, device, StringComparison.OrdinalIgnoreCase), confirmed);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        { return new(false, false); }
        catch (Exception exception) when (exception is not OperationCanceledException)
        { return new(false, false); } // Retry quietly, never extend the fixed deadline.
        finally
        {
            lock (stateGate) { if (ReferenceEquals(activeAttempt, attempt)) activeAttempt = null; }
            attempt?.Dispose(); singleAttempt.Release();
        }
    }

    private bool IsCurrent(long version)
    {
        lock (stateGate) { ExpireCore(); return !suppressed && version == revision; }
    }
    private void ExpireCore()
    {
        if (deadline is { } end && now() >= end) CancelCore(false);
    }
    private void CancelCore(bool suppress)
    {
        revision++; rememberedDevice = null; wasConnected = false;
        deadline = null; nextAttempt = null; suppressed = suppress;
        activeAttempt?.Cancel();
    }
}
