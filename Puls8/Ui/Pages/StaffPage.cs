using System.Text.Json.Nodes;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Puls8.Staff;

namespace Puls8.Ui.Pages;

public enum StaffSection : byte
{
    Events,
    Hours,
    Club,
}

public sealed partial class StaffPage
{
    private static readonly string[] SectionLabels = ["EVENTS", "HOURS", "CLUB"];

    private readonly Plugin plugin;
    private StaffSection section;
    private string keyInput = string.Empty;
    private bool rememberKey = true;

    public StaffPage(Plugin plugin)
    {
        this.plugin = plugin;
    }

    private StaffSession Session => plugin.Staff;

    public void Draw()
    {
        if (!Session.IsUnlocked)
        {
            DrawUnlock();
            return;
        }

        EditorFields.PushStyle();
        DrawPublishBar();
        var draft = Session.Draft;
        if (draft is null)
        {
            Widgets.IconText(FontAwesomeIcon.Spinner, "Loading the live venue info...", Palette.Cyan);
        }
        else
        {
            DrawSectionPicker();
            var changed = section switch
            {
                StaffSection.Events => DrawEvents(draft),
                StaffSection.Hours => DrawHours(draft),
                _ => DrawClub(draft),
            };

            if (changed)
            {
                Session.MarkDirty();
            }
        }

        EditorFields.PopStyle();
    }

    private void DrawUnlock()
    {
        Widgets.SectionTitle("STAFF ACCESS", 111);
        using var card = Widgets.Card(Palette.Magenta);
        Widgets.Wrapped("This area is for Puls8 staff. Paste the staff key Arka gave you; GitHub checks it and only keys allowed to edit the venue repo get in.", Palette.InkMuted);
        ImGui.Spacing();
        EditorFields.PushStyle();
        ImGui.TextColored(Palette.InkDim, "STAFF KEY");
        ImGui.SetNextItemWidth(card.InnerWidth);
        var submitted = ImGui.InputTextWithHint("##staffkey", "github_pat_...", ref keyInput, 256, ImGuiInputTextFlags.Password | ImGuiInputTextFlags.EnterReturnsTrue);
        EditorFields.PopStyle();
        ImGui.Spacing();
        Widgets.Toggle("##rememberkey", "Remember on this PC", "Stored encrypted for your Windows account only.", ref rememberKey);
        ImGui.Spacing();

        var verifying = Session.State == StaffState.Verifying;
        var label = verifying ? "CHECKING WITH GITHUB..." : "UNLOCK";
        if (Widgets.Button("##unlock", label, FontAwesomeIcon.Key, new Vector2(card.InnerWidth, 42f * Widgets.Scale), ButtonTone.Primary, !verifying) || (submitted && !verifying))
        {
            var candidate = keyInput;
            keyInput = string.Empty;
            _ = Session.UnlockAsync(candidate, rememberKey);
        }

        DrawMessage();
    }

    private void DrawPublishBar()
    {
        using var card = Widgets.Card(Session.IsDirty ? Palette.Magenta : Palette.Mint);
        Widgets.Chip($"SIGNED IN AS {Session.Login.ToUpperInvariant()}", Palette.Cyan);
        ImGui.SameLine(0f, 8f * Widgets.Scale);
        if (Session.IsDirty)
        {
            Widgets.Chip("UNPUBLISHED CHANGES", Palette.Magenta, true);
        }
        else
        {
            Widgets.Chip("IN SYNC", Palette.Mint);
        }

        var issues = Session.Issues;
        for (var issueIndex = 0; issueIndex < issues.Count; issueIndex++)
        {
            Widgets.IconText(FontAwesomeIcon.ExclamationTriangle, issues[issueIndex], Palette.Amber);
        }

        ImGui.Spacing();
        var scale = Widgets.Scale;
        var gap = 8f * scale;
        var publishWidth = card.InnerWidth;
        var canPublish = Session.IsDirty && issues.Count == 0 && !Session.IsBusy;
        if (Widgets.Button("##publish", Session.IsBusy ? "WORKING..." : "PUBLISH TO PLAYERS", FontAwesomeIcon.Upload, new Vector2(publishWidth, 42f * scale), ButtonTone.Primary, canPublish))
        {
            _ = Session.PublishAsync();
        }

        ImGui.Spacing();
        var thirdWidth = (card.InnerWidth - gap * 2f) / 3f;
        var previewLabel = Session.IsPreviewing ? "STOP PREVIEW" : "PREVIEW";
        if (Widgets.Button("##preview", previewLabel, FontAwesomeIcon.Eye, new Vector2(thirdWidth, 32f * scale), ButtonTone.Ghost, Session.Draft is not null))
        {
            Session.TogglePreview();
        }

        ImGui.SameLine(0f, gap);
        if (Widgets.Button("##reload", Session.IsDirty ? "DISCARD" : "RELOAD", FontAwesomeIcon.Undo, new Vector2(thirdWidth, 32f * scale), ButtonTone.Ghost, !Session.IsBusy))
        {
            _ = Session.ReloadAsync();
        }

        ImGui.SameLine(0f, gap);
        if (Widgets.Button("##lock", "LOCK", FontAwesomeIcon.Lock, new Vector2(thirdWidth, 32f * scale), ButtonTone.Ghost))
        {
            Session.Lock();
        }

        if (Session.IsPreviewing)
        {
            ImGui.TextColored(Palette.InkDim, "Other tabs now show your draft. Only you can see it until you publish.");
        }

        DrawMessage();
    }

    private void DrawMessage()
    {
        if (Session.Message.Length == 0)
        {
            return;
        }

        ImGui.Spacing();
        Widgets.IconText(Session.MessageIsError ? FontAwesomeIcon.ExclamationCircle : FontAwesomeIcon.InfoCircle, Session.Message, Session.MessageIsError ? Palette.Danger : Palette.InkMuted);
    }

    private void DrawSectionPicker()
    {
        var scale = Widgets.Scale;
        var gap = 6f * scale;
        var width = (ImGui.GetContentRegionAvail().X - gap * (SectionLabels.Length - 1)) / SectionLabels.Length;
        for (var sectionIndex = 0; sectionIndex < SectionLabels.Length; sectionIndex++)
        {
            if (sectionIndex > 0)
            {
                ImGui.SameLine(0f, gap);
            }

            var selected = (int)section == sectionIndex;
            if (Widgets.Button($"##section{sectionIndex}", SectionLabels[sectionIndex], FontAwesomeIcon.None, new Vector2(width, 32f * scale), selected ? ButtonTone.Primary : ButtonTone.Ghost))
            {
                section = (StaffSection)sectionIndex;
            }
        }

        ImGui.Spacing();
        ImGui.Spacing();
    }

    private static bool AddButton(string id, string label)
        => Widgets.Button(id, label, FontAwesomeIcon.Plus, new Vector2(ImGui.GetContentRegionAvail().X, 36f * Widgets.Scale), ButtonTone.Ghost);

    private static float Half(float width) => (width - 8f * Widgets.Scale) * 0.5f;

    private static JsonObject? ObjectAt(JsonArray array, int index) => array[index] as JsonObject;
}
