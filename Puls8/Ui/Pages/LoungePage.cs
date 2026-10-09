using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Puls8.Venue;

namespace Puls8.Ui.Pages;

public sealed class LoungePage
{
    private const float PortraitAspect = 4f / 3f;
    private const float MinimumPortraitWidth = 150f;
    private const int MaximumPortraitColumns = 4;
    private const float ThumbnailSize = 58f;

    private static readonly Vector4[] RoleColors = [Palette.Magenta, Palette.Cyan, Palette.Violet, Palette.Electric];

    private readonly Plugin plugin;

    public LoungePage(Plugin plugin)
    {
        this.plugin = plugin;
    }

    public void Draw()
    {
        var profile = plugin.Feed.Current;
        DrawCrew(profile.Staff);
        DrawMenu(profile.Menu);
        DrawRules(profile.Rules);
        DrawLinks(profile.Links);
    }

    private void DrawCrew(StaffMember[] staff)
    {
        if (staff.Length == 0)
        {
            return;
        }

        Widgets.SectionTitle("THE CREW", 31);
        var scale = Widgets.Scale;
        var width = ImGui.GetContentRegionAvail().X;
        var gap = 12f * scale;
        var columns = Math.Clamp((int)((width + gap) / (MinimumPortraitWidth * scale + gap)), 2, MaximumPortraitColumns);
        var cardWidth = (width - gap * (columns - 1)) / columns;
        var cardSize = new Vector2(cardWidth, cardWidth * PortraitAspect);
        var origin = ImGui.GetCursorScreenPos();
        var rows = (staff.Length + columns - 1) / columns;
        for (var memberIndex = 0; memberIndex < staff.Length; memberIndex++)
        {
            var column = memberIndex % columns;
            var row = memberIndex / columns;
            var min = origin + new Vector2(column * (cardWidth + gap), row * (cardSize.Y + gap));
            ImGui.SetCursorScreenPos(min);
            DrawPortrait(staff[memberIndex], memberIndex, min, cardSize);
        }

        ImGui.SetCursorScreenPos(origin);
        ImGui.Dummy(new Vector2(width, rows * cardSize.Y + (rows - 1) * gap));
        ImGui.Spacing();
    }

    private void DrawPortrait(StaffMember member, int index, Vector2 min, Vector2 size)
    {
        var drawList = ImGui.GetWindowDrawList();
        var scale = Widgets.Scale;
        var id = $"##crew{index}";
        var itemId = ImGui.GetID(id);
        ImGui.InvisibleButton(id, size);
        var hover = Widgets.Hover(itemId, ImGui.IsItemHovered() ? 1f : 0f);
        var lift = new Vector2(0f, -3f * hover * scale);
        min += lift;
        var max = min + size;
        var rounding = 14f * scale;
        var accent = RoleColors[index % RoleColors.Length];

        Fx.GlowRect(drawList, min, max, accent, rounding, 0.35f + hover * 0.65f);
        var photo = plugin.Images.Get(member.Image);
        if (photo is not null)
        {
            Fx.ImageCover(drawList, photo.Handle, new Vector2(photo.Width, photo.Height), min, max, rounding);
        }
        else
        {
            Fx.VerticalGradient(drawList, min, max, Palette.Mix(Palette.Indigo, accent, 0.35f), Palette.Night, rounding, ImDrawFlags.RoundCornersAll);
            using (Fonts.Hero())
            {
                var initial = member.Name.Length > 0 ? char.ToUpperInvariant(member.Name[0]).ToString() : "?";
                var initialSize = ImGui.CalcTextSize(initial);
                var center = new Vector2((min.X + max.X) * 0.5f, min.Y + size.Y * 0.4f);
                Fx.GlowText(drawList, ImGui.GetFont(), ImGui.GetFontSize(), center - initialSize * 0.5f, initial, Palette.Core, accent, 3f * scale);
            }
        }

        var fadeTop = min.Y + size.Y * 0.55f;
        Fx.VerticalGradient(drawList, new Vector2(min.X, fadeTop), max, Palette.WithAlpha(Palette.Void, 0f), Palette.WithAlpha(Palette.Void, 0.92f), rounding, ImDrawFlags.RoundCornersBottom);
        Fx.Scanlines(drawList, min, max, 0.08f);

        var padding = 10f * scale;
        var roleText = member.Role.ToUpperInvariant();
        var roleSize = ImGui.CalcTextSize(roleText);
        var rolePosition = new Vector2(min.X + padding, max.Y - padding - roleSize.Y);
        drawList.AddText(rolePosition, Palette.U32(accent), roleText);
        using (Fonts.Lead())
        {
            var nameSize = ImGui.CalcTextSize(member.Name);
            var namePosition = new Vector2(min.X + padding, rolePosition.Y - nameSize.Y - 2f * scale);
            drawList.PushClipRect(min, max, true);
            drawList.AddText(ImGui.GetFont(), ImGui.GetFontSize(), namePosition, Palette.U32(Palette.Core), member.Name);
            drawList.PopClipRect();
        }

        drawList.AddRectFilled(new Vector2(min.X + padding, max.Y - 3f * scale), new Vector2(min.X + padding + 28f * scale, max.Y - 1.5f * scale), Palette.U32(accent), 1f);
        Fx.GradientBorder(drawList, min, max, rounding, 1.2f, 0.35f + hover * 0.6f);
    }

