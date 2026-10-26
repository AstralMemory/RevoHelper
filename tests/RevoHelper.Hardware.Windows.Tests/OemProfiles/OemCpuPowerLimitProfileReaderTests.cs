using RevoHelper.Hardware.Windows.OemProfiles;

namespace RevoHelper.Hardware.Windows.Tests.OemProfiles;

public sealed class OemCpuPowerLimitProfileReaderTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), $"RevoHelper-{Guid.NewGuid():N}");

    [Fact]
    public void Read_MapsOemModesDeterministically()
    {
        Directory.CreateDirectory(directory);
        WriteProfile("Mode1_Profile1.json", 75, 85);
        WriteProfile("Mode2_Profile1.json", 45, 45);
        WriteProfile("Mode3_Profile1.json", 210, 210);
        WriteProfile("Mode4_Profile2.json", 70, 80);

        var profiles = new OemCpuPowerLimitProfileReader(directory).Read();

        Assert.Contains(profiles, profile => profile.Key == "balanced:1" && profile.Pl1Watts == 75);
        Assert.Contains(profiles, profile => profile.Key == "office:1" && profile.Pl1Watts == 45);
        Assert.Contains(profiles, profile => profile.Key == "turbo:1" && profile.Pl1Watts == 210);
        Assert.Contains(profiles, profile => profile.Key == "custom:2" && profile.Pl1Watts == 70);
    }

    [Fact]
    public void Read_InvalidValueTypeInOneFile_DoesNotDiscardOtherProfiles()
    {
        Directory.CreateDirectory(directory);
        WriteProfile("Mode1_Profile1.json", 45, 65);
        File.WriteAllText(Path.Combine(directory, "Mode2_Profile1.json"), """{"CPU":{"PL1":{},"PL2":35}}""");
        WriteProfile("Mode3_Profile1.json", 80, 100);

        var profiles = new OemCpuPowerLimitProfileReader(directory).Read();

        Assert.Equal(2, profiles.Count);
        Assert.Contains(profiles, profile => profile.Key == "balanced:1");
        Assert.Contains(profiles, profile => profile.Key == "turbo:1");
    }

    [Fact]
    public void Read_MissingDirectory_ReturnsEmptyList()
    {
        Assert.Empty(new OemCpuPowerLimitProfileReader(directory).Read());
    }

    private void WriteProfile(string fileName, int pl1, int pl2) =>
        File.WriteAllText(Path.Combine(directory, fileName), $$"""
            { "Name": "{{Path.GetFileNameWithoutExtension(fileName)}}", "CPU": { "PL1": {{pl1}}, "PL1_Enabled": false, "PL2": {{pl2}}, "PL2_Enabled": false } }
            """);

    public void Dispose()
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, true);
        }
    }
}
