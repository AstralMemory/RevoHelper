using System.ComponentModel;
using Microsoft.Win32.SafeHandles;
using RevoHelper.App.Lifecycle;

namespace RevoHelper.App.Tests.Lifecycle;

public sealed class WindowsOfficialFrontendProcessesTests
{
    private const string Package = "CCU.WinUI_5.56.60.17_x64__wrbgcf7aesyd8";
    private const string Root = @"D:\WindowsApps\" + Package;

    [Theory]
    [InlineData("CCUWinUI", Root + @"\CCUWinUI.exe", true)]
    [InlineData("SystrayComponent", Root + @"\Win32\SystrayComponent.exe", true)]
    [InlineData("SystrayComponent", @"D:\Tools\SystrayComponent.exe", false)]
    [InlineData("SystrayComponent", @"C:\Backup\WindowsApps\" + Package + @"\Win32\SystrayComponent.exe", false)]
    [InlineData("SystrayComponent", @"D:\WindowsApps\Other.App_1.0.0.0_x64__test\Win32\SystrayComponent.exe", false)]
    [InlineData("SystrayComponent", @"D:\WindowsApps\CCU.WinUI_6.0.0.0_x64__wrbgcf7aesyd8\Win32\SystrayComponent.exe", false)]
    [InlineData("SystrayComponent", Root + @"\SystrayComponent.exe", false)]
    [InlineData("CCUWinUI", Root + @"\Win32\CCUWinUI.exe", false)]
    [InlineData("CCUWinUI", Root + @"\GCUService.exe", false)]
    [InlineData("OSDTpDetect", Root + @"\Win32\OSDTpDetect.exe", false)]
    public void OnlyTheTwoImagesAtTheRegisteredPackageLocationCanBeClosed(string name, string image, bool official)
    {
        var api = new FakeWindows { Name = name, Image = image };
        var backend = new WindowsOfficialFrontendProcesses(api);

        var snapshot = backend.Find();
        Assert.Equal(official, snapshot.Targets.Count == 1);
        Assert.False(snapshot.HasUnknown);
        Assert.Equal(official ? OfficialFrontendCloseOutcome.Closed : OfficialFrontendCloseOutcome.Gone,
            backend.Close(new(5, name), () => true));
        Assert.Equal(official ? 1 : 0, api.Terminations);
    }

    [Fact]
    public void OtherSessionsAreExcludedAtDiscoveryAndImmediatelyBeforeClosing()
    {
        var api = new FakeWindows();
        var backend = new WindowsOfficialFrontendProcesses(api);
        var target = Assert.Single(backend.Find().Targets);

        api.TargetSession = 3;
        var snapshot = backend.Find();
        Assert.Empty(snapshot.Targets);
        Assert.False(snapshot.LegacyRunning);
        Assert.Equal(OfficialFrontendCloseOutcome.Gone, backend.Close(target, () => true));
        Assert.Equal(0, api.Terminations);
    }

    [Theory]
    [InlineData("ControlCenterU")]
    [InlineData("GamingCenterU")]
    public void LegacyConsolesInThisSessionAreReportedAndNeverClosed(string name)
    {
        var api = new FakeWindows { Name = name };
        var backend = new WindowsOfficialFrontendProcesses(api);
        Assert.True(backend.Find().LegacyRunning);
        Assert.Empty(backend.Find().Targets);
        Assert.Equal(OfficialFrontendCloseOutcome.Gone, backend.Close(new(5, name), () => true));
        Assert.Equal(0, api.Terminations);
        api.TargetSession = 3;
        Assert.False(backend.Find().LegacyRunning);
    }

    [Fact]
    public void InaccessibleImageIsUnknownAndCannotBeReportedAsClosed()
    {
        var api = new FakeWindows { DenyImage = true };
        var backend = new WindowsOfficialFrontendProcesses(api);
        Assert.True(backend.Find().HasUnknown);
        Assert.Equal(OfficialFrontendCloseOutcome.Failed, backend.Close(new(5, api.Name), () => true));
        Assert.Equal(0, api.Terminations);
    }

    [Fact]
    public void EndingTheWatchWhileResolvingThePackageSkipsTermination()
    {
        var mayClose = true;
        var api = new FakeWindows { OnPackageQuery = () => mayClose = false };
        var backend = new WindowsOfficialFrontendProcesses(api);
        Assert.Equal(OfficialFrontendCloseOutcome.Skipped, backend.Close(new(5, api.Name), () => mayClose));
        Assert.Equal(0, api.Terminations);
    }

    [Fact]
    public void ReusedPidWithAnotherImageIsNotClosed()
    {
        var api = new FakeWindows();
        var backend = new WindowsOfficialFrontendProcesses(api);
        var target = Assert.Single(backend.Find().Targets);
        api.Image = @"C:\Tools\SystrayComponent.exe";
        Assert.Equal(OfficialFrontendCloseOutcome.Gone, backend.Close(target, () => true));
        Assert.Equal(0, api.Terminations);
    }

    [Fact]
    public void RegisteredPackageWithTheSameNameButAnotherPublisherIsExcluded()
    {
        var otherRoot = @"D:\WindowsApps\CCU.WinUI_5.56.60.17_x64__123456789abcd";
        var api = new FakeWindows { RegisteredRoot = otherRoot, Image = otherRoot + @"\Win32\SystrayComponent.exe" };
        var backend = new WindowsOfficialFrontendProcesses(api);
        Assert.Empty(backend.Find().Targets);
        Assert.Equal(OfficialFrontendCloseOutcome.Gone, backend.Close(new(5, api.Name), () => true));
        Assert.Equal(0, api.Terminations);
    }

    private sealed class FakeWindows : IOfficialFrontendWindowsApi
    {
        public string Name { get; init; } = "SystrayComponent";
        public string Image { get; set; } = Root + @"\Win32\SystrayComponent.exe";
        public string RegisteredRoot { get; init; } = Root;
        public int TargetSession { get; set; } = 2;
        public int Terminations { get; private set; }
        public bool DenyImage { get; init; }
        public Action? OnPackageQuery { get; init; }
        private SafeProcessHandle? verifiedHandle;

        public IEnumerable<(int Id, string Name, int SessionId)> Snapshot() => [(5, Name, TargetSession)];
        public int SessionOf(int id) => id == Environment.ProcessId ? 2 : TargetSession;
        public SafeProcessHandle Open(int id, bool forExit) => new(new IntPtr(5), ownsHandle: false);
        public string ImagePath(SafeProcessHandle handle)
        {
            Assert.False(handle.IsClosed);
            if (DenyImage) throw new Win32Exception(5);
            verifiedHandle = handle;
            return Image;
        }
        public string? InstalledPackagePath(string fullName)
        {
            OnPackageQuery?.Invoke();
            return string.Equals(fullName, Path.GetFileName(RegisteredRoot), StringComparison.OrdinalIgnoreCase)
                ? RegisteredRoot : null;
        }
        public OfficialFrontendCloseOutcome TerminateAndWait(SafeProcessHandle handle)
        {
            Assert.False(handle.IsClosed);
            Assert.Same(verifiedHandle, handle);
            Terminations++;
            return OfficialFrontendCloseOutcome.Closed;
        }
    }
}
