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

        /// <summary>Drawn around the fill; transparent by default so plain handles are unchanged.</summary>
        public Color OutlineColor;

        public Shape Shape;
        public float Radius; // unscaled screen pixels
        public bool Editable;

        public static Style Default(Color color, Shape shape = Shape.Circle, bool editable = true)
        {
            return new Style
                       {
                           Color = color,
                           ActiveColor = UiColors.ForegroundFull,
                           OutlineColor = new Color(0f, 0f, 0f, 0f),
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
        var isHeld = false;
        if (style.Editable)
        {
            var hitSize = new Vector2(radius * 3);
            ImGui.SetCursorScreenPos(screen - hitSize * 0.5f);
            ImGui.InvisibleButton("handle", hitSize);
            isHovered = ImGui.IsItemHovered();
            isHeld = ImGui.IsItemActive(); // stays true through a paused drag, when hover/drag both read false
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

        var isActive = isHovered || isHeld;
        var color = isActive ? style.ActiveColor : style.Color;
        var outlineWidth = 1.5f * T3Ui.UiScaleFactor;
        var hasOutline = style.OutlineColor.Rgba.W > 0.01f;

        if (style.Shape == Shape.Square)
        {
            var half = new Vector2(radius);
            dl.AddRectFilled(screen - half, screen + half, color);
            if (hasOutline)
                dl.AddRect(screen - half, screen + half, style.OutlineColor, 0, ImDrawFlags.None, outlineWidth);
        }
        else
        {
            dl.AddCircleFilled(screen, radius, color);
            if (hasOutline)
                dl.AddCircle(screen, radius, style.OutlineColor, 0, outlineWidth);
        }

        return phase;
    }

    // Only one handle drags at a time, so a single shared grab offset is sufficient.
    private static Vector2 _grabOffset;
}
