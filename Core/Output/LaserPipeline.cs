#nullable enable
using System;
using System.Collections.Generic;
using System.Numerics;
using T3.Core.DataTypes;

namespace T3.Core.Output;

/// <summary>
/// A laser frame: the point buffer plus its exact length. The buffer is owned and reused by its
/// producer (the output pipeline's session), so a consumer that keeps the frame across frames must
/// copy it; <see cref="Count"/> is the frame, while <c>Points.Length</c> is only the buffer's
/// capacity - treating it as the frame length pads every stream with blanked tail points and makes
/// previews read a buffer the next compute is rewriting. <see cref="SafetyApplied"/> is true only when
/// the pipeline ran the output's safety transforms over the points; alignment shapes and the test
/// screen skip that stage.
/// </summary>
public readonly record struct LaserFrame(LaserPoint[] Points, int Count, bool SafetyApplied = false)
{
    public static readonly LaserFrame Empty = new(Array.Empty<LaserPoint>(), 0);

    public bool IsEmpty => Count <= 0;
}

/// <summary>
/// The setup-driven laser pipeline: optimizer + safety transforms configured on a laser output and
/// applied by the output operators before streaming. While the output's safety is uncalibrated, upstream content is
/// ignored and the manual alignment rectangle streams instead (the same behavior as the operator).
/// </summary>
public static class LaserPipeline
{
    /// <summary>Per-consumer scratch (optimizer buffers + safety work lists + frame mapping and
    /// result buffers); not thread-safe. Everything is reused across frames: a rig pumping at
    /// show rate allocates several large arrays per output per pump otherwise, and that churn
    /// lands in the large-object heap, where it turns into growing full-collection stalls.</summary>
    public sealed class Session
    {
        private readonly LaserOptimizerTransforms.Scratch _optimizerScratch = new();
        private LaserPoint[] _motionBuffer = [];
        private readonly List<LaserPoint> _safetyWorkList = new(4096);
        private readonly List<LaserPoint> _safetySpareList = new(4096);
        private LaserPoint[] _mapped = [];
        private LaserPoint[] _result = [];
        private int _resultWritten;
        private LaserPoint[]? _setupRectangle;
        private LaserPoint[] _shapeFrame = [];
        private (int Cx, int Cy, int HalfWidth, int HalfHeight) _setupRectangleKey;

        /// <summary>A buffer of at least <paramref name="minimumSize"/> points for the frame
        /// mapping stage to write into, reused across frames.</summary>
        public LaserPoint[] GetMappedBuffer(int minimumSize)
        {
            if (_mapped.Length < minimumSize)
                _mapped = new LaserPoint[minimumSize];
            return _mapped;
        }

        /// <summary>
        /// Appends the safety-shape outlines (safe zone + masks, see <see cref="BuildSafetyShapes"/>)
        /// to a calibrated frame: content first into one session-owned buffer, so a stream that
        /// truncates at a wire budget loses outlines, never content.
        /// </summary>
        public LaserPoint[] AppendSafetyShapes(LaserProcessingSettings.SafetySettings safety, LaserPoint[] content, int contentCount, out int frameCount)
        {
            var shapeCapacity = (1 + safety.Masks.Count) * 4 * 20 + 8;
            if (_shapeFrame.Length < contentCount + shapeCapacity)
                _shapeFrame = new LaserPoint[contentCount + shapeCapacity];

            Array.Copy(content, _shapeFrame, contentCount);
            frameCount = contentCount + BuildSafetyShapes(safety, _shapeFrame.AsSpan(contentCount));
            return _shapeFrame;
        }

        /// <summary>
        /// Runs the pipeline for a laser output's processing settings. Returns the buffer holding the
        /// points to stream plus its exact length in <paramref name="frameCount"/>, and a status line.
        /// Empty input with calibration on yields an empty result ("No input points").
        /// The returned array is owned by the session: it is overwritten by the next Apply (or map)
        /// on it, so consumers that keep a frame (a stream queue, a preview cache) copy it — and they
        /// must use <paramref name="frameCount"/>, never the array's Length, which is capacity.
        /// </summary>
        public LaserPoint[] Apply(LaserProcessingSettings settings, LaserPoint[]? input, int count, out string status, out int frameCount)
        {
            var result = Apply(settings, input, count, out frameCount);
            status = DescribeLastRun();
            return result;
        }

