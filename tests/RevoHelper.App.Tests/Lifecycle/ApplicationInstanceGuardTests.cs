using RevoHelper.App.Lifecycle;

namespace RevoHelper.App.Tests.Lifecycle;

public sealed class ApplicationInstanceGuardTests
{
    [Fact]
    public void TryAcquire_WhenAnotherRevoHelperProcessExists_RejectsLaunch()
    {
        using var guard = new ApplicationInstanceGuard(
            "revohelper-test-" + Guid.NewGuid().ToString("N"),
            currentProcessId: 20,
            processSnapshot: () => [
                new(20, "RevoHelper"),
                new(10, "RevoHelper"),
                new(30, "GCUService")]);

        Assert.False(guard.TryAcquire());
    }

    [Fact]
    public void TryAcquire_WhenOlderPreviewBuildExists_RejectsLaunch()
    {
        using var guard = new ApplicationInstanceGuard(
            "revohelper-test-" + Guid.NewGuid().ToString("N"),
            currentProcessId: 20,
            processSnapshot: () => [
                new(20, "RevoHelper-Preview-20260813-PerformanceFix"),
                new(10, "RevoHelper-Preview-20260812"),
                new(30, "GCUService")]);

        Assert.False(guard.TryAcquire());
    }

    [Fact]
    public void TryAcquire_IgnoresCurrentProcessAndUnrelatedServices()
    {
        using var guard = new ApplicationInstanceGuard(
            "revohelper-test-" + Guid.NewGuid().ToString("N"),
            currentProcessId: 20,
            processSnapshot: () => [
                new(20, "RevoHelper"),
                new(30, "GCUService"),
                new(40, "XtuService")]);

        Assert.True(guard.TryAcquire());
    }
}
