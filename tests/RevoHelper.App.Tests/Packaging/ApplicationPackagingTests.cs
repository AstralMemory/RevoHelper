using System.Xml.Linq;

namespace RevoHelper.App.Tests.Packaging;

public sealed class ApplicationPackagingTests
{
    [Fact]
    public void Project_UsesCompactFrameworkDependentSingleFilePackaging()
    {
        var projectPath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..",
            "src", "RevoHelper.App", "RevoHelper.App.csproj"));
        var document = XDocument.Load(projectPath);
        var properties = document.Descendants("PropertyGroup")
            .SelectMany(group => group.Elements())
            .ToDictionary(element => element.Name.LocalName, element => element.Value);

        Assert.Equal("true", properties["PublishSingleFile"]);
        Assert.Equal("false", properties["SelfContained"]);
        Assert.Equal("true", properties["IncludeNativeLibrariesForSelfExtract"]);
        Assert.Equal("false", properties["EnableCompressionInSingleFile"]);
    }
}
