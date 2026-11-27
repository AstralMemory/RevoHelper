using System.Runtime.InteropServices;
using RevoHelper.App.Settings;

namespace RevoHelper.App.Tests.Settings;

// Start with Windows could not be switched on where it had never been on: Task Scheduler is called
// through late-bound COM, which reports a missing task as FileNotFoundException, and only
// COMException was taken as "no task".
public sealed class ScheduledTaskFolderTests
{
    private const int NotFound = unchecked((int)0x80070002);

    // Read-only: asks the real Task Scheduler for a task that cannot exist.
    private static readonly string MissingTask = $"RevoHelper test {Guid.NewGuid():N}";

    // On .NET 10 the runtime binder raises FileNotFoundException here, which a COMException-only
    // catch missed. Either form is accepted, so the contract is the error code, not the type.
    [Fact]
    public void ThroughLateBoundCom_AMissingTaskIsRecognised()
    {
        var exception = Record.Exception(() =>
            ScheduledTaskFolder.With<object?>(folder => folder.GetTask(MissingTask)));

        Assert.NotNull(exception);
        Assert.Equal(NotFound, exception.HResult);
        Assert.True(ScheduledTaskFolder.IsMissingTask(exception));
    }

    [Fact]
    public void AMissingTask_ReadsAsNone()
    {
        Assert.Null(ScheduledTaskFolder.ReadTaskXml(MissingTask));
    }

    [Fact]
    public void BothFormsOfNotFound_AreAMissingTask()
    {
        Assert.True(ScheduledTaskFolder.IsMissingTask(Marshal.GetExceptionForHR(NotFound)!));
        Assert.True(ScheduledTaskFolder.IsMissingTask(new COMException("missing", NotFound)));
    }

    [Theory]
    [InlineData(unchecked((int)0x80070005))] // access denied
    [InlineData(unchecked((int)0x80070003))] // path not found
    public void OtherFailures_AreNotAMissingTask(int hresult)
    {
        Assert.False(ScheduledTaskFolder.IsMissingTask(new COMException("failed", hresult)));
        Assert.False(ScheduledTaskFolder.IsMissingTask(Marshal.GetExceptionForHR(hresult)!));
    }
}
