namespace Puls8.Ui;

public static class Links
{
    // venue.json is remote, so only plain https links may reach the browser.
    public static void Open(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
        {
            Services.Log.Warning($"Refusing to open non-https link '{url}'");
            return;
        }

        Dalamud.Utility.Util.OpenLink(uri.AbsoluteUri);
    }

    public static void OpenPluginInstaller() => Services.Commands.ProcessCommand("/xlplugins");
}
