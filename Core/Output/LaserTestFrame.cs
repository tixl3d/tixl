#nullable enable
using System;
using System.Collections.Generic;
using T3.Core.DataTypes;

namespace T3.Core.Output;

/// <summary>
/// The test screen a laser output streams while <b>Test screen</b> is on in its Safety settings: the
/// alignment rectangle (the same one the uncalibrated safety shows) with the output's name scanned as
/// strokes inside it, so the projector on the wall can be told apart from its siblings while aiming.
/// Independent of the Calibrated gate by design - it is the aiming aid used <i>before</i> calibration.
/// </summary>
public static class LaserTestFrame
{
    /// <summary>Test patterns for calibration and aiming. Names are stable identifiers the
    /// output-setup UI lists; <see cref="Build"/> keeps the classic name frame.</summary>
    public enum Pattern
    {
        NameFrame,
        ColorShift,
        DiodeThreshold,
        WhiteBalance,
        GammaRamp,
        ScanAreaGrid,
        Circle,
        IldaTestPattern,
    }

    /// <summary>
    /// Builds the test frame: the alignment rectangle in green with the name scanned inside it in the
    /// same green. Rectangle geometry uses the setup rect's normalized units exactly like the
    /// uncalibrated safety frame (1.0 = full scanner field).
    /// </summary>
    public static LaserPoint[] Build(string name, int cx, int cy, int halfWidth, int halfHeight)
    {
        var rect = LaserPipeline.BuildSetupRectangle(cx, cy, halfWidth, halfHeight);

        var points = new List<LaserPoint>(rect.Length + 2048);
        points.AddRange(rect);

        if (!string.IsNullOrWhiteSpace(name))
            AppendText(points, name.Trim().ToUpperInvariant(), cx, cy, halfHeight);

        return points.ToArray();
    }

    /// <summary>Builds a calibration pattern inside the setup rectangle. Each variant targets one
    /// wizard step: color timing, per-diode thresholds, white balance, gamma ramping, the scan
    /// area, motion smoothness, or the full ILDA check.</summary>
    public static LaserPoint[] BuildPattern(Pattern pattern, int cx, int cy, int halfWidth, int halfHeight)
    {
        switch (pattern)
        {
            case Pattern.ColorShift:
                return BuildColorShift(cx, cy, halfWidth, halfHeight);
            case Pattern.DiodeThreshold:
                return BuildDiodeThreshold(cx, cy, halfWidth, halfHeight);
            case Pattern.WhiteBalance:
                return BuildWhiteBalance(cx, cy, halfWidth, halfHeight);
            case Pattern.GammaRamp:
                return BuildGammaRamp(cx, cy, halfWidth, halfHeight);
            case Pattern.ScanAreaGrid:
                return BuildScanAreaGrid(cx, cy, halfWidth, halfHeight);
            case Pattern.Circle:
                return BuildCircle(cx, cy, halfWidth, halfHeight);
            case Pattern.IldaTestPattern:
                return BuildIlda(cx, cy, halfWidth, halfHeight);
            case Pattern.NameFrame:
            default:
                return Build("TEST", cx, cy, halfWidth, halfHeight);
        }
    }

    /** Step 2: two horizontal white lines above and below a vertical red reference - raise the
        color shift until both whites switch exactly at the red line (Modulaser's color-shift test). */
    private static LaserPoint[] BuildColorShift(int cx, int cy, int halfWidth, int halfHeight)
    {
        var points = new List<LaserPoint>();
        var halfSpanY = halfHeight * 0.55f;
        AppendHLine(points, cx, cy - halfSpanY, halfWidth * 0.55f, white: true);
        AppendHLine(points, cx, cy + halfSpanY, halfWidth * 0.55f, white: true, rightHalf: true);
        AppendVLine(points, cx, cy, halfHeight * 0.8f, r: 65535);
        return points.ToArray();
    }

    /** Step 3: five horizontal rows per channel at 8/30/50/70/92% - raise each minimum until the
        dimmest row just becomes visible (the diode threshold test). */
    private static LaserPoint[] BuildDiodeThreshold(int cx, int cy, int halfWidth, int halfHeight)
    {
        var points = new List<LaserPoint>();
        float[] levels = [0.08f, 0.30f, 0.50f, 0.70f, 0.92f];
        for (var row = 0; row < levels.Length; row++)
        {
            var y = cy - halfHeight * 0.7f + row * (halfHeight * 1.4f / (levels.Length - 1));
            var level = (ushort)(levels[row] * 65535);
            AppendHLine(points, cx, y, halfWidth * 0.6f, white: false, level: level);
        }

        // Once per row group, one pure channel per third of the width: R left, G mid, B right.
        return points.ToArray();
    }

