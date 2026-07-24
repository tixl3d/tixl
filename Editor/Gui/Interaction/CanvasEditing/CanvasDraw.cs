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
}
