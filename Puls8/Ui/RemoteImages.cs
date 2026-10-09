using System.IO;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Dalamud.Interface.Textures.TextureWraps;
using Puls8.Net;

namespace Puls8.Ui;

// Venue photos (crew portraits, menu items) are files in the Puls8 repo. Only repo-relative paths are accepted so a
// remote venue.json can't make every player's client contact an arbitrary server.
public sealed partial class RemoteImages : IDisposable
{
    public const int MaxBytes = 2 * 1024 * 1024;

    private const string RawBase = "https://raw.githubusercontent.com/IttoJinrah/puls8/main/";
    private static readonly TimeSpan CacheLifetime = TimeSpan.FromHours(6);
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(20);

    private readonly Dictionary<string, Entry> entries = new(StringComparer.Ordinal);
    private readonly string cacheDirectory;

    public RemoteImages()
    {
        cacheDirectory = Path.Combine(Services.PluginInterface.GetPluginConfigDirectory(), "images");
    }

    public static bool IsValidPath(string path) => SafePathPattern().IsMatch(path) && !path.Contains("..", StringComparison.Ordinal);

    public IDalamudTextureWrap? Get(string path)
    {
        if (path.Length == 0)
        {
            return null;
        }

        if (entries.TryGetValue(path, out var entry))
        {
            return entry.Texture;
        }

        entry = new Entry();
        entries[path] = entry;
        if (IsValidPath(path))
        {
            _ = LoadAsync(path, entry);
        }

        return null;
    }

    public void Dispose()
    {
        foreach (var entry in entries.Values)
        {
            entry.Texture?.Dispose();
        }

        entries.Clear();
    }

    private async Task LoadAsync(string path, Entry entry)
    {
        try
        {
            var bytes = await ReadCachedAsync(path).ConfigureAwait(false) ?? await DownloadAsync(path).ConfigureAwait(false);
            if (bytes is null)
            {
                return;
            }

            entry.Texture = await Services.Textures.CreateFromImageAsync(bytes, $"Puls8:{path}").ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            Services.Log.Information($"Couldn't load venue image '{path}': {exception.Message}");
        }
    }

    private async Task<byte[]?> ReadCachedAsync(string path)
    {
        var file = CacheFile(path);
        if (!File.Exists(file) || DateTime.UtcNow - File.GetLastWriteTimeUtc(file) > CacheLifetime)
        {
            return null;
        }

        return await File.ReadAllBytesAsync(file).ConfigureAwait(false);
    }

    private async Task<byte[]?> DownloadAsync(string path)
    {
        using var timeout = new CancellationTokenSource(RequestTimeout);
        using var response = await Http.Client.GetAsync(RawBase + path, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode || response.Content.Headers.ContentLength > MaxBytes)
        {
            return CachedEvenIfStale(path);
        }

        var bytes = await response.Content.ReadAsByteArrayAsync(timeout.Token).ConfigureAwait(false);
        if (bytes.Length > MaxBytes)
        {
            return null;
        }

        Directory.CreateDirectory(cacheDirectory);
        await File.WriteAllBytesAsync(CacheFile(path), bytes, timeout.Token).ConfigureAwait(false);
        return bytes;
    }

    private byte[]? CachedEvenIfStale(string path)
    {
        var file = CacheFile(path);
        return File.Exists(file) ? File.ReadAllBytes(file) : null;
    }

    private string CacheFile(string path) => Path.Combine(cacheDirectory, path.Replace('/', '_'));

    [GeneratedRegex(@"^images/[A-Za-z0-9_\-/]{1,120}\.(png|jpe?g)$", RegexOptions.IgnoreCase)]
    private static partial Regex SafePathPattern();

    private sealed class Entry
    {
        public IDalamudTextureWrap? Texture;
    }
}
