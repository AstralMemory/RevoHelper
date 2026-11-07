using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using RevoHelper.App.Updates;

namespace RevoHelper.App.Tests.Updates;

public sealed class GitHubLatestReleaseSourceTests
{
    [Fact]
    public async Task ReadLatest_SendsOnlyAPlainRequestToTheFixedAddress()
    {
        var handler = new FakeHandler(HttpStatusCode.OK, """{"tag_name":"v1.0.5"}""");
        var latest = await new GitHubLatestReleaseSource(handler).ReadLatestAsync(CancellationToken.None);

        Assert.Equal("V1.0.5", latest.ToString());
        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal("https://api.github.com/repos/AstralMemory/RevoHelper/releases/latest", request.RequestUri!.ToString());
        Assert.Null(request.Content);
        Assert.Equal("RevoHelper", request.Headers.UserAgent.ToString());
        Assert.Equal(["User-Agent", "Accept"], request.Headers.Select(header => header.Key).Order().Reverse());
    }

    [Fact]
    public async Task ReadLatest_UsesTheVersionTagAndIgnoresEverythingElseInTheResponse()
    {
        var handler = new FakeHandler(HttpStatusCode.OK, """
            {"tag_name":"v1.0.5","html_url":"https://example.com/","body":"run me","assets":[{"browser_download_url":"https://example.com/a.exe"}]}
            """);
        Assert.Equal("V1.0.5", (await new GitHubLatestReleaseSource(handler).ReadLatestAsync(CancellationToken.None)).ToString());
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("""{"tag_name":5}""")]
    [InlineData("""{"tag_name":null}""")]
    [InlineData("""{"tag_name":"nightly"}""")]
    public async Task ReadLatest_ThrowsWhenTheResponseHasNoUsableVersion(string body)
    {
        var source = new GitHubLatestReleaseSource(new FakeHandler(HttpStatusCode.OK, body));
        await Assert.ThrowsAnyAsync<Exception>(() => source.ReadLatestAsync(CancellationToken.None));
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task ReadLatest_ThrowsOnHttpFailure(HttpStatusCode status)
    {
        var source = new GitHubLatestReleaseSource(new FakeHandler(status, """{"tag_name":"v1.0.5"}"""));
        await Assert.ThrowsAsync<HttpRequestException>(() => source.ReadLatestAsync(CancellationToken.None));
    }

    [Fact]
    public async Task ReadLatest_RefusesAnOversizedResponse()
    {
        var padding = new string(' ', 1024 * 1024 + 1);
        var source = new GitHubLatestReleaseSource(new FakeHandler(HttpStatusCode.OK, $$"""{"tag_name":"v1.0.5"}{{padding}}"""));
        await Assert.ThrowsAsync<HttpRequestException>(() => source.ReadLatestAsync(CancellationToken.None));
    }

    [Fact]
    public void App_HasNoOtherInternetAccessAndNeverActsOnTheResponse()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "RevoHelper.sln"))) directory = directory.Parent;
        Assert.NotNull(directory);
        var sources = Directory.EnumerateFiles(Path.Combine(directory.FullName, "src"), "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") &&
                !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .ToArray();

        var network = sources.Where(path => File.ReadAllText(path).Contains("HttpClient", StringComparison.Ordinal));
        Assert.Equal("LatestReleaseSource.cs", Path.GetFileName(Assert.Single(network)));

        var update = string.Concat(sources.Where(path => path.Contains($"{Path.DirectorySeparatorChar}Updates{Path.DirectorySeparatorChar}") ||
            Path.GetFileName(path) == "MainWindowViewModel.Updates.cs").Select(File.ReadAllText));
        foreach (var forbidden in new[] { "Process", "File.", "html_url", "browser_download_url", "MessageBox" })
            Assert.DoesNotContain(forbidden, update, StringComparison.Ordinal);
    }

    private sealed class FakeHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
        }
    }
}
