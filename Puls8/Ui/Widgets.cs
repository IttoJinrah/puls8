using System.Runtime.InteropServices;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;

namespace Puls8.Ui;

public enum ButtonTone : byte
{
    Primary,
    Ghost,
    Danger,
}

public static class Widgets
{
    private const float ButtonRounding = 10f;
    private const float HoverSmoothTime = 0.09f;
    private const double CopiedFlashSeconds = 1.6;

    private static readonly Dictionary<uint, Spring> HoverSprings = new();
    private static uint copiedId;
    private static double copiedAt;

    public static float Scale => ImGuiHelpersScale();

    public static CardScope Card(Vector4 accent, float paddingScale = 1f) => new(accent, 14f * paddingScale * Scale);

    public static bool Button(string id, string label, FontAwesomeIcon icon, Vector2 size, ButtonTone tone = ButtonTone.Primary, bool enabled = true)
    {
        var drawList = ImGui.GetWindowDrawList();
        var min = ImGui.GetCursorScreenPos();
        var max = min + size;
        var itemId = ImGui.GetID(id);
        var clicked = ImGui.InvisibleButton(id, size) && enabled;
        var hovered = enabled && ImGui.IsItemHovered();
        var held = enabled && ImGui.IsItemActive();
        if (hovered)
        {
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
        }

        var hover = Hover(itemId, hovered ? 1f : 0f);
        var press = held ? 0.97f : 1f;
        var center = (min + max) * 0.5f;
        var scaledMin = center + (min - center) * press;
        var scaledMax = center + (max - center) * press;
        var dim = enabled ? 1f : 0.4f;

        switch (tone)
        {
            case ButtonTone.Primary:
                Fx.GlowRect(drawList, scaledMin, scaledMax, Palette.Magenta, ButtonRounding, (0.35f + hover * 0.65f) * dim);
                Fx.GradientFill(drawList, scaledMin, scaledMax, ButtonRounding, (0.82f + hover * 0.18f) * dim);
                Fx.VerticalGradient(drawList, scaledMin, new Vector2(scaledMax.X, (scaledMin.Y + scaledMax.Y) * 0.5f),
                    Palette.WithAlpha(Palette.Core, 0.18f), Palette.WithAlpha(Palette.Core, 0.02f), ButtonRounding, ImDrawFlags.RoundCornersTop);
                break;
            case ButtonTone.Danger:
                drawList.AddRectFilled(scaledMin, scaledMax, Palette.U32(Palette.Danger, (0.18f + hover * 0.14f) * dim), ButtonRounding);
                drawList.AddRect(scaledMin, scaledMax, Palette.U32(Palette.Danger, 0.8f * dim), ButtonRounding, ImDrawFlags.None, 1.4f);
                break;
            default:
                drawList.AddRectFilled(scaledMin, scaledMax, Palette.U32(Palette.Mix(Palette.PanelRaised, Palette.PanelHover, hover), 0.92f * dim), ButtonRounding);
                Fx.GradientBorder(drawList, scaledMin, scaledMax, ButtonRounding, 1.2f, (0.55f + hover * 0.45f) * dim);
                break;
        }

        var ink = Palette.U32(tone == ButtonTone.Primary ? Palette.Core : Palette.Ink, dim);
        DrawIconLabel(drawList, icon, label, center, ink);
        return clicked;
    }

    public static bool IconButton(string id, FontAwesomeIcon icon, float size, string tooltip, Vector4 accent)
    {
        var drawList = ImGui.GetWindowDrawList();
        var min = ImGui.GetCursorScreenPos();
        var itemId = ImGui.GetID(id);
        var clicked = ImGui.InvisibleButton(id, new Vector2(size));
        var hovered = ImGui.IsItemHovered();
        if (hovered)
        {
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
            ImGui.SetTooltip(tooltip);
        }

        var hover = Hover(itemId, hovered ? 1f : 0f);
        var center = min + new Vector2(size * 0.5f);
        var radius = size * 0.5f;
        drawList.AddCircleFilled(center, radius, Palette.U32(Palette.Mix(Palette.PanelRaised, accent, hover * 0.35f), 0.75f), 32);
        drawList.AddCircle(center, radius - 0.5f, Palette.U32(accent, 0.35f + hover * 0.55f), 32, 1.2f);
        if (hover > 0.01f)
        {
            drawList.AddCircle(center, radius + 2.5f * Scale, Palette.U32(accent, 0.25f * hover), 32, 2f);
        }

        using (Fonts.Icon())
        {
            var glyph = icon.ToIconString();
            var glyphSize = ImGui.CalcTextSize(glyph);
            drawList.AddText(center - glyphSize * 0.5f, Palette.U32(Palette.Mix(Palette.InkMuted, Palette.Core, hover)), glyph);
        }

        return clicked;
    }

