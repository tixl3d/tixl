#nullable enable
using ImGuiNET;
using T3.Editor.Gui.Interaction.Gamepad;
using T3.Editor.Gui.MagGraph.Model;
using T3.Editor.Gui.MagGraph.States;
using T3.Editor.Gui.Styling;
using T3.Editor.UiModel.ProjectHandling;

namespace T3.Editor.Gui.MagGraph.Interaction;

/// <summary>
/// While X is held on a game controller, marks where releasing it will insert an operator: the edge of the selected
/// operator it attaches to, and a faint outline of the new operator beside it.
/// </summary>
internal static class GamepadInsertPreview
{
    internal static void Draw(GraphUiContext context, ImDrawListPtr drawList)
    {
        if (context.ProjectView != ProjectView.Focused
            || context.StateMachine.CurrentState != GraphStates.Default
            || !GamepadControl.IsChoosingInsertSide(out var side)
            || !GraphStates.TryGetSingleSelectedItem(context, out var item)
            || !GraphStates.TryResolveInsertSide(item, side, out var isUpstream, out var direction))
            return;

        var area = context.View.TransformRect(item.Area);
        var min = new Vector2(MathF.Round(area.Min.X), MathF.Round(area.Min.Y));
        var max = new Vector2(MathF.Round(area.Max.X), MathF.Round(area.Max.Y));
        var size = max - min;

        // The new operator takes the full size along the flow and one row (or column) beside it
        var isHorizontal = direction == MagGraphItem.Directions.Horizontal;
        var offset = isHorizontal
                         ? new Vector2(isUpstream ? -size.X : size.X, 0)
                         : new Vector2(0, isUpstream ? -size.Y : size.Y);

        Vector2 edgeStart, edgeEnd;
        if (isHorizontal)
        {
            var x = isUpstream ? min.X : max.X;
            (edgeStart, edgeEnd) = (new Vector2(x, min.Y), new Vector2(x, max.Y));
        }
        else
        {
            var y = isUpstream ? min.Y : max.Y;
            (edgeStart, edgeEnd) = (new Vector2(min.X, y), new Vector2(max.X, y));
        }

        var scale = T3Ui.UiScaleFactor;
        var rounding = MathF.Round(3 * scale);
        drawList.AddRectFilled(min + offset, max + offset, UiColors.ForegroundFull.Fade(GhostFillOpacity), rounding);
        drawList.AddRect(min + offset, max + offset, UiColors.ForegroundFull.Fade(GhostOutlineOpacity), rounding);
        drawList.AddLine(edgeStart, edgeEnd, UiColors.ForegroundFull.Fade(EdgeOpacity), MathF.Max(2, MathF.Round(EdgeThickness * scale)));
    }

    private const float GhostFillOpacity = 0.06f;
    private const float GhostOutlineOpacity = 0.35f;
    private const float EdgeOpacity = 0.9f;
    private const float EdgeThickness = 2;
}
