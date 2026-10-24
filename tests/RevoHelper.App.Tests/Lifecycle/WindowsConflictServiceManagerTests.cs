using RevoHelper.App.Lifecycle;

namespace RevoHelper.App.Tests.Lifecycle;

public sealed class WindowsConflictServiceManagerTests
{
    [Fact]
    public async Task StopAsync_UsesDependencySafeOrderAndRejectsUnknownServices()
    {
        var commands = new List<string>();
        var manager = new WindowsConflictServiceManager((command, serviceName, _) =>
        {
            commands.Add($"{command}:{serviceName}");
            return Task.FromResult(0);
        });

        await manager.StopAsync(["ipfsvc", "XtuService", "dptftcs"], CancellationToken.None);

        Assert.Equal(["stop:XtuService", "stop:dptftcs", "stop:ipfsvc"], commands);
        await Assert.ThrowsAsync<ArgumentException>(() =>
            manager.StopAsync(["UnknownService"], CancellationToken.None));
    }

    [Fact]
    public async Task RestoreAsync_UsesReverseDependencyOrder()
    {
        var commands = new List<string>();
        var manager = new WindowsConflictServiceManager((command, serviceName, _) =>
        {
            commands.Add($"{command}:{serviceName}");
            return Task.FromResult(0);
        });

        await manager.RestoreAsync(["XtuService", "dptftcs", "ipfsvc"], CancellationToken.None);

        Assert.Equal(["start:ipfsvc", "start:dptftcs", "start:XtuService"], commands);
    }

    [Fact]
    public async Task StopAsync_WhenServiceCommandFails_ReportsTheSpecificService()
    {
        var manager = new WindowsConflictServiceManager((_, _, _) => Task.FromResult(5));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            manager.StopAsync(["dptftcs"], CancellationToken.None));

        Assert.Contains("dptftcs", exception.Message, StringComparison.Ordinal);
        Assert.Contains("5", exception.Message, StringComparison.Ordinal);
    }
}