    public static void Chip(string text, Vector4 color, bool pulse = false)
    {
        var drawList = ImGui.GetWindowDrawList();
        var textSize = ImGui.CalcTextSize(text);
        var padding = new Vector2(9f, 3f) * Scale;
        var dot = 7f * Scale;
        var size = new Vector2(textSize.X + padding.X * 2f + dot + 5f * Scale, textSize.Y + padding.Y * 2f);
        var min = ImGui.GetCursorScreenPos();
        var max = min + size;
        drawList.AddRectFilled(min, max, Palette.U32(color, 0.14f), size.Y * 0.5f);
        drawList.AddRect(min, max, Palette.U32(color, 0.55f), size.Y * 0.5f, ImDrawFlags.None, 1f);
        var dotCenter = new Vector2(min.X + padding.X + dot * 0.5f, (min.Y + max.Y) * 0.5f);
        var beat = pulse ? Motion.Heartbeat() : 0f;
        drawList.AddCircleFilled(dotCenter, dot * (0.5f + beat * 0.35f) + 2f, Palette.U32(color, 0.25f + beat * 0.3f), 12);
        drawList.AddCircleFilled(dotCenter, dot * 0.38f, Palette.U32(color), 12);
        drawList.AddText(new Vector2(dotCenter.X + dot * 0.5f + 5f * Scale, min.Y + padding.Y), Palette.U32(color), text);
        ImGui.Dummy(size);
    }

    public static void Progress(float fraction, float width, string caption)
    {
        var drawList = ImGui.GetWindowDrawList();
        var height = 8f * Scale;
        var min = ImGui.GetCursorScreenPos();
        var max = min + new Vector2(width, height);
        drawList.AddRectFilled(min, max, Palette.U32(Palette.Void, 0.8f), height * 0.5f);
        var fillMax = new Vector2(min.X + width * Math.Clamp(fraction, 0f, 1f), max.Y);
        if (fillMax.X - min.X > height)
        {
            Fx.GradientFill(drawList, min, fillMax, height * 0.5f, 1f);
            var shimmerX = min.X + (fillMax.X - min.X) * Motion.Phase(1400.0);
            drawList.AddCircleFilled(new Vector2(shimmerX, (min.Y + max.Y) * 0.5f), height * 0.9f, Palette.U32(Palette.Core, 0.25f), 12);
        }

        drawList.AddRect(min, max, Palette.U32(Palette.Violet, 0.5f), height * 0.5f, ImDrawFlags.None, 1f);
        ImGui.Dummy(new Vector2(width, height));
        if (caption.Length > 0)
        {
            ImGui.TextColored(Palette.InkMuted, caption);
        }
    }

    public static void CopyField(string label, string value, string id)
    {
        ImGui.TextColored(Palette.InkDim, label);
        var drawList = ImGui.GetWindowDrawList();
        var width = ImGui.GetContentRegionAvail().X;
        var height = ImGui.GetFrameHeight() + 8f * Scale;
        var min = ImGui.GetCursorScreenPos();
        var max = min + new Vector2(width, height);
        var itemId = ImGui.GetID(id);
        var clicked = ImGui.InvisibleButton(id, max - min);
        var hovered = ImGui.IsItemHovered();
        if (hovered)
        {
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
        }

        if (clicked)
        {
            ImGui.SetClipboardText(value);
            copiedId = itemId;
            copiedAt = ImGui.GetTime();
        }

        var hover = Hover(itemId, hovered ? 1f : 0f);
        drawList.AddRectFilled(min, max, Palette.U32(Palette.Mix(Palette.Void, Palette.PanelRaised, 0.4f + hover * 0.6f)), 8f * Scale);
        drawList.AddRect(min, max, Palette.U32(Palette.Violet, 0.35f + hover * 0.4f), 8f * Scale, ImDrawFlags.None, 1f);
        var textY = min.Y + (height - ImGui.GetTextLineHeight()) * 0.5f;
        drawList.AddText(new Vector2(min.X + 10f * Scale, textY), Palette.U32(Palette.Core), value);

        var copied = copiedId == itemId && ImGui.GetTime() - copiedAt < CopiedFlashSeconds;
        var hint = copied ? "COPIED" : "COPY";
        string glyph;
        using (Fonts.Icon())
        {
            glyph = (copied ? FontAwesomeIcon.Check : FontAwesomeIcon.Copy).ToIconString();
        }

        var hintSize = ImGui.CalcTextSize(hint);
        var hintColor = Palette.U32(copied ? Palette.Mint : Palette.Mix(Palette.InkDim, Palette.Magenta, hover));
        var hintX = max.X - hintSize.X - 12f * Scale;
        drawList.AddText(new Vector2(hintX, textY), hintColor, hint);
        using (Fonts.Icon())
        {
            var glyphSize = ImGui.CalcTextSize(glyph);
            drawList.AddText(new Vector2(hintX - glyphSize.X - 6f * Scale, min.Y + (height - glyphSize.Y) * 0.5f), hintColor, glyph);
        }
    }

