using RevoHelper.App.Charging;

namespace RevoHelper.App.Tests.Charging;

/// <summary>
/// The status file is replaced several times a minute while the window reads it every second.
/// Windows refuses to replace a file that anyone has open, even read-only for a moment; that
/// failure used to be counted as an EC failure, left a temporary file behind each time, and five
/// in a row would have stopped the charge limit.
/// </summary>
public sealed class HoldChargeHostTests : IDisposable
{
    private readonly string folder = Path.Combine(Path.GetTempPath(), "revohelper-hold-" + Guid.NewGuid().ToString("N"));
    private string Target => Path.Combine(folder, "hold-status.json");

    public HoldChargeHostTests()
    {
        Directory.CreateDirectory(folder);
        File.WriteAllText(Target, "old");
    }

    [Fact]
    public async Task AReaderHoldingTheStatusForAMomentDoesNotFailTheWrite()
    {
        var reader = new FileStream(Target, FileMode.Open, FileAccess.Read, FileShare.Read);
        var release = Task.Delay(50).ContinueWith(_ => reader.Dispose());

        HoldChargeHost.ReplaceFile(Target, "new");
        await release;

        Assert.Equal("new", File.ReadAllText(Target));
        Assert.Empty(Directory.GetFiles(folder, "*.tmp"));
    }

    [Fact]
    public void AWriteThatKeepsFailingLeavesNoTemporaryFileBehind()
    {
        using (new FileStream(Target, FileMode.Open, FileAccess.Read, FileShare.Read))
            Assert.ThrowsAny<Exception>(() => HoldChargeHost.ReplaceFile(Target, "new"));

        Assert.Equal("old", File.ReadAllText(Target));
        Assert.Empty(Directory.GetFiles(folder, "*.tmp"));
    }

    [Fact]
    public void TemporaryFilesLeftByEarlierVersionsAreRemoved()
    {
        File.WriteAllText(Target + ".0123456789abcdef0123456789abcdef.tmp", "left over");
        File.WriteAllText(Path.Combine(folder, "unrelated.tmp"), "keep");

        HoldChargeHost.RemoveStaleTemporaryFiles(folder);

        Assert.Equal(["unrelated.tmp"], Directory.GetFiles(folder, "*.tmp").Select(Path.GetFileName));
        Assert.Equal("old", File.ReadAllText(Target));
    }

    public void Dispose()
    {
        try { Directory.Delete(folder, recursive: true); } catch (IOException) { }
    }
}
