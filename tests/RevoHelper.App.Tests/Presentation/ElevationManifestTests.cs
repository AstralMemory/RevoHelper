using System.Xml.Linq;

namespace RevoHelper.App.Tests.Presentation;

public sealed class ElevationManifestTests
{
    [Fact]
    public void AppManifest_RequiresAdministratorForPawnIoCpuSensors()
    {
        var projectDirectory = FindProjectDirectory();
        var manifest = XDocument.Load(Path.Combine(projectDirectory, "app.manifest"));
        var executionLevel = manifest.Descendants()
            .Single(element => element.Name.LocalName == "requestedExecutionLevel");

        Assert.Equal("requireAdministrator", executionLevel.Attribute("level")?.Value);
    }

    private static string FindProjectDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "src", "RevoHelper.App");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate src/RevoHelper.App.");
    }
}
