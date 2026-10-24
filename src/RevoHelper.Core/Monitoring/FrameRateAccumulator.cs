namespace RevoHelper.Core.Monitoring;

public readonly record struct PresentedFrame(
    int ProcessId,
    ulong SwapChainAddress,
    double TimeInSeconds,
    double MillisecondsBetweenPresents);

/// <summary>
/// Bounded, per-target rolling render-present rate. This is not display refresh rate or generated-frame FPS.
/// Each swap chain is independent; multiple surfaces in one process are never added together.
/// </summary>
public sealed class FrameRateAccumulator
{
    private const int MaximumSwapChains = 16;
    private const int MaximumSamplesPerChain = 2048;
    private readonly Dictionary<ulong, Chain> chains = [];
    private ProcessTarget? target;

    public void SetTarget(ProcessTarget? value)
    {
        if (target?.ProcessId == value?.ProcessId && target?.StartedAtUtc == value?.StartedAtUtc)
        {
            target = value;
            return;
        }
        target = value;
        chains.Clear();
    }

    public void Reset()
    {
        chains.Clear();
        target = null;
    }

    public void Add(PresentedFrame frame, DateTimeOffset receivedAtUtc)
    {
        if (frame.ProcessId != target?.ProcessId ||
            !double.IsFinite(frame.TimeInSeconds) || frame.TimeInSeconds < 0 ||
            !double.IsFinite(frame.MillisecondsBetweenPresents) ||
            frame.MillisecondsBetweenPresents <= 0 || frame.MillisecondsBetweenPresents > 10000)
        {
            return;
        }

        if (!chains.TryGetValue(frame.SwapChainAddress, out var chain))
        {
            if (chains.Count >= MaximumSwapChains)
            {
                var oldest = chains.MinBy(pair => pair.Value.ReceivedAtUtc).Key;
                chains.Remove(oldest);
            }
            chain = new Chain();
            chains.Add(frame.SwapChainAddress, chain);
        }

        // Reject repeated/out-of-order rows rather than counting a frame twice.
        if (chain.Samples.Count > 0 && frame.TimeInSeconds <= chain.LatestTimeInSeconds)
        {
            return;
        }
        chain.LatestTimeInSeconds = frame.TimeInSeconds;
        chain.ReceivedAtUtc = receivedAtUtc;
        chain.Samples.Enqueue(new Sample(frame.TimeInSeconds, frame.MillisecondsBetweenPresents));
        chain.TotalIntervalMilliseconds += frame.MillisecondsBetweenPresents;
        while (chain.Samples.Count > MaximumSamplesPerChain ||
               (chain.Samples.Count > 1 && chain.Samples.Peek().TimeInSeconds < frame.TimeInSeconds - 1))
        {
            chain.TotalIntervalMilliseconds -= chain.Samples.Dequeue().MillisecondsBetweenPresents;
        }
    }

    public FrameRateSnapshot GetSnapshot(DateTimeOffset now)
    {
        if (target is null)
        {
            return new(null, null, now, FrameRateStatus.NoTarget);
        }

        Chain? dominant = null;
        foreach (var chain in chains.Values)
        {
            if (now - chain.ReceivedAtUtc > TimeSpan.FromSeconds(2) || now < chain.ReceivedAtUtc ||
                chain.Samples.Count < 2)
            {
                continue;
            }
            if (dominant is null || chain.Samples.Count > dominant.Samples.Count ||
                (chain.Samples.Count == dominant.Samples.Count && chain.ReceivedAtUtc > dominant.ReceivedAtUtc))
            {
                dominant = chain;
            }
        }

        if (dominant is null)
        {
            return new(target, null, now, FrameRateStatus.WaitingForFrames);
        }
        var rate = dominant.Samples.Count * 1000d / dominant.TotalIntervalMilliseconds;
        return double.IsFinite(rate) && rate > 0
            ? new(target, rate, dominant.ReceivedAtUtc, FrameRateStatus.Available)
            : new(target, null, now, FrameRateStatus.WaitingForFrames);
    }

    private sealed class Chain
    {
        public Queue<Sample> Samples { get; } = new();
        public double TotalIntervalMilliseconds { get; set; }
        public double LatestTimeInSeconds { get; set; }
        public DateTimeOffset ReceivedAtUtc { get; set; }
    }

    private readonly record struct Sample(double TimeInSeconds, double MillisecondsBetweenPresents);
}
