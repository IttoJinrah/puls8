using System.IO;
using System.Threading.Tasks;
using Penumbra.Api.Enums;
using Puls8.Venue;

namespace Puls8.Mods;

public sealed class PackSlot
{
    public PackSlot(PackDefinition definition, InstalledPack? record)
    {
        Definition = definition;
        Record = record;
    }

    public PackDefinition Definition { get; }

    public InstallProgress Progress { get; } = new();

    public InstalledPack? Record { get; set; }

    public PackRelease? Latest { get; set; }

    public bool InPenumbra { get; set; }

    public bool Enabled { get; set; }

    public Problem? Problem { get; set; }

    public bool IsWorking => Progress.Stage is not (InstallStage.Idle or InstallStage.Done or InstallStage.Failed);

    public bool NeedsInstall => !InPenumbra || Record is null || (Latest is not null && !string.Equals(Record.Tag, Latest.Tag, StringComparison.Ordinal));
}

public sealed class PackInstaller : IDisposable
{
    private readonly Configuration configuration;
    private readonly PenumbraBridge penumbra;
    private readonly ReleaseCatalog catalog;
    private readonly MannequinLinker mannequins;
    private CancellationTokenSource? cancellation;
    private PackSlot[] slots = [];
    private VenueAddress address = new();
    private int busy;
    private bool updateNoticeShown;

    public PackInstaller(Configuration configuration, PenumbraBridge penumbra, ReleaseCatalog catalog, MannequinLinker mannequins)
    {
        this.configuration = configuration;
        this.penumbra = penumbra;
        this.catalog = catalog;
        this.mannequins = mannequins;
        penumbra.Changed += MarkStale;
    }

    public PackSlot[] Slots => slots;

    public PenumbraState Penumbra { get; private set; }

    public Problem? CatalogProblem { get; private set; }

    public bool IsBusy => Volatile.Read(ref busy) == 1;

    public bool IsChecking { get; private set; }

    public bool IsStale { get; private set; } = true;

    public int PendingCount
    {
        get
        {
            var pending = 0;
            for (var slotIndex = 0; slotIndex < slots.Length; slotIndex++)
            {
                if (slots[slotIndex].NeedsInstall)
                {
                    pending++;
                }
            }

            return pending;
        }
    }

    // Feed refreshes rebind often; reusing slots whose pack identity is unchanged keeps progress and
    // check results that an in-flight install or update check is still writing into.
    public void Bind(VenueProfile profile)
    {
        var definitions = profile.Packs;
        var previous = slots;
        var rebuilt = new PackSlot[definitions.Length];
        var changed = definitions.Length != previous.Length;
        for (var definitionIndex = 0; definitionIndex < definitions.Length; definitionIndex++)
        {
            var definition = definitions[definitionIndex];
            var reused = FindReusable(previous, definition);
            if (reused is not null)
            {
                rebuilt[definitionIndex] = reused;
                continue;
            }

            configuration.Packs.TryGetValue(definition.Id, out var record);
            rebuilt[definitionIndex] = new PackSlot(definition, record);
            changed = true;
        }

        slots = rebuilt;
        address = profile.Address;
        if (changed)
        {
            IsStale = true;
        }
    }

    private static PackSlot? FindReusable(PackSlot[] previous, PackDefinition definition)
    {
        for (var slotIndex = 0; slotIndex < previous.Length; slotIndex++)
        {
            var candidate = previous[slotIndex].Definition;
            if (string.Equals(candidate.Id, definition.Id, StringComparison.Ordinal)
                && string.Equals(candidate.Folder, definition.Folder, StringComparison.Ordinal)
                && string.Equals(candidate.TagPrefix, definition.TagPrefix, StringComparison.Ordinal)
                && string.Equals(candidate.Target, definition.Target, StringComparison.OrdinalIgnoreCase)
                && string.Equals(candidate.Collection, definition.Collection, StringComparison.Ordinal))
            {
                return previous[slotIndex];
            }
        }

        return null;
    }

