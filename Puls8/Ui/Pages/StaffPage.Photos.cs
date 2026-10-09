using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.ImGuiFileDialog;
using Puls8.Staff;

namespace Puls8.Ui.Pages;

public sealed partial class StaffPage
{
    private const string ImageProperty = "image";
    private const string ImageFilters = "Photos{.png,.jpg,.jpeg}";

    private readonly FileDialogManager fileDialog = new();
    private JsonObject? uploadTarget;

    private bool DrawPhoto(JsonObject node, string folder, string label, float width, string id)
    {
        var scale = Widgets.Scale;
        var drawList = ImGui.GetWindowDrawList();
        var size = 56f * scale;
        var gap = 10f * scale;
        var path = StaffSession.Text(node, ImageProperty);
        var min = ImGui.GetCursorScreenPos();
        var max = min + new Vector2(size);
        var photo = plugin.Images.Get(path);
        if (photo is not null)
        {
            Fx.ImageCover(drawList, photo.Handle, new Vector2(photo.Width, photo.Height), min, max, 8f * scale);
        }
        else
        {
            drawList.AddRectFilled(min, max, Palette.U32(Palette.Night), 8f * scale);
            using (Fonts.Icon())
            {
                var glyph = FontAwesomeIcon.Camera.ToIconString();
                drawList.AddText((min + max) * 0.5f - ImGui.CalcTextSize(glyph) * 0.5f, Palette.U32(Palette.InkDim), glyph);
            }
        }

        drawList.AddRect(min, max, Palette.U32(Palette.Violet, 0.6f), 8f * scale, ImDrawFlags.None, 1f);
        ImGui.Dummy(new Vector2(size));
        ImGui.SameLine(0f, gap);
        ImGui.BeginGroup();
        var uploading = Session.IsUploading && ReferenceEquals(uploadTarget, node);
        ImGui.TextColored(Palette.InkDim, uploading ? "UPLOADING..." : path.Length > 0 ? "PHOTO" : "NO PHOTO YET");
        var buttonWidth = (width - size - gap * 2f) * 0.5f;
        var changed = false;
        if (Widgets.Button($"##{id}upload", "UPLOAD", FontAwesomeIcon.Upload, new Vector2(buttonWidth, 28f * scale), ButtonTone.Ghost, !Session.IsUploading))
        {
            uploadTarget = node;
            fileDialog.OpenFileDialog($"Choose a photo for {label}", ImageFilters, (picked, file) => OnPhotoPicked(picked, file, node, folder, label));
        }

        ImGui.SameLine(0f, gap);
        if (Widgets.Button($"##{id}removephoto", "REMOVE", FontAwesomeIcon.Times, new Vector2(buttonWidth, 28f * scale), ButtonTone.Ghost, path.Length > 0))
        {
            node.Remove(ImageProperty);
            changed = true;
        }

        ImGui.EndGroup();
        return changed;
    }

    private void OnPhotoPicked(bool picked, string file, JsonObject node, string folder, string label)
    {
        if (picked)
        {
            _ = UploadPhotoAsync(file, node, folder, label);
        }
    }

    // The draft is only touched on the framework thread, where the editor reads it every frame.
    private async Task UploadPhotoAsync(string file, JsonObject node, string folder, string label)
    {
        var repoPath = await Session.UploadImageAsync(file, folder, label.Length > 0 ? label : folder).ConfigureAwait(false);
        if (repoPath is null)
        {
            return;
        }

        await Services.Framework.RunOnFrameworkThread(() =>
        {
            node[ImageProperty] = repoPath;
            Session.MarkDirty();
        }).ConfigureAwait(false);
    }
}
