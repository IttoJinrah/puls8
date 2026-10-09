using System.Text.Json.Nodes;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;

namespace Puls8.Ui.Pages;

public sealed partial class StaffPage
{
    private static readonly string[] Days = ["Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday", "Sunday"];
    private static readonly string[] Districts = ["Mist", "Lavender Beds", "Goblet", "Shirogane", "Empyreum"];
    private static readonly string[] LinkKinds = ["discord", "website", "twitch", "other"];

    private delegate bool RowDrawer(JsonObject row, float width, string id);

    private bool DrawHours(JsonObject draft)
    {
        var changed = false;
        using (var card = Widgets.Card(Palette.Electric))
        {
            Widgets.Wrapped("Opening hours are entered in the club's time zone. Players see them converted to their own.", Palette.InkMuted);
            ImGui.Spacing();
            changed |= EditorFields.Text(draft, "timeZone", "CLUB TIME ZONE", card.InnerWidth, "Europe/London");
            if (Widgets.Button("##usemyzone", "USE MY TIME ZONE", FontAwesomeIcon.Globe, new Vector2(card.InnerWidth, 30f * Widgets.Scale), ButtonTone.Ghost)
                && TimeZoneInfo.TryConvertWindowsIdToIanaId(TimeZoneInfo.Local.Id, out var iana))
            {
                draft["timeZone"] = iana;
                changed = true;
            }
        }

        var schedule = EditorFields.Array(draft, "schedule");
        if (AddButton("##addslot", "ADD OPENING SLOT"))
        {
            schedule.Add(new JsonObject { ["day"] = "Friday", ["open"] = "21:00", ["close"] = "01:00" });
            return true;
        }

        ImGui.Spacing();
        return DrawRows(schedule, "slot", Palette.Electric, DrawSlot) || changed;
    }

    private bool DrawLounge(JsonObject draft)
    {
        Widgets.SectionTitle("CREW", 121);
        var staff = EditorFields.Array(draft, "staff");
        if (AddButton("##addstaff", "ADD CREW MEMBER"))
        {
            staff.Add(new JsonObject { ["name"] = string.Empty, ["role"] = string.Empty });
            return true;
        }

        ImGui.Spacing();
        var changed = DrawRows(staff, "staff", Palette.Magenta, DrawCrewMember);

        Widgets.SectionTitle("MENU", 122);
        var menu = EditorFields.Array(draft, "menu");
        if (AddButton("##addsection", "ADD MENU SECTION"))
        {
            menu.Add(new JsonObject { ["section"] = "New section", ["items"] = new JsonArray() });
            return true;
        }

        ImGui.Spacing();
        changed |= DrawRows(menu, "menu", Palette.Violet, DrawMenuSection);

        Widgets.SectionTitle("HOUSE RULES", 123);
        changed |= DrawRules(EditorFields.Array(draft, "rules"));

        Widgets.SectionTitle("LINKS", 124);
        var links = EditorFields.Array(draft, "links");
        if (AddButton("##addlink", "ADD LINK"))
        {
            links.Add(new JsonObject { ["label"] = string.Empty, ["url"] = "https://", ["kind"] = "website" });
            return true;
        }

        ImGui.Spacing();
        return DrawRows(links, "link", Palette.Cyan, DrawLink) || changed;
    }

