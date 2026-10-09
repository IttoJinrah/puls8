using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Puls8.Venue;

namespace Puls8.Ui.Pages;

public sealed class EventsPage
{
    private readonly Plugin plugin;

    public EventsPage(Plugin plugin)
    {
        this.plugin = plugin;
    }

    public void Draw()
    {
        Widgets.SectionTitle("TONIGHT & BEYOND", 21);
        var upcoming = plugin.Clock.UpcomingEvents;
        if (upcoming.Count == 0)
        {
            using var empty = Widgets.Card(Palette.Violet);
            Widgets.Wrapped("No events on the board right now. Hop into the Discord to hear about the next one first.", Palette.InkMuted);
        }

        for (var eventIndex = 0; eventIndex < upcoming.Count; eventIndex++)
        {
            DrawEvent(upcoming[eventIndex], eventIndex);
        }

        ImGui.Spacing();
        Widgets.SectionTitle("OPENING HOURS", 22);
        DrawSchedule();
    }

    private void DrawEvent(VenueEvent venueEvent, int index)
    {
        var scale = Widgets.Scale;
        var now = DateTimeOffset.UtcNow;
        var live = venueEvent.Starts <= now;
        using var card = Widgets.Card(live ? Palette.Magenta : Palette.Violet);
        var drawList = ImGui.GetWindowDrawList();
        var blockSize = new Vector2(58f, 64f) * scale;
        var blockMin = ImGui.GetCursorScreenPos();
        var blockMax = blockMin + blockSize;
        Fx.GradientFill(drawList, blockMin, blockMax, 10f * scale, live ? 0.95f : 0.55f);
        var local = venueEvent.Starts.ToLocalTime();
        using (Fonts.Title())
        {
            var day = local.Day.ToString();
            var daySize = ImGui.CalcTextSize(day);
            drawList.AddText(new Vector2(blockMin.X + (blockSize.X - daySize.X) * 0.5f, blockMin.Y + 6f * scale), Palette.U32(Palette.Core), day);
        }

        var month = local.ToString("MMM").ToUpperInvariant();
        var monthSize = ImGui.CalcTextSize(month);
        drawList.AddText(new Vector2(blockMin.X + (blockSize.X - monthSize.X) * 0.5f, blockMax.Y - monthSize.Y - 6f * scale), Palette.U32(Palette.Core), month);
        ImGui.Dummy(blockSize);
        ImGui.SameLine(0f, 14f * scale);

        ImGui.BeginGroup();
        using (Fonts.Lead())
        {
            ImGui.TextColored(Palette.Core, venueEvent.Title);
        }

        if (venueEvent.Host.Length > 0)
        {
            Widgets.IconText(FontAwesomeIcon.Headphones, venueEvent.Host, Palette.Magenta);
        }

        ImGui.TextColored(Palette.InkMuted, $"{VenueClock.FormatLocal(venueEvent.Starts, "dddd HH:mm")} - {VenueClock.FormatLocal(venueEvent.Ends, "HH:mm")} your time");
        if (live)
        {
            Widgets.Chip("HAPPENING NOW", Palette.Magenta, true);
        }
        else
        {
            Widgets.Chip($"STARTS IN {VenueClock.FormatCountdown(venueEvent.Starts - now).ToUpperInvariant()}", Palette.Cyan);
        }

        ImGui.EndGroup();

        if (venueEvent.Blurb.Length > 0)
        {
            ImGui.Spacing();
            Widgets.Wrapped(venueEvent.Blurb, Palette.Ink);
        }

        if (live)
        {
            return;
        }

        ImGui.Spacing();
        var reminders = plugin.Reminders;
        var isSet = reminders.IsSet(venueEvent);
        var label = isSet ? "REMINDER SET (TAP TO CANCEL)" : "REMIND ME 10 MIN BEFORE";
        if (Widgets.Button($"##remind{index}", label, isSet ? FontAwesomeIcon.BellSlash : FontAwesomeIcon.Bell, new Vector2(card.InnerWidth, 32f * scale), isSet ? ButtonTone.Ghost : ButtonTone.Primary))
        {
            reminders.Toggle(venueEvent);
        }
    }

    private void DrawSchedule()
    {
        var openings = plugin.Clock.Openings;
        using var card = Widgets.Card(Palette.Electric);
        if (openings.Count == 0)
        {
            Widgets.Wrapped("Opening hours haven't been posted yet.", Palette.InkMuted);
            return;
        }

        ImGui.TextColored(Palette.InkDim, $"Shown in your local time ({TimeZoneInfo.Local.StandardName}).");
        ImGui.Spacing();
        var now = DateTimeOffset.UtcNow;
        var shown = 0;
        for (var openingIndex = 0; openingIndex < openings.Count && shown < 7; openingIndex++)
        {
            var opening = openings[openingIndex];
            var open = opening.Opens <= now;
            var day = VenueClock.FormatLocal(opening.Opens, "dddd");
            var hours = $"{VenueClock.FormatLocal(opening.Opens, "HH:mm")} - {VenueClock.FormatLocal(opening.Closes, "HH:mm")}";
            ImGui.TextColored(open ? Palette.Mint : Palette.Ink, day);
            ImGui.SameLine(card.InnerWidth * 0.45f);
            ImGui.TextColored(open ? Palette.Mint : Palette.InkMuted, hours);
            if (open)
            {
                ImGui.SameLine();
                Widgets.Chip("OPEN", Palette.Mint, true);
            }

            shown++;
        }
    }
}
