using System.Net.Http;
using System.Text.Json;

namespace RevoHelper.App.Updates;

public interface ILatestReleaseSource
{
    /// <summary>Returns the latest public release, or throws when it cannot be read.</summary>
    Task<ReleaseVersion> ReadLatestAsync(CancellationToken cancellationToken);
}

/// <summary>
/// The app's only internet access. It reads one version number from a fixed address and nothing
/// else: no credentials, no device information, no downloads. RevoHelper runs as administrator,
/// so nothing in the response may be opened, saved or executed; the notice links to the fixed
/// <see cref="Presentation.AnnouncementCopy.DownloadsUrl"/> instead.
/// </summary>
public sealed class GitHubLatestReleaseSource(HttpMessageHandler? handler = null) : ILatestReleaseSource
{
    public const string Endpoint = "https://api.github.com/repos/AstralMemory/RevoHelper/releases/latest";
    private const int MaxResponseBytes = 1024 * 1024;
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(5);
    private HttpClient? client;

    // Created on first use: startup does not pay for it, and with automatic checks off the
    // network stack is never loaded. Calls never overlap, the view model allows one at a time.
    private HttpClient Client => client ??= CreateClient();

    public async Task<ReleaseVersion> ReadLatestAsync(CancellationToken cancellationToken)
    {
        using var response = await Client.GetAsync(Endpoint, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
        using var document = JsonDocument.Parse(body);
        var tag = document.RootElement.ValueKind == JsonValueKind.Object &&
            document.RootElement.TryGetProperty("tag_name", out var value) && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;
        return ReleaseVersion.TryParse(tag) ?? throw new FormatException("The latest release has no usable version tag.");
    }

    private HttpClient CreateClient()
    {
        var created = new HttpClient(handler ?? new SocketsHttpHandler { UseCookies = false, MaxAutomaticRedirections = 3 })
        {
            Timeout = RequestTimeout,
            MaxResponseContentBufferSize = MaxResponseBytes
        };
        created.DefaultRequestHeaders.UserAgent.ParseAdd("RevoHelper");
        created.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return created;
    }
}
