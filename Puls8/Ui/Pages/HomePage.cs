using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Puls8.Travel;
using Puls8.Venue;

namespace Puls8.Ui.Pages;

public sealed class HomePage
{
    private const float HeroAspect = 0.6f;
    private const float HeroMaxHeight = 320f;
    private const float HorizonFraction = 0.68f;

    private readonly Plugin plugin;
    private readonly Action<Page> navigate;

    public HomePage(Plugin plugin, Action<Page> navigate)
    {
        this.plugin = plugin;
        this.navigate = navigate;
    }

    public void Draw()
    {
        var profile = plugin.Feed.Current;
        DrawHero();
        DrawTitle(profile);
        ImGui.Spacing();
        DrawTravel(profile);
        DrawPulse();
        DrawTiles(profile);
    }

    private static void DrawHero()
    {
        var scale = Widgets.Scale;
        var drawList = ImGui.GetWindowDrawList();
        var width = ImGui.GetContentRegionAvail().X;
        var height = MathF.Min(width * HeroAspect, HeroMaxHeight * scale);
        var min = ImGui.GetCursorScreenPos();
        var max = min + new Vector2(width, height);
        var rounding = 16f * scale;
        var horizon = min.Y + height * HorizonFraction;

        Fx.VerticalGradient(drawList, min, new Vector2(max.X, horizon), Palette.Navy, Palette.Mix(Palette.Navy, Palette.Violet, 0.75f), rounding, ImDrawFlags.RoundCornersTop);
        drawList.AddRectFilled(new Vector2(min.X, horizon), max, Palette.U32(Palette.Void), rounding, ImDrawFlags.RoundCornersBottom);
        drawList.PushClipRect(min, max, true);
        Fx.Stars(drawList, min, new Vector2(max.X, horizon - 10f * scale), 11);
        Fx.Grid(drawList, new Vector2(min.X, horizon), max, horizon, 1f);

        var logo = Images.Logo;
        if (logo is not null)
        {
            var beat = Motion.Heartbeat();
            var logoSize = height * 0.8f * (1f + beat * 0.025f);
            var center = new Vector2((min.X + max.X) * 0.5f, min.Y + height * 0.46f);
            Fx.Halo(drawList, center, logoSize * 0.5f, Palette.Magenta, 1f + beat * 1.5f);
            var logoMin = center - new Vector2(logoSize * 0.5f);
            var logoMax = center + new Vector2(logoSize * 0.5f);
            drawList.AddImage(logo.Handle, logoMin, logoMax);
            Fx.GlitchImage(drawList, logo.Handle, logoMin, logoMax, 5, 1f);
        }

        var ecgHeight = 34f * scale;
        Fx.Ecg(drawList, new Vector2(min.X, horizon - ecgHeight * 0.5f), new Vector2(max.X, horizon + ecgHeight * 0.5f), Palette.Cyan);
        Fx.Scanlines(drawList, min, max, 0.16f);
        drawList.PopClipRect();
        Fx.GradientBorder(drawList, min, max, rounding, 1.4f, 0.8f);
        ImGui.Dummy(new Vector2(width, height));
    }

    private static void DrawTitle(VenueProfile profile)
    {
        var scale = Widgets.Scale;
        ImGui.Dummy(new Vector2(0f, 6f * scale));
        var width = ImGui.GetContentRegionAvail().X;
        using (Fonts.Hero())
        {
            var drawList = ImGui.GetWindowDrawList();
            var font = ImGui.GetFont();
            var size = ImGui.GetFontSize();
            var word = profile.Name.ToUpperInvariant();
            var wordSize = ImGui.CalcTextSize(word);
            var position = ImGui.GetCursorScreenPos() + new Vector2((width - wordSize.X) * 0.5f, 0f);
            Fx.GlowText(drawList, font, size, position, word, Palette.Core, Palette.Magenta, 3f * scale);
            Fx.GradientText(drawList, font, size, position, word, wordSize.X, Motion.Phase(5000.0));
            Fx.GlitchText(drawList, font, size, position, wordSize, word, 9, 1.2f);
            ImGui.Dummy(new Vector2(width, wordSize.Y));
        }

        if (profile.Tagline.Length > 0)
        {
            using (Fonts.Lead())
            {
                var tagline = profile.Tagline.ToUpperInvariant();
                var taglineWidth = ImGui.CalcTextSize(tagline).X;
                ImGui.SetCursorPosX(ImGui.GetCursorPosX() + MathF.Max(0f, (width - taglineWidth) * 0.5f));
                ImGui.TextColored(Palette.InkMuted, tagline);
            }
        }

        ImGui.Dummy(new Vector2(0f, 4f * scale));
        Widgets.Wrapped(profile.Description, Palette.Ink);
        ImGui.Dummy(new Vector2(0f, 8f * scale));
    }