    private bool DrawClub(JsonObject draft)
    {
        var changed = false;
        using (var card = Widgets.Card(Palette.Magenta))
        {
            var width = card.InnerWidth;
            changed |= EditorFields.Text(draft, "name", "CLUB NAME", width);
            changed |= EditorFields.Text(draft, "tagline", "TAGLINE", width, "Synthwave nightclub on Raiden");
            changed |= EditorFields.Multiline(draft, "description", "DESCRIPTION", width, 5);
        }

        Widgets.SectionTitle("ADDRESS", 131);
        if (draft["address"] is not JsonObject address)
        {
            address = new JsonObject();
            draft["address"] = address;
        }

        using (var card = Widgets.Card(Palette.Electric))
        {
            var half = Half(card.InnerWidth);
            changed |= PairOfFields(address, half,
                static (node, width) => EditorFields.Text(node, "world", "WORLD", width, "Raiden"),
                static (node, width) => EditorFields.Text(node, "dataCenter", "DATA CENTER", width, "Light"));
            changed |= EditorFields.Combo(address, "district", "DISTRICT", card.InnerWidth, Districts);
            changed |= PairOfFields(address, half,
                static (node, width) => EditorFields.Number(node, "ward", "WARD", width, 1, 30),
                static (node, width) => EditorFields.Number(node, "plot", "PLOT", width, 1, 60));
            ImGui.TextColored(Palette.InkDim, "Travel and \"you're in the club\" detection use this address.");
        }

        Widgets.SectionTitle("CLUB WIFI", 132);
        var sync = EditorFields.Array(draft, "sync");
        for (var entryIndex = 0; entryIndex < sync.Count; entryIndex++)
        {
            if (ObjectAt(sync, entryIndex) is not { } entry)
            {
                continue;
            }

            ImGui.PushID(entryIndex);
            using (var card = Widgets.Card(Palette.Cyan))
            {
                using (Fonts.Label())
                {
                    ImGui.TextColored(Palette.Core, Staff.StaffSession.Text(entry, "service").ToUpperInvariant());
                }

                if (string.Equals(Staff.StaffSession.Text(entry, "kind"), "zonesync", StringComparison.OrdinalIgnoreCase))
                {
                    changed |= EditorFields.Multiline(entry, "note", "NOTE FOR PLAYERS", card.InnerWidth);
                }
                else
                {
                    changed |= EditorFields.Text(entry, "id", "SYNCSHELL ID", card.InnerWidth, "LLS-...");
                    changed |= EditorFields.Text(entry, "password", "PASSWORD", card.InnerWidth);
                }
            }

            ImGui.PopID();
        }

        ImGui.TextColored(Palette.InkDim, "Mod packs are configured in venue.json on GitHub, on purpose: a typo there could break installs.");
        return changed;
    }

    private static bool DrawRows(JsonArray rows, string id, Vector4 accent, RowDrawer drawRow)
    {
        var changed = false;
        for (var rowIndex = 0; rowIndex < rows.Count; rowIndex++)
        {
            if (ObjectAt(rows, rowIndex) is not { } row)
            {
                continue;
            }

            ImGui.PushID(rowIndex);
            var outcome = DrawRow(rows, row, rowIndex, $"{id}{rowIndex}", accent, drawRow);
            ImGui.PopID();
            if (outcome == RowOutcome.Structure)
            {
                return true;
            }

            changed |= outcome == RowOutcome.Edited;
        }

        return changed;
    }

    private static RowOutcome DrawRow(JsonArray rows, JsonObject row, int index, string id, Vector4 accent, RowDrawer drawRow)
    {
        using var card = Widgets.Card(accent);
        var edited = drawRow(row, card.InnerWidth, id);
        ImGui.Spacing();
        if (EditorFields.Move(rows, index, $"##{id}move"))
        {
            return RowOutcome.Structure;
        }

        if (EditorFields.Delete($"##{id}delete", card.InnerWidth))
        {
            rows.RemoveAt(index);
            return RowOutcome.Structure;
        }

        return edited ? RowOutcome.Edited : RowOutcome.None;
    }

    private static bool DrawSlot(JsonObject slot, float width, string id)
    {
        var changed = EditorFields.Combo(slot, "day", "DAY", width, Days);
        return PairOfFields(slot, Half(width),
            static (node, half) => EditorFields.Text(node, "open", "OPENS", half, "21:00", 5),
            static (node, half) => EditorFields.Text(node, "close", "CLOSES", half, "01:00", 5)) || changed;
    }

