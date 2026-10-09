using Dalamud.Bindings.ImGui;

namespace Puls8.Ui;

public static class Fx
{
    private const float GlitchPeriodMs = 2600f;
    private const float GlitchActiveFraction = 0.16f;
    private const int GlitchSteps = 8;
    private const int TextGlitchBands = 3;
    private const int ImageGlitchBands = 7;
    private const int EcgSamples = 160;
    private const int StarCount = 46;

    private static readonly Vector2[] EcgPoints = new Vector2[EcgSamples];
    private static readonly Vector2[] GlowOffsets =
    [
        new(1f, 0f), new(-1f, 0f), new(0f, 1f), new(0f, -1f),
        new(0.7f, 0.7f), new(-0.7f, 0.7f), new(0.7f, -0.7f), new(-0.7f, -0.7f),
    ];

    public static void GlowLine(ImDrawListPtr drawList, Vector2 from, Vector2 to, Vector4 color, float thickness, float alpha = 1f)
    {
        drawList.AddLine(from, to, Palette.U32(color, 0.12f * alpha), thickness * 3.6f);
        drawList.AddLine(from, to, Palette.U32(color, 0.32f * alpha), thickness * 1.9f);
        drawList.AddLine(from, to, Palette.U32(Palette.Mix(color, Palette.Core, 0.55f), alpha), thickness);
    }

    public static void GlowRect(ImDrawListPtr drawList, Vector2 min, Vector2 max, Vector4 color, float rounding, float intensity)
    {
        if (intensity <= 0.01f)
        {
            return;
        }

        for (var layer = 3; layer >= 1; layer--)
        {
            var spread = layer * 2.4f;
            drawList.AddRect(min - new Vector2(spread), max + new Vector2(spread), Palette.U32(color, 0.07f * intensity * (4 - layer)), rounding + spread, ImDrawFlags.None, 2f);
        }
    }

    public static void GradientBorder(ImDrawListPtr drawList, Vector2 min, Vector2 max, float rounding, float thickness, float alpha)
    {
        var firstVertex = drawList.VtxBuffer.Size;
        drawList.AddRect(min, max, Palette.U32(Palette.Core, alpha), rounding, ImDrawFlags.None, thickness);
        Recolor(drawList, firstVertex, min, max, alpha);
    }

    public static void GradientFill(ImDrawListPtr drawList, Vector2 min, Vector2 max, float rounding, float alpha)
    {
        var firstVertex = drawList.VtxBuffer.Size;
        drawList.AddRectFilled(min, max, Palette.U32(Palette.Core, alpha), rounding);
        Recolor(drawList, firstVertex, min, max, alpha);
    }

    // Rounded rect whose colour fades top to bottom; flat multi-colour rects would poke out past rounded corners.
    public static void VerticalGradient(ImDrawListPtr drawList, Vector2 min, Vector2 max, Vector4 top, Vector4 bottom, float rounding, ImDrawFlags corners)
    {
        var firstVertex = drawList.VtxBuffer.Size;
        drawList.AddRectFilled(min, max, Palette.U32(Palette.Core), rounding, corners);
        var height = MathF.Max(1f, max.Y - min.Y);
        var vertices = drawList.VtxBuffer.AsSpan();
        for (var vertexIndex = firstVertex; vertexIndex < vertices.Length; vertexIndex++)
        {
            ref var vertex = ref vertices[vertexIndex];
            vertex.Col = Palette.U32(Vector4.Lerp(top, bottom, Math.Clamp((vertex.Pos.Y - min.Y) / height, 0f, 1f)));
        }
    }

    public static void GradientText(ImDrawListPtr drawList, ImFontPtr font, float size, Vector2 position, string text, float width, float shift = 0f)
    {
        var firstVertex = drawList.VtxBuffer.Size;
        drawList.AddText(font, size, position, Palette.U32(Palette.Core), text);
        if (width <= 0f)
        {
            return;
        }

        var vertices = drawList.VtxBuffer.AsSpan();
        for (var vertexIndex = firstVertex; vertexIndex < vertices.Length; vertexIndex++)
        {
            ref var vertex = ref vertices[vertexIndex];
            var progress = (vertex.Pos.X - position.X) / width;
            vertex.Col = Palette.U32(Palette.Sunset(Fraction(progress * 0.8f + shift)));
        }
    }

