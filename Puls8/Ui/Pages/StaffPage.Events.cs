using System.Globalization;
using System.Text.Json.Nodes;
using Dalamud.Bindings.ImGui;
using Puls8.Staff;

namespace Puls8.Ui.Pages;

public sealed partial class StaffPage
{
    private const string DateFormat = "yyyy-MM-dd";
    private const string TimeFormat = "HH:mm";
    private const int DefaultEventMinutes = 180;
    private const int DefaultEventHour = 21;

    // Typed date/time text lives here so a half-typed "2026-10-3" doesn't get rejected and wiped every frame.
    private readonly Dictionary<JsonObject, EventClock> eventClocks = new(ReferenceEqualityComparer.Instance);

    private bool DrawEvents(JsonObject draft)
    {
        var events = EditorFields.Array(draft, "events");
        Widgets.Wrapped($"Times are in your local time ({TimeZoneInfo.Local.StandardName}); every player sees them converted to theirs. Past events disappear on their own.", Palette.InkMuted);
        ImGui.Spacing();
        if (AddButton("##addevent", "ADD EVENT"))
        {
            events.Insert(0, NewEvent());
            return true;
        }

        ImGui.Spacing();
        var changed = false;
        for (var eventIndex = 0; eventIndex < events.Count; eventIndex++)
        {
            var venueEvent = ObjectAt(events, eventIndex);
            if (venueEvent is null)
            {
                continue;
            }

            ImGui.PushID(eventIndex);
            var outcome = DrawEvent(events, venueEvent, eventIndex);
            ImGui.PopID();
            if (outcome == RowOutcome.Structure)
            {
                return true;
            }

            changed |= outcome == RowOutcome.Edited;
        }

        return changed;
    }

    private RowOutcome DrawEvent(JsonArray events, JsonObject venueEvent, int index)
    {
        using var card = Widgets.Card(Palette.Violet);
        var width = card.InnerWidth;
        var changed = EditorFields.Text(venueEvent, "title", "TITLE", width, "Neon Nightmare: Halloween Night");
        changed |= EditorFields.Text(venueEvent, "host", "DJ / HOST", width, "DJ Vex");
        changed |= DrawEventClock(venueEvent, width);
        changed |= EditorFields.Number(venueEvent, "minutes", "LENGTH (MINUTES)", Half(width), 15, 1440);
        changed |= EditorFields.Multiline(venueEvent, "blurb", "DESCRIPTION", width);
        ImGui.Spacing();
        if (EditorFields.Move(events, index, "##eventmove"))
        {
            return RowOutcome.Structure;
        }

        if (EditorFields.Delete($"##eventdelete{index}", width))
        {
            events.RemoveAt(index);
            eventClocks.Remove(venueEvent);
            return RowOutcome.Structure;
        }

        return changed ? RowOutcome.Edited : RowOutcome.None;
    }

    private bool DrawEventClock(JsonObject venueEvent, float width)
    {
        if (!eventClocks.TryGetValue(venueEvent, out var clock))
        {
            clock = EventClock.From(StaffSession.Text(venueEvent, "starts"));
            eventClocks[venueEvent] = clock;
        }

        var half = Half(width);
        ImGui.BeginGroup();
        ImGui.TextColored(Palette.InkDim, "DATE (YYYY-MM-DD)");
        ImGui.SetNextItemWidth(half);
        var edited = ImGui.InputTextWithHint("##eventdate", "2026-10-31", ref clock.Date, 10);
        ImGui.EndGroup();
        ImGui.SameLine(0f, 8f * Widgets.Scale);
        ImGui.BeginGroup();
        ImGui.TextColored(Palette.InkDim, "START (24H)");
        ImGui.SetNextItemWidth(half);
        edited |= ImGui.InputTextWithHint("##eventtime", "21:00", ref clock.Time, 5);
        ImGui.EndGroup();

        if (!clock.TryCompose(out var starts))
        {
            ImGui.TextColored(Palette.Amber, "Use a date like 2026-10-31 and a time like 21:00.");
            return false;
        }

        if (!edited)
        {
            return false;
        }

        venueEvent["starts"] = starts;
        return true;
    }

    private static JsonObject NewEvent()
    {
        var start = NextFriday();
        return new JsonObject
        {
            ["id"] = $"event-{DateTime.UtcNow:yyyyMMddHHmmss}",
            ["title"] = "New event",
            ["host"] = string.Empty,
            ["starts"] = start.ToString("yyyy-MM-ddTHH:mm:sszzz", CultureInfo.InvariantCulture),
            ["minutes"] = DefaultEventMinutes,
            ["blurb"] = string.Empty,
        };
    }

    private static DateTimeOffset NextFriday()
    {
        var today = DateTime.Today;
        var daysAhead = ((int)DayOfWeek.Friday - (int)today.DayOfWeek + 7) % 7;
        var local = today.AddDays(daysAhead == 0 ? 7 : daysAhead).AddHours(DefaultEventHour);
        return new DateTimeOffset(local, TimeZoneInfo.Local.GetUtcOffset(local));
    }

    private enum RowOutcome : byte
    {
        None,
        Edited,
        Structure,
    }

    private sealed class EventClock
    {
        public string Date = string.Empty;
        public string Time = string.Empty;

        public static EventClock From(string starts)
        {
            if (!DateTimeOffset.TryParse(starts, CultureInfo.InvariantCulture, DateTimeStyles.None, out var moment))
            {
                return new EventClock();
            }

            var local = moment.ToLocalTime();
            return new EventClock
            {
                Date = local.ToString(DateFormat, CultureInfo.InvariantCulture),
                Time = local.ToString(TimeFormat, CultureInfo.InvariantCulture),
            };
        }

        // The offset is taken for that specific date, so an event after a DST switch keeps the right wall-clock time.
        public bool TryCompose(out string starts)
        {
            starts = string.Empty;
            if (!DateTime.TryParseExact($"{Date} {Time}", $"{DateFormat} {TimeFormat}", CultureInfo.InvariantCulture, DateTimeStyles.None, out var local))
            {
                return false;
            }

            var moment = new DateTimeOffset(local, TimeZoneInfo.Local.GetUtcOffset(local));
            starts = moment.ToString("yyyy-MM-ddTHH:mm:sszzz", CultureInfo.InvariantCulture);
            return true;
        }
    }
}
