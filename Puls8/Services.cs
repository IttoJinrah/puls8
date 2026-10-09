using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;

namespace Puls8;

internal sealed class Services
{
    [PluginService]
    public static IDalamudPluginInterface PluginInterface { get; private set; } = null!;

    [PluginService]
    public static ICommandManager Commands { get; private set; } = null!;

    [PluginService]
    public static IChatGui Chat { get; private set; } = null!;

    [PluginService]
    public static IPluginLog Log { get; private set; } = null!;

    [PluginService]
    public static IObjectTable Objects { get; private set; } = null!;

    [PluginService]
    public static IFramework Framework { get; private set; } = null!;

    [PluginService]
    public static ITextureProvider Textures { get; private set; } = null!;

    [PluginService]
    public static IClientState ClientState { get; private set; } = null!;

    [PluginService]
    public static IDataManager Data { get; private set; } = null!;

    [PluginService]
    public static INotificationManager Notifications { get; private set; } = null!;
}
