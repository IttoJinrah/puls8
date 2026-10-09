namespace Puls8.Ui;

// Sampled from the Puls8 logo: magenta sun top, violet stripes, indigo base, white-hot wireframe core.
public static class Palette
{
    public static readonly Vector4 Void = Rgb(0x07010F);
    public static readonly Vector4 Night = Rgb(0x0D0420);
    public static readonly Vector4 Panel = Rgb(0x140830);
    public static readonly Vector4 PanelRaised = Rgb(0x1D0D42);
    public static readonly Vector4 PanelHover = Rgb(0x28125A);

    public static readonly Vector4 Magenta = Rgb(0xFB0AD1);
    public static readonly Vector4 Violet = Rgb(0xC603DD);
    public static readonly Vector4 Purple = Rgb(0x7B2FF7);
    public static readonly Vector4 Indigo = Rgb(0x18008C);
    public static readonly Vector4 Navy = Rgb(0x030149);
    public static readonly Vector4 Electric = Rgb(0x3D7BFF);
    public static readonly Vector4 Cyan = Rgb(0x2DE2FF);
    public static readonly Vector4 Core = Rgb(0xFFF0FF);

    public static readonly Vector4 Ink = Rgb(0xF6EEFF);
    public static readonly Vector4 InkMuted = Rgb(0xC3B2E6);
    public static readonly Vector4 InkDim = Rgb(0x8A79AD);

    public static readonly Vector4 Mint = Rgb(0x2EF2C2);
    public static readonly Vector4 Amber = Rgb(0xFFB547);
    public static readonly Vector4 Danger = Rgb(0xFF4D6D);

    public static Vector4 WithAlpha(Vector4 color, float alpha) => color with { W = color.W * alpha };

    public static uint U32(Vector4 color) => Dalamud.Bindings.ImGui.ImGui.GetColorU32(color);

    public static uint U32(Vector4 color, float alpha) => U32(WithAlpha(color, alpha));

    public static Vector4 Mix(Vector4 from, Vector4 to, float amount) => Vector4.Lerp(from, to, Math.Clamp(amount, 0f, 1f));

    // Sunset ramp across the brand: magenta -> violet -> purple -> electric blue.
    public static Vector4 Sunset(float progress)
    {
        var clamped = Math.Clamp(progress, 0f, 1f) * 3f;
        if (clamped < 1f)
        {
            return Vector4.Lerp(Magenta, Violet, clamped);
        }

        return clamped < 2f ? Vector4.Lerp(Violet, Purple, clamped - 1f) : Vector4.Lerp(Purple, Electric, clamped - 2f);
    }

    private static Vector4 Rgb(uint hex)
        => new(((hex >> 16) & 0xFF) / 255f, ((hex >> 8) & 0xFF) / 255f, (hex & 0xFF) / 255f, 1f);
}
