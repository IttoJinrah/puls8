using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Penumbra.Api.Enums;
using Puls8.Mods;

namespace Puls8.Ui.Pages;

// One status card that only offers a button when something needs doing, a quiet list, and everything else folded away.
public sealed class ModsPage
{
    private static readonly string[] BaseSteps = ["CHECK", "DOWNLOAD", "UNPACK", "SWAP", "REGISTER", "ENABLE"];
    private static readonly string[] MannequinSteps = ["CHECK", "DOWNLOAD", "UNPACK", "SWAP", "REGISTER", "ENABLE", "LINK"];
    private static readonly TimeSpan MannequinScanInterval = TimeSpan.FromSeconds(1);

    private readonly Plugin plugin;
    private DateTime nextMannequinScanUtc;
    private bool advancedOpen;

    public ModsPage(Plugin plugin)
    {
        this.plugin = plugin;
    }

    public void OnShown()
    {
        if (plugin.Installer.IsStale)
        {
            _ = plugin.Installer.CheckAsync(false);
        }
    }

    public void Draw()
    {
        var installer = plugin.Installer;
        Widgets.SectionTitle("VENUE MODS", 81);
        DrawStatus(installer);
        DrawList(installer);
        DrawAdvanced(installer);
    }

    private void DrawStatus(PackInstaller installer)
    {
        var working = FindWorking(installer.Slots);
        var problem = installer.Penumbra.Blocking ?? installer.CatalogProblem;
        var problemSlot = problem is null ? FindProblem(installer.Slots) : null;
        problem ??= problemSlot?.Problem;
        var pending = installer.PendingCount;
        var accent = working is not null ? Palette.Cyan : problem is not null ? Palette.Danger : pending > 0 ? Palette.Magenta : Palette.Mint;

        using var card = Widgets.Card(accent);
        DrawCornerButtons(installer, card.InnerWidth);

        if (working is not null)
        {
            DrawWorking(installer, working, card.InnerWidth);
        }
        else if (problem is not null)
        {
            DrawProblem(problem, problemSlot, card.InnerWidth);
        }
        else if (installer.IsChecking && !installer.Penumbra.Ready)
        {
            Headline("Checking...", Palette.Cyan);
            ImGui.TextColored(Palette.InkDim, "Looking at Penumbra and the latest releases.");
        }
        else if (pending > 0)
        {
            DrawPending(installer, pending, card.InnerWidth);
        }
        else
        {
            Headline("All set", Palette.Mint);
            ImGui.TextColored(Palette.InkDim, "Every venue pack is installed, current and switched on.");
        }

        if (installer.Penumbra.Warning is { } warning && working is null)
        {
            ImGui.Spacing();
            Widgets.IconText(FontAwesomeIcon.ExclamationTriangle, warning.Title, Palette.Amber);
        }

        if (installer.NeedsZoneReload)
        {
            ImGui.Spacing();
            Widgets.IconText(FontAwesomeIcon.DoorOpen, "Step outside and back in to load the new furniture and pool.", Palette.Amber);
        }
    }

    private void DrawCornerButtons(PackInstaller installer, float innerWidth)
    {
        var scale = Widgets.Scale;
        var size = 26f * scale;
        var gap = 6f * scale;
        var origin = ImGui.GetCursorScreenPos();
        ImGui.SetCursorScreenPos(new Vector2(origin.X + innerWidth - size * 2f - gap, origin.Y));
        if (Widgets.IconButton("##modscheck", FontAwesomeIcon.SyncAlt, size, installer.IsChecking ? "Checking..." : "Check for updates", Palette.Cyan)
            && !installer.IsChecking && !installer.IsBusy)
        {
            _ = installer.CheckAsync(false);
        }

        ImGui.SameLine(0f, gap);
        if (Widgets.IconButton("##modsredraw", FontAwesomeIcon.Magic, size, "Redraw everyone now", Palette.Violet) && installer.Penumbra.Ready)
        {
            plugin.Penumbra.RedrawEverything();
        }

        ImGui.SetCursorScreenPos(origin);
    }

