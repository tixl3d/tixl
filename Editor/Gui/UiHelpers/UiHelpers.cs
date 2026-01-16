#nullable enable
using ImGuiNET;

namespace T3.Editor.Gui.UiHelpers;

/// hack to distinguish right mouse click from right mouse drag
internal static class UiHelpers
{
    /// <summary>
    /// Helper method to distinguish right mouse button clicks from drag background events. 
    /// </summary>
    internal static bool WasRightMouseClick()
    {
        var rightMouseDragDelta = (ImGui.GetIO().MouseClickedPos[1] - ImGui.GetIO().MousePos).Length();
        return rightMouseDragDelta < UserSettings.Config.ClickThreshold;
    }

    /// <summary>
    /// Sadly, ImGui.IsWindowHovered() does not work reliably
    /// </summary>
    internal static bool IsClickedOutsideWindow()
    {
        if (!ImGui.IsMouseClicked(ImGuiMouseButton.Left))
            return false;

        if (ImGui.IsWindowAppearing())
            return false;

        var winPos = ImGui.GetWindowPos();
        var winSize = ImGui.GetWindowSize();

        var mouse = ImGui.GetMousePos();

        var inside =
            mouse.X >= winPos.X &&
            mouse.X <= winPos.X + winSize.X &&
            mouse.Y >= winPos.Y &&
            mouse.Y <= winPos.Y + winSize.Y;

        return !inside;
    }
}