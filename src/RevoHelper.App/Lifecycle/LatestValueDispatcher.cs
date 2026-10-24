namespace RevoHelper.App.Lifecycle;

/// <summary>Coalesces display-only telemetry. Control consumers must use the original samples.</summary>
public sealed class LatestValueDispatcher<T>(Action<Action> post, Action<T> display)
{
    private readonly object sync = new();
    private T latest = default!;
    private bool hasValue;
    private bool queued;
    private bool enabled = true;

    public void Publish(T value)
    {
        lock (sync) { latest = value; hasValue = true; }
        Schedule();
    }

    public void SetPresentationEnabled(bool value)
    {
        lock (sync) enabled = value;
        Schedule();
    }

    private void Schedule()
    {
        lock (sync)
        {
            if (!enabled || queued || !hasValue) return;
            queued = true;
        }
        post(Drain);
    }

    private void Drain()
    {
        T value;
        lock (sync)
        {
            queued = false;
            if (!enabled || !hasValue) return;
            value = latest;
            hasValue = false;
        }
        display(value);
    }
}
