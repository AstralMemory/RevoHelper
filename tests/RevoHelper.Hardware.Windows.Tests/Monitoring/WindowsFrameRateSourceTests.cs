using RevoHelper.Core.Monitoring;
using RevoHelper.Hardware.Windows.Monitoring;

namespace RevoHelper.Hardware.Windows.Tests.Monitoring;

public sealed class WindowsFrameRateSourceTests
{
    [Fact]
    public void StartInfo_CapturesReadOnlyWithPrivateSession_WithoutInstallingOrInjecting()
    {
        var info = WindowsFrameRateSource.CreateStartInfo("C:\\RevoHelper\\PresentMon.exe", "RevoHelper.FPS.test");

        Assert.False(info.UseShellExecute);
        Assert.True(info.CreateNoWindow);
        Assert.True(info.RedirectStandardOutput);
        Assert.True(info.RedirectStandardError);
        Assert.Contains("--output_stdout", info.ArgumentList);
        Assert.Contains("--v1_metrics", info.ArgumentList);
        Assert.Contains("--no_track_gpu", info.ArgumentList);
        Assert.Contains("--no_track_input", info.ArgumentList);
        Assert.Contains("RevoHelper.FPS.test", info.ArgumentList);
        Assert.DoesNotContain("--no_track_display", info.ArgumentList);
        Assert.DoesNotContain("--stop_existing_session", info.ArgumentList);
        Assert.DoesNotContain("--terminate_existing_session", info.ArgumentList);
        Assert.DoesNotContain("--restart_as_admin", info.ArgumentList);
        Assert.DoesNotContain("--output_file", info.ArgumentList);
    }

    [Fact]
    public async Task ConstructingOrStoppingSource_DoesNotStartCollector()
    {
        await using var source = new WindowsFrameRateSource(new FakeTargets());
        Assert.Equal(FrameRateStatus.Stopped, source.GetSnapshot().Status);
        Assert.Null(source.GetSnapshot().FramesPerSecond);

        await source.StopAsync();

        Assert.Equal(FrameRateStatus.Stopped, source.GetSnapshot().Status);
    }

    [Fact]
    public async Task EmbeddedCollector_IsPresentInHardwareAssembly()
    {
        await using var resource = typeof(PresentMonExecutable).Assembly.GetManifestResourceStream(PresentMonExecutable.ResourceName);
        Assert.NotNull(resource);
        Assert.True(resource.Length > 100000);
        var prefix = new byte[2];
        Assert.Equal(2, await resource.ReadAsync(prefix));
        Assert.Equal(new byte[] { (byte)'M', (byte)'Z' }, prefix);
    }

    private sealed class FakeTargets : IProcessTargetProvider
    {
        public ProcessTarget? GetForegroundTarget() => null;
        public bool IsAlive(ProcessTarget target) => false;
        public IReadOnlyList<ProcessTarget> GetWindowTargets() => [];
    }
}
