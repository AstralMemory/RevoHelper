using RevoHelper.App.Settings;

namespace RevoHelper.App.Tests.Settings;

public sealed class StartupRegistrationSynchronizerTests
{
    [Fact]
    public void Synchronize_MigratesApprovedRegistrationFromOldPortablePath()
    {
        var registration = new FakeRegistration(current: false, registeredAndApproved: true);

        var enabled = StartupRegistrationSynchronizer.Synchronize(registration);

        Assert.True(enabled);
        Assert.Equal([true], registration.Writes);
    }

    [Fact]
    public void Synchronize_DoesNotReenableEntryDisabledByWindows()
    {
        var registration = new FakeRegistration(current: false, registeredAndApproved: false);

        var enabled = StartupRegistrationSynchronizer.Synchronize(registration);

        Assert.False(enabled);
        Assert.Empty(registration.Writes);
    }

    [Fact]
    public void Synchronize_LeavesCurrentRegistrationUntouched()
    {
        var registration = new FakeRegistration(current: true, registeredAndApproved: true);

        Assert.True(StartupRegistrationSynchronizer.Synchronize(registration));
        Assert.Empty(registration.Writes);
    }

    private sealed class FakeRegistration(bool current, bool registeredAndApproved) : IStartupRegistration
    {
        private bool currentState = current;
        public List<bool> Writes { get; } = [];
        public bool IsEnabled() => currentState;
        public bool HasEnabledRegistration() => registeredAndApproved;
        public void SetEnabled(bool enabled) { Writes.Add(enabled); currentState = enabled; }
    }
}