    private static void DrawWorking(PackInstaller installer, PackSlot slot, float width)
    {
        var progress = slot.Progress;
        Headline(progress.ReachedStage <= InstallStage.Checking ? $"Preparing {slot.Definition.Title}" : $"Installing {slot.Definition.Title}", Palette.Cyan);
        ImGui.Spacing();
        Widgets.Steps(slot.Definition.TargetsMannequin ? MannequinSteps : BaseSteps, StepIndex(progress.ReachedStage), false);
        if (progress.Stage is InstallStage.Downloading or InstallStage.Unpacking)
        {
            var caption = progress.Stage == InstallStage.Downloading
                ? $"{Format.Bytes(progress.BytesDone)} of {Format.Bytes(progress.BytesTotal)}"
                : "Unpacking files...";
            Widgets.Progress(progress.Fraction, width, caption);
        }

        ImGui.Spacing();
        if (Widgets.Button("##cancelinstall", "CANCEL", FontAwesomeIcon.Stop, new Vector2(width, 28f * Widgets.Scale), ButtonTone.Ghost))
        {
            installer.Cancel();
        }
    }

    private void DrawPending(PackInstaller installer, int pending, float width)
    {
        var slots = installer.Slots;
        var setupOnly = true;
        long downloadBytes = 0;
        for (var slotIndex = 0; slotIndex < slots.Length; slotIndex++)
        {
            var slot = slots[slotIndex];
            if (!slot.NeedsInstall)
            {
                continue;
            }

            setupOnly = false;
            downloadBytes += slot.Latest?.Size ?? 0;
        }

        var firstName = FindAttention(slots)?.Definition.Title ?? "A pack";
        var title = setupOnly
            ? pending == 1 ? $"{firstName} needs setup" : $"{pending} packs need setup"
            : pending == 1 ? $"{firstName} is ready to install" : $"{pending} packs are ready to install";
        Headline(title, Palette.Core);
        ImGui.TextColored(Palette.InkDim, setupOnly ? "No download needed, it only takes a moment." : $"About {Format.Bytes(downloadBytes)} to download.");
        ImGui.Spacing();
        var label = setupOnly ? "FINISH SETUP" : "INSTALL";
        using (Fonts.Label())
        {
            if (Widgets.Button("##installall", label, setupOnly ? FontAwesomeIcon.Check : FontAwesomeIcon.Download, new Vector2(width, 44f * Widgets.Scale), ButtonTone.Primary, installer.Penumbra.Ready && !installer.IsBusy))
            {
                installer.InstallAll();
            }
        }
    }

    private void DrawProblem(Problem problem, PackSlot? slot, float width)
    {
        Headline(problem.Title, problem.Severity == ProblemSeverity.Warning ? Palette.Amber : Palette.Danger);
        Widgets.Wrapped(problem.Detail, Palette.InkMuted);
        if (problem.Fix == FixAction.None)
        {
            return;
        }

        ImGui.Spacing();
        if (Widgets.Button("##fix", problem.FixLabel.ToUpperInvariant(), FixIcon(problem.Fix), new Vector2(width, 40f * Widgets.Scale)))
        {
            RunFix(problem.Fix, slot);
        }

        if (slot is null || problem.Fix is not (FixAction.OpenPenumbraCollections or FixAction.OpenPenumbraSettings))
        {
            return;
        }

        ImGui.Spacing();
        if (Widgets.Button("##continue", "DONE, CONTINUE", FontAwesomeIcon.Play, new Vector2(width, 30f * Widgets.Scale), ButtonTone.Ghost))
        {
            plugin.Installer.InstallAll();
        }
    }

    private void DrawList(PackInstaller installer)
    {
        var slots = installer.Slots;
        using var card = Widgets.Card(Palette.Violet);
        for (var slotIndex = 0; slotIndex < slots.Length; slotIndex++)
        {
            var slot = slots[slotIndex];
            var (status, color) = Describe(slot);
            var version = slot.Record?.Tag ?? slot.Latest?.Tag ?? string.Empty;
            if (Row($"##pack{slotIndex}", slot.Definition.Title, status, color, version, card.InnerWidth))
            {
                ImGui.SetTooltip(PackTooltip(slot));
            }
        }

        var mannequinPack = FindMannequinPack(slots);
        if (mannequinPack is not null)
        {
            DrawMannequinRow(installer, mannequinPack, card.InnerWidth);
        }
    }

