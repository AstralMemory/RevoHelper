using RevoHelper.App.Lifecycle;
using RevoHelper.App.Presentation;

namespace RevoHelper.App.Tests.Lifecycle;

/// <summary>
/// Windows lets a running program file be moved on the same drive. The place it started from
/// stays in Environment.ProcessPath; the process image name follows the file.
/// </summary>
public sealed class RunningExecutableTests
{
    private const string Started = @"C:\Users\u\Desktop\RevoHelper V1.0.8\RevoHelper\RevoHelper.exe";
    private const string Moved = @"C:\Users\u\Desktop\RevoHelper.exe";

    [Fact]
    public void TheFileIsFoundWhereItIsNow()
    {
        Assert.Equal(Moved, RunningExecutable.Locate(Moved, path => path == Moved));
        Assert.Equal(Moved, RunningExecutable.TryLocate(Moved, path => path == Moved));
        Assert.True(RunningExecutable.SamePath(Started.ToUpperInvariant(), Started));
        Assert.False(RunningExecutable.SamePath(Started, Moved));
    }

    // Nothing falls back to the old place (another file may be there by now), and nothing is
    // started or registered from the Recycle Bin.
    [Theory]
    [InlineData(null, true, RunningExecutable.NotFoundMessage)]
    [InlineData("RevoHelper.exe", true, RunningExecutable.NotFoundMessage)]
    [InlineData(Moved, false, RunningExecutable.NotFoundMessage)]
    [InlineData(@"C:\$Recycle.Bin\S-1-5-21-1-2-3-1001\$R4QF2KD.exe", true, RunningExecutable.RecycledMessage)]
    public void AFileThatCannotBeConfirmedIsRefusedWithAClearMessage(string? image, bool exists, string expected)
    {
        var error = Assert.Throws<InvalidOperationException>(() => RunningExecutable.Locate(image, _ => exists));
        Assert.Equal(expected, error.Message);
        Assert.Null(RunningExecutable.TryLocate(image, _ => exists));
        Assert.DoesNotContain(UiTextCatalog.Translate(error.Message, "en-US"), c => c is >= '一' and <= '鿿');
    }

    [Fact]
    public void AHelperCopyStartsFromTheFileAsItIsNow_InItsOwnFolder()
    {
        var info = RunningExecutable.StartInfo(Moved, ["--hold-charge-worker", "80"]);
        Assert.Equal(Moved, info.FileName);
        Assert.Equal(@"C:\Users\u\Desktop", info.WorkingDirectory);
        Assert.Equal(["--hold-charge-worker", "80"], info.ArgumentList);
        Assert.False(info.UseShellExecute);
        Assert.True(info.CreateNoWindow);
    }

    [Fact]
    public void TheRunningProcessIsFound()
    {
        Assert.True(File.Exists(RunningExecutable.Locate()));
        Assert.Equal(RunningExecutable.Locate(), RunningExecutable.TryLocate());
    }
}
