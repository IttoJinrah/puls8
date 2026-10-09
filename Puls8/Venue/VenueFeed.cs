using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Puls8.Net;

namespace Puls8.Venue;

public enum FeedSource : byte
{
    Bundled,
    Cached,
    Live,
}

public sealed partial class VenueFeed
{
    private const string RemoteUrl = "https://raw.githubusercontent.com/IttoJinrah/puls8/main/venue.json";
    private const string FileName = "venue.json";
    private const string CacheFileName = "venue.cache.json";
    private const string ETagFileName = "venue.etag";
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(15);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private readonly string cachePath;
    private readonly string eTagPath;
    private int refreshing;

    public VenueFeed()
    {
        var configDirectory = Services.PluginInterface.GetPluginConfigDirectory();
        cachePath = Path.Combine(configDirectory, CacheFileName);
        eTagPath = Path.Combine(configDirectory, ETagFileName);
        live = LoadLocal(out var source);
        Source = source;
    }

    private VenueProfile live;
    private VenueProfile? preview;

    // Staff previewing an unpublished draft see it everywhere; nobody else is affected.
    public VenueProfile Current => preview ?? live;

    public bool IsPreviewing => preview is not null;

    public FeedSource Source { get; private set; }

    public DateTime LastCheckedUtc { get; private set; }

    public bool IsRefreshing => Volatile.Read(ref refreshing) == 1;

    public event Action? Changed;

    public void RefreshIfStale()
    {
        if (DateTime.UtcNow - LastCheckedUtc < RefreshInterval)
        {
            return;
        }

        _ = RefreshAsync();
    }

    public async Task RefreshAsync()
    {
        if (Interlocked.Exchange(ref refreshing, 1) == 1)
        {
            return;
        }

        try
        {
            await FetchRemoteAsync().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            Services.Log.Information($"Venue feed refresh failed, keeping the {Source} copy: {exception.Message}");
        }
        finally
        {
            LastCheckedUtc = DateTime.UtcNow;
            Volatile.Write(ref refreshing, 0);
        }
    }

    private async Task FetchRemoteAsync()
    {
        using var timeout = new CancellationTokenSource(RequestTimeout);
        using var request = new HttpRequestMessage(HttpMethod.Get, RemoteUrl);
        var eTag = ReadText(eTagPath);
        if (eTag.Length > 0 && File.Exists(cachePath))
        {
            request.Headers.TryAddWithoutValidation("If-None-Match", eTag);
        }

        using var response = await Http.Client.SendAsync(request, timeout.Token).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotModified)
        {
            if (Source != FeedSource.Live)
            {
                Publish(live, FeedSource.Live);
            }

            return;
        }

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(false);
        var profile = Parse(body);
        if (profile is null)
        {
            Services.Log.Warning("Remote venue.json failed validation; keeping the previous copy");
            return;
        }

        await File.WriteAllTextAsync(cachePath, body, timeout.Token).ConfigureAwait(false);
        var newTag = response.Headers.ETag?.Tag ?? string.Empty;
        await File.WriteAllTextAsync(eTagPath, newTag, timeout.Token).ConfigureAwait(false);
        Publish(profile, FeedSource.Live);
    }

    private void Publish(VenueProfile profile, FeedSource source)
    {
        live = profile;
        Source = source;
        Changed?.Invoke();
    }

    private VenueProfile LoadLocal(out FeedSource source)
    {
        var cached = Parse(ReadText(cachePath));
        if (cached is not null)
        {
            source = FeedSource.Cached;
            return cached;
        }

        var bundledPath = Path.Combine(Services.PluginInterface.AssemblyLocation.DirectoryName ?? string.Empty, FileName);
        source = FeedSource.Bundled;
        return Parse(ReadText(bundledPath)) ?? new VenueProfile();
    }

    private static string ReadText(string path)
    {
        try
        {
            return File.Exists(path) ? File.ReadAllText(path).Trim() : string.Empty;
        }
        catch (IOException)
        {
            return string.Empty;
        }
    }

    public void SetPreview(VenueProfile? draft) => preview = draft;

    // raw.githubusercontent.com caches for minutes; applying the just-published file avoids showing staff stale data.
    public void ApplyPublished(string json)
    {
        var profile = Parse(json);
        if (profile is null)
        {
            return;
        }

        try
        {
            File.WriteAllText(cachePath, json);
            File.Delete(eTagPath);
        }
        catch (IOException exception)
        {
            Services.Log.Information($"Could not cache the published venue.json: {exception.Message}");
        }

        Publish(profile, FeedSource.Live);
    }

    public static VenueProfile? Parse(string json)
    {
        if (json.Length == 0)
        {
            return null;
        }

        try
        {
            var profile = JsonSerializer.Deserialize<VenueProfile>(json, JsonOptions);
            return profile is not null && HasSafePacks(profile) ? profile : null;
        }
        catch (JsonException exception)
        {
            Services.Log.Warning($"venue.json is not valid JSON: {exception.Message}");
            return null;
        }
    }

    // Pack folders are deleted and recreated inside the Penumbra root, so a remote edit must never be able to point outside it.
    private static bool HasSafePacks(VenueProfile profile)
    {
        var packs = profile.Packs;
        for (var packIndex = 0; packIndex < packs.Length; packIndex++)
        {
            var pack = packs[packIndex];
            if (!SafeFolderPattern().IsMatch(pack.Folder) || pack.TagPrefix.Length == 0 || pack.Id.Length == 0)
            {
                Services.Log.Warning($"venue.json pack '{pack.Id}' has an unsafe folder or missing tag prefix");
                return false;
            }
        }

        return true;
    }

    [GeneratedRegex("^Puls8_[A-Za-z0-9_]{1,48}$")]
    private static partial Regex SafeFolderPattern();
}
