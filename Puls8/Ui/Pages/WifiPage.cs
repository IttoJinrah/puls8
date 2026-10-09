using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Puls8.Sync;
using Puls8.Venue;

namespace Puls8.Ui.Pages;

public sealed class WifiPage
{
    private static readonly string[] SyncshellSteps =
    [
        "Open the Syncshells tab in the window that just opened.",
        "Press \"Join existing Syncshell\".",
        "Paste the ID with Ctrl+V. It's already on your clipboard.",
        "Copy the password below, paste it, then press Join and accept the permissions.",
    ];

    private readonly Plugin plugin;
    private string guidedService = string.Empty;
    private string zoneSyncMessage = string.Empty;

    public WifiPage(Plugin plugin)
    {
        this.plugin = plugin;
    }

    public void Draw()
    {
        Widgets.SectionTitle("CLUB WIFI", 71);
        Widgets.Wrapped("Sync plugins let you see everyone's custom looks inside the club. Pick the one you use; you only need one.", Palette.InkMuted);
        ImGui.Spacing();

        var entries = plugin.Feed.Current.Sync;
        for (var entryIndex = 0; entryIndex < entries.Length; entryIndex++)
        {
            var entry = entries[entryIndex];
            if (string.Equals(entry.Kind, "zonesync", StringComparison.OrdinalIgnoreCase))
            {
                DrawZoneSync(entry, entryIndex);
            }
            else
            {
                DrawSyncshell(entry, entryIndex);
            }
        }
    }

    private void DrawSyncshell(SyncEntry entry, int index)
    {
        var service = SyncClients.Parse(entry.Service);
        var installed = SyncClients.IsLoaded(service);
        using var card = Widgets.Card(Palette.Magenta);
        DrawHeader(entry.Service, "Syncshell", installed);
        ImGui.Spacing();
        Widgets.CopyField("SYNCSHELL ID", entry.Id, $"##syncid{index}");
        ImGui.Spacing();
        Widgets.CopyField("PASSWORD", entry.Password, $"##syncpw{index}");
        ImGui.Spacing();

        var buttonSize = new Vector2(card.InnerWidth, 44f * Widgets.Scale);
        if (!installed)
        {
            Widgets.Wrapped($"{entry.Service} isn't installed or is turned off. It comes from its own plugin repository; once it's running this card lights up.", Palette.Amber);
            if (Widgets.Button($"##syncinstall{index}", "OPEN PLUGIN INSTALLER", FontAwesomeIcon.PuzzlePiece, buttonSize, ButtonTone.Ghost))
            {
                Links.OpenPluginInstaller();
            }

            return;
        }

        if (Widgets.Button($"##syncjoin{index}", $"COPY ID & OPEN {entry.Service.ToUpperInvariant()}", FontAwesomeIcon.Wifi, buttonSize))
        {
            ImGui.SetClipboardText(entry.Id);
            SyncClients.OpenWindow(service);
            guidedService = entry.Service;
        }

        if (!string.Equals(guidedService, entry.Service, StringComparison.Ordinal))
        {
            ImGui.TextColored(Palette.InkDim, "One tap copies the ID and opens the join screen for you.");
            return;
        }

        ImGui.Spacing();
        for (var stepIndex = 0; stepIndex < SyncshellSteps.Length; stepIndex++)
        {
            DrawStep(stepIndex + 1, SyncshellSteps[stepIndex]);
        }
    }

    private void DrawZoneSync(SyncEntry entry, int index)
    {
        var installed = SyncClients.IsLoaded(SyncService.PlayerSync);
        using var card = Widgets.Card(Palette.Cyan);
        DrawHeader(entry.Service, "ZoneSync", installed);
        ImGui.Spacing();
        if (entry.Note.Length > 0)
        {
            Widgets.Wrapped(entry.Note, Palette.Ink);
            ImGui.Spacing();
        }

        var buttonSize = new Vector2(card.InnerWidth, 44f * Widgets.Scale);
        if (!installed)
        {
            Widgets.Wrapped($"{entry.Service} isn't installed or is turned off.", Palette.Amber);
            if (Widgets.Button($"##zoneinstall{index}", "OPEN PLUGIN INSTALLER", FontAwesomeIcon.PuzzlePiece, buttonSize, ButtonTone.Ghost))
            {
                Links.OpenPluginInstaller();
            }

            return;
        }

        if (Widgets.Button($"##zoneon{index}", "TURN ON ZONESYNC", FontAwesomeIcon.Bolt, buttonSize))
        {
            zoneSyncMessage = SyncClients.EnableZoneSync()
                ? "Done. PlayerSync confirms it in chat and joins the zone shell in a moment."
                : "PlayerSync didn't answer the command. Use the manual steps below.";
        }

        if (zoneSyncMessage.Length > 0)
        {
            ImGui.Spacing();
            Widgets.IconText(FontAwesomeIcon.CheckCircle, zoneSyncMessage, Palette.Mint);
        }

        ImGui.Spacing();
        ImGui.TextColored(Palette.InkDim, "Prefer doing it by hand? Settings > Pairing Settings > Turn on ZoneSync.");
        if (Widgets.Button($"##zoneopen{index}", "OPEN PLAYERSYNC", FontAwesomeIcon.ExternalLinkAlt, new Vector2(card.InnerWidth, 32f * Widgets.Scale), ButtonTone.Ghost))
        {
            SyncClients.OpenWindow(SyncService.PlayerSync);
        }
    }

    private static void DrawHeader(string service, string kind, bool installed)
    {
        using (Fonts.Label())
        {
            ImGui.TextColored(Palette.Core, service.ToUpperInvariant());
        }

        ImGui.SameLine(0f, 8f * Widgets.Scale);
        ImGui.TextColored(Palette.InkDim, kind);
        ImGui.SameLine(0f, 12f * Widgets.Scale);
        Widgets.Chip(installed ? "READY" : "NOT FOUND", installed ? Palette.Mint : Palette.Amber, installed);
    }

    private static void DrawStep(int number, string text)
    {
        var drawList = ImGui.GetWindowDrawList();
        var size = 22f * Widgets.Scale;
        var min = ImGui.GetCursorScreenPos();
        var center = min + new Vector2(size * 0.5f);
        drawList.AddCircleFilled(center, size * 0.5f, Palette.U32(Palette.Magenta, 0.2f), 20);
        drawList.AddCircle(center, size * 0.5f, Palette.U32(Palette.Magenta), 20, 1.2f);
        var label = number.ToString();
        var labelSize = ImGui.CalcTextSize(label);
        drawList.AddText(center - labelSize * 0.5f, Palette.U32(Palette.Core), label);
        ImGui.Dummy(new Vector2(size));
        ImGui.SameLine(0f, 10f * Widgets.Scale);
        Widgets.Wrapped(text, Palette.Ink);
    }
}
