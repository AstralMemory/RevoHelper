using RevoHelper.App.Settings;

namespace RevoHelper.App.Tests.Settings;

public sealed class BootWaterTaskManagerTests
{
    [Theory]
    [InlineData("<DisallowStartIfOnBatteries>false", "<DisallowStartIfOnBatteries>true")]
    [InlineData("<StopIfGoingOnBatteries>false", "<StopIfGoingOnBatteries>true")]
    [InlineData("<StartWhenAvailable>true", "<StartWhenAvailable>false")]
    [InlineData("IgnoreNew", "Parallel")]
    [InlineData("</UserId>", "</UserId><LogonType>InteractiveToken</LogonType>")]
    public async Task AlteredBootPolicyRequiresRepair(string before, string after)
    {
        var platform = new FakePlatform { ValidHelper = true, Xml = BootWaterTaskDefinition.Create(Executable).Replace(before, after, StringComparison.Ordinal) };
        var result = await new BootWaterTaskManager(platform, Executable).ReadAsync(default);
        Assert.Equal(BootWaterTaskState.RepairRequired, result.State);
    }

    private const string Executable = @"C:\ProgramData\RevoHelper\BootWater\RevoHelper.BootWater.exe";

    [Fact]
    public async Task ReadMissing_DoesNotInstallAnything()
    {
        var platform = new FakePlatform();
        var manager = new BootWaterTaskManager(platform, Executable);
        var result = await manager.ReadAsync(default);
        Assert.Equal(BootWaterTaskState.NotInstalled, result.State);
        Assert.Equal(0, platform.Writes);
    }

    [Fact]
    public async Task Install_DeploysThenRegistersAndVerifies()
    {
        var platform = new FakePlatform();
        var manager = new BootWaterTaskManager(platform, Executable);
        var result = await manager.InstallAsync(default);
        Assert.Equal(BootWaterTaskState.Installed, result.State);
        Assert.Equal(new[] { "deploy", "register" }, platform.Operations);
        Assert.Contains("<BootTrigger>", platform.Xml);
        Assert.Contains("S-1-5-18", platform.Xml);
        Assert.Contains("PT45S", platform.Xml);
        Assert.Contains("--boot", platform.Xml);
    }

    [Fact]
    public async Task ForeignTask_IsNeverOverwrittenOrRemoved()
    {
        var platform = new FakePlatform { Xml = BootWaterTaskDefinition.Create(@"C:\Other\program.exe") };
        var manager = new BootWaterTaskManager(platform, Executable);
        Assert.Equal(BootWaterTaskState.ForeignTask, (await manager.InstallAsync(default)).State);
        Assert.Equal(BootWaterTaskState.ForeignTask, (await manager.RemoveAsync(default)).State);
        Assert.Equal(0, platform.Writes);
    }

    [Theory]
    [InlineData("<Arguments>--boot</Arguments>", "<Arguments>--evil</Arguments>")]
    [InlineData("S-1-5-18", "AnotherUser")]
    [InlineData("<Actions Context=\"Author\">", "<Actions Context=\"Author\"><Exec><Command>evil.exe</Command></Exec>")]
    public void Ownership_RejectsUnexpectedPrincipalArgumentsOrAdditionalActions(string before, string after)
    {
        var xml = BootWaterTaskDefinition.Create(Executable).Replace(before, after, StringComparison.Ordinal);
        Assert.False(BootWaterTaskDefinition.IsOwned(xml, Executable));
    }

    [Fact]
    public async Task DeploymentFailure_DoesNotRegisterTask()
    {
        var platform = new FakePlatform { FailDeployment = true };
        var result = await new BootWaterTaskManager(platform, Executable).InstallAsync(default);
        Assert.Equal(BootWaterTaskState.Error, result.State);
        Assert.Null(platform.Xml);
        Assert.DoesNotContain("register", platform.Operations);
    }

    [Fact]
    public async Task RegistrationFailure_RestoresPreviousOwnedDefinition()
    {
        var previous = BootWaterTaskDefinition.Create(Executable).Replace("<Enabled>true</Enabled>", "<Enabled>false</Enabled>");
        var platform = new FakePlatform { Xml = previous, FailNextRegistration = true };
        var result = await new BootWaterTaskManager(platform, Executable).InstallAsync(default);
        Assert.Equal(BootWaterTaskState.Error, result.State);
        Assert.Equal(previous, platform.Xml);
    }

    [Fact]
    public async Task RegistrationReadbackMismatch_IsNotSuccess()
    {
        var platform = new FakePlatform { IgnoreRegistration = true };
        Assert.Equal(BootWaterTaskState.Error, (await new BootWaterTaskManager(platform, Executable).InstallAsync(default)).State);
    }

    [Fact]
    public async Task DisabledOrMissingHelper_RequiresRepair()
    {
        var platform = new FakePlatform { Xml = BootWaterTaskDefinition.Create(Executable), ValidHelper = false };
        var manager = new BootWaterTaskManager(platform, Executable);
        Assert.Equal(BootWaterTaskState.RepairRequired, (await manager.ReadAsync(default)).State);
        platform.ValidHelper = true;
        platform.Xml = platform.Xml.Replace("<Enabled>true</Enabled>", "<Enabled>false</Enabled>");
        Assert.Equal(BootWaterTaskState.RepairRequired, (await manager.ReadAsync(default)).State);
    }

    [Fact]
    public async Task Remove_OnlyDeletesOwnTaskAndKeepsHelperAndHistory()
    {
        var platform = new FakePlatform { Xml = BootWaterTaskDefinition.Create(Executable), ValidHelper = true };
        var result = await new BootWaterTaskManager(platform, Executable).RemoveAsync(default);
        Assert.Equal(BootWaterTaskState.NotInstalled, result.State);
        Assert.True(platform.ValidHelper);
        Assert.Equal(new[] { "remove" }, platform.Operations);
    }

    private sealed class FakePlatform : IBootWaterTaskPlatform
    {
        public string? Xml { get; set; }
        public bool ValidHelper { get; set; }
        public bool FailDeployment { get; init; }
        public bool FailNextRegistration { get; set; }
        public bool IgnoreRegistration { get; init; }
        public List<string> Operations { get; } = [];
        public int Writes => Operations.Count;
        public string? ReadTaskXml() => Xml;
        public bool IsInstalledHelperValid() => ValidHelper;
        public string? ReadLastConnectionResult() => "无开机连接记录";
        public void DeployVerifiedHelper()
        {
            Operations.Add("deploy");
            if (FailDeployment) throw new IOException("helper checksum mismatch");
            ValidHelper = true;
        }
        public void RegisterTask(string xml)
        {
            Operations.Add("register");
            if (FailNextRegistration) { FailNextRegistration = false; throw new IOException("registration failed"); }
            if (!IgnoreRegistration) Xml = xml;
        }
        public void RemoveTask() { Operations.Add("remove"); Xml = null; }
    }
}
