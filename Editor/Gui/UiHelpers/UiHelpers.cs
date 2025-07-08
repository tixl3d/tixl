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
}