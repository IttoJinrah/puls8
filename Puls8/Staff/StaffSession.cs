using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Puls8.Venue;

namespace Puls8.Staff;

public enum StaffState : byte
{
    Locked,
    Verifying,
    Unlocked,
}

public sealed class StaffSession
{
    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private static readonly JsonSerializerOptions SeedOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly Configuration configuration;
    private readonly VenueFeed feed;
    private readonly List<string> issues = new(8);
    private string key = string.Empty;
    private string sha = string.Empty;

    public StaffSession(Configuration configuration, VenueFeed feed)
    {
        this.configuration = configuration;
        this.feed = feed;
    }

    public StaffState State { get; private set; }

    public bool IsUnlocked => State == StaffState.Unlocked;

    public string Login { get; private set; } = string.Empty;

    public string Message { get; private set; } = string.Empty;

    public bool MessageIsError { get; private set; }

    // A JSON tree rather than the typed profile, so fields the editor doesn't know about (packs, future keys) survive a publish.
    public JsonObject? Draft { get; private set; }

    public bool IsDirty { get; private set; }

    public bool IsBusy { get; private set; }

    public bool IsPreviewing => feed.IsPreviewing;

    public IReadOnlyList<string> Issues => issues;

    public void Restore()
    {
        var stored = StaffKeyStore.Unprotect(configuration.StaffKey);
        if (stored.Length > 0)
        {
            _ = UnlockAsync(stored, false);
        }
    }

    public async Task UnlockAsync(string candidate, bool remember)
    {
        if (State == StaffState.Verifying || candidate.Trim().Length == 0)
        {
            return;
        }

        State = StaffState.Verifying;
        SetMessage(string.Empty, false);
        var (check, login) = await GitHubContents.VerifyAsync(candidate.Trim()).ConfigureAwait(false);
        switch (check)
        {
            case KeyCheck.Valid:
                key = candidate.Trim();
                Login = login;
                State = StaffState.Unlocked;
                if (remember)
                {
                    configuration.StaffKey = StaffKeyStore.Protect(key);
                    configuration.Save();
                }

                await ReloadAsync().ConfigureAwait(false);
                return;
            case KeyCheck.ReadOnly:
                Fail("That key works, but it can't write to the Puls8 repo. Ask Arka for a key with Contents: Read and write.", true);
                return;
            case KeyCheck.Unreachable:
                State = StaffState.Locked;
                SetMessage("Couldn't reach GitHub. Check your connection and try again.", true);
                return;
            default:
                Fail("That key was refused by GitHub. It may be mistyped, expired or revoked.", true);
                return;
        }
    }

    public void Lock()
    {
        key = string.Empty;
        sha = string.Empty;
        Login = string.Empty;
        Draft = null;
        IsDirty = false;
        feed.SetPreview(null);
        configuration.StaffKey = string.Empty;
        configuration.Save();
        State = StaffState.Locked;
        SetMessage("Locked. The staff key was removed from this PC.", false);
    }

    public async Task ReloadAsync()
    {
        if (!IsUnlocked || IsBusy)
        {
            return;
        }

        IsBusy = true;
        try
        {
            var document = await GitHubContents.LoadAsync(key).ConfigureAwait(false);
            var json = document?.Json ?? JsonSerializer.Serialize(feed.Current, SeedOptions);
            sha = document?.Sha ?? string.Empty;
            Draft = JsonNode.Parse(json) as JsonObject ?? new JsonObject();
            IsDirty = false;
            feed.SetPreview(null);
            Validate();
            SetMessage(document is null ? "venue.json isn't on GitHub yet; publishing will create it." : "Loaded the live venue info.", false);
        }
        catch (Exception exception)
        {
            SetMessage($"Couldn't load venue.json: {exception.Message}", true);
        }
        finally
        {
            IsBusy = false;
        }
    }

    public void MarkDirty()
    {
        IsDirty = true;
        Validate();
        if (feed.IsPreviewing)
        {
            feed.SetPreview(BuildProfile());
        }
    }

    public void TogglePreview() => feed.SetPreview(feed.IsPreviewing ? null : BuildProfile());

    public async Task PublishAsync()
    {
        if (!IsUnlocked || IsBusy || Draft is null || issues.Count > 0)
        {
            return;
        }

        IsBusy = true;
        try
        {
            var json = Draft.ToJsonString(WriteOptions) + "\n";
            var message = $"Update venue info via the Puls8 staff editor ({Login})";
            var result = await GitHubContents.PublishAsync(key, json, sha, message).ConfigureAwait(false);
            switch (result.Outcome)
            {
                case PublishOutcome.Published:
                    sha = result.Sha;
                    IsDirty = false;
                    feed.SetPreview(null);
                    feed.ApplyPublished(json);
                    SetMessage("Published. Players see it within 15 minutes.", false);
                    break;
                case PublishOutcome.Conflict:
                    SetMessage("Someone else published since you started. Reload to get their changes, then redo yours.", true);
                    break;
                case PublishOutcome.Rejected:
                    SetMessage($"GitHub refused the publish ({result.Detail}). The key may have been revoked.", true);
                    break;
                default:
                    SetMessage($"Publishing failed: {result.Detail}", true);
                    break;
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    private VenueProfile? BuildProfile() => Draft is null ? null : VenueFeed.Parse(Draft.ToJsonString());

    private void Validate()
    {
        issues.Clear();
        if (Draft is null)
        {
            return;
        }

        if (BuildProfile() is null)
        {
            issues.Add("The venue info doesn't parse. A date or number field is probably malformed.");
        }

        ValidateEvents();
        ValidateSchedule();
    }

    private void ValidateEvents()
    {
        if (Draft!["events"] is not JsonArray events)
        {
            return;
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var eventIndex = 0; eventIndex < events.Count; eventIndex++)
        {
            if (events[eventIndex] is not JsonObject venueEvent)
            {
                continue;
            }

            var title = Text(venueEvent, "title");
            var name = title.Length > 0 ? title : $"Event {eventIndex + 1}";
            if (title.Length == 0)
            {
                issues.Add($"{name} needs a title.");
            }

            if (!DateTimeOffset.TryParse(Text(venueEvent, "starts"), CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
            {
                issues.Add($"{name} has an invalid start date or time.");
            }

            if (!seen.Add(Text(venueEvent, "id")))
            {
                issues.Add($"{name} shares its id with another event.");
            }
        }
    }

    private void ValidateSchedule()
    {
        if (Draft!["schedule"] is not JsonArray schedule)
        {
            return;
        }

        for (var slotIndex = 0; slotIndex < schedule.Count; slotIndex++)
        {
            if (schedule[slotIndex] is not JsonObject slot)
            {
                continue;
            }

            if (!TimeSpan.TryParse(Text(slot, "open"), CultureInfo.InvariantCulture, out _) || !TimeSpan.TryParse(Text(slot, "close"), CultureInfo.InvariantCulture, out _))
            {
                issues.Add($"Opening slot {slotIndex + 1} needs times like 21:00.");
            }
        }
    }

    private void Fail(string message, bool error)
    {
        key = string.Empty;
        State = StaffState.Locked;
        SetMessage(message, error);
    }

    private void SetMessage(string message, bool error)
    {
        Message = message;
        MessageIsError = error;
    }

    public static string Text(JsonObject node, string property)
        => node[property] is JsonValue value && value.TryGetValue<string>(out var text) ? text : string.Empty;
}
