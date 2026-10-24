namespace RevoHelper.App.Tests.Packaging;

public sealed class DistributionReadmeTests
{
    [Fact]
    public void Readme_ContainsFirstUseWaterCoolingAndCompleteNoticeAnchors()
    {
        var path = Locate("docs", "release", "USER_README.txt");
        var text = File.ReadAllText(path);

        Assert.Contains(".NET 10 Desktop Runtime", text, StringComparison.Ordinal);
        Assert.Contains("以管理员身份运行 RevoHelper.exe", text, StringComparison.Ordinal);
        Assert.Contains("安装 / 修复", text, StringComparison.Ordinal);
        Assert.Contains("BootWater 水冷启动组件已内置", text, StringComparison.Ordinal);
        Assert.Contains("该可选功能会按需部署组件到系统目录", text, StringComparison.Ordinal);
        Assert.Contains("Third-Party.txt", text, StringComparison.Ordinal);
        Assert.Contains("Astral Memory", text, StringComparison.Ordinal);
        Assert.Contains("70 W", text, StringComparison.Ordinal);
        Assert.Contains("关闭 Dynamic Boost", text, StringComparison.Ordinal);
        Assert.Contains("仍需用户真机确认", text, StringComparison.Ordinal);
        Assert.Contains("不向游戏注入代码", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Beta v1.0.0", text, StringComparison.Ordinal);
    }

    private static string Locate(params string[] path)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine([directory.FullName, .. path]);
            if (File.Exists(candidate)) return candidate;
        }
        throw new FileNotFoundException(Path.Combine(path));
    }
}
