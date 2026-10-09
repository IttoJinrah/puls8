using System.Text.Json.Nodes;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Puls8.Staff;

namespace Puls8.Ui;

public static class EditorFields
{
    private const int StyleColorCount = 6;
    private const int StyleVarCount = 3;
    private const int DefaultMaxLength = 200;

    private static string armedDelete = string.Empty;

    public static void PushStyle()
    {
        ImGui.PushStyleColor(ImGuiCol.FrameBg, Palette.WithAlpha(Palette.Night, 0.95f));
        ImGui.PushStyleColor(ImGuiCol.FrameBgHovered, Palette.PanelRaised);
        ImGui.PushStyleColor(ImGuiCol.FrameBgActive, Palette.PanelHover);
        ImGui.PushStyleColor(ImGuiCol.Border, Palette.WithAlpha(Palette.Violet, 0.55f));
        ImGui.PushStyleColor(ImGuiCol.TextSelectedBg, Palette.WithAlpha(Palette.Magenta, 0.45f));
        ImGui.PushStyleColor(ImGuiCol.PopupBg, Palette.Panel);
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 8f * Widgets.Scale);
        ImGui.PushStyleVar(ImGuiStyleVar.FrameBorderSize, 1f);
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(10f, 7f) * Widgets.Scale);
    }

    public static void PopStyle()
    {
        ImGui.PopStyleVar(StyleVarCount);
        ImGui.PopStyleColor(StyleColorCount);
    }

    public static bool Text(JsonObject node, string key, string label, float width, string hint = "", int maxLength = DefaultMaxLength)
    {
        Label(label);
        var value = StaffSession.Text(node, key);
        ImGui.SetNextItemWidth(width);
        if (!ImGui.InputTextWithHint($"##{key}", hint, ref value, maxLength))
        {
            return false;
        }

        node[key] = value;
        return true;
    }

    public static bool Multiline(JsonObject node, string key, string label, float width, int lines = 3)
    {
        Label(label);
        var value = StaffSession.Text(node, key);
        var height = ImGui.GetTextLineHeight() * lines + 16f * Widgets.Scale;
        if (!ImGui.InputTextMultiline($"##{key}", ref value, 1200, new Vector2(width, height)))
        {
            return false;
        }

        node[key] = value;
        return true;
    }

    public static bool Number(JsonObject node, string key, string label, float width, int minimum, int maximum)
    {
        Label(label);
        var value = node[key] is JsonValue json && json.TryGetValue<int>(out var number) ? number : minimum;
        ImGui.SetNextItemWidth(width);
        if (!ImGui.InputInt($"##{key}", ref value))
        {
            return false;
        }

        node[key] = Math.Clamp(value, minimum, maximum);
        return true;
    }

    public static bool Combo(JsonObject node, string key, string label, float width, string[] options)
    {
        Label(label);
        var current = StaffSession.Text(node, key);
        var changed = false;
        ImGui.SetNextItemWidth(width);
        if (ImGui.BeginCombo($"##{key}", current.Length > 0 ? current : "Pick one"))
        {
            for (var optionIndex = 0; optionIndex < options.Length; optionIndex++)
            {
                var option = options[optionIndex];
                if (ImGui.Selectable(option, string.Equals(option, current, StringComparison.OrdinalIgnoreCase)))
                {
                    node[key] = option;
                    changed = true;
                }
            }

            ImGui.EndCombo();
        }

        return changed;
    }

    public static JsonArray Array(JsonObject node, string key)
    {
        if (node[key] is JsonArray existing)
        {
            return existing;
        }

        var created = new JsonArray();
        node[key] = created;
        return created;
    }

    // Two taps so a stray click can't throw away an event or a menu section.
    public static bool Delete(string id, float width)
    {
        var armed = string.Equals(armedDelete, id, StringComparison.Ordinal);
        if (!Widgets.Button(id, armed ? "TAP AGAIN TO DELETE" : "DELETE", FontAwesomeIcon.TrashAlt, new Vector2(width, 28f * Widgets.Scale), armed ? ButtonTone.Danger : ButtonTone.Ghost))
        {
            return false;
        }

        if (!armed)
        {
            armedDelete = id;
            return false;
        }

        armedDelete = string.Empty;
        return true;
    }

    public static bool Move(JsonArray array, int index, string id)
    {
        var size = 28f * Widgets.Scale;
        if (index > 0 && Widgets.IconButton($"{id}up", FontAwesomeIcon.ArrowUp, size, "Move up", Palette.Violet))
        {
            Swap(array, index, index - 1);
            return true;
        }

        if (index > 0)
        {
            ImGui.SameLine(0f, 6f * Widgets.Scale);
        }

        if (index < array.Count - 1 && Widgets.IconButton($"{id}down", FontAwesomeIcon.ArrowDown, size, "Move down", Palette.Violet))
        {
            Swap(array, index, index + 1);
            return true;
        }

        return false;
    }

    private static void Swap(JsonArray array, int from, int to)
    {
        var node = array[from];
        array.RemoveAt(from);
        array.Insert(to, node);
    }

    private static void Label(string label) => ImGui.TextColored(Palette.InkDim, label);
}
