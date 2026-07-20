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
        public Color TopLeftColor;
        public Color LabelColor;
        public Color CheckerColor;
        public bool DrawChecker;
        public string? Label;
        public bool Editable;

        /// <summary>A surface quad: blue edges (linked mapping), orange top-left marker, faint checker.</summary>
        public static Style ForSurface(string? label, bool editable)
        {
            return new Style
                       {
                           EdgeColor = UiColors.StatusAutomated,
                           HandleColor = UiColors.ForegroundFull,
                           TopLeftColor = UiColors.StatusAnimated,
                           LabelColor = UiColors.Text,
                           CheckerColor = UiColors.ForegroundFull.Fade(0.06f),
                           DrawChecker = true,
                           Label = label,
                           Editable = editable,
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
        draggedCorner = -1;
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
            DrawCenteredLabel(dl, screen, style.Label!, style.LabelColor);

        var phase = CanvasPointHandle.DragPhase.None;
        for (var i = 0; i < 4; i++)
        {
            ImGui.PushID(i);
            var handleStyle = CanvasPointHandle.Style.Default(
                                                              i == 0 ? style.TopLeftColor : style.HandleColor,
                                                              i == 0 ? CanvasPointHandle.Shape.Square : CanvasPointHandle.Shape.Circle,
                                                              style.Editable);
            var handlePhase = CanvasPointHandle.Draw(ref corners[i], projection, handleStyle);
            if (handlePhase != CanvasPointHandle.DragPhase.None)
            {
                phase = handlePhase;
                draggedCorner = i;
            }

            ImGui.PopID();
        }

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

    private static void DrawCenteredLabel(ImDrawListPtr dl, ReadOnlySpan<Vector2> screen, string label, Color color)
    {
        var centroid = (screen[0] + screen[1] + screen[2] + screen[3]) * 0.25f;
        ImGui.PushFont(Fonts.FontSmall);
        var size = ImGui.CalcTextSize(label);
        ImGui.PopFont();
        dl.AddText(Fonts.FontSmall, Fonts.FontSmall.FontSize, centroid - size * 0.5f, color, label);
    }
}