    private void DrawMenu(MenuSection[] menu)
    {
        for (var sectionIndex = 0; sectionIndex < menu.Length; sectionIndex++)
        {
            var section = menu[sectionIndex];
            Widgets.SectionTitle(section.Section.ToUpperInvariant(), 40 + sectionIndex);
            using var card = Widgets.Card(Palette.Violet);
            var items = section.Items;
            for (var itemIndex = 0; itemIndex < items.Length; itemIndex++)
            {
                if (itemIndex > 0)
                {
                    DrawDivider(card.InnerWidth);
                }

                DrawMenuItem(items[itemIndex], card.InnerWidth);
            }
        }
    }

    // Laid out by hand: the price tag is drawn at a fixed spot, so it can never inherit the card's text wrap.
    private void DrawMenuItem(MenuItem item, float width)
    {
        var drawList = ImGui.GetWindowDrawList();
        var scale = Widgets.Scale;
        var gap = 12f * scale;
        var rowStart = ImGui.GetCursorScreenPos();
        var rowStartLocalX = ImGui.GetCursorPosX();

        var tagWidth = 0f;
        var tagBottom = rowStart.Y;
        if (item.Price.Length > 0)
        {
            var priceSize = ImGui.CalcTextSize(item.Price);
            var tagPadding = new Vector2(10f, 4f) * scale;
            var tagSize = priceSize + tagPadding * 2f;
            var tagMin = new Vector2(rowStart.X + width - tagSize.X, rowStart.Y);
            var tagMax = tagMin + tagSize;
            drawList.AddRectFilled(tagMin, tagMax, Palette.U32(Palette.Magenta, 0.14f), tagSize.Y * 0.5f);
            drawList.AddRect(tagMin, tagMax, Palette.U32(Palette.Magenta, 0.7f), tagSize.Y * 0.5f, ImDrawFlags.None, 1f);
            drawList.AddText(tagMin + tagPadding, Palette.U32(Palette.Core), item.Price);
            tagWidth = tagSize.X;
            tagBottom = tagMax.Y;
        }

        var textOffset = 0f;
        var thumbnailBottom = rowStart.Y;
        var photo = plugin.Images.Get(item.Image);
        if (photo is not null)
        {
            var thumbnail = ThumbnailSize * scale;
            var thumbnailMax = rowStart + new Vector2(thumbnail);
            Fx.ImageCover(drawList, photo.Handle, new Vector2(photo.Width, photo.Height), rowStart, thumbnailMax, 10f * scale, 0.5f);
            drawList.AddRect(rowStart, thumbnailMax, Palette.U32(Palette.Violet, 0.6f), 10f * scale, ImDrawFlags.None, 1f);
            textOffset = thumbnail + gap;
            thumbnailBottom = thumbnailMax.Y;
        }

        ImGui.SetCursorScreenPos(rowStart + new Vector2(textOffset, 0f));
        ImGui.BeginGroup();
        ImGui.PushTextWrapPos(rowStartLocalX + width - tagWidth - gap);
        using (Fonts.Lead())
        {
            ImGui.TextColored(Palette.Core, item.Name);
        }

        if (item.Description.Length > 0)
        {
            ImGui.PushStyleColor(ImGuiCol.Text, Palette.InkMuted);
            ImGui.TextWrapped(item.Description);
            ImGui.PopStyleColor();
        }

        ImGui.PopTextWrapPos();
        ImGui.EndGroup();

        var bottom = MathF.Max(ImGui.GetItemRectMax().Y, MathF.Max(tagBottom, thumbnailBottom));
        ImGui.SetCursorScreenPos(new Vector2(rowStart.X, bottom));
        ImGui.Dummy(new Vector2(width, 2f * scale));
    }

    private static void DrawDivider(float width)
    {
        var scale = Widgets.Scale;
        ImGui.Dummy(new Vector2(width, 4f * scale));
        var start = ImGui.GetCursorScreenPos();
        ImGui.GetWindowDrawList().AddRectFilledMultiColor(start, start + new Vector2(width, 1f),
            Palette.U32(Palette.Violet, 0f), Palette.U32(Palette.Violet, 0.5f), Palette.U32(Palette.Violet, 0.5f), Palette.U32(Palette.Violet, 0f));
        ImGui.Dummy(new Vector2(width, 6f * scale));
    }

    private static void DrawRules(string[] rules)
    {
        if (rules.Length == 0)
        {
            return;
        }

        Widgets.SectionTitle("HOUSE RULES", 51);
        using var card = Widgets.Card(Palette.Electric);
        for (var ruleIndex = 0; ruleIndex < rules.Length; ruleIndex++)
        {
            using (Fonts.Label())
            {
                ImGui.TextColored(Palette.Magenta, $"{ruleIndex + 1:00}");
            }

            ImGui.SameLine(0f, 10f * Widgets.Scale);
            ImGui.PushStyleColor(ImGuiCol.Text, Palette.Ink);
            ImGui.TextWrapped(rules[ruleIndex]);
            ImGui.PopStyleColor();
        }
    }

    private static void DrawLinks(VenueLink[] links)
    {
        if (links.Length == 0)
        {
            return;
        }

        Widgets.SectionTitle("LINKS", 61);
        var width = ImGui.GetContentRegionAvail().X;
        for (var linkIndex = 0; linkIndex < links.Length; linkIndex++)
        {
            var link = links[linkIndex];
            var icon = string.Equals(link.Kind, "discord", StringComparison.OrdinalIgnoreCase) ? FontAwesomeIcon.CommentDots : FontAwesomeIcon.ExternalLinkAlt;
            if (Widgets.Button($"##link{linkIndex}", link.Label.ToUpperInvariant(), icon, new Vector2(width, 38f * Widgets.Scale), ButtonTone.Ghost))
            {
                Links.Open(link.Url);
            }

            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip(link.Url);
            }

            ImGui.Spacing();
        }
    }
}
