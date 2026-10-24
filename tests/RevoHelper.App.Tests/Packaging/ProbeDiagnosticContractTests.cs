namespace RevoHelper.App.Tests.Packaging;

public sealed class ProbeDiagnosticContractTests
{
    [Fact]
    public void Monitor_ReportsModelSpecificPerformanceCounterOnlyAsRawDelta()
    {
        var sourcePath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..",
            "tools", "RevoHelper.Probe", "Program.cs"));
        var source = File.ReadAllText(sourcePath);

        Assert.DoesNotContain("PackagePowerLimitThrottledSeconds", source, StringComparison.Ordinal);
        Assert.DoesNotContain("PackagePowerLimitThrottledPercent", source, StringComparison.Ordinal);
        Assert.Contains("PackagePerformanceCounter", source, StringComparison.Ordinal);
        Assert.Contains("Delta = perfDelta", source, StringComparison.Ordinal);
    }
}
