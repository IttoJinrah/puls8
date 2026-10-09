using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Dalamud.Plugin.Ipc.Exceptions;
using Penumbra.Api.Enums;
using Penumbra.Api.IpcSubscribers;
using Puls8.Venue;

namespace Puls8.Mods;

// Penumbra has no API for creating saved collections, so the mannequin pack lives in a temporary collection that is
// rebuilt every session from the installed files. Nothing is written to the player's own Penumbra setup.
public sealed class TemporaryVenue : IDisposable
{
    private const string Identity = "Puls8";
    private const string CollectionName = "Puls8 Venue";
    private const string ModTag = "Puls8.Venue";
    private const string DefaultOptionFile = "default_mod.json";
    private const byte ManipulationFormatJson = 0;

    private readonly CreateTemporaryCollection createCollection;
    private readonly DeleteTemporaryCollection deleteCollection;
    private readonly AddTemporaryMod addMod;
    private readonly AssignTemporaryCollection assignCollection;
    private int building;

    public TemporaryVenue()
    {
        var pluginInterface = Services.PluginInterface;
        createCollection = new CreateTemporaryCollection(pluginInterface);
        deleteCollection = new DeleteTemporaryCollection(pluginInterface);
        addMod = new AddTemporaryMod(pluginInterface);
        assignCollection = new AssignTemporaryCollection(pluginInterface);
    }

    public Guid CollectionId { get; private set; }

    public bool IsReady => CollectionId != Guid.Empty;

    public bool IsBuilding => Volatile.Read(ref building) == 1;

    public int Generation { get; private set; }

    public string LastError { get; private set; } = string.Empty;

    public async Task<bool> BuildAsync(PackDefinition pack, string modRoot)
    {
        if (Interlocked.Exchange(ref building, 1) == 1)
        {
            return IsReady;
        }

        try
        {
            var folder = Path.Combine(modRoot, pack.Folder);
            var content = await Task.Run(() => ReadDefaultOption(folder)).ConfigureAwait(false);
            if (content is null)
            {
                LastError = $"{pack.Title} files are missing; reinstall the pack.";
                return false;
            }

            return await PenumbraBridge.OnFramework(() => Create(content.Value, pack.Priority)).ConfigureAwait(false);
        }
        finally
        {
            Volatile.Write(ref building, 0);
        }
    }

    public PenumbraApiEc Assign(int objectIndex) => assignCollection.Invoke(CollectionId, objectIndex, true);

    // Called when the pack files change or Penumbra reloads, which throws temporary collections away.
    public void Invalidate()
    {
        DeleteCurrent();
        Generation++;
    }

    public void Dispose() => DeleteCurrent();

    private bool Create((Dictionary<string, string> Paths, string Manipulations) content, int priority)
    {
        DeleteCurrent();
        try
        {
            var created = createCollection.Invoke(Identity, CollectionName, out var collection);
            if (created != PenumbraApiEc.Success)
            {
                LastError = $"Penumbra answered {created} when creating the venue collection.";
                return false;
            }

            var added = addMod.Invoke(ModTag, collection, content.Paths, content.Manipulations, priority);
            if (added != PenumbraApiEc.Success)
            {
                deleteCollection.Invoke(collection);
                LastError = $"Penumbra answered {added} when loading the pack into the venue collection.";
                return false;
            }

            CollectionId = collection;
            Generation++;
            LastError = string.Empty;
            return true;
        }
        catch (IpcError exception)
        {
            LastError = $"Penumbra isn't answering: {exception.Message}";
            return false;
        }
    }

    private void DeleteCurrent()
    {
        if (CollectionId == Guid.Empty)
        {
            return;
        }

        try
        {
            deleteCollection.Invoke(CollectionId);
        }
        catch (IpcError)
        {
        }

        CollectionId = Guid.Empty;
    }

    // Only the default option is loaded: the venue packs that target the mannequin ship no option groups.
    private static (Dictionary<string, string> Paths, string Manipulations)? ReadDefaultOption(string folder)
    {
        var optionPath = Path.Combine(folder, DefaultOptionFile);
        if (!File.Exists(optionPath))
        {
            return null;
        }

        using var document = JsonDocument.Parse(File.ReadAllBytes(optionPath));
        var root = document.RootElement;
        var paths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (root.TryGetProperty("Files", out var files))
        {
            foreach (var file in files.EnumerateObject())
            {
                var relative = (file.Value.GetString() ?? string.Empty).Replace('/', '\\');
                var fullPath = Path.Combine(folder, relative);
                if (File.Exists(fullPath))
                {
                    paths[file.Name] = fullPath;
                }
            }
        }

        var manipulations = root.TryGetProperty("Manipulations", out var manipulationArray) && manipulationArray.GetArrayLength() > 0
            ? EncodeManipulations(manipulationArray.GetRawText())
            : string.Empty;
        return (paths, manipulations);
    }

    // Penumbra's version-0 meta format: gzip of a version byte followed by the same JSON array the .pmp already carries.
    private static string EncodeManipulations(string json)
    {
        var payload = Encoding.UTF8.GetBytes(json);
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.Optimal, true))
        {
            gzip.WriteByte(ManipulationFormatJson);
            gzip.Write(payload, 0, payload.Length);
        }

        return Convert.ToBase64String(output.GetBuffer(), 0, (int)output.Length);
    }
}
