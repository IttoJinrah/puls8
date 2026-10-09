using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Penumbra.Api.Enums;
using Puls8.Mods;
using Puls8.Venue;

namespace Puls8.Ui.Pages;

public sealed class ModsPage
{
    private static readonly string[] BaseSteps = ["CHECK", "DOWNLOAD", "UNPACK", "SWAP", "REGISTER", "ENABLE"];
    private static readonly string[] MannequinSteps = ["CHECK", "DOWNLOAD", "UNPACK", "SWAP", "REGISTER", "ENABLE", "LINK"];
    private static readonly TimeSpan MannequinScanInterval = TimeSpan.FromSeconds(1);

    private readonly Plugin plugin;
    private DateTime nextMannequinScanUtc;

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
        Widgets.Wrapped("The club's skyline, pool and decor are Penumbra mods. Install them once and Puls8 keeps them current; every step is checked, so if something goes wrong you'll see exactly what and how to fix it.", Palette.InkMuted);
        ImGui.Spacing();

        DrawPenumbraStatus(installer);
        DrawPrimaryAction(installer);
        DrawApply(installer);
        var slots = installer.Slots;
        for (var slotIndex = 0; slotIndex < slots.Length; slotIndex++)
        {
            DrawPack(installer, slots[slotIndex], slotIndex);
        }

