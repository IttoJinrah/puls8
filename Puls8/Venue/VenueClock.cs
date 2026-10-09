using System.Globalization;

namespace Puls8.Venue;

public readonly record struct Opening(DateTimeOffset Opens, DateTimeOffset Closes);

public readonly record struct VenueStatus(bool IsOpen, DateTimeOffset Boundary, bool HasSchedule);

public sealed class VenueClock
{
    private const int LookaheadDays = 8;
    private static readonly TimeSpan RecomputeInterval = TimeSpan.FromSeconds(1);

    private readonly List<Opening> openings = new(16);
    private readonly List<VenueEvent> upcomingEvents = new(8);
    private VenueProfile? source;
    private DateTimeOffset computedAt;

    public VenueStatus Status { get; private set; }

    public IReadOnlyList<Opening> Openings => openings;

    public IReadOnlyList<VenueEvent> UpcomingEvents => upcomingEvents;

    public VenueEvent? LiveEvent { get; private set; }

    public void Update(VenueProfile profile)
    {
        var now = DateTimeOffset.UtcNow;
        if (ReferenceEquals(profile, source) && now - computedAt < RecomputeInterval)
        {
            return;
        }

        source = profile;
        computedAt = now;
        BuildOpenings(profile, now);
        BuildEvents(profile, now);
        Status = ComputeStatus(now);
    }

    public static string FormatCountdown(TimeSpan span)
    {
        if (span.TotalDays >= 1)
        {
            return $"{(int)span.TotalDays}d {span.Hours}h";
        }

        if (span.TotalHours >= 1)
        {
            return $"{(int)span.TotalHours}h {span.Minutes:00}m";
        }

        return $"{Math.Max(0, span.Minutes)}m {Math.Max(0, span.Seconds):00}s";
    }

    public static string FormatLocal(DateTimeOffset moment, string format)
        => moment.ToLocalTime().ToString(format, CultureInfo.CurrentCulture);

    private void BuildOpenings(VenueProfile profile, DateTimeOffset now)
    {
        openings.Clear();
        var zone = ResolveZone(profile.TimeZone);
        var venueToday = TimeZoneInfo.ConvertTime(now, zone).Date;
        var slots = profile.Schedule;
        for (var dayOffset = -1; dayOffset < LookaheadDays; dayOffset++)
        {
            var date = venueToday.AddDays(dayOffset);
            for (var slotIndex = 0; slotIndex < slots.Length; slotIndex++)
            {
                var slot = slots[slotIndex];
                if (!TryParseDay(slot.Day, out var day) || day != date.DayOfWeek)
                {
                    continue;
                }

                if (!TimeSpan.TryParse(slot.Open, CultureInfo.InvariantCulture, out var open)
                    || !TimeSpan.TryParse(slot.Close, CultureInfo.InvariantCulture, out var close))
                {
                    continue;
                }

                var opens = ToUtc(date + open, zone);
                var closeDate = close <= open ? date.AddDays(1) : date;
                var closes = ToUtc(closeDate + close, zone);
                if (closes > now)
                {
                    openings.Add(new Opening(opens, closes));
                }
            }
        }

        openings.Sort(static (left, right) => left.Opens.CompareTo(right.Opens));
    }

    private void BuildEvents(VenueProfile profile, DateTimeOffset now)
    {
        upcomingEvents.Clear();
        LiveEvent = null;
        var events = profile.Events;
        for (var eventIndex = 0; eventIndex < events.Length; eventIndex++)
        {
            var venueEvent = events[eventIndex];
            if (venueEvent.Ends <= now)
            {
                continue;
            }

            upcomingEvents.Add(venueEvent);
            if (venueEvent.Starts <= now)
            {
                LiveEvent = venueEvent;
            }
        }

        upcomingEvents.Sort(static (left, right) => left.Starts.CompareTo(right.Starts));
    }

    private VenueStatus ComputeStatus(DateTimeOffset now)
    {
        if (openings.Count == 0)
        {
            return new VenueStatus(false, default, false);
        }

        var first = openings[0];
        return first.Opens <= now
            ? new VenueStatus(true, first.Closes, true)
            : new VenueStatus(false, first.Opens, true);
    }

    private static DateTimeOffset ToUtc(DateTime venueLocal, TimeZoneInfo zone)
    {
        // A start time inside a spring-forward gap does not exist locally; nudging it past the gap keeps the slot.
        if (zone.IsInvalidTime(venueLocal))
        {
            venueLocal = venueLocal.AddHours(1);
        }

        var offset = zone.GetUtcOffset(venueLocal);
        return new DateTimeOffset(DateTime.SpecifyKind(venueLocal, DateTimeKind.Unspecified), offset).ToUniversalTime();
    }

    private static TimeZoneInfo ResolveZone(string identifier)
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(identifier);
        }
        catch (Exception)
        {
            return TimeZoneInfo.Utc;
        }
    }

    private static bool TryParseDay(string text, out DayOfWeek day)
        => Enum.TryParse(text, true, out day) && Enum.IsDefined(day);
}