    public static bool Toggle(string id, string label, string detail, ref bool value)
    {
        var drawList = ImGui.GetWindowDrawList();
        var trackSize = new Vector2(38f, 20f) * Scale;
        var min = ImGui.GetCursorScreenPos();
        var itemId = ImGui.GetID(id);
        var clicked = ImGui.InvisibleButton(id, trackSize);
        if (clicked)
        {
            value = !value;
        }

        var knob = Hover(itemId, value ? 1f : 0f);
        if (ImGui.IsItemHovered())
        {
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
        }

        var max = min + trackSize;
        drawList.AddRectFilled(min, max, Palette.U32(Palette.Mix(Palette.PanelRaised, Palette.Magenta, knob * 0.7f)), trackSize.Y * 0.5f);
        drawList.AddRect(min, max, Palette.U32(Palette.Violet, 0.6f), trackSize.Y * 0.5f, ImDrawFlags.None, 1f);
        var radius = trackSize.Y * 0.36f;
        var knobX = min.X + trackSize.Y * 0.5f + (trackSize.X - trackSize.Y) * knob;
        drawList.AddCircleFilled(new Vector2(knobX, (min.Y + max.Y) * 0.5f), radius, Palette.U32(Palette.Core), 16);

        ImGui.SameLine(0f, 10f * Scale);
        ImGui.BeginGroup();
        ImGui.TextColored(Palette.Ink, label);
        if (detail.Length > 0)
        {
            ImGui.PushTextWrapPos(0f);
            ImGui.TextColored(Palette.InkDim, detail);
            ImGui.PopTextWrapPos();
        }

        ImGui.EndGroup();
        return clicked;
    }

    public static void SectionTitle(string text, int seed)
    {
        using (Fonts.Label())
        {
            var drawList = ImGui.GetWindowDrawList();
            var font = ImGui.GetFont();
            var size = ImGui.GetFontSize();
            var position = ImGui.GetCursorScreenPos();
            var textSize = ImGui.CalcTextSize(text);
            Fx.GradientText(drawList, font, size, position, text, textSize.X, Motion.Phase(9000.0));
            Fx.GlitchText(drawList, font, size, position, textSize, text, seed, 0.8f);
            ImGui.Dummy(textSize);
        }

        var lineMin = ImGui.GetCursorScreenPos();
        var lineWidth = ImGui.GetContentRegionAvail().X;
        var drawListLine = ImGui.GetWindowDrawList();
        drawListLine.AddRectFilledMultiColor(lineMin, lineMin + new Vector2(lineWidth, 1.5f * Scale),
            Palette.U32(Palette.Magenta, 0.8f), Palette.U32(Palette.Electric, 0f), Palette.U32(Palette.Electric, 0f), Palette.U32(Palette.Magenta, 0.8f));
        ImGui.Dummy(new Vector2(lineWidth, 6f * Scale));
    }

    public static void Wrapped(string text, Vector4 color)
    {
        ImGui.PushTextWrapPos(0f);
        ImGui.TextColored(color, text);
        ImGui.PopTextWrapPos();
    }

    public static void IconText(FontAwesomeIcon icon, string text, Vector4 color)
    {
        using (Fonts.Icon())
        {
            ImGui.TextColored(color, icon.ToIconString());
        }

        ImGui.SameLine(0f, 8f * Scale);
        ImGui.TextColored(color, text);
    }

    public static void Steps(ReadOnlySpan<string> labels, int active, bool failed)
    {
        var drawList = ImGui.GetWindowDrawList();
        var width = ImGui.GetContentRegionAvail().X;
        var min = ImGui.GetCursorScreenPos();
        var radius = 5f * Scale;
        var spacing = width / labels.Length;
        var lineY = min.Y + radius + 1f;
        var labelHeight = ImGui.GetTextLineHeight();
        for (var stepIndex = 0; stepIndex < labels.Length; stepIndex++)
        {
            var center = new Vector2(min.X + spacing * (stepIndex + 0.5f), lineY);
            if (stepIndex > 0)
            {
                var previous = new Vector2(min.X + spacing * (stepIndex - 0.5f), lineY);
                var color = stepIndex <= active ? Palette.Magenta : Palette.InkDim;
                drawList.AddLine(previous + new Vector2(radius + 2f, 0f), center - new Vector2(radius + 2f, 0f), Palette.U32(color, stepIndex <= active ? 0.9f : 0.35f), 1.5f);
            }

            var done = stepIndex < active;
            var current = stepIndex == active;
            var tone = current && failed ? Palette.Danger : done ? Palette.Mint : current ? Palette.Magenta : Palette.InkDim;
            if (current && !failed)
            {
                drawList.AddCircleFilled(center, radius * (1.8f + Motion.Wave(900.0) * 0.6f), Palette.U32(tone, 0.22f), 20);
            }

            drawList.AddCircleFilled(center, radius, Palette.U32(tone, done || current ? 1f : 0.35f), 20);
            var label = labels[stepIndex];
            var labelSize = ImGui.CalcTextSize(label);
            var labelScale = MathF.Min(1f, (spacing - 4f) / MathF.Max(1f, labelSize.X));
            var font = ImGui.GetFont();
            var fontSize = ImGui.GetFontSize() * MathF.Max(0.75f, labelScale);
            var scaledWidth = labelSize.X * (fontSize / ImGui.GetFontSize());
            drawList.AddText(font, fontSize, new Vector2(center.X - scaledWidth * 0.5f, lineY + radius + 4f * Scale), Palette.U32(tone, done || current ? 1f : 0.6f), label);
        }

        ImGui.Dummy(new Vector2(width, radius * 2f + labelHeight + 8f * Scale));
    }

