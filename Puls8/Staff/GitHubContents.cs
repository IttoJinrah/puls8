using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Puls8.Net;

namespace Puls8.Staff;

public enum KeyCheck : byte
{
    Valid,
    ReadOnly,
    Invalid,
    Unreachable,
}

public enum PublishOutcome : byte
{
    Published,
    Conflict,
    Rejected,
    Failed,
}

public sealed record VenueDocument(string Json, string Sha);

public sealed record PublishResult(PublishOutcome Outcome, string Sha, string Detail);

// Write access is enforced by GitHub, not by this plugin: a key without push rights can never publish.
public static class GitHubContents
{
    private const string Repository = "IttoJinrah/puls8";
    private const string Branch = "main";
    private const string FilePath = "venue.json";
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(20);

    public static async Task<(KeyCheck Check, string Login)> VerifyAsync(string key)
    {
        try
        {
            using var repository = await SendAsync(HttpMethod.Get, $"repos/{Repository}", key, null).ConfigureAwait(false);
            if (repository.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden or HttpStatusCode.NotFound)
            {
                return (KeyCheck.Invalid, string.Empty);
            }

            if (!repository.IsSuccessStatusCode)
            {
                return (KeyCheck.Unreachable, string.Empty);
            }

            using var repositoryJson = JsonDocument.Parse(await repository.Content.ReadAsStringAsync().ConfigureAwait(false));
            var canPush = repositoryJson.RootElement.TryGetProperty("permissions", out var permissions)
                && permissions.TryGetProperty("push", out var push) && push.GetBoolean();
            var login = await ReadLoginAsync(key).ConfigureAwait(false);
            return (canPush ? KeyCheck.Valid : KeyCheck.ReadOnly, login);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException)
        {
            return (KeyCheck.Unreachable, string.Empty);
        }
    }

    public static async Task<VenueDocument?> LoadAsync(string key)
    {
        using var response = await SendAsync(HttpMethod.Get, $"repos/{Repository}/contents/{FilePath}?ref={Branch}", key, null).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync().ConfigureAwait(false));
        var root = document.RootElement;
        var encoded = root.GetProperty("content").GetString() ?? string.Empty;
        var json = Encoding.UTF8.GetString(Convert.FromBase64String(encoded.Replace("\n", string.Empty)));
        return new VenueDocument(json, root.GetProperty("sha").GetString() ?? string.Empty);
    }

    public static Task<PublishResult> PublishAsync(string key, string json, string sha, string message)
        => PutFileAsync(key, FilePath, Encoding.UTF8.GetBytes(json), sha, message);

    public static async Task<PublishResult> PutFileAsync(string key, string path, byte[] content, string sha, string message)
    {
        var body = new JsonObject
        {
            ["message"] = message,
            ["content"] = Convert.ToBase64String(content),
            ["branch"] = Branch,
        };
        if (sha.Length > 0)
        {
            body["sha"] = sha;
        }

        try
        {
            using var response = await SendAsync(HttpMethod.Put, $"repos/{Repository}/contents/{path}", key, body.ToJsonString()).ConfigureAwait(false);
            var text = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            if (response.IsSuccessStatusCode)
            {
                using var document = JsonDocument.Parse(text);
                var newSha = document.RootElement.GetProperty("content").GetProperty("sha").GetString() ?? string.Empty;
                return new PublishResult(PublishOutcome.Published, newSha, string.Empty);
            }

            // GitHub answers 409, or 422 with a sha mismatch, when someone else published since this draft was loaded.
            if (response.StatusCode == HttpStatusCode.Conflict
                || (response.StatusCode == HttpStatusCode.UnprocessableEntity && text.Contains("sha", StringComparison.OrdinalIgnoreCase)))
            {
                return new PublishResult(PublishOutcome.Conflict, string.Empty, string.Empty);
            }

            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden or HttpStatusCode.NotFound)
            {
                return new PublishResult(PublishOutcome.Rejected, string.Empty, $"GitHub answered {(int)response.StatusCode}");
            }

            return new PublishResult(PublishOutcome.Failed, string.Empty, $"GitHub answered {(int)response.StatusCode}");
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException)
        {
            return new PublishResult(PublishOutcome.Failed, string.Empty, exception.Message);
        }
    }

    private static async Task<string> ReadLoginAsync(string key)
    {
        using var response = await SendAsync(HttpMethod.Get, "user", key, null).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            return "staff";
        }

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync().ConfigureAwait(false));
        return document.RootElement.TryGetProperty("login", out var login) ? login.GetString() ?? "staff" : "staff";
    }

    private static async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, string key, string? json)
    {
        using var timeout = new CancellationTokenSource(RequestTimeout);
        using var request = new HttpRequestMessage(method, $"https://api.github.com/{path}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        request.Headers.TryAddWithoutValidation("X-GitHub-Api-Version", "2022-11-28");
        if (json is not null)
        {
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        }

        return await Http.Client.SendAsync(request, timeout.Token).ConfigureAwait(false);
    }
}