    public async Task CheckAsync(bool announceUpdates)
    {
        if (IsChecking || IsBusy)
        {
            return;
        }

        IsChecking = true;
        try
        {
            CatalogProblem = null;
            try
            {
                await catalog.RefreshAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch (InstallException exception)
            {
                CatalogProblem = exception.Problem;
            }

            var current = slots;
            for (var slotIndex = 0; slotIndex < current.Length; slotIndex++)
            {
                current[slotIndex].Latest = catalog.Find(current[slotIndex].Definition);
            }

            await PenumbraBridge.OnFramework(() => ReadPenumbra(current)).ConfigureAwait(false);
            IsStale = false;
            if (announceUpdates)
            {
                AnnounceUpdates(current);
            }
        }
        finally
        {
            IsChecking = false;
        }
    }

    public void InstallAll() => _ = RunAsync(null);

    public void Install(PackSlot slot) => _ = RunAsync(slot);

    public void Cancel() => cancellation?.Cancel();

    public void Dispose()
    {
        penumbra.Changed -= MarkStale;
        cancellation?.Cancel();
        cancellation?.Dispose();
    }

    private async Task RunAsync(PackSlot? only)
    {
        if (Interlocked.Exchange(ref busy, 1) == 1)
        {
            return;
        }

        cancellation?.Dispose();
        cancellation = new CancellationTokenSource();
        var token = cancellation.Token;
        try
        {
            var current = slots;
            for (var slotIndex = 0; slotIndex < current.Length; slotIndex++)
            {
                var slot = current[slotIndex];
                if (only is not null ? !ReferenceEquals(slot, only) : !slot.NeedsInstall)
                {
                    continue;
                }

                var succeeded = await InstallSlotAsync(slot, token).ConfigureAwait(false);
                if (!succeeded || token.IsCancellationRequested)
                {
                    break;
                }
            }

            await PenumbraBridge.OnFramework(() => ReadPenumbra(current)).ConfigureAwait(false);
        }
        finally
        {
            Volatile.Write(ref busy, 0);
        }
    }

    private async Task<bool> InstallSlotAsync(PackSlot slot, CancellationToken token)
    {
        var definition = slot.Definition;
        var progress = slot.Progress;
        progress.Reset();
        slot.Problem = null;
        string? archive = null;
        string? staging = null;
        try
        {
            progress.Stage = InstallStage.Checking;
            var state = await PenumbraBridge.OnFramework(penumbra.ReadState).ConfigureAwait(false);
            Penumbra = state;
            if (state.Blocking is not null)
            {
                throw new InstallException(state.Blocking);
            }

            var collection = await PenumbraBridge.OnFramework(() => ResolveCollection(definition)).ConfigureAwait(false);
            await catalog.RefreshAsync(token).ConfigureAwait(false);
            var release = catalog.Find(definition) ?? throw new InstallException(Problems.NoRelease);
            slot.Latest = release;
            PackFiles.EnsureDiskSpace(state.ModDirectory, release.Size);

            archive = await PackDownloader.DownloadAsync(release, progress, token).ConfigureAwait(false);
            staging = PackFiles.StagingDirectory(state.ModDirectory);
            var stagingPath = staging;
            var archivePath = archive;
            await Task.Run(() => PackFiles.Extract(archivePath, stagingPath, progress), token).ConfigureAwait(false);

            progress.Stage = InstallStage.Swapping;
            await SwapAsync(definition.Folder, state.ModDirectory, staging, token).ConfigureAwait(false);
            staging = null;

            progress.Stage = InstallStage.Registering;
            var registered = await PenumbraBridge.OnFramework(() => Register(definition.Folder)).ConfigureAwait(false);
            if (!registered)
            {
                throw new InstallException(Problems.PenumbraRejected);
            }

            progress.Stage = InstallStage.Enabling;
            var enableCode = await PenumbraBridge.OnFramework(() => EnableAndVerify(collection, definition)).ConfigureAwait(false);
            if (enableCode != PenumbraApiEc.Success)
            {
                throw new InstallException(enableCode == PenumbraApiEc.CollectionMissing
                    ? Problems.CollectionMissing(definition.Collection)
                    : Problems.Unexpected($"Penumbra answered {enableCode} while enabling the pack."));
            }

            if (definition.TargetsMannequin)
            {
                progress.Stage = InstallStage.Linking;
                configuration.MannequinCollectionId = collection;
                await PenumbraBridge.OnFramework(() => LinkIfAtVenue(collection)).ConfigureAwait(false);
            }

            var record = new InstalledPack { Tag = release.Tag, Digest = release.Sha256, InstalledUtc = DateTime.UtcNow };
            configuration.Packs[definition.Id] = record;
            configuration.Save();
            slot.Record = record;
            progress.Stage = InstallStage.Done;
            return true;
        }
        catch (InstallException exception)
        {
            Services.Log.Warning(exception.InnerException, $"Installing {definition.Title} failed: {exception.Problem.Title}");
            Fail(slot, exception.Problem);
            return false;
        }
        catch (OperationCanceledException)
        {
            progress.Reset();
            return false;
        }
        catch (Exception exception)
        {
            Services.Log.Error(exception, $"Installing {definition.Title} failed unexpectedly");
            Fail(slot, Problems.Unexpected(exception.Message));
            return false;
        }
        finally
        {
            if (archive is not null)
            {
                PackDownloader.TryDelete(archive);
            }

            if (staging is not null)
            {
                try
                {
                    await PackFiles.DeleteWithRetriesAsync(staging, CancellationToken.None).ConfigureAwait(false);
                }
                catch (InstallException)
                {
                    Services.Log.Information($"Left a staging folder behind at {staging}; it is swept on next start");
                }
            }
        }
    }

    // Penumbra keeps settings for a deleted mod keyed by folder name, so delete + re-add preserves the player's option choices.
    private async Task SwapAsync(string folder, string modRoot, string staging, CancellationToken token)
    {
        var target = Path.Combine(modRoot, folder);
        var wasRegistered = await PenumbraBridge.OnFramework(() => penumbra.HasMod(folder)).ConfigureAwait(false);
        if (wasRegistered)
        {
            await PenumbraBridge.OnFramework(() => penumbra.DeleteMod(folder)).ConfigureAwait(false);
        }

        try
        {
            await PackFiles.DeleteWithRetriesAsync(target, token).ConfigureAwait(false);
        }
        catch (InstallException) when (wasRegistered)
        {
            await PenumbraBridge.OnFramework(() => penumbra.AddMod(folder)).ConfigureAwait(false);
            throw;
        }

        await PackFiles.MoveWithRetriesAsync(staging, target, token).ConfigureAwait(false);
    }

    private Guid ResolveCollection(PackDefinition definition)
    {
        if (definition.TargetsMannequin)
        {
            var named = penumbra.FindCollection(definition.Collection);
            return named != Guid.Empty ? named : throw new InstallException(Problems.CollectionMissing(definition.Collection));
        }

        var baseCollection = penumbra.BaseCollection();
        return baseCollection?.Id ?? throw new InstallException(Problems.BaseCollectionMissing);
    }

    private bool Register(string folder)
    {
        var code = penumbra.AddMod(folder);
        if (code is not PenumbraApiEc.Success)
        {
            Services.Log.Warning($"Penumbra AddMod({folder}) answered {code}");
        }

        return penumbra.HasMod(folder);
    }

    private PenumbraApiEc EnableAndVerify(Guid collection, PackDefinition definition)
    {
        var code = penumbra.Enable(collection, definition.Folder, definition.Priority);
        if (code != PenumbraApiEc.Success)
        {
            return code;
        }

        return penumbra.IsEnabled(collection, definition.Folder) ? PenumbraApiEc.Success : PenumbraApiEc.UnknownError;
    }

    private int LinkIfAtVenue(Guid collection)
    {
        if (!VenueLocator.IsInside(address))
        {
            return 0;
        }

        mannequins.Scan(penumbra, collection);
        return mannequins.LinkAll(penumbra, collection);
    }

    private void ReadPenumbra(PackSlot[] current)
    {
        Penumbra = penumbra.ReadState();
        var baseCollection = Penumbra.Ready ? penumbra.BaseCollection() : null;
        for (var slotIndex = 0; slotIndex < current.Length; slotIndex++)
        {
            var slot = current[slotIndex];
            slot.InPenumbra = Penumbra.Ready && penumbra.HasMod(slot.Definition.Folder);
            AdoptExistingMannequinCollection(slot);
            var collection = slot.Definition.TargetsMannequin ? configuration.MannequinCollectionId : baseCollection?.Id ?? Guid.Empty;
            slot.Enabled = slot.InPenumbra && collection != Guid.Empty && penumbra.IsEnabled(collection, slot.Definition.Folder);
        }
    }

    // 0.1.x installs already have a hand-made collection; adopting it lets auto-link work before the first reinstall.
    private void AdoptExistingMannequinCollection(PackSlot slot)
    {
        if (!slot.InPenumbra || !slot.Definition.TargetsMannequin || configuration.MannequinCollectionId != Guid.Empty)
        {
            return;
        }

        var existing = penumbra.FindCollection(slot.Definition.Collection);
        if (existing == Guid.Empty)
        {
            return;
        }

        configuration.MannequinCollectionId = existing;
        configuration.Save();
    }

    private void AnnounceUpdates(PackSlot[] current)
    {
        if (updateNoticeShown || !configuration.NotifyPackUpdates)
        {
            return;
        }

        for (var slotIndex = 0; slotIndex < current.Length; slotIndex++)
        {
            var slot = current[slotIndex];
            if (slot.Record is null || slot.Latest is null || !slot.NeedsInstall)
            {
                continue;
            }

            updateNoticeShown = true;
            Services.Chat.Print($"A new {slot.Definition.Title} pack is out ({slot.Latest.Tag}). Open /puls mods to update in one click.", "Puls8", 541);
        }
    }

    private static void Fail(PackSlot slot, Problem problem)
    {
        slot.Problem = problem;
        slot.Progress.Stage = InstallStage.Failed;
    }

    private void MarkStale() => IsStale = true;
}