    public static void GlowText(ImDrawListPtr drawList, ImFontPtr font, float size, Vector2 position, string text, Vector4 color, Vector4 glow, float radius)
    {
        var glowColor = Palette.U32(glow, 0.16f);
        for (var offsetIndex = 0; offsetIndex < GlowOffsets.Length; offsetIndex++)
        {
            drawList.AddText(font, size, position + GlowOffsets[offsetIndex] * radius, glowColor, text);
        }

        drawList.AddText(font, size, position, Palette.U32(color), text);
    }

    // RGB-split tear in short bursts, after the Aetherphone glitch badge: stepped, banded, deterministic.
    public static void GlitchText(ImDrawListPtr drawList, ImFontPtr font, float size, Vector2 position, Vector2 textSize, string text, int seed, float strength = 1f)
    {
        if (!TryGlitchStep(seed, out var step))
        {
            return;
        }

        var bandHeight = textSize.Y / TextGlitchBands;
        var maxShift = size * 0.12f * strength;
        var leading = Palette.U32(Palette.Magenta, 0.8f);
        var trailing = Palette.U32(Palette.Cyan, 0.75f);
        for (var bandIndex = 0; bandIndex < TextGlitchBands; bandIndex++)
        {
            var shift = Motion.Hash(step, bandIndex + seed) * maxShift;
            var top = position.Y + bandHeight * bandIndex;
            drawList.PushClipRect(new Vector2(position.X - maxShift, top), new Vector2(position.X + textSize.X + maxShift, top + bandHeight), true);
            drawList.AddText(font, size, position + new Vector2(shift, 0f), leading, text);
            drawList.AddText(font, size, position - new Vector2(shift, 0f), trailing, text);
            drawList.PopClipRect();
        }
    }

    // Slices the image into bands, shifting a few and ghosting magenta/cyan copies, like a VHS tracking error.
    public static void GlitchImage(ImDrawListPtr drawList, ImTextureID texture, Vector2 min, Vector2 max, int seed, float strength)
    {
        if (!TryGlitchStep(seed, out var step))
        {
            return;
        }

        var size = max - min;
        var bandHeight = size.Y / ImageGlitchBands;
        var maxShift = size.X * 0.035f * strength;
        for (var bandIndex = 0; bandIndex < ImageGlitchBands; bandIndex++)
        {
            var noise = Motion.Hash(step, bandIndex + seed * 7);
            if (MathF.Abs(noise) < 0.45f)
            {
                continue;
            }

            var shift = noise * maxShift;
            var top = min.Y + bandHeight * bandIndex;
            var uvTop = (float)bandIndex / ImageGlitchBands;
            var uvBottom = (float)(bandIndex + 1) / ImageGlitchBands;
            var bandMin = new Vector2(min.X, top);
            var bandMax = new Vector2(max.X, top + bandHeight);
            drawList.AddRectFilled(bandMin, bandMax, Palette.U32(Palette.Void, 0.55f));
            drawList.AddImage(texture, bandMin + new Vector2(shift * 1.6f, 0f), bandMax + new Vector2(shift * 1.6f, 0f), new Vector2(0f, uvTop), new Vector2(1f, uvBottom), Palette.U32(Palette.Magenta, 0.55f));
            drawList.AddImage(texture, bandMin - new Vector2(shift, 0f), bandMax - new Vector2(shift, 0f), new Vector2(0f, uvTop), new Vector2(1f, uvBottom), Palette.U32(Palette.Cyan, 0.45f));
            drawList.AddImage(texture, bandMin + new Vector2(shift * 0.4f, 0f), bandMax + new Vector2(shift * 0.4f, 0f), new Vector2(0f, uvTop), new Vector2(1f, uvBottom), Palette.U32(Palette.Core, 0.85f));
        }
    }