    private void DrawMannequinRow(PackInstaller installer, PackSlot mannequinPack, float width)
    {
        var collection = plugin.Configuration.MannequinCollectionId;
        string status;
        Vector4 color;
        var unlinked = 0;
        if (collection == Guid.Empty || !mannequinPack.Enabled)
        {
            (status, color) = ("Waits for the Cityscape", Palette.InkDim);
        }
        else if (!plugin.Travel.IsInsideVenue)
        {
            (status, color) = ("Links when you're in the club", Palette.InkDim);
        }
        else
        {
            unlinked = CountUnlinked(installer, collection);
            (status, color) = unlinked == 0 ? ("Linked", Palette.Mint) : ("Not linked", Palette.Amber);
        }

        if (Row("##mannequinrow", "Mannequin", status, color, string.Empty, width))
        {
            ImGui.SetTooltip($"The {mannequinPack.Definition.Title} is shown on the club's mannequin through your \"{mannequinPack.Definition.Collection}\" collection.");
        }

        if (unlinked == 0)
        {
            return;
        }

        ImGui.Spacing();
        if (Widgets.Button("##linknow", "LINK NOW", FontAwesomeIcon.Link, new Vector2(width, 30f * Widgets.Scale), ButtonTone.Ghost))
        {
            plugin.Mannequins.LinkAll(plugin.Penumbra, collection);
        }
    }

    private int CountUnlinked(PackInstaller installer, Guid collection)
    {
        var now = DateTime.UtcNow;
        if (now >= nextMannequinScanUtc && installer.Penumbra.Ready)
        {
            nextMannequinScanUtc = now + MannequinScanInterval;
            plugin.Mannequins.Scan(plugin.Penumbra, collection);
        }

        var spots = plugin.Mannequins.Spots;
        var unlinked = 0;
        for (var spotIndex = 0; spotIndex < spots.Count; spotIndex++)
        {
            if (!spots[spotIndex].Linked)
            {
                unlinked++;
            }
        }

        return unlinked;
    }

    private void DrawAdvanced(PackInstaller installer)
    {
        var scale = Widgets.Scale;
        if (Widgets.Button("##advanced", advancedOpen ? "HIDE ADVANCED" : "ADVANCED", advancedOpen ? FontAwesomeIcon.ChevronUp : FontAwesomeIcon.ChevronDown,
                new Vector2(ImGui.GetContentRegionAvail().X, 30f * scale), ButtonTone.Ghost))
        {
            advancedOpen = !advancedOpen;
        }

        if (!advancedOpen)
        {
            return;
        }

        ImGui.Spacing();
        using var card = Widgets.Card(Palette.Electric);
        ImGui.TextColored(Palette.InkDim, "Reinstall downloads a fresh copy, useful if a pack looks broken.");
        ImGui.Spacing();
        var slots = installer.Slots;
        var enabled = installer.Penumbra.Ready && !installer.IsBusy;
        for (var slotIndex = 0; slotIndex < slots.Length; slotIndex++)
        {
            if (Widgets.Button($"##reinstall{slotIndex}", $"REINSTALL {slots[slotIndex].Definition.Title.ToUpperInvariant()}", FontAwesomeIcon.Download,
                    new Vector2(card.InnerWidth, 30f * scale), ButtonTone.Ghost, enabled))
            {
                installer.Install(slots[slotIndex]);
            }

            ImGui.Spacing();
        }

        if (Widgets.Button("##openpenumbra", "OPEN PENUMBRA", FontAwesomeIcon.ExternalLinkAlt, new Vector2(card.InnerWidth, 30f * scale), ButtonTone.Ghost))
        {
            plugin.Penumbra.Open(TabType.Mods);
        }

        if (installer.Penumbra.Ready)
        {
            ImGui.Spacing();
            ImGui.TextColored(Palette.InkDim, $"Mod folder: {installer.Penumbra.ModDirectory}");
        }
    }

