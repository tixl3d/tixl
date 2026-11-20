#nullable enable
using ImGuiNET;
using T3.Editor.Gui.Styling;

namespace T3.Editor.Gui.OutputUi.Previews;

/// <summary>
/// Layout shared by output views that switch between a rendered preview and a data view: a row with a segmented
/// mode button and a summary. Views too small for these controls, like connection tooltips, skip them.
/// </summary>
internal static class PreviewLayout
{
    public static bool IsCompactView()
    {
        return ImGui.GetContentRegionAvail().Y < MinHeightForControls * T3Ui.UiScaleFactor;
    }

    public static void IndentControls()
    {
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + ControlsIndent * T3Ui.UiScaleFactor);
    }

    /** Muted text next to the previous control of the mode row. */
    public static void DrawSummary(string summary)
    {
        ImGui.SameLine(0, 12 * T3Ui.UiScaleFactor);
        ImGui.AlignTextToFramePadding();
        CustomComponents.StylizedText(summary, Fonts.FontSmall, UiColors.TextMuted);
    }

    private const float ControlsIndent = 8;
    private const float MinHeightForControls = 150;
}