    // Crops like CSS object-fit: cover. focusY biases portraits toward the top, where faces usually are.
    public static void ImageCover(ImDrawListPtr drawList, ImTextureID texture, Vector2 textureSize, Vector2 min, Vector2 max, float rounding, float focusY = 0.25f)
    {
        var frame = max - min;
        if (textureSize.X <= 0f || textureSize.Y <= 0f || frame.X <= 0f || frame.Y <= 0f)
        {
            return;
        }

        var scale = MathF.Max(frame.X / textureSize.X, frame.Y / textureSize.Y);
        var visible = new Vector2(frame.X / (textureSize.X * scale), frame.Y / (textureSize.Y * scale));
        var uv0 = new Vector2((1f - visible.X) * 0.5f, (1f - visible.Y) * focusY);
        drawList.AddImageRounded(texture, min, max, uv0, uv0 + visible, Palette.U32(Palette.Core), rounding);
    }

    public static void Scanlines(ImDrawListPtr drawList, Vector2 min, Vector2 max, float alpha)
    {
        var line = Palette.U32(Palette.Void, alpha);
        for (var y = min.Y; y < max.Y; y += 3f)
        {
            drawList.AddLine(new Vector2(min.X, y), new Vector2(max.X, y), line, 1f);
        }

        if (Motion.Reduced)
        {
            return;
        }

        var height = max.Y - min.Y;
        var rollY = min.Y + Motion.Phase(7000.0) * (height + 60f) - 30f;
        drawList.AddRectFilledMultiColor(new Vector2(min.X, rollY - 30f), new Vector2(max.X, rollY), 0u, 0u, Palette.U32(Palette.Core, 0.035f), Palette.U32(Palette.Core, 0.035f));
    }

    // Synthwave floor: rays from the vanishing point plus horizontal lines spaced by 1/z, scrolling toward the viewer.
    public static void Grid(ImDrawListPtr drawList, Vector2 min, Vector2 max, float horizonY, float alpha)
    {
        var width = max.X - min.X;
        var centerX = min.X + width * 0.5f;
        var depth = max.Y - horizonY;
        const int rays = 18;
        for (var rayIndex = -rays; rayIndex <= rays; rayIndex++)
        {
            var bottomX = centerX + rayIndex * width * 0.11f;
            drawList.AddLine(new Vector2(centerX + rayIndex * width * 0.012f, horizonY), new Vector2(bottomX, max.Y), Palette.U32(Palette.Violet, 0.42f * alpha), 1f);
        }

        var scroll = Motion.Phase(1800.0);
        const int rows = 12;
        for (var rowIndex = 0; rowIndex < rows; rowIndex++)
        {
            var z = (rowIndex + 1f - scroll) / rows;
            var perspective = z * z;
            var y = horizonY + depth * perspective;
            var rowAlpha = Math.Clamp(perspective * 1.6f, 0f, 1f) * alpha;
            drawList.AddLine(new Vector2(min.X, y), new Vector2(max.X, y), Palette.U32(Palette.Magenta, 0.55f * rowAlpha), 1.2f);
        }

        drawList.AddRectFilledMultiColor(new Vector2(min.X, horizonY), new Vector2(max.X, horizonY + depth * 0.35f),
            Palette.U32(Palette.Magenta, 0.22f * alpha), Palette.U32(Palette.Magenta, 0.22f * alpha), 0u, 0u);
    }

    public static void Stars(ImDrawListPtr drawList, Vector2 min, Vector2 max, int seed)
    {
        var size = max - min;
        var time = Motion.Seconds;
        for (var starIndex = 0; starIndex < StarCount; starIndex++)
        {
            var x = (Motion.Hash(starIndex, seed) + 1f) * 0.5f;
            var y = (Motion.Hash(starIndex, seed + 31) + 1f) * 0.5f;
            var twinkle = Motion.Reduced ? 0.6f : 0.35f + 0.65f * (MathF.Sin(time * (1.2f + x * 2.3f) + starIndex) + 1f) * 0.5f;
            var radius = 0.6f + (Motion.Hash(starIndex, seed + 77) + 1f) * 0.55f;
            drawList.AddCircleFilled(min + new Vector2(x * size.X, y * size.Y), radius, Palette.U32(Palette.Core, 0.55f * twinkle), 6);
        }
    }

