using System.Reflection;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Puls8.Venue;

namespace Puls8.Ui.Pages;

public sealed class AboutPage
{
    private const string RepositoryUrl = "https://github.com/IttoJinrah/puls8";

    private static readonly string Version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "dev";

    private readonly Plugin plugin;

    public AboutPage(Plugin plugin)
    {
        this.plugin = plugin;
    }

    public void Draw()
    {
        DrawBadge();
        DrawCredits();
        DrawSettings();
        DrawFeed();
    }

    private static void DrawBadge()
    {
        var scale = Widgets.Scale;
        var drawList = ImGui.GetWindowDrawList();
        var width = ImGui.GetContentRegionAvail().X;
        var size = 132f * scale;
        var min = ImGui.GetCursorScreenPos() + new Vector2((width - size) * 0.5f, 8f * scale);
        var logo = Images.Logo;
        if (logo is not null)
        {
            var center = min + new Vector2(size * 0.5f);
            Fx.Halo(drawList, center, size * 0.5f, Palette.Violet, 1f + Motion.Heartbeat());
            drawList.AddImage(logo.Handle, min, min + new Vector2(size));
            Fx.GlitchImage(drawList, logo.Handle, min, min + new Vector2(size), 17, 1f);
        }

        ImGui.Dummy(new Vector2(width, size + 14f * scale));
        var version = $"VERSION {Version}";
        var versionWidth = ImGui.CalcTextSize(version).X;
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + (width - versionWidth) * 0.5f);
        ImGui.TextColored(Palette.InkDim, version);
        ImGui.Spacing();
    }

    private static void DrawCredits()
    {
        Widgets.SectionTitle("MADE BY", 101);
        using var card = Widgets.Card(Palette.Magenta);
        DrawCredit("Arka", "Puls8 venue, packs and the original plugin", Palette.Magenta);
        ImGui.Spacing();
        DrawCredit("XeldarAlz", "Plugin overhaul: design, installer, travel and wifi", Palette.Cyan);
        ImGui.Spacing();
        if (Widgets.Button("##repo", "SOURCE ON GITHUB", FontAwesomeIcon.Code, new Vector2(card.InnerWidth, 34f * Widgets.Scale), ButtonTone.Ghost))
        {
            Links.Open(RepositoryUrl);
        }
    }

    private static void DrawCredit(string name, string role, Vector4 color)
    {
        using (Fonts.Lead())
        {
            var drawList = ImGui.GetWindowDrawList();
            var position = ImGui.GetCursorScreenPos();
            var size = ImGui.CalcTextSize(name);
            Fx.GlowText(drawList, ImGui.GetFont(), ImGui.GetFontSize(), position, name, Palette.Core, color, 2f * Widgets.Scale);
            ImGui.Dummy(size);
        }

        ImGui.TextColored(Palette.InkMuted, role);
    }

    private void DrawSettings()
    {
        var configuration = plugin.Configuration;
        Widgets.SectionTitle("SETTINGS", 102);
        using var card = Widgets.Card(Palette.Violet);
        var reduced = configuration.ReducedMotion;
        if (Widgets.Toggle("##reduced", "Reduced motion", "Freezes glitches, scanline roll and the heartbeat.", ref reduced))
        {
            configuration.ReducedMotion = reduced;
            configuration.Save();
        }

        ImGui.Spacing();
        var notify = configuration.NotifyPackUpdates;
        if (Widgets.Toggle("##notify", "Tell me about pack updates", "A chat line on login when a new venue pack is out.", ref notify))
        {
            configuration.NotifyPackUpdates = notify;
            configuration.Save();
        }
    }

    private void DrawFeed()
    {
        var feed = plugin.Feed;
        Widgets.SectionTitle("VENUE INFO", 103);
        using var card = Widgets.Card(Palette.Electric);
        var source = feed.Source switch
        {
            FeedSource.Live => "Live from the venue",
            FeedSource.Cached => "Saved copy (offline)",
            _ => "Built-in copy",
        };
        Widgets.IconText(feed.Source == FeedSource.Live ? FontAwesomeIcon.Satellite : FontAwesomeIcon.Save, source, feed.Source == FeedSource.Live ? Palette.Mint : Palette.Amber);
        ImGui.TextColored(Palette.InkDim, "Hours, events, staff and syncshells update without a plugin update.");
        ImGui.Spacing();
        var label = feed.IsRefreshing ? "REFRESHING..." : "REFRESH NOW";
        if (Widgets.Button("##refresh", label, FontAwesomeIcon.SyncAlt, new Vector2(card.InnerWidth, 32f * Widgets.Scale), ButtonTone.Ghost, !feed.IsRefreshing))
        {
            _ = feed.RefreshAsync();
        }
    }
}
