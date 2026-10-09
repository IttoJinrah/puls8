namespace Puls8.Sync;

public enum SyncService : byte
{
    Unknown,
    Lightless,
    PlayerSync,
}

public static class SyncClients
{
    private const string LightlessInternalName = "LightlessSync";
    private const string PlayerSyncInternalName = "MareSempiterne";
    private const string LightlessCommand = "/light";
    private const string PlayerSyncPrimaryCommand = "/psync";
    private const string PlayerSyncFallbackCommand = "/sync";

    public static SyncService Parse(string service)
    {
        if (string.Equals(service, "Lightless", StringComparison.OrdinalIgnoreCase))
        {
            return SyncService.Lightless;
        }

        return string.Equals(service, "PlayerSync", StringComparison.OrdinalIgnoreCase) ? SyncService.PlayerSync : SyncService.Unknown;
    }

    public static bool IsLoaded(SyncService service)
    {
        var internalName = service switch
        {
            SyncService.Lightless => LightlessInternalName,
            SyncService.PlayerSync => PlayerSyncInternalName,
            _ => string.Empty,
        };

        if (internalName.Length == 0)
        {
            return false;
        }

        foreach (var plugin in Services.PluginInterface.InstalledPlugins)
        {
            if (string.Equals(plugin.InternalName, internalName, StringComparison.Ordinal) && plugin.IsLoaded)
            {
                return true;
            }
        }

        return false;
    }

    public static bool OpenWindow(SyncService service)
    {
        var command = service == SyncService.Lightless ? LightlessCommand : PlayerSyncCommand();
        return command.Length > 0 && Services.Commands.ProcessCommand(command);
    }

    // PlayerSync always registers /psync and only takes /sync when nothing else claimed it first.
    public static bool EnableZoneSync()
    {
        var command = PlayerSyncCommand();
        return command.Length > 0 && Services.Commands.ProcessCommand($"{command} zonesync on");
    }

    private static string PlayerSyncCommand()
    {
        var commands = Services.Commands.Commands;
        if (commands.ContainsKey(PlayerSyncPrimaryCommand))
        {
            return PlayerSyncPrimaryCommand;
        }

        return commands.ContainsKey(PlayerSyncFallbackCommand) ? PlayerSyncFallbackCommand : string.Empty;
    }
}
