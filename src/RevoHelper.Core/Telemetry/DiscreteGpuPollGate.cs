namespace RevoHelper.Core.Telemetry;

/// <summary>
/// Decides whether this tick may query the discrete GPU's driver. Every driver query counts as
/// activity: asked once a second, an idle GPU never powers down. So a sleeping GPU is never asked,
/// and an awake but idle one is left alone for a short rest so it can fall asleep.
/// </summary>
public sealed class DiscreteGpuPollGate
{
    public static readonly TimeSpan Rest = TimeSpan.FromSeconds(15);
    public const int BaseIdleSamples = 10;
    public const int MaxIdleSamples = 300;
    public const double IdleUsagePercent = 2;

    private int idleSamples;
    private int requiredIdleSamples = BaseIdleSamples;
    private DateTimeOffset? restUntil;

    /// <summary>True while the device itself reports a low-power state.</summary>
    public bool Asleep { get; private set; }

    /// <param name="powered">Device power state from Windows; null when it cannot be read.</param>
    public bool ShouldPoll(bool? powered, DateTimeOffset now)
    {
        if (powered is null)
        {
            // Unknown: behave as before rather than hide a GPU that may be working.
            Asleep = false; restUntil = null; idleSamples = 0;
            return true;
        }
        if (powered == false)
        {
            // The rest worked (or Windows put it to sleep on its own): start fresh next time.
            Asleep = true; restUntil = null; idleSamples = 0; requiredIdleSamples = BaseIdleSamples;
            return false;
        }
        Asleep = false;
        if (restUntil is { } until)
        {
            if (now < until) return false;
            // Still powered after a full rest: something else keeps it on (a monitor on the
            // discrete GPU, a running program). Wait longer before resting again.
            restUntil = null;
            requiredIdleSamples = Math.Min(requiredIdleSamples * 3, MaxIdleSamples);
        }
        return true;
    }

    /// <summary>Feed the reading taken after <see cref="ShouldPoll"/> returned true.</summary>
    public void Observe(double? usagePercent, DateTimeOffset now)
    {
        if (usagePercent is not { } usage || usage > IdleUsagePercent)
        {
            idleSamples = 0;
            if (usagePercent is not null) requiredIdleSamples = BaseIdleSamples;
            return;
        }
        if (++idleSamples < requiredIdleSamples) return;
        idleSamples = 0;
        restUntil = now + Rest;
    }
}