    // A cardiac monitor trace whose head sweeps left to right once per heartbeat, leaving a fading phosphor tail.
    public static void Ecg(ImDrawListPtr drawList, Vector2 min, Vector2 max, Vector4 color)
    {
        var width = max.X - min.X;
        var baseline = (min.Y + max.Y) * 0.5f;
        var amplitude = (max.Y - min.Y) * 0.5f;
        var head = Motion.Reduced ? 1f : Motion.Phase(Motion.HeartbeatPeriodMs * 2.0);
        for (var sampleIndex = 0; sampleIndex < EcgSamples; sampleIndex++)
        {
            var progress = (float)sampleIndex / (EcgSamples - 1);
            EcgPoints[sampleIndex] = new Vector2(min.X + progress * width, baseline - Waveform(progress * 2f) * amplitude);
        }

        for (var sampleIndex = 1; sampleIndex < EcgSamples; sampleIndex++)
        {
            var progress = (float)sampleIndex / (EcgSamples - 1);
            var behind = head - progress;
            if (behind < 0f)
            {
                behind += 1f;
            }

            var fade = Motion.Reduced ? 0.8f : MathF.Pow(1f - behind, 2.2f);
            if (fade < 0.03f)
            {
                continue;
            }

            GlowLine(drawList, EcgPoints[sampleIndex - 1], EcgPoints[sampleIndex], color, 1.6f, fade);
        }

        if (!Motion.Reduced)
        {
            var headIndex = Math.Clamp((int)(head * (EcgSamples - 1)), 0, EcgSamples - 1);
            drawList.AddCircleFilled(EcgPoints[headIndex], 5f, Palette.U32(color, 0.25f), 12);
            drawList.AddCircleFilled(EcgPoints[headIndex], 2.4f, Palette.U32(Palette.Core), 12);
        }
    }

    public static void Halo(ImDrawListPtr drawList, Vector2 center, float radius, Vector4 color, float intensity)
    {
        for (var ring = 6; ring >= 1; ring--)
        {
            var ringRadius = radius * (1f + ring * 0.055f);
            drawList.AddCircleFilled(center, ringRadius, Palette.U32(color, 0.035f * intensity), 64);
        }
    }

    private static bool TryGlitchStep(int seed, out int step)
    {
        step = 0;
        if (Motion.Reduced)
        {
            return false;
        }

        var phase = Fraction((float)(Environment.TickCount64 % (long)GlitchPeriodMs) / GlitchPeriodMs + seed * 0.137f);
        if (phase < 1f - GlitchActiveFraction)
        {
            return false;
        }

        step = (int)(phase * GlitchSteps * 4f) + seed * 13 + (int)(Environment.TickCount64 / (long)GlitchPeriodMs);
        return true;
    }

    private static float Waveform(float cycle)
    {
        var local = cycle - MathF.Floor(cycle);
        return Bump(local, 0.18f, 0.035f) * 0.12f
             - Bump(local, 0.285f, 0.012f) * 0.18f
             + Bump(local, 0.31f, 0.018f) * 0.95f
             - Bump(local, 0.34f, 0.014f) * 0.32f
             + Bump(local, 0.5f, 0.05f) * 0.22f;
    }

    private static float Bump(float value, float center, float width)
    {
        var distance = (value - center) / width;
        return distance is < -1f or > 1f ? 0f : 0.5f * (1f + MathF.Cos(distance * MathF.PI));
    }

    private static void Recolor(ImDrawListPtr drawList, int firstVertex, Vector2 min, Vector2 max, float alpha)
    {
        var width = MathF.Max(1f, max.X - min.X);
        var vertices = drawList.VtxBuffer.AsSpan();
        for (var vertexIndex = firstVertex; vertexIndex < vertices.Length; vertexIndex++)
        {
            ref var vertex = ref vertices[vertexIndex];
            vertex.Col = Palette.U32(Palette.Sunset((vertex.Pos.X - min.X) / width), alpha);
        }
    }

    private static float Fraction(float value) => value - MathF.Floor(value);
}