        /// <summary>The status line of the last <see cref="Apply(LaserProcessingSettings, LaserPoint[], int, out int)"/>.
        /// Built on request: the pump discards it, and formatting it per output per pump is garbage at show rate.</summary>
        public string DescribeLastRun() => _lastRun switch
                                               {
                                                   LastRun.NoSettings => "No laser processing settings",
                                                   LastRun.SetupQuad => "Safety setup (frame corners): drag the 4 corners in the frame preview"
                                                                        + " - switch Calibrated on for content",
                                                   LastRun.SetupRectangle => _lastSettings?.Safety.DescribeSetup() ?? string.Empty,
                                                   LastRun.NoInput => "No input points",
                                                   _ => _lastStats.Describe(_resultWritten),
                                               };

        private enum LastRun { NoSettings, SetupQuad, SetupRectangle, NoInput, Content }

        private LastRun _lastRun;
        private LaserProcessingSettings? _lastSettings;
        private LaserSafetyTransforms.Stats _lastStats;

        /// <summary>Same as the status overload, without building the status line.
        /// <paramref name="scanRatePps"/> is the bound plug's scan rate: the µs motion budgets
        /// convert to point counts at it. 0 falls back to the classic 30 kpps default.</summary>
        public LaserPoint[] Apply(LaserProcessingSettings settings, LaserPoint[]? input, int count, out int frameCount, int scanRatePps = 30000)
        {
            _lastSettings = settings;
            if (settings == null)
            {
                _lastRun = LastRun.NoSettings;
                frameCount = 0;
                return [];
            }

            if (!settings.Safety.Calibrated)
            {
                // With frame warp on, the alignment shape is the corner quad itself: the user
                // trapezes it against a skewed screen before switching Calibrated on.
                if (settings.Safety.FrameWarp)
                {
                    _lastRun = LastRun.SetupQuad;
                    var quad = BuildSetupQuad(WarpQuadFromSafety(settings.Safety));
                    frameCount = quad.Length;
                    return quad;
                }

                RectFromSafety(settings.Safety, out var cx, out var cy, out var hw, out var hh);
                _lastRun = LastRun.SetupRectangle;

                // Pumped every interval while an output is being aimed: rebuilt only when the rectangle moves.
                if (_setupRectangle == null || _setupRectangleKey != (cx, cy, hw, hh))
                {
                    _setupRectangle = BuildSetupRectangle(cx, cy, hw, hh);
                    _setupRectangleKey = (cx, cy, hw, hh);
                }

                frameCount = _setupRectangle.Length;
                return _setupRectangle;
            }

            if (input == null || count <= 0)
            {
                _lastRun = LastRun.NoInput;
                for (var i = 0; i < _resultWritten; i++)
                    _result[i] = default;

                _resultWritten = 0;
                frameCount = 0;
                return [];
            }

            // Drawing speed runs before the optimizer: it stretches the drawn intervals with
            // interpolated samples so the beam cannot cross a path faster than the µs budget
            // allows; the optimizer then tours, blanks and dwells the resampled frame. With the
            // budget at 0 the pass is a pure passthrough and the buffer is untouched.
            var motion = settings.Motion;
            var optimizerInput = input;
            var optimizerCount = count;
            if (motion.DrawingSpeedUs > 0f)
            {
                var ppsForResample = scanRatePps > 0 ? scanRatePps : 30000;
                optimizerInput = LaserMotionTransforms.Apply(input, count,
                                                             new LaserMotionTransforms.Config
                                                                 {
                                                                     DrawingSpeedUs = motion.DrawingSpeedUs,
                                                                     PointsPerSecond = ppsForResample,
                                                                 },
                                                             _motionBuffer, out optimizerCount);
                _motionBuffer = optimizerInput;
            }

            var optimizerConfig = settings.Optimizer.ToConfig(motion, scanRatePps);
            var optimized = LaserOptimizerTransforms.ApplyIntoBuffer(optimizerInput, optimizerCount, optimizerConfig,
                                                                     _optimizerScratch, out var optimizedCount);

            _safetyWorkList.Clear();
            for (var i = 0; i < optimizedCount; i++)
                _safetyWorkList.Add(optimized[i]);

            var safetyConfig = settings.Safety.ToConfig();
            var result = LaserSafetyTransforms.Apply(_safetyWorkList, _safetySpareList, in safetyConfig, ref _lastStats);
            _lastRun = LastRun.Content;

            // The optimizer's buffer is free again once its points are in the work list - the
            // safety result lands there instead of a fresh allocation. Points past the frame are
            // blanked so the capacity-sized tail is invisible everywhere the Length is read.
            if (_result.Length < result.Count)
            {
                _result = new LaserPoint[result.Count];
                _resultWritten = 0;
            }

            for (var i = 0; i < result.Count; i++)
                _result[i] = result[i];

            for (var i = result.Count; i < _resultWritten; i++)
                _result[i] = default; // blanked: zero intensity at the field origin

            _resultWritten = result.Count;
            frameCount = result.Count;
            return _result;
        }
    }

