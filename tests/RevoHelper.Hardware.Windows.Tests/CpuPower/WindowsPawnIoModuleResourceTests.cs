using System.Text;
using RevoHelper.Hardware.Windows.CpuPower;

namespace RevoHelper.Hardware.Windows.Tests.CpuPower;

public sealed class WindowsPawnIoModuleResourceTests
{
    [Fact]
    public void EmbeddedIntelMchbarModule_ExposesReadOnlyMchbarEntryPoints()
    {
        const string resourceName = "RevoHelper.Resources.PawnIo.IntelMCHBAR-0.2.10.bin";
        using var stream = typeof(WindowsPawnIoModule).Assembly.GetManifestResourceStream(resourceName);

        Assert.NotNull(stream);
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        var moduleText = Encoding.ASCII.GetString(memory.ToArray());

        Assert.Contains("ioctl_get_mchbar_addr", moduleText, StringComparison.Ordinal);
        Assert.Contains("ioctl_read_qword", moduleText, StringComparison.Ordinal);
        Assert.DoesNotContain("ioctl_write", moduleText, StringComparison.Ordinal);
    }

    [Fact]
    public void EmbeddedIntelMsrModule_ContainsReadAndWriteEntryPoints()
    {
        const string resourceName = "RevoHelper.Resources.PawnIo.IntelMSR-0.2.10.bin";
        using var stream = typeof(WindowsPawnIoModule).Assembly.GetManifestResourceStream(resourceName);

        Assert.NotNull(stream);
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        var moduleText = Encoding.ASCII.GetString(memory.ToArray());

        Assert.Contains("ioctl_read_msr", moduleText, StringComparison.Ordinal);
        Assert.Contains("ioctl_write_msr", moduleText, StringComparison.Ordinal);
    }
}
