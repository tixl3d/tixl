#nullable enable
using ImGuiNET;
using T3.Core.DataTypes.Vector;
using T3.Editor.Gui.UiHelpers;

namespace T3.Editor.Gui.Windows.OutputSetup;

/// <summary>
/// How the setup views stroke their rectangles. Two rules keep the canvas readable: a <b>frame</b> sits fully
/// outside the content it frames, and a stroke around something <b>inside</b> content — a slice, a patch — sits
/// fully inside its own rect, so a cut filling its source doesn't double the source's frame. Both are snapped to
/// whole pixels and drawn without anti-aliased lines, which is what actually makes them crisp: ImGui spreads a
/// fringe pixel to either side of every stroke, so even a perfectly aligned 1px line renders as two grey rows.
/// <para><b>Note on the half pixel:</b> <c>AddRect</c> and <c>AddLine</c> already shift their input by half a
/// pixel, so a stroke of odd width is centred correctly when it is handed whole numbers — adding another half
/// puts it back on the boundary between two pixels. The offsets below are therefore half a pixel *less* than
/// the geometry suggests. (<c>AddRectFilled</c> does not shift, and <c>AddPolyline</c> does not either, which is
/// why the graph's connections add the half themselves.)</para>
/// </summary>
internal static class SetupStrokes
{
    /// <summary>Snaps a screen position to the pixel grid. For anything with straight, axis-aligned edges —
    /// rectangles, their strokes, the metric grid, drag handles. Free-angle lines and point markers stay
    /// unsnapped: antialiasing carries them, and snapping would make them wobble as the view moves.</summary>
    public static Vector2 Snap(Vector2 screenPos)
    {
        return new Vector2(MathF.Round(screenPos.X), MathF.Round(screenPos.Y));
    }

    public static void SnapRect(ref Vector2 min, ref Vector2 max)
    {
        min = Snap(min);
        max = Snap(max);
    }

    /// <summary>A content frame, drawn outside the rect it frames. Selection reads as the heavier frame.</summary>
    public static void DrawFrame(ImDrawListPtr dl, Vector2 min, Vector2 max, Color color, bool isSelected, float rounding = 0)
    {
        var thickness = FrameThickness(isSelected);
        var offset = new Vector2(thickness * 0.5f + 0.5f);
        var restore = BeginCrispLines(dl, rounding);
        dl.AddRect(min - offset, max + offset, color, rounding, ImDrawFlags.None, thickness);
        dl.Flags = restore;
    }

    /// <summary>A stroke around content inside content, drawn within its own rect.</summary>
    public static void DrawInlineRect(ImDrawListPtr dl, Vector2 min, Vector2 max, Color color, bool isSelected, float rounding = 0)
    {
        var thickness = InlineThickness(isSelected);
        var offset = new Vector2(thickness * 0.5f - 0.5f);
        var restore = BeginCrispLines(dl, rounding);
        dl.AddRect(min + offset, max - offset, color, rounding, ImDrawFlags.None, thickness);
        dl.Flags = restore;
    }

    /// <summary>An axis-aligned line (the metric grid, a snap guide) on whole pixels and without the fringe.</summary>
    public static void DrawCrispLine(ImDrawListPtr dl, Vector2 from, Vector2 to, Color color, float unscaledWidth)
    {
        DrawCrispLine(dl, from, to, color.AsUInt(), unscaledWidth);
    }

    public static void DrawCrispLine(ImDrawListPtr dl, Vector2 from, Vector2 to, uint packedColor, float unscaledWidth)
    {
        // Whole coordinates plus AddLine's own half pixel centre an odd width on a pixel; an even one has to
        // straddle the boundary between two instead.
        var thickness = LineThickness(unscaledWidth);
        var adjust = (int)thickness % 2 == 0 ? new Vector2(-0.5f) : Vector2.Zero;
        var restore = BeginCrispLines(dl);
        dl.AddLine(Snap(from) + adjust, Snap(to) + adjust, packedColor, thickness);
        dl.Flags = restore;
    }

    /// <summary>
    /// Turns the fringe off for one draw; the flags belong to the draw list, so callers restore them. Rounded
    /// corners keep it — their arcs are curves, and without smoothing they read as steps.
    /// </summary>
    private static ImDrawListFlags BeginCrispLines(ImDrawListPtr dl, float rounding = 0)
    {
        var flags = dl.Flags;
        if (rounding <= 0)
            dl.Flags = flags & ~ImDrawListFlags.AntiAliasedLines;

        return flags;
    }

    /// <summary>A line width rounded to whole pixels — for anything axis-aligned (the metric grid, guides).</summary>
    public static float LineThickness(float unscaledWidth) => WholePixels(unscaledWidth);

    public static float FrameThickness(bool isSelected) => WholePixels(isSelected ? 3 : 2);

    public static float InlineThickness(bool isSelected) => WholePixels(isSelected ? 2 : 1);

    /** A fractional stroke width blurs across two pixel rows, so the UI scale lands on whole pixels here. */
    private static float WholePixels(float unscaledWidth)
    {
        return MathF.Max(1, MathF.Round(unscaledWidth * T3Ui.UiScaleFactor));
    }
}