    /// <summary>The setup rect's normalized units mapped to scanner coordinates: center -1..1, width/height
    /// 0..1 where 1.0 spans the full field (-32767..32767). Shared by the uncalibrated safety frame and the
    /// test screen.</summary>
    public static void RectFromSafety(LaserProcessingSettings.SafetySettings safety, out int cx, out int cy, out int halfWidth, out int halfHeight)
    {
        const float field = 32767f;
        cx = (int)(Math.Clamp(safety.SetupCenterX, -1f, 1f) * field);
        cy = (int)(Math.Clamp(safety.SetupCenterY, -1f, 1f) * field);
        halfWidth = (int)(Math.Clamp(safety.SetupWidth, 0.005f, 1f) * field);
        halfHeight = (int)(Math.Clamp(safety.SetupHeight, 0.005f, 1f) * field);
    }

    /// <summary>
    /// Maps the camera frame onto the output's aimed zone (the setup rect). A [LaserCamera] is a
    /// camera: its frame IS the scanner field (-32767..32767), so this is a fixed transform of that
    /// field - never a fit of whatever happens to be lit this frame. Fitting the lit bounding box
    /// would rescale the picture as it animates (a rotating object would pulse in size) and would
    /// make the frame's framing depend on the content. With
    /// <see cref="LaserFrameMappingMode.Stretch"/> both axes fill independently (aspect may distort),
    /// with <see cref="LaserFrameMappingMode.Fit"/> the aspect is preserved (letterboxed in the zone),
    /// with <see cref="LaserFrameMappingMode.Crop"/> the aspect is preserved, the zone is filled and
    /// what falls outside it is blanked. Returns a new array - the input is the camera's live backing
    /// list and must not be mutated.
    /// </summary>
    public static LaserPoint[]? MapFrameIntoZone(LaserPoint[]? input, int count, LaserProcessingSettings.SafetySettings safety, LaserFrameMappingMode mode, LaserPoint[]? destination = null)
    {
        if (mode == LaserFrameMappingMode.Off || input == null || count <= 0)
            return input;

        // The caller's count is a contract with the array; never read past it.
        count = Math.Min(count, input.Length);
        if (count <= 0)
            return input;

        RectFromSafety(safety, out var cx, out var cy, out var halfW, out var halfH);

        const float field = 32767f;
        float scaleX, scaleY;
        var crop = false;
        switch (mode)
        {
            case LaserFrameMappingMode.Stretch:
                scaleX = halfW / field;
                scaleY = halfH / field;
                break;

            case LaserFrameMappingMode.Crop:
                // Fills the zone on its longer axis; the shorter one overflows and gets cut below.
                scaleX = scaleY = MathF.Max(halfW, halfH) / field;
                crop = true;
                break;

            default: // Fit
                scaleX = scaleY = MathF.Min(halfW, halfH) / field;
                break;
        }

        var mapped = destination != null && destination.Length >= count ? destination : new LaserPoint[count];
        for (var i = 0; i < count; i++)
        {
            var p = input[i];
            var x = Math.Clamp((int)MathF.Round(cx + p.X * scaleX), -32767, 32767);
            var y = Math.Clamp((int)MathF.Round(cy + p.Y * scaleY), -32767, 32767);

            var insideZone = !crop || (MathF.Abs(x - cx) <= halfW && MathF.Abs(y - cy) <= halfH);
            mapped[i] = insideZone
                            ? new LaserPoint(x, y, p.R, p.G, p.B, p.I)
                            : LaserPoint.CreateBlanked(x, y);
        }

        return mapped;
    }