        DrawMannequins(installer);
    }

    private void DrawPenumbraStatus(PackInstaller installer)
    {
        var state = installer.Penumbra;
        if (installer.IsChecking && !state.Ready)
        {
            using var checking = Widgets.Card(Palette.Violet);
            Widgets.IconText(FontAwesomeIcon.Spinner, "Checking Penumbra and the latest releases...", Palette.Cyan);
            return;
        }

        if (state.Blocking is not null)
        {
            DrawProblem(state.Blocking, null);
            return;
        }

        if (installer.CatalogProblem is not null)
        {
            DrawProblem(installer.CatalogProblem, null);
        }

        if (state.Warning is not null)
        {
            DrawProblem(state.Warning, null);
        }

        if (!state.Ready)
        {
            return;
        }

        using var card = Widgets.Card(Palette.Mint);
        Widgets.IconText(FontAwesomeIcon.CheckCircle, "Penumbra is ready", Palette.Mint);
        ImGui.TextColored(Palette.InkDim, $"Mod folder: {state.ModDirectory}");
    }

    private void DrawPrimaryAction(PackInstaller installer)
    {
        var scale = Widgets.Scale;
        var width = ImGui.GetContentRegionAvail().X;
        var pending = installer.PendingCount;
        var size = new Vector2(width, 50f * scale);
        using (Fonts.Label())
        {
            if (installer.IsBusy)
            {
                if (Widgets.Button("##cancelall", "INSTALLING... TAP TO CANCEL", FontAwesomeIcon.Stop, size, ButtonTone.Ghost))
                {
                    installer.Cancel();
                }
            }
            else if (pending > 0)
            {
                var ready = installer.Penumbra.Ready;
                var label = pending == 1 ? "INSTALL 1 PACK" : $"INSTALL {pending} PACKS";
                if (Widgets.Button("##installall", label, FontAwesomeIcon.Download, size, ButtonTone.Primary, ready))
                {
                    installer.InstallAll();
                }
            }
            else
            {
                Widgets.Button("##uptodate", "EVERYTHING IS UP TO DATE", FontAwesomeIcon.CheckDouble, size, ButtonTone.Ghost, false);
            }
        }

        ImGui.Spacing();
        var checkLabel = installer.IsChecking ? "CHECKING..." : "CHECK FOR UPDATES";
        if (Widgets.Button("##check", checkLabel, FontAwesomeIcon.SyncAlt, new Vector2(width, 30f * scale), ButtonTone.Ghost, !installer.IsChecking && !installer.IsBusy))
        {
            _ = installer.CheckAsync(false);
        }

        ImGui.Spacing();
    }

    private void DrawApply(PackInstaller installer)
    {
        var configuration = plugin.Configuration;
        using var card = Widgets.Card(Palette.Cyan);
        var redraw = configuration.RedrawAfterInstall;
        if (Widgets.Toggle("##redrawafter", "Refresh everything after installing", "Packs are switched on automatically; this also redraws everyone so changes show without a relog.", ref redraw))
        {
            configuration.RedrawAfterInstall = redraw;
            configuration.Save();
        }

        if (installer.NeedsZoneReload)
        {
            ImGui.Spacing();
            Widgets.IconText(FontAwesomeIcon.DoorOpen, "Step outside and back in to load the new furniture and pool.", Palette.Amber);
        }

        ImGui.Spacing();
        if (Widgets.Button("##redrawnow", "REDRAW NOW", FontAwesomeIcon.SyncAlt, new Vector2(card.InnerWidth, 30f * Widgets.Scale), ButtonTone.Ghost, installer.Penumbra.Ready && !installer.IsBusy))
        {
            plugin.Penumbra.RedrawEverything();
        }

        ImGui.TextColored(Palette.InkDim, "Redraw refreshes characters and the mannequin. Furniture reloads when you re-enter the house.");
    }

    private void DrawPack(PackInstaller installer, PackSlot slot, int index)
    {
        var definition = slot.Definition;
        var progress = slot.Progress;
        var accent = slot.Problem is not null ? Palette.Danger : slot.NeedsInstall ? Palette.Magenta : Palette.Mint;
        using var card = Widgets.Card(accent);
        using (Fonts.Label())
        {
            ImGui.TextColored(Palette.Core, definition.Title.ToUpperInvariant());
        }

        ImGui.SameLine(0f, 10f * Widgets.Scale);
        DrawPackChip(slot);
        Widgets.Wrapped(definition.Blurb, Palette.InkMuted);
        ImGui.Spacing();
        DrawVersions(slot);

        var steps = definition.TargetsMannequin ? MannequinSteps : BaseSteps;
        if (slot.IsWorking || progress.Stage is InstallStage.Done or InstallStage.Failed)
        {
            ImGui.Spacing();
            var active = progress.Stage == InstallStage.Done ? steps.Length : StepIndex(progress.ReachedStage);
            Widgets.Steps(steps, active, progress.Stage == InstallStage.Failed);
        }

        if (progress.Stage is InstallStage.Downloading or InstallStage.Unpacking)
        {
            var caption = progress.Stage == InstallStage.Downloading
                ? $"{Format.Bytes(progress.BytesDone)} of {Format.Bytes(progress.BytesTotal)}"
                : "Unpacking files...";
            Widgets.Progress(progress.Fraction, card.InnerWidth, caption);
        }

        if (slot.Problem is not null)
        {
            ImGui.Spacing();
            DrawProblemBody(slot.Problem, slot, card.InnerWidth);
            return;
        }

        if (progress.Stage == InstallStage.Done)
        {
            Widgets.IconText(FontAwesomeIcon.CheckCircle, "Installed, enabled and verified.", Palette.Mint);
        }

        if (slot.IsWorking || installer.IsBusy)
        {
            return;
        }

        ImGui.Spacing();
        var label = !slot.InPenumbra ? "INSTALL" : slot.NeedsInstall ? "UPDATE" : "REINSTALL";
        var tone = slot.NeedsInstall ? ButtonTone.Primary : ButtonTone.Ghost;
        if (Widgets.Button($"##pack{index}", label, FontAwesomeIcon.Download, new Vector2(card.InnerWidth, 34f * Widgets.Scale), tone, installer.Penumbra.Ready))
        {
            installer.Install(slot);
        }
    }

    private static void DrawPackChip(PackSlot slot)
    {
        if (slot.IsWorking)
        {
            Widgets.Chip("WORKING", Palette.Cyan, true);
        }
        else if (slot.Problem is not null)
        {
            Widgets.Chip("NEEDS ATTENTION", Palette.Danger);
        }
        else if (!slot.InPenumbra)
        {
            Widgets.Chip("NOT INSTALLED", Palette.Amber);
        }
        else if (slot.NeedsInstall)
        {
            Widgets.Chip("UPDATE READY", Palette.Magenta, true);
        }
        else if (!slot.Enabled)
        {
            Widgets.Chip("INSTALLED, OFF", Palette.Amber);
        }
        else
        {
            Widgets.Chip("UP TO DATE", Palette.Mint);
        }
    }

    private static void DrawVersions(PackSlot slot)
    {
        var installed = slot.Record is { } record && slot.InPenumbra
            ? $"{record.Tag} ({record.InstalledUtc.ToLocalTime():d MMM})"
            : slot.InPenumbra ? "unknown version" : "none";
        var latest = slot.Latest is { } release ? $"{release.Tag} · {Format.Bytes(release.Size)}" : "checking...";
        ImGui.TextColored(Palette.InkDim, "Installed");
        ImGui.SameLine(90f * Widgets.Scale);
        ImGui.TextColored(Palette.Ink, installed);
        ImGui.TextColored(Palette.InkDim, "Latest");
        ImGui.SameLine(90f * Widgets.Scale);
        ImGui.TextColored(Palette.Ink, latest);
    }

    private static int StepIndex(InstallStage stage) => stage switch
    {
        InstallStage.Checking => 0,
        InstallStage.Downloading => 1,
        InstallStage.Unpacking => 2,
        InstallStage.Swapping => 3,
        InstallStage.Registering => 4,
        InstallStage.Enabling => 5,
        InstallStage.Linking => 6,
        _ => 0,
    };

    private void DrawProblem(Problem problem, PackSlot? slot)
    {
        using var card = Widgets.Card(problem.Severity == ProblemSeverity.Warning ? Palette.Amber : Palette.Danger);
        DrawProblemBody(problem, slot, card.InnerWidth);
    }

    private void DrawProblemBody(Problem problem, PackSlot? slot, float width)
    {
        var warning = problem.Severity == ProblemSeverity.Warning;
        var color = warning ? Palette.Amber : Palette.Danger;
        Widgets.IconText(warning ? FontAwesomeIcon.ExclamationTriangle : FontAwesomeIcon.ExclamationCircle, problem.Title, color);
        Widgets.Wrapped(problem.Detail, Palette.Ink);
        if (problem.Fix == FixAction.None)
        {
            return;
        }

        ImGui.Spacing();
        if (Widgets.Button($"##fix{problem.Title}{slot?.Definition.Id}", problem.FixLabel.ToUpperInvariant(), FixIcon(problem.Fix), new Vector2(width, 36f * Widgets.Scale), warning ? ButtonTone.Ghost : ButtonTone.Primary))
        {
            RunFix(problem.Fix, slot);
        }

        if (problem.Fix is FixAction.OpenPenumbraCollections or FixAction.OpenPenumbraSettings && slot is not null)
        {
            ImGui.Spacing();
            if (Widgets.Button($"##continue{slot.Definition.Id}", "DONE, CONTINUE", FontAwesomeIcon.Play, new Vector2(width, 32f * Widgets.Scale), ButtonTone.Ghost))
            {
                plugin.Installer.Install(slot);
            }
        }
    }

    private void RunFix(FixAction fix, PackSlot? slot)
    {
        switch (fix)
        {
            case FixAction.Retry when slot is not null:
                plugin.Installer.Install(slot);
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

    private void DrawMannequins(PackInstaller installer)
    {
        var mannequinPack = FindMannequinPack(installer.Slots);
        if (mannequinPack is null)
        {
            return;
        }

        var configuration = plugin.Configuration;
        ImGui.Spacing();
        Widgets.SectionTitle("MANNEQUIN LINK", 91);
        using var card = Widgets.Card(Palette.Cyan);
        Widgets.Wrapped($"The {mannequinPack.Definition.Title} is worn by the club's mannequin through the \"{mannequinPack.Definition.Collection}\" collection. Puls8 links it for you the moment you walk in.", Palette.InkMuted);
        ImGui.Spacing();
        var autoLink = configuration.AutoLinkMannequins;
        if (Widgets.Toggle("##autolink", "Link automatically inside the club", "Penumbra remembers the link, so this only runs when something's missing.", ref autoLink))
        {
            configuration.AutoLinkMannequins = autoLink;
            configuration.Save();
        }

        ImGui.Spacing();
        var collection = configuration.MannequinCollectionId;
        if (collection == Guid.Empty)
        {
            ImGui.TextColored(Palette.InkDim, $"Install the {mannequinPack.Definition.Title} first.");
            return;
        }

        if (!plugin.Travel.IsInsideVenue)
        {
            Widgets.IconText(FontAwesomeIcon.MapMarkerAlt, "Visit the club to see and link its mannequins.", Palette.InkDim);
            return;
        }

        var now = DateTime.UtcNow;
        if (now >= nextMannequinScanUtc && installer.Penumbra.Ready)
        {
            nextMannequinScanUtc = now + MannequinScanInterval;
            plugin.Mannequins.Scan(plugin.Penumbra, collection);
        }

        var spots = plugin.Mannequins.Spots;
        if (spots.Count == 0)
        {
            ImGui.TextColored(Palette.InkDim, "No mannequins in view yet.");
            return;
        }

        var unlinked = 0;
        for (var spotIndex = 0; spotIndex < spots.Count; spotIndex++)
        {
            var spot = spots[spotIndex];
            if (!spot.Linked)
            {
                unlinked++;
            }

            Widgets.IconText(spot.Linked ? FontAwesomeIcon.Link : FontAwesomeIcon.Unlink, $"{spot.Name}  ·  {spot.Distance:0} y", spot.Linked ? Palette.Mint : Palette.Amber);
        }

        if (unlinked == 0)
        {
            return;
        }

        ImGui.Spacing();
        if (Widgets.Button("##linknow", "LINK NOW", FontAwesomeIcon.Link, new Vector2(card.InnerWidth, 34f * Widgets.Scale)))
        {
            plugin.Mannequins.LinkAll(plugin.Penumbra, collection);
        }
    }

    private static PackSlot? FindMannequinPack(PackSlot[] slots)
    {
        for (var slotIndex = 0; slotIndex < slots.Length; slotIndex++)
        {
            if (slots[slotIndex].Definition.TargetsMannequin)
            {
                return slots[slotIndex];
            }
        }

        return null;
    }
}
