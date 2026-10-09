using System.Threading.Tasks;
using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Puls8.Mods;
using Puls8.Staff;
using Puls8.Travel;
using Puls8.Ui;
using Puls8.Venue;

namespace Puls8;

public sealed class Plugin : IDalamudPlugin
{
    private const string CommandName = "/puls";
    private static readonly TimeSpan AutoLinkInterval = TimeSpan.FromSeconds(3);

    private readonly WindowSystem windows = new("Puls8");
    private readonly MainWindow mainWindow;
    private DateTime nextAutoLinkUtc;

    public Plugin(IDalamudPluginInterface pluginInterface)
    {
        pluginInterface.Create<Services>();
        Configuration = Configuration.Load();
        Feed = new VenueFeed();
        Clock = new VenueClock();
        Reminders = new EventReminders(Configuration);
        Penumbra = new PenumbraBridge();
        Catalog = new ReleaseCatalog();
        Mannequins = new MannequinLinker();
        Installer = new PackInstaller(Configuration, Penumbra, Catalog, Mannequins);
        Travel = new TravelService(new LifestreamBridge());
        Staff = new StaffSession(Configuration, Feed);
        Installer.Bind(Feed.Current);
        Feed.Changed += OnVenueChanged;

        Fonts.Initialize(pluginInterface.UiBuilder);
        mainWindow = new MainWindow(this);
        windows.AddWindow(mainWindow);
        pluginInterface.UiBuilder.Draw += windows.Draw;
        pluginInterface.UiBuilder.OpenMainUi += OpenHome;
        pluginInterface.UiBuilder.OpenConfigUi += OpenMods;
        Services.Framework.Update += OnFrameworkUpdate;
        Services.ClientState.Login += OnLogin;
        Services.ClientState.TerritoryChanged += OnTerritoryChanged;
        Services.Commands.AddHandler(CommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "Open Puls8. Add travel, wifi, events, menu or mods to jump straight there.",
        });

        PackDownloader.SweepTempDirectory();
        Staff.Restore();
        _ = StartupAsync();
    }

    public Configuration Configuration { get; }

    public VenueFeed Feed { get; }

    public VenueClock Clock { get; }

    public EventReminders Reminders { get; }

    public PenumbraBridge Penumbra { get; }

    public ReleaseCatalog Catalog { get; }

    public MannequinLinker Mannequins { get; }

    public PackInstaller Installer { get; }

    public TravelService Travel { get; }

    public StaffSession Staff { get; }

    public void Dispose()
    {
        Services.Commands.RemoveHandler(CommandName);
        Services.ClientState.Login -= OnLogin;
        Services.ClientState.TerritoryChanged -= OnTerritoryChanged;
        Services.Framework.Update -= OnFrameworkUpdate;
        Services.PluginInterface.UiBuilder.Draw -= windows.Draw;
        Services.PluginInterface.UiBuilder.OpenMainUi -= OpenHome;
        Services.PluginInterface.UiBuilder.OpenConfigUi -= OpenMods;
        Feed.Changed -= OnVenueChanged;
        windows.RemoveAllWindows();
        mainWindow.Dispose();
        Installer.Dispose();
        Penumbra.Dispose();
        Fonts.Dispose();
    }

    private async Task StartupAsync()
    {
        await Feed.RefreshAsync().ConfigureAwait(false);
        await Installer.CheckAsync(Services.ClientState.IsLoggedIn).ConfigureAwait(false);
        await Services.Framework.RunOnFrameworkThread(() => PackFiles.SweepStaging(Installer.Penumbra.ModDirectory)).ConfigureAwait(false);
    }

    private void OnLogin() => _ = Installer.CheckAsync(true);

    private void OnTerritoryChanged(uint territory) => Installer.OnZoneChanged();

    private void OnVenueChanged() => _ = Services.Framework.RunOnFrameworkThread(() => Installer.Bind(Feed.Current));

    private void OnFrameworkUpdate(IFramework framework)
    {
        var profile = Feed.Current;
        Clock.Update(profile);
        Reminders.Tick(Clock, profile.Name);
        Travel.Tick(profile.Address);
        TryAutoLinkMannequins();
    }

    private void TryAutoLinkMannequins()
    {
        var now = DateTime.UtcNow;
        if (now < nextAutoLinkUtc || !Configuration.AutoLinkMannequins || Configuration.MannequinCollectionId == Guid.Empty)
        {
            return;
        }

        nextAutoLinkUtc = now + AutoLinkInterval;
        if (!Travel.IsInsideVenue || !Installer.Penumbra.Ready)
        {
            return;
        }

        var linked = Mannequins.AutoLink(Penumbra, Configuration.MannequinCollectionId);
        if (linked > 0)
        {
            Services.Chat.Print("The Cityscape is now showing on the venue mannequin. Welcome in!", "Puls8", 541);
        }
    }

    private void OnCommand(string command, string arguments)
    {
        var verb = arguments.Trim().ToLowerInvariant();
        if (verb is "travel" or "go")
        {
            Travel.Go(Feed.Current.Address);
        }

        var page = verb switch
        {
            "wifi" or "sync" => Page.Wifi,
            "events" => Page.Events,
            "menu" or "lounge" => Page.Lounge,
            "mods" or "update" => Page.Mods,
            "about" => Page.About,
            "staff" => Page.Staff,
            _ => Page.Home,
        };

        mainWindow.Show(page);
    }

    private void OpenHome() => mainWindow.Show(Page.Home);

    private void OpenMods() => mainWindow.Show(Page.Mods);
}