    /// <summary>
    /// The frame-warp quad in scanner coordinates (±32767 field): the safety settings' four
    /// corners, ordered top-left → top-right → bottom-right → bottom-left in field space (y up).
    /// </summary>
    public static Vector2[] WarpQuadFromSafety(LaserProcessingSettings.SafetySettings safety)
    {
        const float field = 32767f;
        return
        [
            new Vector2(Math.Clamp(safety.WarpCorner1X, -1f, 1f) * field, Math.Clamp(safety.WarpCorner1Y, -1f, 1f) * field),
            new Vector2(Math.Clamp(safety.WarpCorner2X, -1f, 1f) * field, Math.Clamp(safety.WarpCorner2Y, -1f, 1f) * field),
            new Vector2(Math.Clamp(safety.WarpCorner3X, -1f, 1f) * field, Math.Clamp(safety.WarpCorner3Y, -1f, 1f) * field),
            new Vector2(Math.Clamp(safety.WarpCorner4X, -1f, 1f) * field, Math.Clamp(safety.WarpCorner4Y, -1f, 1f) * field),
        ];
    }

    /// <summary>
    /// Maps the camera frame onto the frame-warp quad with a homography (keystone): the frame's
    /// full field quad lands on the four corners, so straight lines stay straight while the
    /// picture can be trapezed onto a skewed screen. Runs instead of <see cref="MapFrameIntoZone"/>;
    /// the safety transforms still clamp the result afterwards. Returns a new array - the input is
    /// the camera's live backing list and must not be mutated. A degenerate corner set (repeated or
    /// collinear corners, a bow-tie) returns null: the frame goes dark. Falling back to the unwarped
    /// frame would throw the content across the whole field while the user meant a small screen.
    /// </summary>
    public static LaserPoint[]? WarpFrameIntoQuad(LaserPoint[]? input, int count, LaserProcessingSettings.SafetySettings safety, LaserPoint[]? destination = null)
    {
        if (input == null || count <= 0)
            return input;

        count = Math.Min(count, input.Length);
        if (count <= 0)
            return input;

        const float field = 32767f;
        Span<Vector2> source =
        [
            new Vector2(-field, field), new Vector2(field, field),
            new Vector2(field, -field), new Vector2(-field, -field),
        ];

        var destinationQuad = WarpQuadFromSafety(safety);
        if (!IsSimpleQuad(destinationQuad))
            return null;

        if (!Homography.TryComputeQuadToQuad(source, destinationQuad, out var warp))
            return null;

        var mapped = destination != null && destination.Length >= count ? destination : new LaserPoint[count];
        for (var i = 0; i < count; i++)
        {
            var p = input[i];
            var warped = warp.TransformPoint(new Vector2(p.X, p.Y));
            var x = Math.Clamp((int)MathF.Round(warped.X), -32767, 32767);
            var y = Math.Clamp((int)MathF.Round(warped.Y), -32767, 32767);
            mapped[i] = new LaserPoint(x, y, p.R, p.G, p.B, p.I);
        }

        return mapped;
    }

