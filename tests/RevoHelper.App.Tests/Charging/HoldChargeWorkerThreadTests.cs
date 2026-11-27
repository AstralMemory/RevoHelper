using RevoHelper.App.Charging;

namespace RevoHelper.App.Tests.Charging;

// V1.0.9 Snapshot 1 ran the limit on the worker's first thread. Windows' question about the session
// ending reached that thread while it waited between rounds and was answered at once. In two real
// shutdowns the long countdown was never written; inferred, not observed: the process was ended
// right after that answer.
public sealed class HoldChargeWorkerThreadTests
{
    [Fact]
    public void AnswerSessionEnd_WaitsUntilTheLimitHasFinished()
    {
        using var signal = new ManualResetEventSlim();
        var written = false;
        var codes = new List<int>();
        var worker = new HoldChargeWorkerThread(() =>
        {
            signal.Wait();
            Thread.Sleep(200); // writing the long countdown
            written = true;
            return 0;
        }, code => { lock (codes) codes.Add(code); }, () => { signal.Set(); return true; });
        worker.Start();

        Assert.Equal(new SessionEndAnswer(true, true), worker.AnswerSessionEnd(TimeSpan.FromSeconds(10)));

        Assert.True(written);
        lock (codes) Assert.Equal(new[] { 0 }, codes);
    }

    [Fact]
    public void AnswerSessionEnd_GivesUpAfterTheWait()
    {
        using var release = new ManualResetEventSlim();
        var signalled = 0;
        var worker = new HoldChargeWorkerThread(() =>
        {
            release.Wait();
            return 0;
        }, _ => { }, () => Interlocked.Increment(ref signalled) > 0);
        worker.Start();
        try
        {
            var clock = System.Diagnostics.Stopwatch.StartNew();
            Assert.False(worker.AnswerSessionEnd(TimeSpan.FromMilliseconds(200)).Finished);
            Assert.InRange(clock.Elapsed, TimeSpan.FromMilliseconds(150), TimeSpan.FromSeconds(5));
            Assert.Equal(1, signalled);
        }
        finally { release.Set(); }
    }

    [Fact]
    public void TheLimitRunsOnABackgroundThreadOfItsOwn()
    {
        var caller = Environment.CurrentManagedThreadId;
        int? ranOn = null;
        var background = false;
        using var done = new ManualResetEventSlim();
        var worker = new HoldChargeWorkerThread(() =>
        {
            ranOn = Environment.CurrentManagedThreadId;
            background = Thread.CurrentThread.IsBackground;
            return 0;
        }, _ => done.Set(), () => true);

        worker.Start();

        Assert.True(done.Wait(TimeSpan.FromSeconds(10)));
        Assert.NotEqual(caller, ranOn);
        Assert.True(background);
    }

    [Fact]
    public void ALimitThatThrows_EndsWithCodeTwo()
    {
        using var done = new ManualResetEventSlim();
        var codes = new List<int>();
        var worker = new HoldChargeWorkerThread(() => throw new InvalidOperationException("boom"),
            code => { lock (codes) codes.Add(code); done.Set(); }, () => true);

        worker.Start();

        Assert.True(done.Wait(TimeSpan.FromSeconds(10)));
        lock (codes) Assert.Equal(new[] { 2 }, codes);
    }

    [Fact]
    public void ASignalThatCannotBeSet_StillWaitsForTheLimit()
    {
        var worker = new HoldChargeWorkerThread(() => 0, _ => { }, () => throw new UnauthorizedAccessException());
        worker.Start();

        Assert.Equal(new SessionEndAnswer(null, true), worker.AnswerSessionEnd(TimeSpan.FromSeconds(10)));
    }

    // The worker's own answer to Windows. A stop that was asked for came first: nothing is set, so
    // the stop goes ahead and the original values are put back. Without a stop the hold is asked.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void TheWorkersOwnShutdownSignal_IsNotSetAfterAStop(bool stopped)
    {
        // Unnamed: a running RevoHelper on this machine is never signalled by a test.
        using var stop = new EventWaitHandle(stopped, EventResetMode.ManualReset);
        using var shutdown = new EventWaitHandle(false, EventResetMode.ManualReset);

        Assert.Equal(!stopped, HoldChargeHost.SignalShutdownUnlessStopped(stop, shutdown));
        Assert.Equal(!stopped, shutdown.WaitOne(0));
    }
}
