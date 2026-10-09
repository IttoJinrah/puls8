using System.IO;
using System.Threading.Tasks;
using Dalamud.Plugin.Ipc.Exceptions;
using Penumbra.Api.Enums;
using Penumbra.Api.Helpers;
using Penumbra.Api.IpcSubscribers;

namespace Puls8.Mods;

public readonly record struct PenumbraState(bool Ready, string ModDirectory, Problem? Blocking, Problem? Warning);

public readonly record struct ObjectCollection(bool Valid, bool IndividualSet, Guid EffectiveId, string EffectiveName);

// Penumbra mutates its mod list and resolves actor identities without locking, so every call here runs on the framework thread.
public sealed class PenumbraBridge : IDisposable
{
    private const int SupportedBreakingVersion = 5;

    private readonly ApiVersion apiVersion;
    private readonly GetModDirectory getModDirectory;
    private readonly GetEnabledState getEnabledState;
    private readonly GetModList getModList;
    private readonly AddMod addMod;
    private readonly DeleteMod deleteMod;
    private readonly GetCollection getCollection;
    private readonly GetCollectionsByIdentifier getCollectionsByIdentifier;
    private readonly GetCollectionForObject getCollectionForObject;
    private readonly SetCollectionForObject setCollectionForObject;
    private readonly TrySetMod trySetMod;
    private readonly TrySetModPriority trySetModPriority;
    private readonly GetCurrentModSettings getCurrentModSettings;
    private readonly RedrawObject redrawObject;
    private readonly RedrawAll redrawAll;
    private readonly OpenMainWindow openMainWindow;
    private readonly EventSubscriber initialized;
    private readonly EventSubscriber disposed;

    public PenumbraBridge()
    {
        var pluginInterface = Services.PluginInterface;
        apiVersion = new ApiVersion(pluginInterface);
        getModDirectory = new GetModDirectory(pluginInterface);
        getEnabledState = new GetEnabledState(pluginInterface);
        getModList = new GetModList(pluginInterface);
        addMod = new AddMod(pluginInterface);
        deleteMod = new DeleteMod(pluginInterface);
        getCollection = new GetCollection(pluginInterface);
        getCollectionsByIdentifier = new GetCollectionsByIdentifier(pluginInterface);
        getCollectionForObject = new GetCollectionForObject(pluginInterface);
        setCollectionForObject = new SetCollectionForObject(pluginInterface);
        trySetMod = new TrySetMod(pluginInterface);
        trySetModPriority = new TrySetModPriority(pluginInterface);
        getCurrentModSettings = new GetCurrentModSettings(pluginInterface);
        redrawObject = new RedrawObject(pluginInterface);
        redrawAll = new RedrawAll(pluginInterface);
        openMainWindow = new OpenMainWindow(pluginInterface);
        initialized = Initialized.Subscriber(pluginInterface, RaiseChanged);
        disposed = Disposed.Subscriber(pluginInterface, RaiseChanged);
    }

    public event Action? Changed;

    public static Task<T> OnFramework<T>(Func<T> work) => Services.Framework.RunOnFrameworkThread(work);

    public static Task OnFramework(Action work) => Services.Framework.RunOnFrameworkThread(work);

    public PenumbraState ReadState()
    {
        try
        {
            var version = apiVersion.Invoke();
            if (version.Breaking != SupportedBreakingVersion)
            {
                return new PenumbraState(false, string.Empty, Problems.PenumbraOutdated(version.Breaking), null);
            }

            var directory = getModDirectory.Invoke();
            if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
            {
                return new PenumbraState(false, string.Empty, Problems.ModDirectoryMissing, null);
            }

            var warning = getEnabledState.Invoke() ? null : Problems.ModsDisabled;
            return new PenumbraState(true, directory, null, warning);
        }
        catch (IpcError)
        {
            return new PenumbraState(false, string.Empty, Problems.PenumbraMissing, null);
        }
    }

    public bool HasMod(string folder)
    {
        try
        {
            return getModList.Invoke().ContainsKey(folder);
        }
        catch (IpcError)
        {
            return false;
        }
    }

    public PenumbraApiEc DeleteMod(string folder) => deleteMod.Invoke(folder);

    public PenumbraApiEc AddMod(string folder) => addMod.Invoke(folder);

    public (Guid Id, string Name)? BaseCollection() => getCollection.Invoke(ApiCollectionType.Default);

    public Guid FindCollection(string name)
    {
        try
        {
            var matches = getCollectionsByIdentifier.Invoke(name);
            for (var matchIndex = 0; matchIndex < matches.Count; matchIndex++)
            {
                if (string.Equals(matches[matchIndex].Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    return matches[matchIndex].Id;
                }
            }
        }
        catch (IpcError)
        {
        }

        return Guid.Empty;
    }

    public PenumbraApiEc Enable(Guid collection, string folder, int priority)
    {
        var enabled = trySetMod.Invoke(collection, folder, true);
        if (enabled is not (PenumbraApiEc.Success or PenumbraApiEc.NothingChanged))
        {
            return enabled;
        }

        var prioritized = trySetModPriority.Invoke(collection, folder, priority);
        return prioritized is PenumbraApiEc.NothingChanged ? PenumbraApiEc.Success : prioritized;
    }

    public bool IsEnabled(Guid collection, string folder)
    {
        try
        {
            var (code, settings) = getCurrentModSettings.Invoke(collection, folder, string.Empty, true);
            return code == PenumbraApiEc.Success && settings is { Item1: true };
        }
        catch (IpcError)
        {
            return false;
        }
    }

    public ObjectCollection CollectionFor(int objectIndex)
    {
        var (valid, individual, effective) = getCollectionForObject.Invoke(objectIndex);
        return new ObjectCollection(valid, individual, effective.Id, effective.Name);
    }

    public PenumbraApiEc AssignCollection(int objectIndex, Guid collection)
    {
        var (code, _) = setCollectionForObject.Invoke(objectIndex, collection, true, false);
        return code;
    }

    public void Redraw(int objectIndex) => redrawObject.Invoke(objectIndex);

    // Redraws characters only (players, NPCs, mannequins); housing furniture reloads with the zone, not with a redraw.
    public bool RedrawEverything()
    {
        try
        {
            redrawAll.Invoke();
            return true;
        }
        catch (IpcError)
        {
            return false;
        }
    }

    public bool Open(TabType tab)
    {
        try
        {
            return openMainWindow.Invoke(tab) == PenumbraApiEc.Success;
        }
        catch (IpcError)
        {
            return false;
        }
    }

    public void Dispose()
    {
        initialized.Dispose();
        disposed.Dispose();
    }

    private void RaiseChanged() => Changed?.Invoke();
}