    public static float Hover(uint id, float target)
    {
        ref var spring = ref CollectionsMarshal.GetValueRefOrAddDefault(HoverSprings, id, out _);
        return Motion.Reduced ? target : spring.Step(target, HoverSmoothTime, Motion.Delta);
    }

    public static void DrawIconLabel(ImDrawListPtr drawList, FontAwesomeIcon icon, string label, Vector2 center, uint ink)
    {
        var labelSize = ImGui.CalcTextSize(label);
        string glyph;
        Vector2 glyphSize;
        using (Fonts.Icon())
        {
            glyph = icon.ToIconString();
            glyphSize = icon == FontAwesomeIcon.None ? Vector2.Zero : ImGui.CalcTextSize(glyph);
        }

        var gap = glyphSize.X > 0f ? 8f * Scale : 0f;
        var total = glyphSize.X + gap + labelSize.X;
        var left = center.X - total * 0.5f;
        if (glyphSize.X > 0f)
        {
            using (Fonts.Icon())
            {
                drawList.AddText(new Vector2(left, center.Y - glyphSize.Y * 0.5f), ink, glyph);
            }
        }

        drawList.AddText(new Vector2(left + glyphSize.X + gap, center.Y - labelSize.Y * 0.5f), ink, label);
    }

    private static float ImGuiHelpersScale() => Dalamud.Interface.Utility.ImGuiHelpers.GlobalScale;
}

// Content is drawn first on channel 1, then the card background is painted under it on channel 0 once its height is known.
public ref struct CardScope
{
    private readonly ImDrawListPtr drawList;
    private readonly Vector2 origin;
    private readonly float width;
    private readonly float padding;
    private readonly Vector4 accent;

    public CardScope(Vector4 accent, float padding)
    {
        this.accent = accent;
        this.padding = padding;
        drawList = ImGui.GetWindowDrawList();
        origin = ImGui.GetCursorScreenPos();
        width = ImGui.GetContentRegionAvail().X;
        drawList.ChannelsSplit(2);
        drawList.ChannelsSetCurrent(1);
        ImGui.SetCursorScreenPos(origin + new Vector2(padding));
        ImGui.BeginGroup();
        ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + width - padding * 2f);
        ImGui.PushItemWidth(width - padding * 2f);
    }

    public float InnerWidth => width - padding * 2f;

    public void Dispose()
    {
        ImGui.PopItemWidth();
        ImGui.PopTextWrapPos();
        ImGui.EndGroup();
        var bottom = ImGui.GetItemRectMax().Y + padding;
        var max = new Vector2(origin.X + width, bottom);
        drawList.ChannelsSetCurrent(0);
        var rounding = 14f * Widgets.Scale;
        drawList.AddRectFilled(origin, max, Palette.U32(Palette.Panel, 0.86f), rounding);
        Fx.VerticalGradient(drawList, origin, new Vector2(max.X, origin.Y + MathF.Min(60f * Widgets.Scale, bottom - origin.Y)),
            Palette.WithAlpha(accent, 0.12f), Palette.WithAlpha(accent, 0f), rounding, ImDrawFlags.RoundCornersTop);
        drawList.AddRect(origin, max, Palette.U32(accent, 0.35f), rounding, ImDrawFlags.None, 1f);
        drawList.AddLine(new Vector2(origin.X + rounding, origin.Y), new Vector2(origin.X + width * 0.45f, origin.Y), Palette.U32(accent, 0.9f), 1.6f);
        drawList.ChannelsMerge();
        ImGui.SetCursorScreenPos(new Vector2(origin.X, bottom));
        ImGui.Dummy(new Vector2(width, 0f));
        ImGui.Spacing();
    }
}
