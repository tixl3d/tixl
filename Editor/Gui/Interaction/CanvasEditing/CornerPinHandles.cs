#nullable enable
using ImGuiNET;
using T3.Editor.Gui.Styling;
using Color = T3.Core.DataTypes.Vector.Color;
using Vector2 = System.Numerics.Vector2;

namespace T3.Editor.Gui.Interaction.CanvasEditing;

/// <summary>
/// Corner-pin quad editor: composes four <see cref="CanvasPointHandle"/>s (top-left marked with a
/// square) with quad edges, a faint bilinear checker, and a centered label. Agnostic of what the
/// quad means; the caller owns the data (mutated in place) and any undo command, and reads the
/// drag lifecycle to snapshot on <see cref="CanvasPointHandle.DragPhase.Started"/> and commit on
/// <see cref="CanvasPointHandle.DragPhase.Completed"/>. Corners: top-left, top-right, bottom-right,
/// bottom-left.
/// </summary>
internal static class CornerPinHandles
{
    internal struct Style
    {
        public Color EdgeColor;
        public Color HandleColor;
        public Color HandleOutlineColor;
        public Color LabelColor;
        public Color LabelBackgroundColor;
        public Color CheckerColor;
        public bool DrawChecker;
        public string? Label;
        public bool Editable;

        /// <summary>Squares read as "crop along the edge"; a caller whose edge drag scales instead shows circles.</summary>
        public CanvasPointHandle.Shape EdgeHandleShape;

        /// <summary>
        /// A surface quad. Blue means "selected" — an unselected surface stays neutral so the two read apart at
        /// a glance. Handles are white with a blue rim when selected, and recede when not.
        /// </summary>
        public static Style ForSurface(string? label, bool editable, bool selected = false, float emphasis = 1f)
        {
            return new Style
                       {
                           EdgeColor = selected
                                           ? UiColors.StatusActivated.Fade(emphasis)
                                           : UiColors.ForegroundFull.Fade(0.4f * emphasis),
                           HandleColor = UiColors.ForegroundFull.Fade(selected ? emphasis : 0.6f * emphasis),
                           HandleOutlineColor = selected
                                                    ? UiColors.StatusActivated.Fade(emphasis)
                                                    : new Color(0f, 0f, 0f, 0f),
                           LabelColor = selected
                                            ? UiColors.ForegroundFull.Fade(emphasis)
                                            : UiColors.Text.Fade(0.7f * emphasis),
                           LabelBackgroundColor = selected
                                                      ? UiColors.StatusActivated.Fade(emphasis)
                                                      : UiColors.BackgroundFull.Fade(0.6f * emphasis),
                           CheckerColor = UiColors.ForegroundFull.Fade(0.06f * emphasis),
                           DrawChecker = true,
                           Label = label,
                           Editable = editable,
                           EdgeHandleShape = CanvasPointHandle.Shape.Square,
                       };
        }
    }

    /// <summary>
    /// Draws the quad and processes dragging of one corner. <paramref name="corners"/> (length 4,
    /// in canvas space) is mutated in place while dragging. Push a unique ImGui id before calling
    /// when several quads share a frame. Returns the drag phase and, during a live drag, the corner.
    /// </summary>
    public static CanvasPointHandle.DragPhase Draw(Vector2[] corners, ICanvasProjection projection, in Style style, out int draggedCorner)
    {
        return Draw(corners, projection, style, out draggedCorner, out _);
    }

    /// <summary>As <see cref="Draw(Vector2[],ICanvasProjection,in Style,out int)"/>, also reporting whether any
    /// of the four corner handles is hovered — used to cross-highlight the quad's entity from the canvas.
    /// <paramref name="selectedCornersMask"/> marks corners in the canvas sub-element selection (bit per corner).</summary>
    public static CanvasPointHandle.DragPhase Draw(Vector2[] corners, ICanvasProjection projection, in Style style,
                                                   out int draggedCorner, out bool hovered, int selectedCornersMask = 0)
    {
        draggedCorner = -1;
        hovered = false;
        if (corners.Length != 4)
            return CanvasPointHandle.DragPhase.None;

        var dl = ImGui.GetWindowDrawList();
        Span<Vector2> screen = stackalloc Vector2[4];
        for (var i = 0; i < 4; i++)
            screen[i] = projection.CanvasToScreen(corners[i]);

        if (style.DrawChecker)
            DrawChecker(dl, corners, projection, style.CheckerColor);

        var edgeThickness = 1.5f * T3Ui.UiScaleFactor;
        for (var i = 0; i < 4; i++)
            dl.AddLine(screen[i], screen[(i + 1) % 4], style.EdgeColor, edgeThickness);

        if (!string.IsNullOrEmpty(style.Label))
            DrawCenteredLabel(dl, screen, style.Label!, style.LabelColor, style.LabelBackgroundColor);

        var phase = CanvasPointHandle.DragPhase.None;
        for (var i = 0; i < 4; i++)
        {
            ImGui.PushID(i);
            // Corners are circles, edge handles squares — the anchor marker shows orientation, so the corners
            // don't need a winding cue of their own.
            var handleStyle = CanvasPointHandle.Style.Default(style.HandleColor, CanvasPointHandle.Shape.Circle, style.Editable);
            handleStyle.OutlineColor = style.HandleOutlineColor;

            // A selected corner reads as part of the active set: status-colored fill, bright rim, a touch larger.
            if ((selectedCornersMask & (1 << i)) != 0)
            {
                handleStyle.Color = UiColors.StatusActivated;
                handleStyle.OutlineColor = UiColors.ForegroundFull;
                handleStyle.Radius += 1;
            }

            var handlePhase = CanvasPointHandle.Draw(ref corners[i], projection, handleStyle);
            if (handlePhase != CanvasPointHandle.DragPhase.None)
            {
                phase = handlePhase;
                draggedCorner = i;
            }

            hovered |= style.Editable && (ImGui.IsItemHovered() || ImGui.IsItemActive());
            ImGui.PopID();
        }

        return phase;
    }