    /// <summary>
    /// The safety shapes streamed as faint outlines when an output's Render Safety Shapes is on:
    /// the safe-zone rectangle and every enabled mask, so the protected regions
    /// can be checked against the real venue while aiming. Default colors are green (zone) and
    /// orange (masks); a custom color replaces both. Appended after the safety stage on
    /// purpose - the shapes sit inside the safe zone by construction and must not be blanked by
    /// the very masks they draw. Returns the written point count.
    /// </summary>
    public static int BuildSafetyShapes(LaserProcessingSettings.SafetySettings safety, Span<LaserPoint> target)
    {
        var level = Math.Clamp(safety.RenderShapesLevel, 0f, 1f);
        if (level <= 0f || target.Length == 0)
            return 0;

        const float field = 32767f;
        var n = 0;

        // Custom shape color when enabled (a show look, or a dim red for dark venues); otherwise
        // the semantic defaults: green = safe zone, orange = mask.
        var useCustom = safety.RenderShapesUseCustomColor;
        var cr = useCustom ? (int)(Math.Clamp(safety.RenderShapesColorR, 0f, 1f) * 65535) : 0;
        var cg = useCustom ? (int)(Math.Clamp(safety.RenderShapesColorG, 0f, 1f) * 65535) : 65535;
        var cb = useCustom ? (int)(Math.Clamp(safety.RenderShapesColorB, 0f, 1f) * 65535) : 0;
        var mr = useCustom ? cr : 65535;
        var mg = useCustom ? cg : 32000;
        var mb = useCustom ? cb : 0;

        RectFromSafety(safety, out var zcx, out var zcy, out var zhw, out var zhh);
        n = WriteQuadOutline(target, n,
                             zcx - zhw, zcy - zhh, zcx + zhw, zcy - zhh,
                             zcx + zhw, zcy + zhh, zcx - zhw, zcy + zhh,
                             cr, cg, cb, level);

        for (var i = 0; i < safety.Masks.Count && n < target.Length; i++)
        {
            var mask = safety.Masks[i];
            if (!mask.Enabled)
                continue;

            n = WriteQuadOutline(target, n,
                                 Math.Clamp(mask.X1, -1f, 1f) * field, Math.Clamp(mask.Y1, -1f, 1f) * field,
                                 Math.Clamp(mask.X2, -1f, 1f) * field, Math.Clamp(mask.Y2, -1f, 1f) * field,
                                 Math.Clamp(mask.X3, -1f, 1f) * field, Math.Clamp(mask.Y3, -1f, 1f) * field,
                                 Math.Clamp(mask.X4, -1f, 1f) * field, Math.Clamp(mask.Y4, -1f, 1f) * field,
                                 mr, mg, mb, level);
        }

        return n;
    }

    /** One quad outline: per side a blanked approach, a lit corner dwell and straight samples - the same isolated encoding the setup rectangle uses. */
    private static int WriteQuadOutline(Span<LaserPoint> target, int n,
                                        float x1, float y1, float x2, float y2, float x3, float y3, float x4, float y4,
                                        int r, int g, int b, float level)
    {
        const int steps = 16;
        const int cornerDwell = 2;
        const int fieldMin = -32767;
        const int fieldMax = 32767;

        Span<(float X, float Y)> corners = [(x1, y1), (x2, y2), (x3, y3), (x4, y4)];
        var lr = (int)(r * level);
        var lg = (int)(g * level);
        var lb = (int)(b * level);

        for (var side = 0; side < 4 && n < target.Length; side++)
        {
            var a = corners[side];
            var c = corners[(side + 1) % 4];
            var ax = Math.Clamp((int)MathF.Round(a.X), fieldMin, fieldMax);
            var ay = Math.Clamp((int)MathF.Round(a.Y), fieldMin, fieldMax);

            target[n++] = LaserPoint.CreateBlanked(ax, ay);
            for (var d = 0; d <= cornerDwell && n < target.Length; d++)
                target[n++] = new LaserPoint(ax, ay, lr, lg, lb);

            for (var s = 1; s < steps && n < target.Length; s++)
            {
                var f = (float)s / steps;
                var x = Math.Clamp((int)MathF.Round(a.X + (c.X - a.X) * f), fieldMin, fieldMax);
                var y = Math.Clamp((int)MathF.Round(a.Y + (c.Y - a.Y) * f), fieldMin, fieldMax);
                target[n++] = new LaserPoint(x, y, lr, lg, lb);
            }
        }

        return n;
    }