    private static bool DrawCrewMember(JsonObject member, float width, string id)
        => PairOfFields(member, Half(width),
            static (node, half) => EditorFields.Text(node, "name", "NAME", half, "Character name"),
            static (node, half) => EditorFields.Text(node, "role", "ROLE", half, "Bartender"));

    private static bool DrawLink(JsonObject link, float width, string id)
    {
        var changed = PairOfFields(link, Half(width),
            static (node, half) => EditorFields.Text(node, "label", "LABEL", half, "Discord"),
            static (node, half) => EditorFields.Combo(node, "kind", "KIND", half, LinkKinds));
        changed |= EditorFields.Text(link, "url", "URL (HTTPS ONLY)", width, "https://");
        return changed;
    }

    private static bool DrawMenuSection(JsonObject menuSection, float width, string id)
    {
        var changed = EditorFields.Text(menuSection, "section", "SECTION NAME", width, "Drinks");
        var items = EditorFields.Array(menuSection, "items");
        for (var itemIndex = 0; itemIndex < items.Count; itemIndex++)
        {
            if (ObjectAt(items, itemIndex) is not { } item)
            {
                continue;
            }

            ImGui.PushID(itemIndex);
            ImGui.Spacing();
            ImGui.Separator();
            changed |= PairOfFields(item, Half(width),
                static (node, half) => EditorFields.Text(node, "name", "ITEM", half, "Neon Sunset"),
                static (node, half) => EditorFields.Text(node, "price", "PRICE", half, "50k"));
            changed |= EditorFields.Text(item, "description", "DESCRIPTION", width);
            var removed = EditorFields.Delete($"##{id}item{itemIndex}delete", width);
            ImGui.PopID();
            if (removed)
            {
                items.RemoveAt(itemIndex);
                return true;
            }
        }

        ImGui.Spacing();
        if (Widgets.Button($"##{id}additem", "ADD ITEM", FontAwesomeIcon.Plus, new Vector2(width, 30f * Widgets.Scale), ButtonTone.Ghost))
        {
            items.Add(new JsonObject { ["name"] = string.Empty, ["description"] = string.Empty, ["price"] = string.Empty });
            return true;
        }

        return changed;
    }

    private static bool DrawRules(JsonArray rules)
    {
        using var card = Widgets.Card(Palette.Electric);
        var changed = false;
        for (var ruleIndex = 0; ruleIndex < rules.Count; ruleIndex++)
        {
            ImGui.PushID(ruleIndex);
            var text = rules[ruleIndex] is JsonValue value && value.TryGetValue<string>(out var rule) ? rule : string.Empty;
            ImGui.SetNextItemWidth(card.InnerWidth - 36f * Widgets.Scale);
            if (ImGui.InputTextWithHint("##rule", "Be kind to staff and guests.", ref text, 200))
            {
                rules[ruleIndex] = text;
                changed = true;
            }

            ImGui.SameLine(0f, 8f * Widgets.Scale);
            var removed = Widgets.IconButton($"##rule{ruleIndex}remove", FontAwesomeIcon.Times, 28f * Widgets.Scale, "Remove rule", Palette.Danger);
            ImGui.PopID();
            if (removed)
            {
                rules.RemoveAt(ruleIndex);
                return true;
            }
        }

        if (Widgets.Button("##addrule", "ADD RULE", FontAwesomeIcon.Plus, new Vector2(card.InnerWidth, 30f * Widgets.Scale), ButtonTone.Ghost))
        {
            rules.Add(string.Empty);
            return true;
        }

        return changed;
    }

    private static bool PairOfFields(JsonObject node, float half, Func<JsonObject, float, bool> left, Func<JsonObject, float, bool> right)
    {
        ImGui.BeginGroup();
        var changed = left(node, half);
        ImGui.EndGroup();
        ImGui.SameLine(0f, 8f * Widgets.Scale);
        ImGui.BeginGroup();
        changed |= right(node, half);
        ImGui.EndGroup();
        return changed;
    }
}
