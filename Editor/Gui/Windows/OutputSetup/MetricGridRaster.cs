#nullable enable
using ImGuiNET;
using T3.Editor.Gui.Interaction.CanvasEditing;
using T3.Editor.Gui.Styling;
using Vector2 = System.Numerics.Vector2;

namespace T3.Editor.Gui.Windows.OutputSetup;

/// <summary>
/// The Board's metre grid: two line densities cross-faded as the zoom moves, sparse "n m" labels on the
/// major lines, and the floor line at y = 0 drawn stronger. The spacing follows the timeline and curve
/// rasters' log-blend (a 1 → 5 → 10 ladder per decade, the finer level fading as it crowds), so the Board
/// densifies the way the curve editor does; only the two-axis, Y-up drawing is its own. Subtle by design —
/// it must never compete with photos or content.
/// </summary>
internal static class MetricGridRaster
{
    /// <param name="projection">Board metres (Y up) to screen.</param>
    /// <param name="pixelsPerMeter">The current zoom, in screen px per metre.</param>
    /// <param name="emphasis">1 while dragging/snapping, lower at rest.</param>
    public static void Draw(ImDrawListPtr dl, ICanvasProjection projection, Vector2 screenMin, Vector2 screenMax,
                            float pixelsPerMeter, float emphasis)
    {
        if (pixelsPerMeter <= 0.0001f)
            return;

        var scale = T3Ui.UiScaleFactor;

        // Visible board window (Y up: the screen's top edge is the board's max y).
        var topLeft = projection.ScreenToCanvas(screenMin);
        var bottomRight = projection.ScreenToCanvas(screenMax);
        var boardMin = new Vector2(topLeft.X, bottomRight.Y);
        var boardMax = new Vector2(bottomRight.X, topLeft.Y);

        // Same ladder as StandardValueRaster.TryGetRastersForScale: metres per UI pixel picks the decade, and
        // the position inside it alternates the pair (10, 50) / (50, 100), fading the finer one out.
        var metresPerPixel = scale / pixelsPerMeter;
        var logScale = MathF.Log10(metresPerPixel) + Density;
        var logMod = (logScale + 1000) % 1f;
        var decade = MathF.Pow(10, MathF.Floor(logScale));
        float minorSpacing, majorSpacing, fade;
        if (logMod < 0.5f)
        {
            fade = 1 - logMod * 2;
            minorSpacing = decade * 10;
            majorSpacing = decade * 50;
        }
        else
        {
            fade = 1 - (logMod - 0.5f) * 2;
            minorSpacing = decade * 50;
            majorSpacing = decade * 100;
        }

        var minorColor = UiColors.ForegroundFull.Fade(0.10f * fade * emphasis);
        var majorColor = UiColors.ForegroundFull.Fade(0.16f * emphasis);
        var labelColor = UiColors.TextMuted.Fade(0.6f * emphasis);

        DrawLines(dl, projection, boardMin, boardMax, screenMin, screenMax, minorSpacing, majorSpacing, minorColor, false, labelColor, scale);
        DrawLines(dl, projection, boardMin, boardMax, screenMin, screenMax, majorSpacing, 0, majorColor, true, labelColor, scale);

        // The floor line: what every physical entity stands on.
        if (boardMin.Y <= 0 && boardMax.Y >= 0)
        {
            var y = projection.CanvasToScreen(Vector2.Zero).Y;
            dl.AddLine(new Vector2(screenMin.X, y), new Vector2(screenMax.X, y), UiColors.ForegroundFull.Fade(0.35f * emphasis), 1.5f * scale);
            dl.AddText(Fonts.FontSmall, Fonts.FontSmall.FontSize, new Vector2(screenMin.X + 6 * scale, y - Fonts.FontSmall.FontSize - 2 * scale),
                       labelColor, "Floor (0 m)");
        }
    }

    private static void DrawLines(ImDrawListPtr dl, ICanvasProjection projection, Vector2 boardMin, Vector2 boardMax,
                                  Vector2 screenMin, Vector2 screenMax, float spacing, float skipMultipleOf, uint color,
                                  bool labeled, uint labelColor, float scale)
    {
        if (spacing <= 0)
            return;

        // Density cap: a runaway zoom-out must not turn into thousands of lines.
        if ((boardMax.X - boardMin.X) / spacing > 400 || (boardMax.Y - boardMin.Y) / spacing > 400)
            return;

        var firstX = MathF.Floor(boardMin.X / spacing) * spacing;
        for (var x = firstX; x <= boardMax.X; x += spacing)
        {
            if (skipMultipleOf > 0 && IsMultiple(x, skipMultipleOf))
                continue;

            var sx = projection.CanvasToScreen(new Vector2(x, 0)).X;
            dl.AddLine(new Vector2(sx, screenMin.Y), new Vector2(sx, screenMax.Y), color, 1 * scale);
            if (labeled)
                dl.AddText(Fonts.FontSmall, Fonts.FontSmall.FontSize, new Vector2(sx + 3 * scale, screenMax.Y - Fonts.FontSmall.FontSize - 2 * scale), labelColor, MetreLabel(x));
        }

        var firstY = MathF.Floor(boardMin.Y / spacing) * spacing;
        for (var y = firstY; y <= boardMax.Y; y += spacing)
        {
            if (skipMultipleOf > 0 && IsMultiple(y, skipMultipleOf))
                continue;

            if (MathF.Abs(y) < spacing * 0.01f)
                continue; // the floor line is drawn on its own

            var sy = projection.CanvasToScreen(new Vector2(0, y)).Y;
            dl.AddLine(new Vector2(screenMin.X, sy), new Vector2(screenMax.X, sy), color, 1 * scale);
            if (labeled)
                dl.AddText(Fonts.FontSmall, Fonts.FontSmall.FontSize, new Vector2(screenMin.X + 3 * scale, sy - Fonts.FontSmall.FontSize - 1 * scale), labelColor, MetreLabel(y));
        }
    }

    private static bool IsMultiple(float value, float spacing)
    {
        var ratio = value / spacing;
        return MathF.Abs(ratio - MathF.Round(ratio)) < 0.001f;
    }

    // Labels are few (major lines only) and the values repeat across frames, so a small cache keeps the draw
    // free of string building.
    private static string MetreLabel(float metres)
    {
        if (_labelCache.TryGetValue(metres, out var label))
            return label;

        if (_labelCache.Count > 256)
            _labelCache.Clear();

        label = MathF.Abs(metres) >= 1 ? $"{metres:0.#} m" : $"{metres * 100:0.#} cm";
        _labelCache[metres] = label;
        return label;
    }

    /// <summary>Same value the timeline rasters use; shifts where in the zoom the ladder steps.</summary>
    private const float Density = 1f;

    private static readonly Dictionary<float, string> _labelCache = new();
}
