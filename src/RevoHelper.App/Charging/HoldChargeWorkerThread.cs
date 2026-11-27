using System.IO;

namespace RevoHelper.App.Charging;

/// <summary>
/// The worker process runs the charge limit on a thread of its own, so that its first thread keeps
/// answering Windows. When the session ends Windows asks this process too, and may end it once it
/// has answered. Up to V1.0.9 Snapshot 1 the limit ran on that first thread: the question reached
/// it while it waited between rounds and the answer went out at once. In two real shutdowns
/// (2026-11-24 and 25) the long countdown was never written; inferred from the logs, not observed:
/// Windows ended the process right after that answer. Now the answer waits until the limit has
/// finished, for a bounded time.
/// </summary>
public sealed class HoldChargeWorkerThread
{
    private readonly Thread thread;
    private readonly Func<bool> signalShutdown;

    /// <param name="run">The charge limit; returns the process exit code.</param>
    /// <param name="finished">Called on the limit's thread when it has finished, with that code.</param>
    /// <param name="signalShutdown">Tells the limit that the session is ending, as the window process does; false when it was not told because a stop came first.</param>
    public HoldChargeWorkerThread(Func<int> run, Action<int> finished, Func<bool> signalShutdown)
    {
        this.signalShutdown = signalShutdown;
        thread = new Thread(() =>
        {
            var code = 2;
            try { code = run(); }
            catch (Exception) { }
            finally { finished(code); }
        })
        { IsBackground = true, Name = "Charge limit" };
    }

    public void Start() => thread.Start();

    /// <summary>
    /// The session is ending: the limit is told so and this waits, at most <paramref name="wait"/>,
    /// for it to finish: the long countdown written, or the original values put back.
    /// </summary>
    public SessionEndAnswer AnswerSessionEnd(TimeSpan wait)
    {
        bool? told;
        // Best effort: the window process may have set the same signal already.
        try { told = signalShutdown(); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
                                              or WaitHandleCannotBeOpenedException) { told = null; }
        return new(told, thread.Join(wait));
    }
}

/// <param name="Told">Whether the limit was told to hold for the shutdown: false when a stop came first, null when the signal could not be set.</param>
/// <param name="Finished">Whether the limit finished within the wait.</param>
public readonly record struct SessionEndAnswer(bool? Told, bool Finished);
