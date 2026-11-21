#nullable enable
using System;
using ImGuiNET;
using T3.Core.DataTypes;
using T3.Core.Output;
using T3.Editor.Gui.Styling;
using T3.Editor.Gui.UiHelpers;
using Vector2 = System.Numerics.Vector2;
using Color = T3.Core.DataTypes.Vector.Color;

namespace T3.Editor.Gui.Windows.OutputSetup;

/// <summary>
/// Draws the live laser frame of a laser output — not the camera's raw points but the frame the
/// output actually streams: the setup pipeline (optimizer + safety from the output's processing
/// settings, the alignment rectangle while uncalibrated) as the <see cref="LaserDriver"/> ran it.
/// The Board card and the output's parameter card share it, so both preview exactly what leaves
/// for the laser. Runs on the UI thread; frames come from the driver's per-output cache, so the
/// camera evaluates at most once per frame no matter how many cards show it.
/// </summary>
internal static class LaserOutputPreview
{
    /// <summary>
    /// The frame this output streams right now (see <see cref="LaserDriver.GetFrame"/>). The buffer
    /// belongs to the output's pipeline session — draw it now, don't keep it.
    /// </summary>
    public static LaserFrame GetFrame(OutputDefinition output) => LaserDriver.GetFrame(output);

    /// <summary>Upper bound on drawn points per preview. The Board shows one card per output and
    /// a frame can carry the transport cap (~12k points): drawing all of them pushed ~100k
    /// draw-list primitives per frame through ImGui just for the cards. A card is not the wall -
    /// every Nth sample of a run reads identically at this size.</summary>
    private const int MaxDrawnPoints = 2000;

    /// <summary>
    /// Draws a laser frame onto the ILDA scan field inscribed in the rect. The field stays square —
    /// it is the scanner's field, not the card's aspect: a circle previews as a circle. Lit runs draw
    /// as colored polylines (decimated to <see cref="MaxDrawnPoints"/>), the jumps between them as
    /// faint straight lines (invisible on the wall, but they show where the beam travels), and dwell
    /// dots as dots. A dark laser master dims the frame to a hint: nothing is leaving for the laser.
    /// </summary>
    public static void DrawFrame(ImDrawListPtr dl, Vector2 min, Vector2 max, LaserFrame frame, float fade = 1f)
    {
        var uiScale = T3Ui.UiScaleFactor;
        var inset = 3f * uiScale;
        var side = MathF.Min(max.X - min.X, max.Y - min.Y) - 2f * inset;
        if (side < 8f || fade <= 0.001f)
            return;

        var center = (min + max) * 0.5f;
        var half = side * 0.5f;
        var fieldMin = new Vector2(center.X - half, center.Y - half);
        var fieldMax = new Vector2(center.X + half, center.Y + half);
        var alpha = fade * (LaserMasterEnable.Enabled ? 1f : 0.2f);

        dl.PushClipRect(min, max, true);
        dl.AddRect(fieldMin, fieldMax, UiColors.TextMuted.Fade(0.22f * fade), 2f * uiScale);

        // Count, not Length: the buffer is a reused capacity-sized array whose tail is padding.
        var points = frame.Points;
        var count = points == null ? 0 : Math.Min(frame.Count, points.Length);
        if (points != null && count > 0)
        {
            var posScale = half / 32767f;
            var lineThickness = MathF.Max(1.2f * uiScale, side / 220f);
            var dotRadius = MathF.Max(1.5f * uiScale, lineThickness * 0.8f);
            var stride = 1 + count / MaxDrawnPoints;
            Vector2 Map(in LaserPoint p) => new(center.X + p.X * posScale, center.Y - p.Y * posScale);

            Vector2? lastLit = null; // the last drawn point of the current run - where lines and jumps start
            var previousWasLit = false;
            for (var i = 0; i < count; i++)
            {
                if (!IsLit(points[i]))
                {
                    previousWasLit = false;
                    continue;
                }

                var isRunStart = !previousWasLit;
                var draw = stride == 1 || isRunStart || i % stride == 0;
                previousWasLit = true;
                if (!draw)
                    continue;

                var pos = Map(points[i]);
                var color = PointColor(points[i], alpha);

                if (!isRunStart && lastLit is { } from && Vector2.DistanceSquared(from, pos) > 0.01f)
                {
                    dl.AddLine(from, pos, color, lineThickness);
                }
                else if (!isRunStart)
                {
                    // Dwell: consecutive drawn points parked on one spot draw as a dot — a
                    // zero-length line would draw nothing, and a dwell run is how dots are sent.
                    dl.AddCircleFilled(pos, dotRadius, color);
                }
                else
                {
                    // A run starts: a faint jump hint from the previous run (invisible on the
                    // wall, but it shows where the beam travels) plus a dot, which also covers
                    // single-point runs.
                    if (lastLit is { } jump)
                        dl.AddLine(jump, pos, UiColors.TextMuted.Fade(0.10f * fade), uiScale);

                    dl.AddCircleFilled(pos, dotRadius, color);
                }

                lastLit = pos;
            }
        }

        dl.PopClipRect();
    }

    /// <summary>The laser blanking gate is I, not the RGB payload: a point can carry color and
    /// still be blanked (I = 0), and a black point can be lit (I &gt; 0, RGB = 0).</summary>
    private static bool IsLit(in LaserPoint p) => p.I > 0;

    private static Color PointColor(in LaserPoint p, float alpha)
    {
        // A lit point with no chroma still draws — as dim white, so black laser content stays visible.
        var max = Math.Max(p.R, Math.Max(p.G, p.B));
        if (max == 0)
            return new Color(0.35f, 0.35f, 0.35f, 1f).Fade(alpha);

        return new Color(p.R / 65535f, p.G / 65535f, p.B / 65535f, 1f).Fade(alpha);
    }
}
