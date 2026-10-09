using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Windowing;
using Puls8.Ui.Pages;

namespace Puls8.Ui;

public enum Page : byte
{
    Home,
    Events,
    Lounge,
    Wifi,
    Mods,
    About,
}

public sealed class MainWindow : Window, IDisposable
{
    private const float WindowRounding = 18f;
    private const float HeaderHeight = 62f;
    private const float TabsHeight = 40f;
    private const float ContentPadding = 16f;
    private const int StyleVarCount = 4;

    private static readonly string[] TabLabels = ["HOME", "EVENTS", "LOUNGE", "WIFI", "MODS", "ABOUT"];

    private readonly Plugin plugin;
    private readonly HomePage home;
    private readonly EventsPage events;
    private readonly LoungePage lounge;
    private readonly WifiPage wifi;
    private readonly ModsPage mods;
    private readonly AboutPage about;
    private readonly float[] tabCenters = new float[TabLabels.Length];
    private readonly float[] tabWidths = new float[TabLabels.Length];
    private Spring underlineX;
    private Spring underlineWidth;
    private Page page;
    private bool underlineSnapped;

    public MainWindow(Plugin plugin)
        : base("Puls8###Puls8Main", ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse | ImGuiWindowFlags.NoBackground)
    {
        this.plugin = plugin;
        Size = new Vector2(580f, 780f);
        SizeCondition = ImGuiCond.FirstUseEver;
        SizeConstraints = new WindowSizeConstraints { MinimumSize = new Vector2(480f, 560f), MaximumSize = new Vector2(1100f, 1400f) };
        home = new HomePage(plugin, Show);
        events = new EventsPage(plugin);
        lounge = new LoungePage(plugin);
        wifi = new WifiPage(plugin);
        mods = new ModsPage(plugin);
        about = new AboutPage(plugin);
    }

    public void Show(Page target)
    {
        page = target;
        IsOpen = true;
        if (target == Page.Mods)
        {
            mods.OnShown();
        }
    }

    public override void OnOpen()
    {
        plugin.Feed.RefreshIfStale();
        if (plugin.Installer.IsStale)
        {
            _ = plugin.Installer.CheckAsync(false);
        }
    }

