using RevoHelper.App.Lifecycle;

namespace RevoHelper.App.Tests.Lifecycle;

public sealed class ApplicationInstanceGuardTests
{
    [Fact]
    public void TryAcquire_WhenTwoCurrentLaunchesSeeEachOther_OnlyMutexWinnerContinues()
    {
        var mutexName = "revohelper-test-" + Guid.NewGuid().ToString("N");
        ProcessIdentity[] snapshot = [new(20, "RevoHelper"), new(10, "RevoHelper"), new(30, "GCUService")];
        using var first = new ApplicationInstanceGuard(
            mutexName,
            currentProcessId: 20,
            processSnapshot: () => snapshot);
        using var second = new ApplicationInstanceGuard(
            mutexName,
            currentProcessId: 10,
            processSnapshot: () => snapshot);

        Assert.True(first.TryAcquire());
        Assert.False(second.TryAcquire());
        first.Dispose();
        Assert.True(second.TryAcquire());
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
    public void TryAcquire_WhenLegacyPreviewRejectsLaunch_ReleasesMutexForNextAttempt()
    {
        var mutexName = "revohelper-test-" + Guid.NewGuid().ToString("N");
        using var blocked = new ApplicationInstanceGuard(mutexName, 20,
            () => [new(20, "RevoHelper"), new(10, "RevoHelper-Preview-20260812")]);
        Assert.False(blocked.TryAcquire());

        using var next = new ApplicationInstanceGuard(mutexName, 30,
            () => [new(30, "RevoHelper")]);
        Assert.True(next.TryAcquire());
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
