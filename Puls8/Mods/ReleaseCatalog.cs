using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading.Tasks;
using Puls8.Net;
using Puls8.Venue;

namespace Puls8.Mods;

public sealed record PackRelease(string Tag, string AssetName, string DownloadUrl, long Size, string Sha256, DateTimeOffset Published);

public sealed class ReleaseCatalog
{
    private const string ReleasesUrl = "https://api.github.com/repos/IttoJinrah/puls8/releases?per_page=50";
    private const string CacheFileName = "releases.cache.json";
    private const string ETagFileName = "releases.etag";
    private const string PackExtension = ".pmp";
    private const string DigestPrefix = "sha256:";
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(20);

    private readonly string cachePath;
    private readonly string eTagPath;
    private readonly SemaphoreSlim gate = new(1, 1);
    private string body = string.Empty;

    public ReleaseCatalog()
    {
        var configDirectory = Services.PluginInterface.GetPluginConfigDirectory();
        cachePath = Path.Combine(configDirectory, CacheFileName);
        eTagPath = Path.Combine(configDirectory, ETagFileName);
    }

    public DateTime LastFetchedUtc { get; private set; }

    public async Task RefreshAsync(CancellationToken cancellation)
    {
        await gate.WaitAsync(cancellation).ConfigureAwait(false);
        try
        {
            await FetchAsync(cancellation).ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }

    public PackRelease? Find(PackDefinition pack)
    {
        if (body.Length == 0)
        {
            return null;
        }

        using var document = JsonDocument.Parse(body);
        foreach (var release in document.RootElement.EnumerateArray())
        {
            if (release.GetProperty("draft").GetBoolean() || release.GetProperty("prerelease").GetBoolean())
            {
                continue;
            }

            var tag = release.GetProperty("tag_name").GetString() ?? string.Empty;
            if (!tag.StartsWith(pack.TagPrefix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var asset = FindPackAsset(release);
            if (asset is null)
            {
                continue;
            }

            var published = release.TryGetProperty("published_at", out var publishedElement) && publishedElement.ValueKind == JsonValueKind.String
                ? publishedElement.GetDateTimeOffset()
                : default;
            var value = asset.Value;
            return new PackRelease(tag, value.Name, value.Url, value.Size, value.Sha256, published);
        }

        return null;
    }

    private async Task FetchAsync(CancellationToken cancellation)
    {
        if (body.Length == 0)
        {
            body = await ReadTextAsync(cachePath, cancellation).ConfigureAwait(false);
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        timeout.CancelAfter(RequestTimeout);
        using var request = new HttpRequestMessage(HttpMethod.Get, ReleasesUrl);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        request.Headers.TryAddWithoutValidation("X-GitHub-Api-Version", "2022-11-28");
        var eTag = await ReadTextAsync(eTagPath, cancellation).ConfigureAwait(false);
        if (eTag.Length > 0 && body.Length > 0)
        {
            request.Headers.TryAddWithoutValidation("If-None-Match", eTag);
        }

        HttpResponseMessage response;
        try
        {
            response = await Http.Client.SendAsync(request, timeout.Token).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException && !cancellation.IsCancellationRequested)
        {
            if (body.Length > 0)
            {
                Services.Log.Information($"GitHub unreachable, using cached release list: {exception.Message}");
                return;
            }

            throw new InstallException(Problems.Network(exception.Message), exception);
        }

        using (response)
        {
            if (response.StatusCode == HttpStatusCode.NotModified)
            {
                LastFetchedUtc = DateTime.UtcNow;
                return;
            }

            if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests)
            {
                if (body.Length > 0)
                {
                    return;
                }

                throw new InstallException(Problems.RateLimited(ResetTime(response)));
            }

            if (!response.IsSuccessStatusCode)
            {
                throw new InstallException(Problems.Network($"GitHub answered {(int)response.StatusCode} {response.ReasonPhrase}"));
            }

            var fresh = await response.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(false);
            body = fresh;
            LastFetchedUtc = DateTime.UtcNow;
            await File.WriteAllTextAsync(cachePath, fresh, cancellation).ConfigureAwait(false);
            await File.WriteAllTextAsync(eTagPath, response.Headers.ETag?.Tag ?? string.Empty, cancellation).ConfigureAwait(false);
        }
    }

    private static (string Name, string Url, long Size, string Sha256)? FindPackAsset(JsonElement release)
    {
        if (!release.TryGetProperty("assets", out var assets))
        {
            return null;
        }

        foreach (var asset in assets.EnumerateArray())
        {
            var name = asset.GetProperty("name").GetString() ?? string.Empty;
            if (!name.EndsWith(PackExtension, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var url = asset.GetProperty("browser_download_url").GetString() ?? string.Empty;
            var size = asset.GetProperty("size").GetInt64();
            var digest = asset.TryGetProperty("digest", out var digestElement) && digestElement.ValueKind == JsonValueKind.String
                ? digestElement.GetString() ?? string.Empty
                : string.Empty;
            var sha256 = digest.StartsWith(DigestPrefix, StringComparison.OrdinalIgnoreCase) ? digest[DigestPrefix.Length..] : string.Empty;
            return (name, url, size, sha256);
        }

        return null;
    }

    private static DateTimeOffset? ResetTime(HttpResponseMessage response)
    {
        if (!response.Headers.TryGetValues("X-RateLimit-Reset", out var values))
        {
            return null;
        }

        foreach (var value in values)
        {
            if (long.TryParse(value, out var seconds))
            {
                return DateTimeOffset.FromUnixTimeSeconds(seconds);
            }
        }

        return null;
    }

    private static async Task<string> ReadTextAsync(string path, CancellationToken cancellation)
    {
        try
        {
            return File.Exists(path) ? (await File.ReadAllTextAsync(path, cancellation).ConfigureAwait(false)).Trim() : string.Empty;
        }
        catch (IOException)
        {
            return string.Empty;
        }
    }
}
