using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Puls8.Venue;

namespace Puls8.Ui.Pages;

public sealed class LoungePage
{
    private readonly Plugin plugin;

    public LoungePage(Plugin plugin)
    {
        this.plugin = plugin;
    }

    public void Draw()
    {
        var profile = plugin.Feed.Current;
        DrawStaff(profile.Staff);
        DrawMenu(profile.Menu);
        DrawRules(profile.Rules);
        DrawLinks(profile.Links);
    }

    private static void DrawStaff(StaffMember[] staff)
    {
        if (staff.Length == 0)
        {
            return;
        }

        Widgets.SectionTitle("THE CREW", 31);
        using var card = Widgets.Card(Palette.Magenta);
        var columns = card.InnerWidth > 420f * Widgets.Scale ? 2 : 1;
        var columnWidth = card.InnerWidth / columns;
        var startX = ImGui.GetCursorPosX();
        for (var memberIndex = 0; memberIndex < staff.Length; memberIndex++)
        {
            var column = memberIndex % columns;
            if (column > 0)
            {
                ImGui.SameLine(startX + columnWidth * column);
            }

            var member = staff[memberIndex];
            ImGui.BeginGroup();
            DrawAvatar(member.Name, memberIndex);
            ImGui.SameLine(0f, 10f * Widgets.Scale);
            ImGui.BeginGroup();
            ImGui.TextColored(Palette.Core, member.Name);
            ImGui.TextColored(RoleColor(memberIndex), member.Role.ToUpperInvariant());
            ImGui.EndGroup();
            ImGui.EndGroup();
        }
    }

    private static void DrawAvatar(string name, int index)
    {
        var drawList = ImGui.GetWindowDrawList();
        var size = 34f * Widgets.Scale;
        var min = ImGui.GetCursorScreenPos();
        var center = min + new Vector2(size * 0.5f);
        drawList.AddCircleFilled(center, size * 0.5f, Palette.U32(Palette.Mix(Palette.Indigo, RoleColor(index), 0.45f)), 32);
        drawList.AddCircle(center, size * 0.5f, Palette.U32(RoleColor(index), 0.9f), 32, 1.4f);
        var initial = name.Length > 0 ? char.ToUpperInvariant(name[0]).ToString() : "?";
        using (Fonts.Label())
        {
            var initialSize = ImGui.CalcTextSize(initial);
            drawList.AddText(center - initialSize * 0.5f, Palette.U32(Palette.Core), initial);
        }

        ImGui.Dummy(new Vector2(size));
    }

    private static Vector4 RoleColor(int index) => (index % 4) switch
    {
        0 => Palette.Magenta,
        1 => Palette.Cyan,
        2 => Palette.Violet,
        _ => Palette.Electric,
    };

    private static void DrawMenu(MenuSection[] menu)
    {
        for (var sectionIndex = 0; sectionIndex < menu.Length; sectionIndex++)
        {
            var section = menu[sectionIndex];
            Widgets.SectionTitle(section.Section.ToUpperInvariant(), 40 + sectionIndex);
            using var card = Widgets.Card(Palette.Violet);
            var items = section.Items;
            for (var itemIndex = 0; itemIndex < items.Length; itemIndex++)
            {
                var item = items[itemIndex];
                if (itemIndex > 0)
                {
                    ImGui.Spacing();
                }

                var rowX = ImGui.GetCursorPosX();
                ImGui.TextColored(Palette.Core, item.Name);
                if (item.Price.Length > 0)
                {
                    ImGui.SameLine(rowX + card.InnerWidth - ImGui.CalcTextSize(item.Price).X);
                    ImGui.TextColored(Palette.Magenta, item.Price);
                }

                if (item.Description.Length > 0)
                {
                    Widgets.Wrapped(item.Description, Palette.InkMuted);
                }
            }
        }
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
            Widgets.Wrapped(rules[ruleIndex], Palette.Ink);
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