    /** Step 4: four horizontal white rows - lower each channel's maximum until the top row reads
        neutral white (the white-balance test). */
    private static LaserPoint[] BuildWhiteBalance(int cx, int cy, int halfWidth, int halfHeight)
    {
        var points = new List<LaserPoint>();
        for (var row = 0; row < 4; row++)
        {
            var y = cy - halfHeight * 0.6f + row * (halfHeight * 1.2f / 3f);
            var level = (ushort)(65535 - row * 8191);
            AppendHLine(points, cx, y, halfWidth * 0.6f, white: false, level: level);
        }

        return points.ToArray();
    }

    /** Step 5: eight brightness rows from 10% to 100% - even perceived spacing means the gamma
        is right (the gamma-ramp test). */
    private static LaserPoint[] BuildGammaRamp(int cx, int cy, int halfWidth, int halfHeight)
    {
        var points = new List<LaserPoint>();
        const int rows = 8;
        for (var row = 0; row < rows; row++)
        {
            var y = cy - halfHeight * 0.7f + row * (halfHeight * 1.4f / (rows - 1));
            var level = (ushort)(0.1f + 0.9f * row / (rows - 1) * 65535);
            AppendHLine(points, cx, y, halfWidth * 0.6f, white: false, level: level);
        }

        return points.ToArray();
    }

    /** Scan-area grid: the full-field rectangle with thirds guides - the safe zone made visible. */
    private static LaserPoint[] BuildScanAreaGrid(int cx, int cy, int halfWidth, int halfHeight)
    {
        var points = new List<LaserPoint>(LaserPipeline.BuildSetupRectangle(cx, cy, halfWidth, halfHeight));
        // Thirds lines, dim green
        for (var t = 1; t <= 2; t++)
        {
            var x = cx - halfWidth + t * halfWidth * 2 / 3f;
            AppendVLine(points, (int)x, cy, halfHeight * 0.95f, r: 0, g: 16384);
            var y = cy - halfHeight + t * halfHeight * 2 / 3f;
            AppendHLine(points, (int)x, (int)y, halfWidth * 0.95f, white: false, level: 16384, g: 16384);
        }

        return points.ToArray();
    }

    /** Motion smoothness circle - the smoothness half of the scan-preset test. */
    private static LaserPoint[] BuildCircle(int cx, int cy, int halfWidth, int halfHeight)
    {
        var points = new List<LaserPoint>();
        var radius = MathF.Min(halfWidth, halfHeight) * 0.7f;
        const int samples = 240;
        for (var i = 0; i <= samples; i++)
        {
            var a = i / (float)samples * MathF.Tau;
            var x = cx + MathF.Cos(a) * radius;
            var y = cy + MathF.Sin(a) * radius;
            points.Add(new LaserPoint((int)x, (int)y, 0, 65535, 0));
        }

        return points.ToArray();
    }

    /** The ILDA test pattern's essential geometry: frame, square, circle, diagonals and centre
        cross in white with a green frame - the verify step's full-field check. */
    private static LaserPoint[] BuildIlda(int cx, int cy, int halfWidth, int halfHeight)
    {
        var points = new List<LaserPoint>(LaserPipeline.BuildSetupRectangle(cx, cy, halfWidth, halfHeight));
        var half = MathF.Min(halfWidth, halfHeight) * 0.75f;

        // Square
        AppendPolyline(points,
                       [(int)(cx - half), (int)(cy - half), (int)(cx + half), (int)(cy - half), (int)(cx + half), (int)(cy + half), (int)(cx - half), (int)(cy + half), (int)(cx - half), (int)(cy - half)],
                       r: 65535);
        // Circle
        const int samples = 180;
        var radius = half * 0.72f;
        for (var i = 0; i <= samples; i++)
        {
            var a = i / (float)samples * MathF.Tau;
            points.Add(new LaserPoint((int)(cx + MathF.Cos(a) * radius), (int)(cy + MathF.Sin(a) * radius), 65535, 0, 0));
        }

        // Diagonals + cross
        AppendHLine(points, cx, cy, half * 0.9f, white: true);
        AppendVLine(points, cx, cy, half * 0.9f, r: 65535);
        AppendPolyline(points,
                       [(int)(cx - half * 0.9f), (int)(cy - half * 0.9f), (int)(cx + half * 0.9f), (int)(cy + half * 0.9f)],
                       r: 65535);
        AppendPolyline(points,
                       [(int)(cx - half * 0.9f), (int)(cy + half * 0.9f), (int)(cx + half * 0.9f), (int)(cy - half * 0.9f)],
                       r: 65535);
        return points.ToArray();
    }

