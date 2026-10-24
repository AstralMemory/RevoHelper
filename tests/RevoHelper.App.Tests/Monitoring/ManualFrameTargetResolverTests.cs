using RevoHelper.App.Monitoring;

namespace RevoHelper.App.Tests.Monitoring;

public sealed class ManualFrameTargetResolverTests
{
    private static readonly DateTimeOffset Started = new(2026, 10, 24, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Resolver_OnlyProbesExplicitExeName_AndChecksFullPath()
    {
        var probe = new FakeProbe([
            new(100, Started, @"D:\Other\javaw.exe"),
            new(101, Started.AddMinutes(1), @"C:\Games\Minecraft\javaw.exe")]);

        var target = new WindowsManualFrameTargetResolver(probe).FindRunning(@"C:\Games\Minecraft\javaw.exe");

        Assert.Equal(new[] { "javaw" }, probe.RequestedNames);
        Assert.Equal(101, target!.ProcessId);
        Assert.Equal(Started.AddMinutes(1), target.StartedAtUtc);
        Assert.Equal("javaw", target.ProcessName);
        Assert.Equal("javaw.exe", target.DisplayName);
    }

    [Fact]
    public void MatchingPath_IsCaseInsensitive_AndCanonicalized()
    {
        var probe = new FakeProbe([new(101, Started, @"C:\Games\GAME.EXE")]);

        var target = new WindowsManualFrameTargetResolver(probe).FindRunning(@"c:\games\sub\..\game.exe");

        Assert.NotNull(target);
        Assert.Equal(101, target.ProcessId);
    }

    [Fact]
    public void MissingSelectedExe_ReturnsNull_InsteadOfAnotherRunningApplication()
    {
        var probe = new FakeProbe([new(100, Started, @"D:\Other\game.exe")]);

        Assert.Null(new WindowsManualFrameTargetResolver(probe).FindRunning(@"C:\Saved\game.exe"));
    }

    [Fact]
    public void SameProgramMultipleInstances_UsesNewestStartedIdentity()
    {
        var probe = new FakeProbe([
            new(100, Started, @"C:\Games\game.exe"),
            new(101, Started.AddMinutes(1), @"C:\Games\game.exe")]);

        var target = new WindowsManualFrameTargetResolver(probe).FindRunning(@"C:\Games\game.exe");

        Assert.Equal(101, target!.ProcessId);
        Assert.Equal(Started.AddMinutes(1), target.StartedAtUtc);
    }

    [Theory]
    [InlineData("game.exe")]
    [InlineData(@"C:\Games\game.bat")]
    [InlineData(@"C:\Games\*.exe")]
    public void InvalidSavedPath_DoesNotProbeOtherProcesses(string path)
    {
        var probe = new FakeProbe([]);

        Assert.Null(new WindowsManualFrameTargetResolver(probe).FindRunning(path));
        Assert.Empty(probe.RequestedNames);
    }

    private sealed class FakeProbe(IReadOnlyList<ManualExecutableProcess> candidates) : IManualExecutableProcessProbe
    {
        public List<string> RequestedNames { get; } = [];
        public IReadOnlyList<ManualExecutableProcess> FindByExecutableName(string processName)
        {
            RequestedNames.Add(processName);
            return candidates;
        }
    }
}
