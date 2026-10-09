namespace Puls8.Venue;

public sealed class VenueProfile
{
    public int Schema { get; init; }

    public string Name { get; init; } = "Puls8";

    public string Tagline { get; init; } = string.Empty;

    public string Description { get; init; } = string.Empty;

    public VenueAddress Address { get; init; } = new();

    public string TimeZone { get; init; } = "UTC";

    public ScheduleSlot[] Schedule { get; init; } = [];

    public VenueEvent[] Events { get; init; } = [];

    public VenueLink[] Links { get; init; } = [];

    public SyncEntry[] Sync { get; init; } = [];

    public PackDefinition[] Packs { get; init; } = [];
}

public sealed class VenueAddress
{
    public string World { get; init; } = string.Empty;

    public string DataCenter { get; init; } = string.Empty;

    public string District { get; init; } = string.Empty;

    public int Ward { get; init; }

    public int Plot { get; init; }

    public string Display => $"{World} · {District} · Ward {Ward} · Plot {Plot}";
}

public sealed class ScheduleSlot
{
    public string Day { get; init; } = string.Empty;

    public string Open { get; init; } = string.Empty;

    public string Close { get; init; } = string.Empty;
}

public sealed class VenueEvent
{
    public string Id { get; init; } = string.Empty;

    public string Title { get; init; } = string.Empty;

    public string Host { get; init; } = string.Empty;

    public DateTimeOffset Starts { get; init; }

    public int Minutes { get; init; }

    public string Blurb { get; init; } = string.Empty;

    public DateTimeOffset Ends => Starts.AddMinutes(Minutes);
}

public sealed class VenueLink
{
    public string Label { get; init; } = string.Empty;

    public string Url { get; init; } = string.Empty;

    public string Kind { get; init; } = string.Empty;
}

public sealed class SyncEntry
{
    public string Service { get; init; } = string.Empty;

    public string Kind { get; init; } = string.Empty;

    public string Id { get; init; } = string.Empty;

    public string Password { get; init; } = string.Empty;

    public string Note { get; init; } = string.Empty;
}

public sealed class PackDefinition
{
    public string Id { get; init; } = string.Empty;

    public string Title { get; init; } = string.Empty;

    public string Blurb { get; init; } = string.Empty;

    public string TagPrefix { get; init; } = string.Empty;

    public string Folder { get; init; } = string.Empty;

    public string Target { get; init; } = "base";

    public string Collection { get; init; } = string.Empty;

    public int Priority { get; init; } = 100;

    public bool AllOptions { get; init; }

    public bool TargetsMannequin => string.Equals(Target, "mannequin", StringComparison.OrdinalIgnoreCase);
}