    private void DrawTravel(VenueProfile profile)
    {
        var travel = plugin.Travel;
        var scale = Widgets.Scale;
        using var card = Widgets.Card(Palette.Magenta);
        var buttonSize = new Vector2(card.InnerWidth, 52f * scale);
        if (travel.IsInsideVenue)
        {
            Widgets.Button("##travel", "YOU'RE IN THE CLUB", FontAwesomeIcon.Music, buttonSize, ButtonTone.Ghost, false);
        }
        else if (travel.IsActive)
        {
            if (Widgets.Button("##travelcancel", "TRAVELLING... TAP TO CANCEL", FontAwesomeIcon.Route, buttonSize, ButtonTone.Ghost))
            {
                travel.Cancel();
            }
        }
        else if (!travel.LifestreamAvailable)
        {
            if (Widgets.Button("##getlifestream", "GET LIFESTREAM TO TRAVEL", FontAwesomeIcon.Download, buttonSize, ButtonTone.Ghost))
            {
                Links.OpenPluginInstaller();
            }

            Widgets.Wrapped("Tap-to-travel uses the Lifestream plugin. Install it from the plugin installer, then come back.", Palette.InkDim);
        }
        else
        {
            using (Fonts.Label())
            {
                if (Widgets.Button("##travel", "TAP TO TRAVEL", FontAwesomeIcon.MapMarkerAlt, buttonSize))
                {
                    travel.Go(profile.Address);
                }
            }
        }

        if (travel.Message.Length > 0)
        {
            var tone = travel.State switch
            {
                TravelState.Arrived => Palette.Mint,
                TravelState.Failed or TravelState.Stopped => Palette.Amber,
                _ => Palette.Cyan,
            };
            ImGui.Spacing();
            Widgets.IconText(travel.IsActive ? FontAwesomeIcon.Spinner : FontAwesomeIcon.InfoCircle, travel.Message, tone);
            if (travel.IsActive)
            {
                ImGui.SameLine();
                ImGui.TextColored(Palette.InkDim, $"{(int)(DateTime.UtcNow - travel.StartedUtc).TotalSeconds}s");
            }
        }

        ImGui.Spacing();
        Widgets.CopyField("ADDRESS", profile.Address.Display, "##address");
        ImGui.TextColored(Palette.InkDim, $"Data Center: {profile.Address.DataCenter}. Lifestream handles world and data center travel for you.");
    }

    private void DrawPulse()
    {
        var clock = plugin.Clock;
        var status = clock.Status;
        using var card = Widgets.Card(status.IsOpen ? Palette.Mint : Palette.Violet);
        if (clock.LiveEvent is { } live)
        {
            Widgets.Chip("LIVE NOW", Palette.Magenta, true);
            using (Fonts.Lead())
            {
                ImGui.TextColored(Palette.Core, live.Title);
            }

            ImGui.TextColored(Palette.InkMuted, $"{live.Host} · until {VenueClock.FormatLocal(live.Ends, "HH:mm")}");
        }
        else if (status.IsOpen)
        {
            Widgets.Chip("DOORS OPEN", Palette.Mint, true);
            ImGui.TextColored(Palette.InkMuted, $"Open until {VenueClock.FormatLocal(status.Boundary, "HH:mm")} your time");
        }
        else if (status.HasSchedule)
        {
            Widgets.Chip("CLOSED", Palette.Amber);
            using (Fonts.Lead())
            {
                ImGui.TextColored(Palette.Core, $"Opens in {VenueClock.FormatCountdown(status.Boundary - DateTimeOffset.UtcNow)}");
            }

            ImGui.TextColored(Palette.InkMuted, $"{VenueClock.FormatLocal(status.Boundary, "dddd HH:mm")} your time");
        }

        var upcoming = clock.UpcomingEvents;
        for (var eventIndex = 0; eventIndex < upcoming.Count; eventIndex++)
        {
            var next = upcoming[eventIndex];
            if (ReferenceEquals(next, clock.LiveEvent))
            {
                continue;
            }

            ImGui.Spacing();
            ImGui.TextColored(Palette.InkDim, "NEXT UP");
            ImGui.TextColored(Palette.Ink, $"{next.Title}  ·  {VenueClock.FormatLocal(next.Starts, "ddd d MMM, HH:mm")}");
            break;
        }

        ImGui.Spacing();
        if (Widgets.Button("##events", "SCHEDULE & EVENTS", FontAwesomeIcon.CalendarAlt, new Vector2(card.InnerWidth, 34f * Widgets.Scale), ButtonTone.Ghost))
        {
            navigate(Page.Events);
        }
    }

