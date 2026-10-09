using System.IO;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Textures.TextureWraps;

namespace Puls8.Ui;

public static class Images
{
    private static ISharedImmediateTexture? logo;

    public static IDalamudTextureWrap? Logo
    {
        get
        {
            logo ??= Services.Textures.GetFromFile(Path.Combine(Services.PluginInterface.AssemblyLocation.DirectoryName ?? string.Empty, "Images", "Logo.png"));
            return logo.GetWrapOrDefault();
        }
    }
}
