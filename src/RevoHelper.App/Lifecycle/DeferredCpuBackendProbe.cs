using RevoHelper.Core.Hardware;
using RevoHelper.Hardware.Windows.Discovery;

namespace RevoHelper.App.Lifecycle;

/// <summary>
/// Optional privileged verification runs once per process, independently of OEM rediscovery.
/// Ownership transfers only while the application is alive; late candidates are disposed.
/// </summary>
public sealed class DeferredCpuBackendProbe : IDisposable
{
    private readonly object sync = new();
    private readonly CancellationTokenSource lifetime = new();
    private readonly CancellationToken cancellationToken;
    private readonly Func<CpuIdentity, CancellationToken, Task<DirectCpuPolicyBackendProbeResult>> probe;
    private readonly Func<Action, Task> dispatch;
    private readonly Action<DirectCpuPolicyBackendProbeResult> accept;
    private readonly Action<Exception> reportFailure;
    private Task? runningTask;
    private bool stopped;

    public DeferredCpuBackendProbe(
        Func<CpuIdentity, CancellationToken, Task<DirectCpuPolicyBackendProbeResult>> probe,
        Func<Action, Task> dispatch,
        Action<DirectCpuPolicyBackendProbeResult> accept,
        Action<Exception> reportFailure)
    {
        this.probe = probe;
        this.dispatch = dispatch;
        this.accept = accept;
        this.reportFailure = reportFailure;
        cancellationToken = lifetime.Token;
    }

    public Task StartAsync(CpuIdentity cpu)
    {
        lock (sync)
        {
            if (stopped || cpu.Vendor is CpuVendor.Unknown) return Task.CompletedTask;
            return runningTask ??= Task.Run(() => RunAsync(cpu));
        }
    }

    private async Task RunAsync(CpuIdentity cpu)
    {
        DirectCpuPolicyBackendProbeResult? result = null;
        var accepted = false;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            result = await probe(cpu, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            await dispatch(() =>
            {
                lock (sync)
                {
                    if (stopped) return;
                    accept(result);
                    accepted = true;
                }
            }).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception exception) { reportFailure(exception); }
        finally
        {
            if (!accepted && result?.Controller is IDisposable disposable)
            {
                try { disposable.Dispose(); }
                catch (Exception exception) { reportFailure(exception); }
            }
        }
    }

    public void Dispose()
    {
        Task? pending;
        lock (sync)
        {
            if (stopped) return;
            stopped = true;
            pending = runningTask;
        }
        lifetime.Cancel();
        if (pending is null || pending.IsCompleted) lifetime.Dispose();
        else _ = pending.ContinueWith(_ => lifetime.Dispose(), CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }
}