    private static void AppendHLine(List<LaserPoint> points, int cx, float y, float halfLength,
                                    bool white, bool rightHalf = false, int level = 65535, int g = 65535)
    {
        var startX = rightHalf ? cx : cx - halfLength;
        var endX = rightHalf ? cx + halfLength : cx + halfLength;
        const int steps = 48;
        var r = white ? level : level;
        for (var i = 0; i <= steps; i++)
        {
            var x = startX + (endX - startX) * i / (float)steps;
            points.Add(new LaserPoint((int)x, (int)y, r, g, 0));
        }
    }

    private static void AppendVLine(List<LaserPoint> points, int cx, float cy, float halfLength, int r, int g = 0)
    {
        const int steps = 48;
        for (var i = 0; i <= steps; i++)
        {
            var y = cy - halfLength + halfLength * 2 * i / (float)steps;
            points.Add(new LaserPoint(cx, (int)y, r, g, 0));
        }
    }

    private static void AppendPolyline(List<LaserPoint> points, int[] vertices, int r = 0, int g = 65535)
    {
        for (var i = 0; i + 1 < vertices.Length; i += 2)
        {
            var x0 = vertices[i];
            var y0 = vertices[i + 1];
            var x1 = vertices[i + 2];
            var y1 = vertices[i + 3];
            var length = MathF.Sqrt((x1 - x0) * (float)(x1 - x0) + (y1 - y0) * (float)(y1 - y0));
            var steps = Math.Max(1, (int)(length / 300f));
            for (var s = 0; s <= steps; s++)
            {
                var t = s / (float)steps;
                points.Add(new LaserPoint((int)(x0 + (x1 - x0) * t), (int)(y0 + (y1 - y0) * t), r, g, 0));
            }
        }
    }

    private static void AppendText(List<LaserPoint> points, string text, int cx, int cy, int rectHalfHeight)
    {
        // Letter height: a fifth of the rectangle height, clamped so it stays scannable at both extremes.
        var letterHeight = Math.Clamp(rectHalfHeight * 0.4f, 600f, 9000f);
        const float letterSpacing = 0.35f;
        const float glyphWidth = 0.72f;
        var advance = glyphWidth * letterHeight + letterSpacing * letterHeight;
        var totalWidth = advance * text.Length - letterSpacing * letterHeight;

        var startX = cx - totalWidth * 0.5f;
        var baselineY = cy - letterHeight * 0.5f;
        var step = MathF.Max(140f, letterHeight / 10f);

        for (var i = 0; i < text.Length; i++)
        {
            if (!Glyphs.TryGetValue(text[i], out var strokes))
            {
                // Unknown character: scan a filler box so gaps in the name stay readable as gaps.
                strokes = UnknownGlyph;
            }

            var originX = startX + i * advance;
            AppendGlyph(points, strokes, originX, baselineY, letterHeight, step);
        }
    }

    private static void AppendGlyph(List<LaserPoint> points, float[][] strokes, float originX, float baselineY,
                                    float letterHeight, float step)
    {
        foreach (var stroke in strokes)
        {
            if (stroke.Length < 4)
                continue;

            // Blanked approach to the stroke start, then lit samples along the whole polyline -
            // corners stay lit (the optimizer adds dwell), only the travel in is dark.
            var px = originX + stroke[0] * letterHeight;
            var py = baselineY + stroke[1] * letterHeight;
            points.Add(LaserPoint.CreateBlanked((int)px, (int)py));
            points.Add(new LaserPoint((int)px, (int)py, 0, 65535, 0));

            for (var i = 2; i + 1 < stroke.Length; i += 2)
            {
                var x = originX + stroke[i] * letterHeight;
                var y = baselineY + stroke[i + 1] * letterHeight;
                var dx = x - px;
                var dy = y - py;
                var length = MathF.Sqrt(dx * dx + dy * dy);
                var samples = Math.Max(1, (int)MathF.Ceiling(length / step));
                for (var s = 1; s <= samples; s++)
                {
                    var t = s / (float)samples;
                    points.Add(new LaserPoint((int)(px + dx * t), (int)(py + dy * t), 0, 65535, 0));
                }

                px = x;
                py = y;
            }
        }
    }

