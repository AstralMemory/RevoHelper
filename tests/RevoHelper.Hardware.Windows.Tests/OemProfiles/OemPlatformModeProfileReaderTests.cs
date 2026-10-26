using RevoHelper.Core.Control;
using RevoHelper.Hardware.Windows.OemProfiles;

namespace RevoHelper.Hardware.Windows.Tests.OemProfiles;

public sealed class OemPlatformModeProfileReaderTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), $"RevoHelper-mode-evidence-{Guid.NewGuid():N}");

    [Fact]
    public void ReadEvidence_ModeProfilesWithoutPowerData_ConfirmOnlyPresentBaseModes()
    {
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "Mode1_Profile1.json"), """{"Name":"Balanced","FAN":{}}""");
        File.WriteAllText(Path.Combine(directory, "Mode3_Profile1.json"), """{"Name":"Turbo","Activated":true}""");

        var evidence = new OemPlatformModeProfileReader(directory).ReadEvidence();

        Assert.Equal(2, evidence.SupportedPlatformModes.Count);
        Assert.Contains(PlatformMode.Balanced, evidence.SupportedPlatformModes);
        Assert.Contains(PlatformMode.TurboQuiet, evidence.SupportedPlatformModes);
        Assert.DoesNotContain(PlatformMode.TurboHigh, evidence.SupportedPlatformModes);
        Assert.DoesNotContain(PlatformMode.Office, evidence.SupportedPlatformModes);
        Assert.False(evidence.HasCpuPowerLimits);
        Assert.Empty(new OemCpuPowerLimitProfileReader(directory).Read());
    }

    [Fact]
    public void ReadEvidence_InvalidFilesAreIsolatedAndUnknownFileNamesDoNotEnableModes()
    {
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "Mode1_Profile1.json"), """{"Name":"Balanced"}""");
        File.WriteAllText(Path.Combine(directory, "Mode2_Profile1.json"), "not-json");
        File.WriteAllText(Path.Combine(directory, "Mode3_Profile1.json"), "[]");
        File.WriteAllText(Path.Combine(directory, "Mode4_Profile1.json"), "null");
        File.WriteAllText(Path.Combine(directory, "Mode99_Profile1.json"), """{"Name":"Unknown"}""");
        File.WriteAllText(Path.Combine(directory, "Mode2_Profile0.json"), """{"Name":"Invalid profile number"}""");

        var evidence = new OemPlatformModeProfileReader(directory).ReadEvidence();

        Assert.Equal(PlatformMode.Balanced, Assert.Single(evidence.SupportedPlatformModes));
        Assert.False(evidence.HasCpuPowerLimits);
    }

    [Fact]
    public void ReadEvidence_MissingDirectory_ReturnsNoEvidence() =>
        Assert.Empty(new OemPlatformModeProfileReader(directory).ReadEvidence().SupportedPlatformModes);

    public void Dispose()
    {
        if (Directory.Exists(directory)) Directory.Delete(directory, true);
    }
}
