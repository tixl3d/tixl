#nullable enable
using ImGuiNET;
using T3.Editor.Gui.Styling;
using Color = T3.Core.DataTypes.Vector.Color;
using Vector2 = System.Numerics.Vector2;

namespace T3.Editor.Gui.Interaction.CanvasEditing;

/// <summary>
/// Screen-space drawing primitives shared by canvas editors — the shapes that come up whatever is being edited
/// (an outline, a crosshair marker, a scrim around a focus rect), separated from the interaction logic that
/// decides where and when to draw them. Everything here takes already-projected screen coordinates and a color;
/// line/marker sizes are unscaled pixels, scaled by <see cref="T3Ui.UiScaleFactor"/> inside.
/// </summary>
internal static class CanvasDraw
{
    /// <summary>The axis-aligned bounds of a point set (any winding, at least one point).</summary>
    public static void Bounds(System.ReadOnlySpan<Vector2> points, out Vector2 min, out Vector2 max)
    {
        min = max = points[0];
        for (var i = 1; i < points.Length; i++)
        {
            min = Vector2.Min(min, points[i]);
            max = Vector2.Max(max, points[i]);
        }
    }

    /// <summary>Whether a point lies inside an axis-aligned rect, edges included.</summary>
    public static bool Contains(Vector2 min, Vector2 max, Vector2 p)
    {
        return p.X >= min.X && p.X <= max.X && p.Y >= min.Y && p.Y <= max.Y;
    }

    /// <summary>The four edges of a quad (any winding), as a closed outline.</summary>
    public static void QuadOutline(ImDrawListPtr dl, System.ReadOnlySpan<Vector2> screen, Color color, float thickness = 1f)
    {
        if (screen.Length != 4)
            return;

        var width = thickness * T3Ui.UiScaleFactor;
        for (var i = 0; i < 4; i++)
            dl.AddLine(screen[i], screen[(i + 1) % 4], color, width);
    }

    /// <summary>A ring with a horizontal + vertical arm — an origin/anchor marker that can't be mistaken for a corner dot.</summary>
    public static void Crosshair(ImDrawListPtr dl, Vector2 screen, Color color, float radius = 7f, float thickness = 1.5f)
    {
        var r = radius * T3Ui.UiScaleFactor;
        var arm = r * 1.9f;
        var width = thickness * T3Ui.UiScaleFactor;

        dl.AddCircle(screen, r, color, 0, width);
        dl.AddLine(screen - new Vector2(arm, 0), screen + new Vector2(arm, 0), color, width);
        dl.AddLine(screen - new Vector2(0, arm), screen + new Vector2(0, arm), color, width);
    }

    /// <summary>
    /// Fills the region of an outer rect that lies outside an inner one — the letterbox scrim that dims
    /// everything but a focused area. Both rects are axis-aligned in screen space; the inner is assumed to sit
    /// within the outer.
    /// </summary>
    public static void ScrimOutside(ImDrawListPtr dl, Vector2 outerMin, Vector2 outerMax, Vector2 innerMin, Vector2 innerMax, Color color)
    {
        dl.AddRectFilled(outerMin, new Vector2(outerMax.X, innerMin.Y), color);                    // above
        dl.AddRectFilled(new Vector2(outerMin.X, innerMax.Y), outerMax, color);                    // below
        dl.AddRectFilled(new Vector2(outerMin.X, innerMin.Y), new Vector2(innerMin.X, innerMax.Y), color); // left
        dl.AddRectFilled(new Vector2(innerMax.X, innerMin.Y), new Vector2(outerMax.X, innerMax.Y), color); // right
    }

    /// <summary>
    /// Text centred on <paramref name="centre"/> and turned to run along <paramref name="direction"/> (screen
    /// space), flipped so it never reads upside down. The glyphs are laid out flat and their vertices turned
    /// afterwards — the draw list has no rotated text of its own.
    /// </summary>
    public static void TextAlong(ImDrawListPtr dl, ImFontPtr font, float fontSize, Vector2 centre, Vector2 direction, Color color, string text)
    {
        var angle = MathF.Atan2(direction.Y, direction.X);
        if (angle > MathF.PI * 0.5f)
            angle -= MathF.PI;
        else if (angle < -MathF.PI * 0.5f)
            angle += MathF.PI;

        ImGui.PushFont(font);
        var textSize = ImGui.CalcTextSize(text);
        ImGui.PopFont();
        var first = dl.VtxBuffer.Size;
        dl.AddText(font, fontSize, centre - textSize * 0.5f, color, text);
        var last = dl.VtxBuffer.Size;

        var cos = MathF.Cos(angle);
        var sin = MathF.Sin(angle);
        for (var i = first; i < last; i++)
        {
            var vertex = dl.VtxBuffer[i];
            var p = vertex.pos - centre;
            vertex.pos = centre + new Vector2(p.X * cos - p.Y * sin, p.X * sin + p.Y * cos);
        }
    }
}
