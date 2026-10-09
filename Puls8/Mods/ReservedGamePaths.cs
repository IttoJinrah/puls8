using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Puls8.Mods;

// Penumbra refuses to redirect these shared placeholder textures (ReservedFileService.cs) and raises a "Reserved File
// Redirection" warning every time a pack that lists them loads. Dropping them at install changes nothing visually.
public static class ReservedGamePaths
{
    private const string DefaultOptionFile = "default_mod.json";
    private const string GroupFilePattern = "group_*.json";
    private const string FilesProperty = "Files";

    private static readonly HashSet<string> Paths = new(StringComparer.OrdinalIgnoreCase)
    {
        "common/graphics/texture/dummy.tex",
        "chara/common/texture/white.tex",
        "chara/common/texture/black.tex",
        "chara/common/texture/id_16.tex",
        "chara/common/texture/common_id.tex",
        "chara/common/texture/red.tex",
        "chara/common/texture/green.tex",
        "chara/common/texture/blue.tex",
        "chara/common/texture/null_normal.tex",
        "chara/common/texture/skin_mask.tex",
    };

    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

    public static bool IsReserved(string gamePath) => Paths.Contains(gamePath.Replace('\\', '/'));

    public static int StripFromPack(string folder)
    {
        var removed = StripFile(Path.Combine(folder, DefaultOptionFile));
        var groups = Directory.GetFiles(folder, GroupFilePattern);
        for (var groupIndex = 0; groupIndex < groups.Length; groupIndex++)
        {
            removed += StripFile(groups[groupIndex]);
        }

        return removed;
    }

    private static int StripFile(string path)
    {
        if (!File.Exists(path) || JsonNode.Parse(File.ReadAllText(path)) is not { } root)
        {
            return 0;
        }

        var removed = Strip(root);
        if (removed > 0)
        {
            File.WriteAllText(path, root.ToJsonString(WriteOptions));
        }

        return removed;
    }

    // Option files nest "Files" maps under Options[] in groups and at the top level in default_mod.json.
    private static int Strip(JsonNode node)
    {
        var removed = 0;
        if (node is JsonObject jsonObject)
        {
            if (jsonObject[FilesProperty] is JsonObject files)
            {
                removed += StripReservedKeys(files);
            }

            foreach (var property in jsonObject)
            {
                if (property.Value is not null && !ReferenceEquals(property.Value, jsonObject[FilesProperty]))
                {
                    removed += Strip(property.Value);
                }
            }
        }
        else if (node is JsonArray array)
        {
            for (var itemIndex = 0; itemIndex < array.Count; itemIndex++)
            {
                if (array[itemIndex] is { } item)
                {
                    removed += Strip(item);
                }
            }
        }

        return removed;
    }

    private static int StripReservedKeys(JsonObject files)
    {
        var reserved = new List<string>(2);
        foreach (var file in files)
        {
            if (IsReserved(file.Key))
            {
                reserved.Add(file.Key);
            }
        }

        for (var keyIndex = 0; keyIndex < reserved.Count; keyIndex++)
        {
            files.Remove(reserved[keyIndex]);
        }

        return reserved.Count;
    }
}
