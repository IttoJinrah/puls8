using Dalamud.Interface.ImGuiNotification;

namespace Puls8.Venue;

public sealed class EventReminders
{
    private static readonly TimeSpan LeadTime = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(5);

    private readonly Configuration configuration;
    private DateTime nextCheckUtc;

    public EventReminders(Configuration configuration)
    {
        this.configuration = configuration;
    }

    public bool IsSet(VenueEvent venueEvent) => configuration.EventReminders.Contains(venueEvent.Id);

    public void Toggle(VenueEvent venueEvent)
    {
        if (!configuration.EventReminders.Remove(venueEvent.Id))
        {
            configuration.EventReminders.Add(venueEvent.Id);
            configuration.FiredReminders.Remove(venueEvent.Id);
        }

        configuration.Save();
    }

    public void Tick(VenueClock clock, string venueName)
    {
        var nowUtc = DateTime.UtcNow;
        if (nowUtc < nextCheckUtc || configuration.EventReminders.Count == 0)
        {
            return;
        }

        nextCheckUtc = nowUtc + CheckInterval;
        var now = DateTimeOffset.UtcNow;
        var events = clock.UpcomingEvents;
        for (var eventIndex = 0; eventIndex < events.Count; eventIndex++)
        {
            var venueEvent = events[eventIndex];
            if (!configuration.EventReminders.Contains(venueEvent.Id)
                || configuration.FiredReminders.Contains(venueEvent.Id)
                || now < venueEvent.Starts - LeadTime)
            {
                continue;
            }

            Fire(venueEvent, venueName, now);
        }
    }

    private void Fire(VenueEvent venueEvent, string venueName, DateTimeOffset now)
    {
        configuration.FiredReminders.Add(venueEvent.Id);
        configuration.Save();

        var when = venueEvent.Starts <= now
            ? "is starting now"
            : $"starts in {VenueClock.FormatCountdown(venueEvent.Starts - now)}";
        var message = $"{venueEvent.Title} {when} at {venueName}. Open /puls to travel there.";
        Services.Chat.Print(message, venueName, 541);
        Services.Notifications.AddNotification(new Notification
        {
            Title = venueName,
            Content = message,
            Type = NotificationType.Info,
            InitialDuration = TimeSpan.FromSeconds(12),
        });
    }
}
