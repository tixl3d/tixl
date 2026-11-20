#nullable enable
using ImGuiNET;
using T3.Editor.Gui.Styling;

namespace T3.Editor.Gui.OutputUi.GeometryViews;

/// <summary>Shared look of highlighted geometry elements on a rendered preview.</summary>
internal static class SelectionOverlayStyle
{
    public const float LineThickness = 3;

    public static void DrawDot(ImDrawListPtr drawList, Vector2 center)
    {
        var scale = T3Ui.UiScaleFactor;
        drawList.AddCircleFilled(center, (DotRadius + 1) * scale, UiColors.BackgroundFull);
        drawList.AddCircleFilled(center, DotRadius * scale, UiColors.Selection);
    }

    /** A ring for a point selected by a selection attribute; its opacity follows the weight. */
    public static void DrawRing(ImDrawListPtr drawList, Vector2 center, float weight)
    {
        var scale = T3Ui.UiScaleFactor;
        drawList.AddCircle(center, RingRadius * scale, UiColors.Selection.Fade(weight), 12, MathF.Max(1, MathF.Round(1.5f * scale)));
    }

    public static void DrawPivot(ImDrawListPtr drawList, Vector2 pivot, float lineThickness)
    {
        var arm = PivotArm * T3Ui.UiScaleFactor;
        drawList.AddLine(pivot - new Vector2(arm, 0), pivot + new Vector2(arm, 0), UiColors.Selection, lineThickness);
        drawList.AddLine(pivot - new Vector2(0, arm), pivot + new Vector2(0, arm), UiColors.Selection, lineThickness);
    }

    private const float DotRadius = 4;
    private const float RingRadius = 6;
    private const float PivotArm = 6;
}