    /// <summary>
    /// Draws a handle at the midpoint of each edge and processes its drag. Unlike the corners — which move
    /// freely and so introduce perspective — an edge drag means "move this edge", which the caller resolves in
    /// its own space (for a surface: crop the rectangle, keeping the opposite edge fixed). The handle position
    /// is therefore reported rather than written back into <paramref name="corners"/>.
    /// Edges are indexed 0 = top, 1 = right, 2 = bottom, 3 = left, matching the TL, TR, BR, BL winding.
    /// </summary>
    public static CanvasPointHandle.DragPhase DrawEdgeHandles(Vector2[] corners, ICanvasProjection projection, in Style style,
                                                              out int draggedEdge, out Vector2 draggedPosition)
    {
        return DrawEdgeHandles(corners, projection, style, out draggedEdge, out draggedPosition, out _);
    }

    /// <summary>As the four-out overload, also reporting whether any edge handle is hovered.</summary>
    public static CanvasPointHandle.DragPhase DrawEdgeHandles(Vector2[] corners, ICanvasProjection projection, in Style style,
                                                              out int draggedEdge, out Vector2 draggedPosition, out bool hovered)
    {
        draggedEdge = -1;
        draggedPosition = Vector2.Zero;
        hovered = false;
        if (corners.Length != 4)
            return CanvasPointHandle.DragPhase.None;

        var phase = CanvasPointHandle.DragPhase.None;
        ImGui.PushID("edges");
        for (var i = 0; i < 4; i++)
        {
            ImGui.PushID(i);
            var midpoint = (corners[i] + corners[(i + 1) % 4]) * 0.5f;
            var handleStyle = CanvasPointHandle.Style.Default(style.HandleColor, style.EdgeHandleShape, style.Editable);
            handleStyle.OutlineColor = style.HandleOutlineColor;
            handleStyle.Radius = 4;

            var handlePhase = CanvasPointHandle.Draw(ref midpoint, projection, handleStyle);
            if (handlePhase != CanvasPointHandle.DragPhase.None)
            {
                phase = handlePhase;
                draggedEdge = i;
                draggedPosition = midpoint;
            }

            hovered |= style.Editable && (ImGui.IsItemHovered() || ImGui.IsItemActive());
            ImGui.PopID();
        }

        ImGui.PopID();
        return phase;
    }

    private static void DrawChecker(ImDrawListPtr dl, Vector2[] corners, ICanvasProjection projection, Color color)
    {
        const int cellsX = 6;
        const int cellsY = 4;
        for (var cy = 0; cy < cellsY; cy++)
        {
            for (var cx = 0; cx < cellsX; cx++)
            {
                if (((cx + cy) & 1) == 0)
                    continue;

                var a = projection.CanvasToScreen(BilinearCorner(corners, (float)cx / cellsX, (float)cy / cellsY));
                var b = projection.CanvasToScreen(BilinearCorner(corners, (float)(cx + 1) / cellsX, (float)cy / cellsY));
                var c = projection.CanvasToScreen(BilinearCorner(corners, (float)(cx + 1) / cellsX, (float)(cy + 1) / cellsY));
                var d = projection.CanvasToScreen(BilinearCorner(corners, (float)cx / cellsX, (float)(cy + 1) / cellsY));
                dl.AddQuadFilled(a, b, c, d, color);
            }
        }
    }

    // Bilinear guide only — a linear grid, not the true perspective warp. corners: TL, TR, BR, BL.
    private static Vector2 BilinearCorner(Vector2[] corners, float u, float v)
    {
        var top = Vector2.Lerp(corners[0], corners[1], u);
        var bottom = Vector2.Lerp(corners[3], corners[2], u);
        return Vector2.Lerp(top, bottom, v);
    }

    /// <summary>
    /// Screen rect of the centered label chip. Exposed separately so callers can hit-test it — the label
    /// doubles as the surface's grab handle.
    /// </summary>
    public static (Vector2 Min, Vector2 Max) GetCenteredLabelRect(ReadOnlySpan<Vector2> screen, string label)
    {
        var centroid = (screen[0] + screen[1] + screen[2] + screen[3]) * 0.25f;
        ImGui.PushFont(Fonts.FontSmall);
        var size = ImGui.CalcTextSize(label);
        ImGui.PopFont();

        var padding = new Vector2(5, 2) * T3Ui.UiScaleFactor;
        var position = centroid - size * 0.5f;
        return (position - padding, position + size + padding);
    }

    /// <summary>Label on a chip, so it stays legible over content, a raster, or another surface behind it.</summary>
    public static void DrawLabelChip(ImDrawListPtr dl, (Vector2 Min, Vector2 Max) rect, string label, Color color, Color backgroundColor)
    {
        if (backgroundColor.Rgba.W > 0.01f)
            dl.AddRectFilled(rect.Min, rect.Max, backgroundColor, 3 * T3Ui.UiScaleFactor);

        var padding = new Vector2(5, 2) * T3Ui.UiScaleFactor;
        dl.AddText(Fonts.FontSmall, Fonts.FontSmall.FontSize, rect.Min + padding, color, label);
    }

    public static void DrawCenteredLabel(ImDrawListPtr dl, ReadOnlySpan<Vector2> screen, string label, Color color, Color backgroundColor)
    {
        DrawLabelChip(dl, GetCenteredLabelRect(screen, label), label, color, backgroundColor);
    }
}
