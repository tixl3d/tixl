#nullable enable
using ImGuiNET;
using T3.Editor.Gui.Styling;
using Color = T3.Core.DataTypes.Vector.Color;
using Vector2 = System.Numerics.Vector2;

namespace T3.Editor.Gui.Interaction.CanvasEditing;

/// <summary>
/// A single draggable point on a <see cref="ScalableCanvas"/> — the shared primitive behind
/// corner-pins, annotation-line endpoints, calibration points, etc. Owns the consistent handle
/// glyph, hover state, hit-testing, the drag lifecycle, and an optional snapping pass; the caller
/// owns the point data (mutated in place) and any undo command. Push a unique ImGui id before
/// calling when several handles share a frame.
/// </summary>
internal static class CanvasPointHandle
{
    internal enum DragPhase
    {
        None,
        Started,
        Dragging,
        Completed,
    }

    internal enum Shape
    {
        Circle,
        Square,
    }

    internal struct Style
    {
        public Color Color;
        public Color ActiveColor;
        public Shape Shape;
        public float Radius; // unscaled screen pixels
        public bool Editable;

        public static Style Default(Color color, Shape shape = Shape.Circle, bool editable = true)
        {
            return new Style
                       {
                           Color = color,
                           ActiveColor = UiColors.ForegroundFull,
                           Shape = shape,
                           Radius = 5,
                           Editable = editable,
                       };
        }
    }

    /// <summary>
    /// Draws one handle and processes its drag. <paramref name="posInCanvas"/> is mutated in place
    /// while dragging (after the optional snap). Returns the drag phase for the caller's undo logic.
    /// </summary>
    public static DragPhase Draw(ref Vector2 posInCanvas, ICanvasProjection projection, in Style style, ICanvasPointSnapper? snapper = null)
    {
        var dl = ImGui.GetWindowDrawList();
        var screen = projection.CanvasToScreen(posInCanvas);
        var radius = style.Radius * T3Ui.UiScaleFactor;

        var phase = DragPhase.None;
        var isHovered = false;
        if (style.Editable)
        {
            var hitSize = new Vector2(radius * 3);
            ImGui.SetCursorScreenPos(screen - hitSize * 0.5f);
            ImGui.InvisibleButton("handle", hitSize);
            isHovered = ImGui.IsItemHovered();
            if (isHovered)
                ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);

            if (ImGui.IsItemActivated())
            {
                // Grab offset keeps the point from jumping to the cursor on the first drag frame.
                _grabOffset = ImGui.GetMousePos() - screen;
                phase = DragPhase.Started;
            }
            else if (ImGui.IsItemActive() && ImGui.IsMouseDragging(ImGuiMouseButton.Left, 0f))
            {
                var candidate = projection.ScreenToCanvas(ImGui.GetMousePos() - _grabOffset);
                if (snapper != null)
                    candidate = snapper.TrySnap(candidate, projection);

                posInCanvas = candidate;
                screen = projection.CanvasToScreen(posInCanvas);
                phase = DragPhase.Dragging;
            }
            else if (ImGui.IsItemDeactivated())
            {
                phase = DragPhase.Completed;
            }
        }

        var isActive = isHovered || phase == DragPhase.Started || phase == DragPhase.Dragging;
        var color = isActive ? style.ActiveColor : style.Color;
        if (style.Shape == Shape.Square)
        {
            var half = new Vector2(radius);
            dl.AddRectFilled(screen - half, screen + half, color);
        }
        else
        {
            dl.AddCircleFilled(screen, radius, color);
        }

        return phase;
    }

    // Only one handle drags at a time, so a single shared grab offset is sufficient.
    private static Vector2 _grabOffset;
}