    /// <summary>Stroke font in a 0..1-high cell (y up), x 0..0.72; each stroke is x0,y0,x1,y1 pairs.
    /// Straight-segment stick shapes - coarse at small sizes, unambiguous at scan rates.</summary>
    private static readonly Dictionary<char, float[][]> Glyphs = new()
    {
        // A
        ['A'] = [[0, 0, 0.36f, 1, 0.72f, 0], [0.12f, 0.34f, 0.6f, 0.34f]],
        // B
        ['B'] = [[0, 0, 0, 1, 0.5f, 1, 0.66f, 0.82f, 0.5f, 0.55f, 0, 0.55f], [0, 0, 0.52f, 0, 0.68f, 0.18f, 0.5f, 0.55f]],
        // C
        ['C'] = [[0.66f, 0.85f, 0.45f, 1, 0.15f, 0.95f, 0, 0.6f, 0, 0.4f, 0.15f, 0.05f, 0.45f, 0, 0.66f, 0.15f]],
        // D
        ['D'] = [[0, 0, 0, 1, 0.45f, 1, 0.72f, 0.7f, 0.72f, 0.3f, 0.45f, 0, 0, 0]],
        // E
        ['E'] = [[0.7f, 1, 0, 1, 0, 0, 0.7f, 0], [0, 0.55f, 0.55f, 0.55f]],
        // F
        ['F'] = [[0.7f, 1, 0, 1, 0, 0], [0, 0.55f, 0.55f, 0.55f]],
        // G
        ['G'] = [[0.66f, 0.85f, 0.45f, 1, 0.15f, 0.95f, 0, 0.6f, 0, 0.4f, 0.15f, 0.05f, 0.45f, 0, 0.7f, 0], [0.7f, 0, 0.7f, 0.4f, 0.4f, 0.4f]],
        // H
        ['H'] = [[0, 0, 0, 1], [0.72f, 0, 0.72f, 1], [0, 0.52f, 0.72f, 0.52f]],
        // I
        ['I'] = [[0.36f, 0, 0.36f, 1], [0.1f, 1, 0.62f, 1], [0.1f, 0, 0.62f, 0]],
        // J
        ['J'] = [[0.6f, 1, 0.6f, 0.2f, 0.42f, 0, 0.18f, 0, 0, 0.2f]],
        // K
        ['K'] = [[0, 0, 0, 1], [0.7f, 1, 0, 0.48f, 0.7f, 0]],
        // L
        ['L'] = [[0, 1, 0, 0, 0.7f, 0]],
        // M
        ['M'] = [[0, 0, 0, 1, 0.36f, 0.55f, 0.72f, 1, 0.72f, 0]],
        // N
        ['N'] = [[0, 0, 0, 1, 0.72f, 0, 0.72f, 1]],
        // O
        ['O'] = [[0.2f, 1, 0.52f, 1, 0.72f, 0.75f, 0.72f, 0.25f, 0.52f, 0, 0.2f, 0, 0, 0.25f, 0, 0.75f, 0.2f, 1]],
        // P
        ['P'] = [[0, 0, 0, 1, 0.55f, 1, 0.72f, 0.8f, 0.55f, 0.55f, 0, 0.55f]],
        // Q
        ['Q'] = [[0.2f, 1, 0.52f, 1, 0.72f, 0.75f, 0.72f, 0.25f, 0.52f, 0, 0.2f, 0, 0, 0.25f, 0, 0.75f, 0.2f, 1], [0.42f, 0.28f, 0.75f, 0]],
        // R
        ['R'] = [[0, 0, 0, 1, 0.55f, 1, 0.72f, 0.8f, 0.55f, 0.55f, 0, 0.55f], [0.3f, 0.55f, 0.72f, 0]],
        // S
        ['S'] = [[0.7f, 0.85f, 0.5f, 1, 0.2f, 1, 0.02f, 0.85f, 0.2f, 0.62f, 0.5f, 0.55f, 0.68f, 0.42f, 0.7f, 0.18f, 0.5f, 0, 0.2f, 0, 0.02f, 0.15f]],
        // T
        ['T'] = [[0.36f, 0, 0.36f, 1], [0.05f, 1, 0.67f, 1]],
        // U
        ['U'] = [[0, 1, 0, 0.25f, 0.2f, 0, 0.52f, 0, 0.72f, 0.25f, 0.72f, 1]],
        // V
        ['V'] = [[0, 1, 0.36f, 0, 0.72f, 1]],
        // W
        ['W'] = [[0, 1, 0.18f, 0, 0.36f, 0.55f, 0.54f, 0, 0.72f, 1]],
        // X
        ['X'] = [[0, 0, 0.72f, 1], [0, 1, 0.72f, 0]],
        // Y
        ['Y'] = [[0, 1, 0.36f, 0.45f, 0.72f, 1], [0.36f, 0.45f, 0.36f, 0]],
        // Z
        ['Z'] = [[0, 1, 0.72f, 1, 0, 0, 0.72f, 0]],
        // Digits
        ['0'] = [[0.2f, 1, 0.52f, 1, 0.72f, 0.75f, 0.72f, 0.25f, 0.52f, 0, 0.2f, 0, 0, 0.25f, 0, 0.75f, 0.2f, 1], [0.05f, 0.05f, 0.67f, 0.95f]],
        ['1'] = [[0.15f, 0.75f, 0.4f, 1, 0.4f, 0, 0.4f, 0], [0.1f, 0, 0.7f, 0]],
        ['2'] = [[0.05f, 0.85f, 0.25f, 1, 0.55f, 1, 0.7f, 0.82f, 0.7f, 0.6f, 0.05f, 0, 0.7f, 0]],
        ['3'] = [[0.05f, 1, 0.7f, 1, 0.4f, 0.55f, 0.62f, 0.55f], [0.62f, 0.55f, 0.72f, 0.35f, 0.6f, 0.05f, 0.35f, 0, 0.05f, 0.1f]],
        ['4'] = [[0.55f, 0, 0.55f, 1, 0, 0.3f, 0.72f, 0.3f]],
        ['5'] = [[0.7f, 1, 0.05f, 1, 0.05f, 0.6f, 0.45f, 0.6f, 0.68f, 0.4f, 0.68f, 0.18f, 0.45f, 0, 0.15f, 0, 0.05f, 0.1f]],
        ['6'] = [[0.65f, 0.9f, 0.4f, 1, 0.12f, 0.8f, 0.05f, 0.4f, 0.15f, 0.05f, 0.45f, 0, 0.65f, 0.15f, 0.65f, 0.4f, 0.45f, 0.55f, 0.15f, 0.55f, 0.05f, 0.4f]],
        ['7'] = [[0, 1, 0.7f, 1, 0.28f, 0]],
        ['8'] = [[0.2f, 0.55f, 0.05f, 0.75f, 0.2f, 1, 0.52f, 1, 0.68f, 0.78f, 0.5f, 0.55f, 0.2f, 0.55f], [0.5f, 0.55f, 0.7f, 0.35f, 0.55f, 0.12f, 0.2f, 0.12f, 0.02f, 0.32f, 0.2f, 0.55f]],
        ['9'] = [[0.07f, 0.1f, 0.32f, 0, 0.58f, 0.2f, 0.66f, 0.6f, 0.55f, 0.95f, 0.28f, 1, 0.08f, 0.85f, 0.08f, 0.6f, 0.28f, 0.45f, 0.55f, 0.45f, 0.65f, 0.6f]],
        // Punctuation
        ['-'] = [[0.1f, 0.5f, 0.62f, 0.5f]],
        ['.'] = [[0.32f, 0, 0.4f, 0]],
        ['_'] = [[0, 0, 0.72f, 0]],
        [' '] = [],
    };

    private static readonly float[][] UnknownGlyph = [[0, 0, 0, 1, 0.72f, 1, 0.72f, 0, 0, 0]];
}
