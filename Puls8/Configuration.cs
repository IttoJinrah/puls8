using Dalamud.Configuration;

namespace Puls8;

[Serializable]
public sealed class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 2;

    public Dictionary<string, InstalledPack> Packs { get; set; } = new();

    public Guid MannequinCollectionId { get; set; }

    public bool AutoLinkMannequins { get; set; } = true;

    public bool NotifyPackUpdates { get; set; } = true;

    public bool ReducedMotion { get; set; }

    public HashSet<string> EventReminders { get; set; } = new();

    public HashSet<string> FiredReminders { get; set; } = new();

    public void Save() => Services.PluginInterface.SavePluginConfig(this);

    public static Configuration Load()
    {
        try
        {
            if (Services.PluginInterface.GetPluginConfig() is Configuration configuration)
            {
                return configuration;
            }
        }
        catch (Exception exception)
        {
            // 0.1.x stored a different type; starting fresh is safe because install state is re-read from Penumbra.
            Services.Log.Warning(exception, "Discarding an unreadable configuration from an older Puls8 build");
        }

        return new Configuration();
    }
}

[Serializable]
public sealed class InstalledPack
{
    public string Tag { get; set; } = string.Empty;

    public string Digest { get; set; } = string.Empty;

    public DateTime InstalledUtc { get; set; }
}