    /// <summary>
    /// Manually parameterized alignment rectangle (green). The user starts with a tiny square at
    /// center and extends width/height step by step — no auto animation. Lifted 1:1 from the
    /// The setup pipeline emits the same frames the safety transform stage produces.
    /// </summary>
    public static LaserPoint[] BuildSetupRectangle(int cx, int cy, int halfW, int halfH)
    {
        const int steps = 40;          // samples per side
        const int cornerDwell = 3;     // extra lit points per corner
        var pts = new LaserPoint[4 * (2 + cornerDwell + steps) + 8];

        Span<(int X, int Y)> corners = stackalloc (int X, int Y)[4]
        {
            (cx - halfW, cy - halfH), (cx + halfW, cy - halfH),
            (cx + halfW, cy + halfH), (cx - halfW, cy + halfH)
        };

        var n = 0;
        for (var side = 0; side < 4; side++)
        {
            var a = corners[side];
            var b = corners[(side + 1) % 4];
            pts[n++] = LaserPoint.CreateBlanked(a.X, a.Y);
            pts[n++] = new LaserPoint(a.X, a.Y, 0, 65535, 0);
            pts[n++] = new LaserPoint(a.X, a.Y, 0, 65535, 0);
            for (var s2 = 1; s2 < steps; s2++)
            {
                var f = (float)s2 / steps;
                var x = a.X + (int)((b.X - a.X) * f);
                var y = a.Y + (int)((b.Y - a.Y) * f);
                pts[n++] = new LaserPoint(x, y, 0, 65535, 0);
            }
        }

        pts[n++] = LaserPoint.CreateBlanked(corners[0].X, corners[0].Y);
        pts[n++] = LaserPoint.CreateBlanked(0, 0);
        Array.Resize(ref pts, n);
        return pts;
    }

    /// <summary>Rejects quads without a meaningful, non-self-intersecting interior — a repeated
    /// corner collapses an edge, three collinear corners collapse the area, and a bow-tie
    /// ordering makes the homography fold the frame over itself. In every case the warp is
    /// refused and the frame goes dark instead of being folded.</summary>
    private static bool IsSimpleQuad(Vector2[] corners)
    {
        if (corners is not { Length: >= 4 })
            return false;

        // Shoelace area: a degenerate quad has no surface for the homography to map onto.
        var twiceArea = 0f;
        var positiveTurns = 0;
        var negativeTurns = 0;
        for (var i = 0; i < 4; i++)
        {
            var a = corners[i];
            var b = corners[(i + 1) % 4];
            var c = corners[(i + 2) % 4];
            if (!float.IsFinite(a.X) || !float.IsFinite(a.Y)
                || Vector2.DistanceSquared(a, b) < 1f)
                return false;

            twiceArea += a.X * b.Y - b.X * a.Y;

            // Cross product of consecutive edges: a simple (convex) quad turns the same way
            // at every corner. Opposing signs are the signature of a self-intersecting bow-tie.
            var cross = (b.X - a.X) * (c.Y - b.Y) - (b.Y - a.Y) * (c.X - b.X);
            if (cross > 1f)
                positiveTurns++;
            else if (cross < -1f)
                negativeTurns++;
        }

        return MathF.Abs(twiceArea) >= 2f && (positiveTurns == 0 || negativeTurns == 0);
    }

    /// <summary>
    /// The alignment shape as a four-corner quad (green) — the frame-warp targets. Same scanning
    /// pattern as <see cref="BuildSetupRectangle"/>: blanked approach, corner dwell, sampled sides.
    /// </summary>
    public static LaserPoint[] BuildSetupQuad(Vector2[] cornersField)
    {
        if (cornersField is not { Length: >= 4 })
            return [];

        const int steps = 40;          // samples per side
        const int cornerDwell = 3;     // extra lit points per corner
        var pts = new LaserPoint[4 * (2 + cornerDwell + steps) + 8];

        var n = 0;
        for (var side = 0; side < 4; side++)
        {
            var a = cornersField[side];
            var b = cornersField[(side + 1) % 4];
            var ax = Math.Clamp((int)a.X, -32767, 32767);
            var ay = Math.Clamp((int)a.Y, -32767, 32767);

            pts[n++] = LaserPoint.CreateBlanked(ax, ay);
            pts[n++] = new LaserPoint(ax, ay, 0, 65535, 0);
            for (var d = 0; d < cornerDwell; d++)
                pts[n++] = new LaserPoint(ax, ay, 0, 65535, 0);

            for (var s2 = 1; s2 < steps; s2++)
            {
                var f = (float)s2 / steps;
                var x = Math.Clamp((int)(a.X + (b.X - a.X) * f), -32767, 32767);
                var y = Math.Clamp((int)(a.Y + (b.Y - a.Y) * f), -32767, 32767);
                pts[n++] = new LaserPoint(x, y, 0, 65535, 0);
            }
        }

        pts[n++] = LaserPoint.CreateBlanked((int)cornersField[0].X, (int)cornersField[0].Y);
        pts[n++] = LaserPoint.CreateBlanked(0, 0);
        Array.Resize(ref pts, n);
        return pts;
    }
}
