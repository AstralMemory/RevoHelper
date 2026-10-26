using RevoHelper.App.Lifecycle;
using RevoHelper.Core.Control;
using RevoHelper.Core.Hardware;
using RevoHelper.Hardware.Windows.Discovery;

namespace RevoHelper.App.Tests.Lifecycle;

public sealed class DeferredCpuBackendProbeTests
{
    private static readonly CpuIdentity Intel = new(CpuVendor.Intel, "GenuineIntel", 6, 198, 0, 9);

    [Fact]
    public async Task Rechecks_ReuseCompletedProbe_WithoutRepeatingPrivilegedVerification()
    {
        var probes = 0;
        var accepted = 0;
        using var probe = new DeferredCpuBackendProbe(
            (_, _) => { probes++; return Task.FromResult(new DirectCpuPolicyBackendProbeResult(null, null)); },
            action => { action(); return Task.CompletedTask; },
            _ => accepted++, exception => Assert.Fail(exception.Message));

        await probe.StartAsync(Intel);
        await probe.StartAsync(Intel);
        Assert.Equal(1, probes);
        Assert.Equal(1, accepted);
    }

    [Fact]
    public async Task UnknownCpu_DoesNotConsumeTheOneProbeAttempt()
    {
        var probes = 0;
        using var probe = new DeferredCpuBackendProbe(
            (_, _) => { probes++; return Task.FromResult(new DirectCpuPolicyBackendProbeResult(null, null)); },
            action => { action(); return Task.CompletedTask; },
            _ => { }, exception => Assert.Fail(exception.Message));

        await probe.StartAsync(CpuIdentity.Unknown);
        Assert.Equal(0, probes);
        await probe.StartAsync(Intel);
        Assert.Equal(1, probes);
    }

    [Fact]
    public async Task PendingOptionalBackend_DoesNotBlockBasicDiscoveryCompletion()
    {
        var backendResult = new TaskCompletionSource<DirectCpuPolicyBackendProbeResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var probe = new DeferredCpuBackendProbe(
            (_, _) => backendResult.Task, action => { action(); return Task.CompletedTask; },
            _ => { }, exception => Assert.Fail(exception.Message));
        var basicPublished = false;
        Task? optionalTask = null;
        using var coordinator = new RetryableDiscoveryCoordinator(
            _ => { basicPublished = true; optionalTask = probe.StartAsync(Intel); return Task.FromResult(true); },
            _ => Task.CompletedTask, exception => Assert.Fail(exception.Message));

        await coordinator.RequestAsync().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(basicPublished);
        Assert.NotNull(optionalTask);
        Assert.False(optionalTask.IsCompleted);
        backendResult.SetResult(new(null, null));
        await optionalTask;
    }

    [Fact]
    public async Task ShutdownDuringNativeProbe_DisposesLateController_WithoutInstallingIt()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var result = new TaskCompletionSource<DirectCpuPolicyBackendProbeResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var controller = new FakeController();
        var accepted = 0;
        using var probe = new DeferredCpuBackendProbe(
            (_, _) => { started.SetResult(); return result.Task; },
            action => { action(); return Task.CompletedTask; },
            _ => accepted++, exception => Assert.Fail(exception.Message));

        var pending = probe.StartAsync(Intel);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        probe.Dispose();
        result.SetResult(new(controller, null));
        await pending.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(0, accepted);
        Assert.Equal(1, controller.DisposeCalls);
    }

    [Fact]
    public async Task ShutdownAfterQueuedUiCallback_PreventsInstallationAndDisposesCandidate()
    {
        var queued = new TaskCompletionSource<Action>(TaskCreationOptions.RunContinuationsAsynchronously);
        var displayed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var controller = new FakeController();
        var accepted = 0;
        using var probe = new DeferredCpuBackendProbe(
            (_, _) => Task.FromResult(new DirectCpuPolicyBackendProbeResult(controller, null)),
            action => { queued.SetResult(action); return displayed.Task; },
            _ => accepted++, exception => Assert.Fail(exception.Message));

        var pending = probe.StartAsync(Intel);
        var callback = await queued.Task.WaitAsync(TimeSpan.FromSeconds(5));
        probe.Dispose();
        callback();
        displayed.SetResult();
        await pending.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(0, accepted);
        Assert.Equal(1, controller.DisposeCalls);
    }

    private sealed class FakeController : ICpuPolicyController, IDisposable
    {
        public int DisposeCalls { get; private set; }
        public ValueTask<CpuPolicy?> ReadCpuPolicyAsync(CancellationToken cancellationToken) =>
            throw new InvalidOperationException("The test never reads hardware.");
        public ValueTask<ControlResult> ApplyCpuPolicyAsync(CpuPolicy policy, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("The test never writes hardware.");
        public void Dispose() => DisposeCalls++;
    }
}