    public override void PreDraw()
    {
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 0f);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, WindowRounding);
        ImGui.PushStyleVar(ImGuiStyleVar.ScrollbarSize, 8f);
    }

    public override void PostDraw() => ImGui.PopStyleVar(StyleVarCount);

    public override void Draw()
    {
        Motion.Reduced = plugin.Configuration.ReducedMotion;
        var scale = Widgets.Scale;
        var drawList = ImGui.GetWindowDrawList();
        var windowMin = ImGui.GetWindowPos();
        var windowMax = windowMin + ImGui.GetWindowSize();
        DrawBackdrop(drawList, windowMin, windowMax, scale);
        DrawHeader(drawList, windowMin, windowMax, scale);
        DrawTabs(drawList, windowMin, windowMax, scale);
        DrawContent(windowMin, windowMax, scale);
        Fx.Scanlines(drawList, windowMin + new Vector2(0f, WindowRounding), windowMax - new Vector2(0f, WindowRounding), 0.10f);
        Fx.GradientBorder(drawList, windowMin, windowMax, WindowRounding, 1.4f, 0.75f);
    }

    public void Dispose()
    {
    }

    private static void DrawBackdrop(ImDrawListPtr drawList, Vector2 min, Vector2 max, float scale)
    {
        drawList.AddRectFilled(min, max, Palette.U32(Palette.Void, 0.97f), WindowRounding);
        Fx.VerticalGradient(drawList, min, new Vector2(max.X, min.Y + 240f * scale),
            Palette.WithAlpha(Palette.Indigo, 0.6f), Palette.WithAlpha(Palette.Indigo, 0f), WindowRounding, ImDrawFlags.RoundCornersTop);
        Fx.VerticalGradient(drawList, new Vector2(min.X, max.Y - 160f * scale), max,
            Palette.WithAlpha(Palette.Magenta, 0f), Palette.WithAlpha(Palette.Magenta, 0.12f), WindowRounding, ImDrawFlags.RoundCornersBottom);
    }

    private void DrawHeader(ImDrawListPtr drawList, Vector2 min, Vector2 max, float scale)
    {
        var height = HeaderHeight * scale;
        var logoSize = 40f * scale;
        var logoMin = min + new Vector2(16f * scale, (height - logoSize) * 0.5f + 4f * scale);
        var logo = Images.Logo;
        if (logo is not null)
        {
            var center = logoMin + new Vector2(logoSize * 0.5f);
            Fx.Halo(drawList, center, logoSize * 0.5f, Palette.Magenta, 0.8f + Motion.Heartbeat());
            drawList.AddImage(logo.Handle, logoMin, logoMin + new Vector2(logoSize));
            Fx.GlitchImage(drawList, logo.Handle, logoMin, logoMin + new Vector2(logoSize), 3, 0.8f);
        }

        var profile = plugin.Feed.Current;
        using (Fonts.Title())
        {
            var font = ImGui.GetFont();
            var fontSize = ImGui.GetFontSize();
            var word = profile.Name.ToUpperInvariant();
            var wordSize = ImGui.CalcTextSize(word);
            var position = new Vector2(logoMin.X + logoSize + 12f * scale, min.Y + (height - wordSize.Y) * 0.5f + 2f * scale);
            Fx.GradientText(drawList, font, fontSize, position, word, wordSize.X, Motion.Phase(6000.0));
            Fx.GlitchText(drawList, font, fontSize, position, wordSize, word, 1);
        }

        DrawCloseButton(drawList, new Vector2(max.X - 40f * scale, min.Y + (height - 26f * scale) * 0.5f + 2f * scale), 26f * scale);
        DrawStatusChip(max.X - 52f * scale, min.Y + height * 0.5f + 2f * scale);
    }

    private void DrawStatusChip(float right, float centerY)
    {
        var status = plugin.Clock.Status;
        var live = plugin.Clock.LiveEvent;
        string text;
        Vector4 color;
        if (live is not null)
        {
            text = "LIVE NOW";
            color = Palette.Magenta;
        }
        else if (status.IsOpen)
        {
            text = "OPEN NOW";
            color = Palette.Mint;
        }
        else if (status.HasSchedule)
        {
            text = $"OPENS IN {VenueClockText(status.Boundary)}";
            color = Palette.Amber;
        }
        else
        {
            return;
        }

        var textSize = ImGui.CalcTextSize(text);
        var width = textSize.X + 34f * Widgets.Scale;
        var height = textSize.Y + 6f * Widgets.Scale;
        ImGui.SetCursorScreenPos(new Vector2(right - width, centerY - height * 0.5f));
        Widgets.Chip(text, color, true);
    }

    private static string VenueClockText(DateTimeOffset boundary)
        => Venue.VenueClock.FormatCountdown(boundary - DateTimeOffset.UtcNow).ToUpperInvariant();

    private void DrawCloseButton(ImDrawListPtr drawList, Vector2 min, float size)
    {
        ImGui.SetCursorScreenPos(min);
        var itemId = ImGui.GetID("##close");
        if (ImGui.InvisibleButton("##close", new Vector2(size)))
        {
            IsOpen = false;
        }

        var hover = Widgets.Hover(itemId, ImGui.IsItemHovered() ? 1f : 0f);
        var center = min + new Vector2(size * 0.5f);
        drawList.AddCircleFilled(center, size * 0.5f, Palette.U32(Palette.Magenta, 0.08f + hover * 0.25f), 24);
        var arm = size * 0.2f;
        var ink = Palette.U32(Palette.Mix(Palette.InkMuted, Palette.Core, hover));
        drawList.AddLine(center - new Vector2(arm), center + new Vector2(arm), ink, 1.8f);
        drawList.AddLine(center + new Vector2(-arm, arm), center + new Vector2(arm, -arm), ink, 1.8f);
    }

    private void DrawTabs(ImDrawListPtr drawList, Vector2 min, Vector2 max, float scale)
    {
        var top = min.Y + HeaderHeight * scale;
        var height = TabsHeight * scale;
        var left = min.X + ContentPadding * scale;
        var width = max.X - min.X - ContentPadding * 2f * scale;
        var slot = width / TabLabels.Length;
        var updates = plugin.Installer.PendingCount;
        using (Fonts.Label())
        {
            for (var tabIndex = 0; tabIndex < TabLabels.Length; tabIndex++)
            {
                var label = TabLabels[tabIndex];
                var slotMin = new Vector2(left + slot * tabIndex, top);
                ImGui.SetCursorScreenPos(slotMin);
                var tabId = $"##tab{tabIndex}";
                var itemId = ImGui.GetID(tabId);
                if (ImGui.InvisibleButton(tabId, new Vector2(slot, height)))
                {
                    Show((Page)tabIndex);
                }

                var hovered = ImGui.IsItemHovered();
                if (hovered)
                {
                    ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
                }

                var hover = Widgets.Hover(itemId, hovered ? 1f : 0f);
                var selected = (int)page == tabIndex;
                var labelSize = ImGui.CalcTextSize(label);
                var fontScale = MathF.Min(1f, (slot - 6f * scale) / MathF.Max(1f, labelSize.X));
                var fontSize = ImGui.GetFontSize() * fontScale;
                var scaledWidth = labelSize.X * fontScale;
                var textPosition = new Vector2(slotMin.X + (slot - scaledWidth) * 0.5f, top + (height - labelSize.Y * fontScale) * 0.5f);
                var ink = selected ? Palette.Core : Palette.Mix(Palette.InkDim, Palette.Ink, hover);
                drawList.AddText(ImGui.GetFont(), fontSize, textPosition, Palette.U32(ink), label);
                tabCenters[tabIndex] = slotMin.X + slot * 0.5f;
                tabWidths[tabIndex] = scaledWidth;

                if (tabIndex == (int)Page.Mods && updates > 0)
                {
                    var badge = new Vector2(textPosition.X + scaledWidth + 6f * scale, textPosition.Y + 2f * scale);
                    drawList.AddCircleFilled(badge, 4f * scale + Motion.Heartbeat() * 2f * scale, Palette.U32(Palette.Magenta, 0.35f), 12);
                    drawList.AddCircleFilled(badge, 3.2f * scale, Palette.U32(Palette.Magenta), 12);
                }
            }
        }

        var targetX = tabCenters[(int)page];
        var targetWidth = tabWidths[(int)page] + 10f * scale;
        if (!underlineSnapped)
        {
            underlineX.Snap(targetX);
            underlineWidth.Snap(targetWidth);
            underlineSnapped = true;
        }

        var x = Motion.Reduced ? targetX : underlineX.Step(targetX, 0.11f, Motion.Delta);
        var w = Motion.Reduced ? targetWidth : underlineWidth.Step(targetWidth, 0.11f, Motion.Delta);
        var lineY = top + height - 6f * scale;
        Fx.GlowLine(drawList, new Vector2(x - w * 0.5f, lineY), new Vector2(x + w * 0.5f, lineY), Palette.Magenta, 2f * scale);
        drawList.AddLine(new Vector2(left, top + height), new Vector2(left + width, top + height), Palette.U32(Palette.Violet, 0.25f), 1f);
    }

    private void DrawContent(Vector2 min, Vector2 max, float scale)
    {
        var top = min.Y + (HeaderHeight + TabsHeight + 8f) * scale;
        var padding = ContentPadding * scale;
        ImGui.SetCursorScreenPos(new Vector2(min.X + padding, top));
        var size = new Vector2(max.X - min.X - padding * 2f + 6f * scale, max.Y - top - padding * 0.5f);
        ImGui.PushStyleColor(ImGuiCol.ScrollbarBg, Vector4.Zero);
        ImGui.PushStyleColor(ImGuiCol.ScrollbarGrab, Palette.WithAlpha(Palette.Violet, 0.45f));
        ImGui.PushStyleColor(ImGuiCol.ScrollbarGrabHovered, Palette.WithAlpha(Palette.Magenta, 0.7f));
        ImGui.PushStyleColor(ImGuiCol.ScrollbarGrabActive, Palette.Magenta);
        ImGui.PushStyleColor(ImGuiCol.Text, Palette.Ink);
        if (ImGui.BeginChild("##puls8content", size, false, ImGuiWindowFlags.NoBackground))
        {
            ImGui.PushItemWidth(-1f);
            ImGui.Dummy(new Vector2(0f, 2f * scale));
            switch (page)
            {
                case Page.Home:
                    home.Draw();
                    break;
                case Page.Events:
                    events.Draw();
                    break;
                case Page.Lounge:
                    lounge.Draw();
                    break;
                case Page.Wifi:
                    wifi.Draw();
                    break;
                case Page.Mods:
                    mods.Draw();
                    break;
                default:
                    about.Draw();
                    break;
            }

            ImGui.Dummy(new Vector2(0f, 12f * scale));
            ImGui.PopItemWidth();
        }

        ImGui.EndChild();
        ImGui.PopStyleColor(5);
    }
}
