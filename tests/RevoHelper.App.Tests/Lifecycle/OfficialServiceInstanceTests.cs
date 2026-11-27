using RevoHelper.App.Lifecycle;

namespace RevoHelper.App.Tests.Lifecycle;

// The channel to the official service connects again after any drop. Only a new process of the
// service, or one that has appeared since, opens the official tray again.
public sealed class OfficialServiceInstanceTests
{
    private static readonly DateTime Started = new(2026, 11, 26, 8, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void TheFirstLookOnlyRecords_AndTheSameProcessIsNoChange()
    {
        var service = new OfficialServiceInstance(() => new(4120, Started));

        Assert.False(service.LookAgain());
        Assert.False(service.Missing);
        Assert.False(service.LookAgain());
    }

    [Theory]
    [InlineData(5000, 0)]      // another process
    [InlineData(4120, 31)]     // the same ID used again by a process started later
    public void AnotherProcess_IsAChange(int id, int secondsLater)
    {
        OfficialServiceProcess? now = new(4120, Started);
        var service = new OfficialServiceInstance(() => now);
        service.LookAgain();

        now = new(id, Started.AddSeconds(secondsLater));

        Assert.True(service.LookAgain());
        Assert.False(service.LookAgain());
    }

    // RevoHelper started by hand before the official service was up.
    [Fact]
    public void AServiceThatAppearsSinceTheLastLook_IsAChange()
    {
        OfficialServiceProcess? now = null;
        var service = new OfficialServiceInstance(() => now);
        Assert.False(service.LookAgain());
        Assert.True(service.Missing);
        Assert.False(service.LookAgain());

        now = new(4120, Started);

        Assert.True(service.LookAgain());
        Assert.False(service.Missing);
    }

    [Fact]
    public void AServiceThatHasGone_IsNoChangeUntilItIsBack()
    {
        OfficialServiceProcess? now = new(4120, Started);
        var service = new OfficialServiceInstance(() => now);
        service.LookAgain();

        now = null;
        Assert.False(service.LookAgain());
        Assert.True(service.Missing);

        now = new(6000, Started.AddMinutes(1));
        Assert.True(service.LookAgain());
    }
}