    // Returns true while the row is hovered so the caller can attach a tooltip.
    private static bool Row(string id, string title, string status, Vector4 color, string trailing, float width)
    {
        var drawList = ImGui.GetWindowDrawList();
        var scale = Widgets.Scale;
        var height = ImGui.GetTextLineHeight() + 14f * scale;
        var min = ImGui.GetCursorScreenPos();
        var itemId = ImGui.GetID(id);
        ImGui.InvisibleButton(id, new Vector2(width, height));
        var hovered = ImGui.IsItemHovered();
        var hover = Widgets.Hover(itemId, hovered ? 1f : 0f);
        var max = min + new Vector2(width, height);
        if (hover > 0.01f)
        {
            drawList.AddRectFilled(min, max, Palette.U32(Palette.PanelHover, 0.6f * hover), 8f * scale);
        }

        var midY = (min.Y + max.Y) * 0.5f;
        var textY = midY - ImGui.GetTextLineHeight() * 0.5f;
        drawList.AddCircleFilled(new Vector2(min.X + 10f * scale, midY), 4f * scale, Palette.U32(color), 12);
        var titleX = min.X + 22f * scale;
        drawList.AddText(new Vector2(titleX, textY), Palette.U32(Palette.Core), title);
        var statusX = titleX + ImGui.CalcTextSize(title).X + 10f * scale;
        drawList.AddText(new Vector2(statusX, textY), Palette.U32(color), status);
        if (trailing.Length > 0)
        {
            var trailingWidth = ImGui.CalcTextSize(trailing).X;
            drawList.AddText(new Vector2(max.X - trailingWidth - 8f * scale, textY), Palette.U32(Palette.InkDim), trailing);
        }

        return hovered;
    }

    private static (string Status, Vector4 Color) Describe(PackSlot slot)
    {
        if (slot.IsWorking)
        {
            return ("Working...", Palette.Cyan);
        }

        if (slot.Problem is not null)
        {
            return ("Needs attention", Palette.Danger);
        }

        if (!slot.InPenumbra)
        {
            return ("Not installed", Palette.Amber);
        }

        if (slot.NeedsInstall)
        {
            return ("Update ready", Palette.Magenta);
        }

        return slot.Enabled ? ("Ready", Palette.Mint) : ("Needs setup", Palette.Amber);
    }

    private static string PackTooltip(PackSlot slot)
    {
        var installed = slot.Record is { } record && slot.InPenumbra ? $"{record.Tag}, {record.InstalledUtc.ToLocalTime():d MMM}" : slot.InPenumbra ? "unknown version" : "not installed";
        var latest = slot.Latest is { } release ? $"{release.Tag}, {Format.Bytes(release.Size)}" : "checking";
        return $"{slot.Definition.Blurb}\n\nInstalled: {installed}\nLatest: {latest}";
    }

    private static void Headline(string text, Vector4 color)
    {
        using (Fonts.Lead())
        {
            ImGui.TextColored(color, text);
        }
    }

    private static int StepIndex(InstallStage stage) => stage switch
    {
        InstallStage.Downloading => 1,
        InstallStage.Unpacking => 2,
        InstallStage.Swapping => 3,
        InstallStage.Registering => 4,
        InstallStage.Enabling => 5,
        InstallStage.Linking => 6,
        _ => 0,
    };

    private void RunFix(FixAction fix, PackSlot? slot)
    {
        switch (fix)
        {
            case FixAction.Retry when slot is not null:
                plugin.Installer.InstallAll();
                break;
            case FixAction.Retry:
                _ = plugin.Installer.CheckAsync(false);
                break;
            case FixAction.OpenPluginInstaller:
                Links.OpenPluginInstaller();
                break;
            case FixAction.OpenPenumbraSettings:
                plugin.Penumbra.Open(TabType.Settings);
                break;
            case FixAction.OpenPenumbraCollections:
                plugin.Penumbra.Open(TabType.Collections);
                break;
            case FixAction.OpenPenumbraMods:
                plugin.Penumbra.Open(TabType.Mods);
                break;
        }
    }

    private static FontAwesomeIcon FixIcon(FixAction fix) => fix switch
    {
        FixAction.Retry => FontAwesomeIcon.Redo,
        FixAction.OpenPluginInstaller => FontAwesomeIcon.PuzzlePiece,
        _ => FontAwesomeIcon.ExternalLinkAlt,
    };

    private static PackSlot? FindWorking(PackSlot[] slots) => Find(slots, static slot => slot.IsWorking);

    private static PackSlot? FindProblem(PackSlot[] slots) => Find(slots, static slot => slot.Problem is not null);

    private static PackSlot? FindAttention(PackSlot[] slots) => Find(slots, static slot => slot.NeedsAttention);

    private static PackSlot? FindMannequinPack(PackSlot[] slots) => Find(slots, static slot => slot.Definition.TargetsMannequin);

    private static PackSlot? Find(PackSlot[] slots, Func<PackSlot, bool> predicate)
    {
        for (var slotIndex = 0; slotIndex < slots.Length; slotIndex++)
        {
            if (predicate(slots[slotIndex]))
            {
                return slots[slotIndex];
            }
        }

        return null;
    }
}