    private void DrawTiles(VenueProfile profile)
    {
        var scale = Widgets.Scale;
        var width = ImGui.GetContentRegionAvail().X;
        var gap = 10f * scale;
        var tileWidth = (width - gap * 2f) / 3f;
        var size = new Vector2(tileWidth, 78f * scale);
        if (Tile("##tilewifi", FontAwesomeIcon.Wifi, "WIFI", "Syncshells", size, false))
        {
            navigate(Page.Wifi);
        }

        ImGui.SameLine(0f, gap);
        var pending = plugin.Installer.PendingCount;
        if (Tile("##tilemods", FontAwesomeIcon.Cubes, "MODS", pending > 0 ? $"{pending} to install" : "Up to date", size, pending > 0))
        {
            navigate(Page.Mods);
        }

        ImGui.SameLine(0f, gap);
        var discord = FindLink(profile, "discord");
        if (Tile("##tilediscord", FontAwesomeIcon.CommentDots, "DISCORD", "Join the crew", size, false) && discord is not null)
        {
            Links.Open(discord.Url);
        }
    }

    private static VenueLink? FindLink(VenueProfile profile, string kind)
    {
        var links = profile.Links;
        for (var linkIndex = 0; linkIndex < links.Length; linkIndex++)
        {
            if (string.Equals(links[linkIndex].Kind, kind, StringComparison.OrdinalIgnoreCase))
            {
                return links[linkIndex];
            }
        }

        return null;
    }

    private static bool Tile(string id, FontAwesomeIcon icon, string label, string caption, Vector2 size, bool badge)
    {
        var drawList = ImGui.GetWindowDrawList();
        var min = ImGui.GetCursorScreenPos();
        var max = min + size;
        var itemId = ImGui.GetID(id);
        var clicked = ImGui.InvisibleButton(id, size);
        var hovered = ImGui.IsItemHovered();
        if (hovered)
        {
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
        }

        var hover = Widgets.Hover(itemId, hovered ? 1f : 0f);
        var rounding = 12f * Widgets.Scale;
        var lift = new Vector2(0f, -2f * hover * Widgets.Scale);
        Fx.GlowRect(drawList, min + lift, max + lift, Palette.Violet, rounding, hover);
        drawList.AddRectFilled(min + lift, max + lift, Palette.U32(Palette.Mix(Palette.Panel, Palette.PanelHover, hover), 0.92f), rounding);
        Fx.GradientBorder(drawList, min + lift, max + lift, rounding, 1.2f, 0.45f + hover * 0.5f);

        var center = (min + max) * 0.5f + lift;
        using (Fonts.IconLarge())
        {
            var glyph = icon.ToIconString();
            var glyphSize = ImGui.CalcTextSize(glyph);
            var glyphPosition = new Vector2(center.X - glyphSize.X * 0.5f, min.Y + lift.Y + 10f * Widgets.Scale);
            Fx.GradientText(drawList, ImGui.GetFont(), ImGui.GetFontSize(), glyphPosition, glyph, glyphSize.X, Motion.Phase(4000.0));
        }

        using (Fonts.Label())
        {
            var labelSize = ImGui.CalcTextSize(label);
            drawList.AddText(new Vector2(center.X - labelSize.X * 0.5f, max.Y + lift.Y - labelSize.Y - 20f * Widgets.Scale), Palette.U32(Palette.Core), label);
        }

        var captionSize = ImGui.CalcTextSize(caption);
        drawList.AddText(new Vector2(center.X - captionSize.X * 0.5f, max.Y + lift.Y - captionSize.Y - 5f * Widgets.Scale), Palette.U32(badge ? Palette.Magenta : Palette.InkDim), caption);
        if (badge)
        {
            var dot = new Vector2(max.X - 12f * Widgets.Scale, min.Y + 12f * Widgets.Scale) + lift;
            drawList.AddCircleFilled(dot, (5f + Motion.Heartbeat() * 3f) * Widgets.Scale, Palette.U32(Palette.Magenta, 0.3f), 12);
            drawList.AddCircleFilled(dot, 4f * Widgets.Scale, Palette.U32(Palette.Magenta), 12);
        }

        return clicked;
    }
}
