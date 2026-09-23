#nullable enable
using ImGuiNET;
using T3.Editor.Gui.Styling;

namespace T3.Editor.Gui.Windows.OutputSetup;

/// <summary>
/// The setup views' popups — their menus and the small dialogs that ask for one number. A bare ImGui popup
/// carries no window padding, so its content sits against the frame; these are the metrics
/// <see cref="CustomComponents.ContextMenuForItem"/> uses, so a menu opened from an outliner item and one
/// opened on the canvas look the same.
/// </summary>
internal static class SetupPopup
{
    /// <summary>Pair with <see cref="End"/> exactly like <see cref="ImGui.BeginPopup(string)"/>.</summary>
    public static bool Begin(string id)
    {
        PushStyle();
        if (ImGui.BeginPopup(id))
            return true;

        ImGui.PopStyleVar(StyleVarCount);
        return false;
    }

    public static void End()
    {
        ImGui.EndPopup();
        ImGui.PopStyleVar(StyleVarCount);
    }

    private static void PushStyle()
    {
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(6, 6));
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(6, 6));
        ImGui.PushStyleVar(ImGuiStyleVar.PopupRounding, 6);

        // Menu rows measure themselves with GetFrameHeight, so a caller that lowered FramePadding for its own
        // content would otherwise squash the whole popup.
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, T3Style.DefaultFramePadding);
    }

    private const int StyleVarCount = 4;
}
