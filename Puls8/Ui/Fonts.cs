using System.IO;
using Dalamud.Interface;
using Dalamud.Interface.ManagedFontAtlas;
using Dalamud.Interface.Utility.Raii;

namespace Puls8.Ui;

// Every tier is a multiple of the font size chosen in Dalamud settings so the window follows that setting.
public static class Fonts
{
    private const string DisplayFontFile = "Audiowide-Regular.ttf";
    private const float HeroScale = 2.6f;
    private const float TitleScale = 1.55f;
    private const float LabelScale = 1.05f;
    private const float LeadScale = 1.15f;
    private const float IconLargeScale = 1.6f;

    private static readonly ushort[] DisplayRanges = [0x0020, 0x00FF, 0x2010, 0x2027, 0];
    private static readonly NoOpScope NoOp = new();

    private static IUiBuilder? builder;
    private static string displayFontPath = string.Empty;
    private static IFontHandle? hero;
    private static IFontHandle? title;
    private static IFontHandle? label;
    private static IFontHandle? lead;
    private static IFontHandle? iconLarge;

    public static void Initialize(IUiBuilder uiBuilder)
    {
        builder = uiBuilder;
        displayFontPath = Path.Combine(Services.PluginInterface.AssemblyLocation.DirectoryName ?? string.Empty, "Fonts", DisplayFontFile);
        hero = DisplayHandle(HeroScale);
        title = DisplayHandle(TitleScale);
        label = DisplayHandle(LabelScale);
        lead = uiBuilder.FontAtlas.NewDelegateFontHandle(e => e.OnPreBuild(tk => tk.AddDalamudDefaultFont(UnitPx() * LeadScale)));
        iconLarge = uiBuilder.FontAtlas.NewDelegateFontHandle(e => e.OnPreBuild(tk => tk.AddFontAwesomeIconFont(new SafeFontConfig { SizePx = UnitPx() * IconLargeScale })));
        uiBuilder.DefaultFontChanged += Rebuild;
    }

    public static void Dispose()
    {
        if (builder is not null)
        {
            builder.DefaultFontChanged -= Rebuild;
        }

        hero?.Dispose();
        title?.Dispose();
        label?.Dispose();
        lead?.Dispose();
        iconLarge?.Dispose();
        hero = title = label = lead = iconLarge = null;
        builder = null;
    }

    public static IDisposable Hero() => Push(hero);

    public static IDisposable Title() => Push(title);

    public static IDisposable Label() => Push(label);

    public static IDisposable Lead() => Push(lead);

    public static IDisposable Icon() => ImRaii.PushFont(UiBuilder.IconFont);

    public static IDisposable IconLarge() => iconLarge is { Available: true } ? iconLarge.Push() : ImRaii.PushFont(UiBuilder.IconFont);

    private static IDisposable Push(IFontHandle? handle) => handle is { Available: true } ? handle.Push() : NoOp;

    private static float UnitPx() => builder?.DefaultFontSpec.SizePx ?? UiBuilder.DefaultFontSizePx;

    private static IFontHandle DisplayHandle(float scale)
        => builder!.FontAtlas.NewDelegateFontHandle(e => e.OnPreBuild(tk =>
        {
            var sizePx = UnitPx() * scale;
            tk.Font = File.Exists(displayFontPath)
                ? tk.AddFontFromFile(displayFontPath, new SafeFontConfig { SizePx = sizePx, GlyphRanges = DisplayRanges })
                : tk.AddDalamudDefaultFont(sizePx);
        }));

    private static void Rebuild() => _ = builder?.FontAtlas.BuildFontsAsync();

    private sealed class NoOpScope : IDisposable
    {
        public void Dispose()
        {
        }
    }
}
