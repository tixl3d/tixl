using System;
using System.Buffers;
using System.Collections.Generic;

namespace T3.Core.DataTypes;

/// <summary>
/// Point-data safety transforms for laser output, applied in a fixed order:
/// blanking normalization → scan-field clamp → color time shift → mask blanking →
/// jump-distance limiting → static-dwell blanking → intensity ceiling → per-channel calibration →
/// out-of-bounds audit.
/// A later stage relies on the guarantees of the earlier ones, so the order must not be changed.
/// The time shift moves colors between points, so it runs before every stage that judges a
/// point's color or level (masks dim by position, the ceiling caps by value) - a shifted color
/// can never carry a level into a region that attenuates it. Masks sit before the jump limiter
/// so the approach into a masked region is already blanked travel for it; the calibration stage
/// sits last among the shaping passes because it may lift levels back up and must not undo a
/// protective stage.
///
/// These functions reduce the risk of unsafe content reaching a projector, but they
/// are not a compliance certificate: audience scanning also requires a hardware
/// scan-fail interlock that cuts the beam independently of this software
/// (IEC 60825-1 / FDA CDRH variance territory).
/// </summary>
public static class LaserSafetyTransforms
{
    /// <summary>Symmetric ILDA 16-bit coordinate bounds.</summary>
    public const int FieldMin = -32767;
    public const int FieldMax = 32767;

    /** Full scale of a point's R, G, B and I channels. */
    private const int ColorMax = 65535;

    /// <summary>Largest per-channel color time shift, in points (about 1 ms at 30 kpps).</summary>
    public const int MaxColorShiftPoints = 32;

    /// <summary>Hard cap on list growth from jump subdivision, so a tiny MaxStepPerPoint
    /// against full-field jumps cannot balloon memory. Real points are never dropped,
    /// only subdivision points stop being inserted.</summary>
    private const int MaxTotalPoints = 131072;

    /// <summary>
    /// Safety configuration. Externalized so limits are never hardcoded at a call site
    /// and can be tuned per venue from operator inputs.
    /// </summary>
    public struct Config
    {
        /// <summary>Required because the struct uses field initializers (CS8983).</summary>
        public Config()
        {
        }

        /// <summary>Master beam switch. False blanks every point (positions are kept so the scanners keep moving the same path).</summary>
        public bool BeamEnable;

        /// <summary>Safe-zone rectangle in ILDA 16-bit units; everything outside is clipped in. Default is the full field.</summary>
        public float ZoneCenterX, ZoneCenterY;
        public float ZoneHalfWidth, ZoneHalfHeight;

        /// <summary>Consecutive points farther apart than this get subdivided so scanners can track the move linearly instead of overshooting.</summary>
        public float MaxStepPerPoint;

        /// <summary>How many consecutive near-motionless lit points are allowed before the rest of the run is blanked. Scan-fail protection.</summary>
        public int MaxStaticPoints;

        /// <summary>Step length (ILDA units) below which a point counts as "not moving".</summary>
        public int StaticDistance;

        /// <summary>0..1 ceiling applied to R, G and B of every lit point, independent of what upstream content requests.</summary>
        public float MaxBrightness;

        /// <summary>Per-channel projector calibration: gains multiply each diode (0..2), minimums
        /// (0..1) shut a diode off below that fraction - laser diodes start emitting at slightly
        /// different voltages, so near-black content would otherwise show a dim unwanted color.
        /// Applied after the brightness ceiling; these shape the output, they do not protect.</summary>
        public float ColorGainR = 1f, ColorGainG = 1f, ColorGainB = 1f;
        public float ColorMinR, ColorMinG, ColorMinB;

        /// <summary>Per-channel gamma exponent applied in calibration: output = (input)^(1/gamma)
        /// with gamma 1 = linear, 2.2 = the sRGB-style perceptual default, higher = mid-tones
        /// compressed more. Compensates diode brightness curves so ramps look even; per-channel
        /// values let mixed direct-diode/DPSS systems be tuned independently.</summary>
        public float ColorGammaR = 1f, ColorGammaG = 1f, ColorGammaB = 1f;

        /// <summary>TTL profile type: diodes without analog modulation are binary - input at or
        /// above 50% outputs the channel's maximum, below 50% turns it off. Gains, minimums and
        /// gamma are meaningless in this mode and are skipped.</summary>
        public bool TtlMode;

        /// <summary>Intensity cap (0..1) for beams that hold one position while lit: a full-power
        /// stationary beam burns skin and ignites materials, so a holding beam is dimmed to this
        /// fraction of its requested level. 1 disables the cap (venue-controlled, see the safety
        /// guide's "Max non-moving beam intensity").</summary>
        public float MaxStaticIntensity = 1f;

        /// <summary>Per-channel time shift in points: how many points a diode's color trails the
        /// beam position (negative = leads), within ±<see cref="MaxColorShiftPoints"/>. Compensates
        /// diodes that react at different speeds, so color edges land on the shape instead of
        /// fringing along the scan direction.</summary>
        public int ColorShiftR, ColorShiftG, ColorShiftB;

        /// <summary>Safety mask quads in ILDA 16-bit units: eight floats per mask (x1,y1 … x4,y4,
        /// corners in draw order), parallel to <see cref="MaskAllowOnlyInside"/> and
        /// <see cref="MaskLevels"/>. Points inside a blank mask are blanked; with an allow mask
        /// present, everything outside every allow mask is blanked. Disabled masks are absent
        /// from the arrays. Null or empty = no masks.</summary>
        public float[]? MaskCorners;
        public bool[]? MaskAllowOnlyInside;

        /// <summary>Per blank mask, the beam level kept inside (0..1): 0 blanks, 0.1 keeps 10%
        /// brightness - the attenuating mask for jurisdictions that allow reduced-level audience
        /// scanning. Only blank masks carry a level; allow masks blank outside unconditionally.</summary>
        public float[]? MaskLevels;
        public int MaskCount;

        /// <summary>Mirror the output horizontally/vertically (geometry flip). Applied in the
        /// zone clamp's coordinate space so clamps, masks and the out-of-bounds audit keep their
        /// guarantees after the flip.</summary>
        public bool FlipX, FlipY;

        public static Config Default => new()
        {
            BeamEnable = true,
            ZoneCenterX = 0f,
            ZoneCenterY = 0f,
            ZoneHalfWidth = FieldMax,
            ZoneHalfHeight = FieldMax,
            MaxStepPerPoint = 4000f,
            MaxStaticPoints = 64,
            StaticDistance = 5,
            MaxBrightness = 1f,
        };
    }

    /// <summary>Counters from the last Apply, surfaced to the operator user so safety actions are visible.</summary>
    public struct Stats
    {
        public int ClampedPoints;
        public int MaskedPoints;
        public int JumpPointsInserted;
        public int StaticBlanked;
        public int IntensityScaled;
        public int OutOfBoundsBlanked;
        public bool BeamDisabled;

        public readonly bool AnythingEngaged => BeamDisabled || ClampedPoints > 0 || MaskedPoints > 0 || JumpPointsInserted > 0
                                                || StaticBlanked > 0 || IntensityScaled > 0 || OutOfBoundsBlanked > 0;

        public readonly string Describe(int pointCount)
        {
            if (BeamDisabled)
                return $"BEAM DISABLED - {pointCount} points blanked";

            if (!AnythingEngaged)
                return $"OK - {pointCount} points";

            var parts = new List<string>(6);
            if (ClampedPoints > 0) parts.Add($"safe zone moved {ClampedPoints}");
            if (MaskedPoints > 0) parts.Add($"mask blanked {MaskedPoints}");
            if (JumpPointsInserted > 0) parts.Add($"jump limit +{JumpPointsInserted}");
            if (StaticBlanked > 0) parts.Add($"static dwell blanked {StaticBlanked}");
            if (IntensityScaled > 0) parts.Add($"brightness capped {IntensityScaled}");
            if (OutOfBoundsBlanked > 0) parts.Add($"out-of-bounds blanked {OutOfBoundsBlanked}");
            return $"SAFETY: {string.Join(" | ", parts)} - {pointCount} points out";
        }
    }

    /// <summary>
    /// Runs the full safety stage. Returns the processed point list (the source list
    /// when no subdivision points were needed). Counters are reset for this run, so a
    /// caller can safely reuse one <see cref="Stats"/> instance across frames.
    /// </summary>
    public static List<LaserPoint> Apply(List<LaserPoint> points, in Config config, ref Stats stats)
    {
        return Apply(points, null, in config, ref stats);
    }

    /// <summary>
    /// The same stage with a caller-owned scratch list for the jump-subdivision pass, so a
    /// steady-state pump never allocates: when subdivision runs, <paramref name="subdivisionScratch"/>
    /// is cleared and filled and returned instead of a fresh list. May be null (allocates then).
    /// </summary>
    public static List<LaserPoint> Apply(List<LaserPoint> points, List<LaserPoint> subdivisionScratch, in Config config, ref Stats stats)
    {
        // Stats describe THIS call ("counters from the last Apply"). Reset up front so a
        // long-lived stream that reuses its Stats does not accumulate a show-long history
        // and report every frame as unsafe.
        stats = default;

        if (points == null)
            return [];

        if (points.Count == 0)
            return points;

        NormalizeBlanking(points);

        if (!config.BeamEnable)
        {
            for (var i = 0; i < points.Count; i++)
                points[i] = LaserPoint.CreateBlanked(points[i].X, points[i].Y);
            stats.BeamDisabled = true;
            return points;
        }

        var safeConfig = Sanitize(in config);
        ClampToScanField(points, in safeConfig, ref stats);
        ApplyColorTimeShift(points, in safeConfig);
        ApplyMasks(points, in safeConfig, ref stats);
        points = LimitJumpDistance(points, subdivisionScratch, in safeConfig, ref stats);
        LimitStaticDwell(points, in safeConfig, ref stats);
        ClampIntensity(points, in safeConfig, ref stats);
        ApplyColorLevels(points, in safeConfig);
        DetectAndBlankOutOfBounds(points, ref stats);
        return points;
    }

    /// <summary>
    /// Folds non-finite limit values into a conservative configuration. NaN makes every clamp
    /// comparison false, so a NaN limit is not "unlimited" - it silently disables the stage (a NaN
    /// safe-zone width used to pass every coordinate through). Non-finite limits are broken values,
    /// not operator intent: they collapse to the most restrictive interpretation.
    /// </summary>
    private static Config Sanitize(in Config config)
    {
        var result = config;

        // A non-finite center has no meaningful location; the field origin is the only
        // conservative answer and keeps the zone intersected with the physical field.
        if (!float.IsFinite(result.ZoneCenterX))
            result.ZoneCenterX = 0f;
        if (!float.IsFinite(result.ZoneCenterY))
            result.ZoneCenterY = 0f;

        // An infinite or negative extent is already intersected with the physical field, so only
        // NaN needs a value - and the center line is the smallest zone it can mean.
        result.ZoneHalfWidth = NormalizeZoneExtent(result.ZoneHalfWidth);
        result.ZoneHalfHeight = NormalizeZoneExtent(result.ZoneHalfHeight);

        if (!float.IsFinite(result.MaxStepPerPoint))
            result.MaxStepPerPoint = Config.Default.MaxStepPerPoint;

        if (!float.IsFinite(result.MaxBrightness))
            result.MaxBrightness = 0f;

        // Calibration gains shape the output; a broken value must not boost a diode beyond
        // twice its content level (and NaN gains would zero it - NaN fails every comparison).
        result.ColorGainR = Math.Clamp(float.IsFinite(result.ColorGainR) ? result.ColorGainR : 1f, 0f, 2f);
        result.ColorGainG = Math.Clamp(float.IsFinite(result.ColorGainG) ? result.ColorGainG : 1f, 0f, 2f);
        result.ColorGainB = Math.Clamp(float.IsFinite(result.ColorGainB) ? result.ColorGainB : 1f, 0f, 2f);
        result.ColorMinR = Math.Clamp(float.IsFinite(result.ColorMinR) ? result.ColorMinR : 0f, 0f, 1f);
        result.ColorMinG = Math.Clamp(float.IsFinite(result.ColorMinG) ? result.ColorMinG : 0f, 0f, 1f);
        result.ColorMinB = Math.Clamp(float.IsFinite(result.ColorMinB) ? result.ColorMinB : 0f, 0f, 1f);
        // Gamma shapes the output curve; a broken value folds to linear rather than boosting.
        result.ColorGammaR = Math.Clamp(float.IsFinite(result.ColorGammaR) ? result.ColorGammaR : 1f, 0.1f, 8f);
        result.ColorGammaG = Math.Clamp(float.IsFinite(result.ColorGammaG) ? result.ColorGammaG : 1f, 0.1f, 8f);
        result.ColorGammaB = Math.Clamp(float.IsFinite(result.ColorGammaB) ? result.ColorGammaB : 1f, 0.1f, 8f);
        // The static-beam hold power is a venue decision: clamp into 0..1 so a wild value can
        // neither blank everything (negative) nor exceed full power (>1).
        result.MaxStaticIntensity = Math.Clamp(float.IsFinite(result.MaxStaticIntensity) ? result.MaxStaticIntensity : 0f, 0f, 1f);
        result.ColorShiftR = Math.Clamp(result.ColorShiftR, -MaxColorShiftPoints, MaxColorShiftPoints);
        result.ColorShiftG = Math.Clamp(result.ColorShiftG, -MaxColorShiftPoints, MaxColorShiftPoints);
        result.ColorShiftB = Math.Clamp(result.ColorShiftB, -MaxColorShiftPoints, MaxColorShiftPoints);

        // The dwell cap and the motion threshold are counts, not geometry: clamp them into
        // the range the stage can actually express instead of letting negative/overflow
        // values silently blank or pass every point.
        result.MaxStaticPoints = Math.Clamp(result.MaxStaticPoints, 0, 100_000);
        result.StaticDistance = Math.Clamp(result.StaticDistance, 0, FieldMax * 2);

        return result;
    }

    /// <summary>
    /// Makes <c>I &lt;= 0</c> mean dark on every transport. Every later stage treats such a point as
    /// blanked and skips it, but no encoder can rely on the I channel alone: PONK and CITP carry no
    /// intensity at all, and many ILDA projectors ignore I and emit on RGB. A point with I = 0 and
    /// colour would therefore bypass the brightness, dwell and bounds stages and still be drawn.
    /// </summary>
    public static void NormalizeBlanking(List<LaserPoint> points)
    {
        for (var i = 0; i < points.Count; i++)
        {
            var p = points[i];
            if (p.I <= 0 && (p.I != 0 || p.R != 0 || p.G != 0 || p.B != 0))
                points[i] = LaserPoint.CreateBlanked(p.X, p.Y);
        }
    }

    private static float NormalizeZoneExtent(float halfExtent)
        => float.IsNaN(halfExtent) ? 0f : MathF.Min(MathF.Abs(halfExtent), FieldMax);

    /// <summary>
    /// Hard-clips every point into the safe-zone rectangle, intersected with the
    /// physical field. The last line of defense against the beam ever leaving the
    /// intended coverage area.
    /// </summary>
    public static void ClampToScanField(List<LaserPoint> points, in Config config, ref Stats stats)
    {
        var safeConfig = Sanitize(in config);
        var minX = Math.Max(safeConfig.ZoneCenterX - safeConfig.ZoneHalfWidth, FieldMin);
        var maxX = Math.Min(safeConfig.ZoneCenterX + safeConfig.ZoneHalfWidth, FieldMax);
        var minY = Math.Max(safeConfig.ZoneCenterY - safeConfig.ZoneHalfHeight, FieldMin);
        var maxY = Math.Min(safeConfig.ZoneCenterY + safeConfig.ZoneHalfHeight, FieldMax);
        if (minX > maxX) (minX, maxX) = (maxX, minX);
        if (minY > maxY) (minY, maxY) = (maxY, minY);

        // Geometry flip around the field centre: applied inside the clamp so masks, the
        // zone and the out-of-bounds audit keep their guarantees in flipped space too.
        var flipX = safeConfig.FlipX;
        var flipY = safeConfig.FlipY;

        for (var i = 0; i < points.Count; i++)
        {
            var p = points[i];
            var x = Math.Clamp(p.X, minX, maxX);
            var y = Math.Clamp(p.Y, minY, maxY);
            if (flipX)
                x = minX + maxX - x;
            if (flipY)
                y = minY + maxY - y;
            var ix = (int)x;
            var iy = (int)y;
            if (ix != p.X || iy != p.Y)
            {
                points[i] = new LaserPoint(ix, iy, p.R, p.G, p.B, p.I);
                stats.ClampedPoints++;
            }
        }
    }

    /// <summary>
    /// Shapes every lit point a safety mask covers, position kept: the scanners keep moving the
    /// same path with the beam off (or dimmed, by the mask's level) inside the mask - deleting
    /// the points would connect the neighbours with a lit stroke straight across the masked
    /// area. With an allow-only mask present, everything outside every allow mask is blanked
    /// first; blank masks subtract on top, so a blank mask inside an allow mask wins. Points
    /// already blanked are skipped.
    /// </summary>
    private static void ApplyMasks(List<LaserPoint> points, in Config config, ref Stats stats)
    {
        var corners = config.MaskCorners;
        var maskCount = config.MaskCount;
        if (corners == null || maskCount <= 0 || corners.Length < maskCount * 8)
            return;

        var levels = config.MaskLevels;
        var allowOnly = config.MaskAllowOnlyInside;
        var hasAllowOnly = false;
        for (var m = 0; m < maskCount; m++)
        {
            if (allowOnly != null && m < allowOnly.Length && allowOnly[m])
            {
                hasAllowOnly = true;
                break;
            }
        }

        for (var i = 0; i < points.Count; i++)
        {
            var p = points[i];
            if (p.I == 0 && p.R == 0 && p.G == 0 && p.B == 0)
                continue;

            var masked = false;
            float level = 0f;
            if (hasAllowOnly)
            {
                var insideAnyAllow = false;
                for (var m = 0; m < maskCount && !insideAnyAllow; m++)
                {
                    if (allowOnly[m] && InsideQuad(p.X, p.Y, corners, m))
                        insideAnyAllow = true;
                }

                masked = !insideAnyAllow;
            }

            for (var m = 0; m < maskCount && !masked; m++)
            {
                if (allowOnly != null && m < allowOnly.Length && allowOnly[m])
                    continue;

                if (InsideQuad(p.X, p.Y, corners, m))
                {
                    masked = true;
                    level = levels != null && m < levels.Length ? levels[m] : 0f;
                }
            }

            if (!masked)
                continue;

            if (level > 0f && level < 1f)
            {
                // Attenuating mask: the beam stays on inside the mask but dimmed - the
                // reduced-level audience-scanning case. Level 1 means blank.
                points[i] = new LaserPoint(p.X, p.Y,
                                           (int)(p.R * level), (int)(p.G * level), (int)(p.B * level),
                                           (int)(p.I * level));
            }
            else
            {
                points[i] = LaserPoint.CreateBlanked(p.X, p.Y);
            }

            stats.MaskedPoints++;
        }
    }

    /// <summary>Even-odd point-in-quad test; handles concave quads. A point exactly on an edge
    /// counts either way - both readings are a safe answer.</summary>
    private static bool InsideQuad(int px, int py, float[] corners, int maskIndex)
    {
        var inside = false;
        var offset = maskIndex * 8;
        for (var i = 0; i < 4; i++)
        {
            var j = (i + 3) & 3;
            var xi = corners[offset + 2 * i];
            var yi = corners[offset + 2 * i + 1];
            var xj = corners[offset + 2 * j];
            var yj = corners[offset + 2 * j + 1];
            if ((yi > py) != (yj > py) && px < (xj - xi) * (py - yi) / (yj - yi) + xi)
                inside = !inside;
        }

        return inside;
    }

    /// <summary>
    /// Subdivides jumps between consecutive points that are longer than MaxStepPerPoint,
    /// so the scanners can track the move linearly instead of overshooting or arcing
    /// through unintended positions. Inserted points are blanked when either end of the
    /// jump is blanked; lit jumps carry the destination point's color.
    /// </summary>
    public static List<LaserPoint> LimitJumpDistance(List<LaserPoint> points, in Config config, ref Stats stats)
    {
        return LimitJumpDistance(points, null, in config, ref stats);
    }

    private static List<LaserPoint> LimitJumpDistance(List<LaserPoint> points, List<LaserPoint> subdivisionScratch, in Config config, ref Stats stats)
    {
        var maxStep = MathF.Max(1f, Sanitize(in config).MaxStepPerPoint);

        // Fast path: measure first so frames without long jumps don't allocate.
        // Cast to float before squaring to avoid int32 overflow (e.g. 60000*60000 > int.MaxValue).
        var longestSq = 0f;
        for (var i = 1; i < points.Count; i++)
        {
            var dx = (float)(points[i].X - points[i - 1].X);
            var dy = (float)(points[i].Y - points[i - 1].Y);
            longestSq = MathF.Max(longestSq, dx * dx + dy * dy);
        }

        if (MathF.Sqrt(longestSq) <= maxStep)
            return points;

        var result = subdivisionScratch ?? new List<LaserPoint>(points.Count * 2);
        result.Clear();
        result.Add(points[0]);

        for (var i = 1; i < points.Count; i++)
        {
            var prev = points[i - 1];
            var cur = points[i];
            // Cast to float to avoid int32 overflow on long jumps (e.g. 60000*60000 > int.MaxValue)
            var dx = (float)(cur.X - prev.X);
            var dy = (float)(cur.Y - prev.Y);
            var dist = MathF.Sqrt(dx * dx + dy * dy);

            if (dist > maxStep && result.Count < MaxTotalPoints)
            {
                var steps = (int)MathF.Ceiling(dist / maxStep);
                var jumpLit = prev.I > 0 && cur.I > 0;

                for (var s = 1; s < steps && result.Count < MaxTotalPoints; s++)
                {
                    var t = s / (float)steps;
                    var x = (int)MathF.Round(prev.X + dx * t);
                    var y = (int)MathF.Round(prev.Y + dy * t);
                    result.Add(jumpLit
                                   ? new LaserPoint(x, y, cur.R, cur.G, cur.B, cur.I)
                                   : LaserPoint.CreateBlanked(x, y));
                    stats.JumpPointsInserted++;
                }
            }

            result.Add(cur);
        }

        return result;
    }

    /// <summary>
    /// Caps how long the beam may sit near-motionless at one coordinate - a frozen lit beam is the
    /// classic scan-failure hazard. Points beyond the cap keep their position but are blanked, so
    /// the scanners keep moving with the beam off.
    ///
    /// The frame is treated as a loop, because that is how it is played: a DAC replays the last
    /// frame until a new one arrives. A run therefore continues across the seam (last point back
    /// to the first), and only movement ends it - blanking in place does not, since the beam comes
    /// back on at the same spot. A frame that never moves at all would be a static beam for as long
    /// as it is shown, so all of its lit points are blanked.
    /// </summary>
    public static void LimitStaticDwell(List<LaserPoint> points, in Config config, ref Stats stats)
    {
        var count = points.Count;
        if (count == 0)
            return;

        var safeConfig = Sanitize(in config);
        // Widened before squaring: a full-field step squared overflows int and would read as "not moving".
        var staticDistance = (long)safeConfig.StaticDistance;
        var thresholdSq = staticDistance * staticDistance;
        var maxStaticPoints = safeConfig.MaxStaticPoints;

        // MadMapper's "max non-moving beam intensity": a stationary beam beyond the dwell limit is
        // dimmed to this fraction of its level instead of blanked - a full-power beam that holds one
        // position burns skin and ignites materials, and the venue decides how much hold power is
        // acceptable. 1 keeps the strict blank (the scan-fail default); 0 blanks as well.
        var staticIntensity = safeConfig.MaxStaticIntensity;
        var dimInsteadOfBlank = staticIntensity < 1f;

        // Start right after a movement, so the run that wraps over the seam is counted in one pass.
        var start = -1;
        for (var i = 0; i < count; i++)
        {
            if (MovesFromPrevious(points, i, thresholdSq))
            {
                start = i;
                break;
            }
        }

        if (start < 0)
        {
            for (var i = 0; i < count; i++)
            {
                var p = points[i];
                if (p.I <= 0)
                    continue;

                points[i] = DimOrBlank(p, staticIntensity, dimInsteadOfBlank);
                stats.StaticBlanked++;
            }

            return;
        }

        var run = 0;
        for (var k = 0; k < count; k++)
        {
            var i = (start + k) % count;
            if (MovesFromPrevious(points, i, thresholdSq))
                run = 0;

            var p = points[i];
            if (p.I <= 0)
                continue;

            run++;
            if (run > maxStaticPoints)
            {
                points[i] = DimOrBlank(p, staticIntensity, dimInsteadOfBlank);
                stats.StaticBlanked++;
            }
        }
    }

    /** A beam that has held past the dwell limit: dimmed to the venue's static-intensity fraction, or blanked. */
    private static LaserPoint DimOrBlank(LaserPoint p, float staticIntensity, bool dimInsteadOfBlank)
    {
        if (!dimInsteadOfBlank || staticIntensity <= 0f)
            return LaserPoint.CreateBlanked(p.X, p.Y);

        var r = (int)(p.R * staticIntensity);
        var g = (int)(p.G * staticIntensity);
        var b = (int)(p.B * staticIntensity);
        var i2 = (int)(p.I * staticIntensity);
        return r + g + b + i2 > 0
                   ? new LaserPoint(p.X, p.Y, r, g, b, i2)
                   : LaserPoint.CreateBlanked(p.X, p.Y);
    }

    /** True when point <paramref name="index"/> is farther than the static threshold from the point before it in loop order. */
    private static bool MovesFromPrevious(List<LaserPoint> points, int index, long thresholdSq)
    {
        var previous = points[index == 0 ? points.Count - 1 : index - 1];
        var current = points[index];
        long dx = current.X - previous.X;
        long dy = current.Y - previous.Y;
        return dx * dx + dy * dy > thresholdSq;
    }

    /// <summary>
    /// Hard ceiling on output color, independent of whatever upstream content requests. Scales the
    /// I channel too: a single-colour projector driven from I would otherwise ignore the ceiling.
    /// A lit point whose intensity rounds to zero becomes a canonical blank. Also folds stray
    /// out-of-range values into 0..65535.
    /// </summary>
    public static void ClampIntensity(List<LaserPoint> points, in Config config, ref Stats stats)
    {
        var scale = Math.Clamp(Sanitize(in config).MaxBrightness, 0f, 1f);

        for (var i = 0; i < points.Count; i++)
        {
            var p = points[i];
            if (p.I <= 0)
                continue;

            var r = (int)Math.Min(Math.Max(p.R, 0) * scale, 65535f);
            var g = (int)Math.Min(Math.Max(p.G, 0) * scale, 65535f);
            var b = (int)Math.Min(Math.Max(p.B, 0) * scale, 65535f);
            var intensity = (int)Math.Min(p.I * scale, 65535f);
            if (r == p.R && g == p.G && b == p.B && intensity == p.I)
                continue;

            points[i] = intensity > 0
                            ? new LaserPoint(p.X, p.Y, r, g, b, intensity)
                            : LaserPoint.CreateBlanked(p.X, p.Y);
            stats.IntensityScaled++;
        }
    }

    /// <summary>
    /// Moves each diode's color along the point stream by its own number of points, so diodes
    /// that react slower or faster than the others line up with the beam position. The frame is
    /// not treated as a loop and colors shifted past either end are dropped. Only lit points
    /// take part: a blanked point stays dark and the intensity gate never changes, so the mask,
    /// dwell and brightness stages that run afterwards see the final colors and keep their
    /// guarantees. A lit point whose colors all shift away goes dark instead of lingering
    /// at I &gt; 0 on projectors that read the intensity channel. Default shifts leave the frame
    /// untouched.
    /// </summary>
    public static void ApplyColorTimeShift(List<LaserPoint> points, in Config config)
    {
        var safeConfig = Sanitize(in config);
        var shiftR = safeConfig.ColorShiftR;
        var shiftG = safeConfig.ColorShiftG;
        var shiftB = safeConfig.ColorShiftB;
        if (shiftR == 0 && shiftG == 0 && shiftB == 0)
            return;

        var count = points.Count;
        var original = ArrayPool<int>.Shared.Rent(count * 3);
        try
        {
            for (var i = 0; i < count; i++)
            {
                var p = points[i];
                original[i * 3] = Math.Max(p.R, 0);
                original[i * 3 + 1] = Math.Max(p.G, 0);
                original[i * 3 + 2] = Math.Max(p.B, 0);
            }

            for (var i = 0; i < count; i++)
            {
                var p = points[i];
                if (p.I <= 0)
                    continue;

                var hadColor = original[i * 3] != 0 || original[i * 3 + 1] != 0 || original[i * 3 + 2] != 0;
                var r = ShiftedChannel(original, count, 0, i - shiftR);
                var g = ShiftedChannel(original, count, 1, i - shiftG);
                var b = ShiftedChannel(original, count, 2, i - shiftB);
                if (r == original[i * 3] && g == original[i * 3 + 1] && b == original[i * 3 + 2])
                    continue;

                points[i] = hadColor && r == 0 && g == 0 && b == 0
                                ? LaserPoint.CreateBlanked(p.X, p.Y)
                                : new LaserPoint(p.X, p.Y, r, g, b, p.I);
            }
        }
        finally
        {
            ArrayPool<int>.Shared.Return(original);
        }
    }

    private static int ShiftedChannel(int[] original, int count, int channel, int sourceIndex)
        => (uint)sourceIndex < (uint)count ? original[sourceIndex * 3 + channel] : 0;

    /// <summary>
    /// Per-channel projector calibration: gains shape each diode's level, minimums cut a diode
    /// off entirely below its threshold (a diode near its minimum voltage emits a faint
    /// unwanted glow on near-black content). Runs after the brightness ceiling; gains can lift
    /// levels back up, so a point that left here dim may still light - this stage calibrates,
    /// it does not protect. Default gains/minimums leave the frame untouched.
    /// </summary>
    public static void ApplyColorLevels(List<LaserPoint> points, in Config config)
    {
        var safeConfig = Sanitize(in config);

        // TTL diodes: binary on/off at the 50% line - gains, minimums and gamma are meaningless.
        if (safeConfig.TtlMode)
        {
            var half = ColorMax / 2;
            for (var i = 0; i < points.Count; i++)
            {
                var p = points[i];
                var r = p.R >= half ? ColorMax : 0;
                var g = p.G >= half ? ColorMax : 0;
                var b = p.B >= half ? ColorMax : 0;
                if (r == p.R && g == p.G && b == p.B)
                    continue;

                points[i] = r + g + b > 0
                                ? new LaserPoint(p.X, p.Y, r, g, b, p.I)
                                : LaserPoint.CreateBlanked(p.X, p.Y);
            }

            return;
        }

        var gains = safeConfig.ColorGainR == 1f && safeConfig.ColorGainG == 1f && safeConfig.ColorGainB == 1f;
        var minimums = safeConfig.ColorMinR == 0f && safeConfig.ColorMinG == 0f && safeConfig.ColorMinB == 0f;
        var gammaR = safeConfig.ColorGammaR;
        var gammaG = safeConfig.ColorGammaG;
        var gammaB = safeConfig.ColorGammaB;
        var linear = gammaR == 1f && gammaG == 1f && gammaB == 1f;
        if (gains && minimums && linear)
            return;

        var minR = safeConfig.ColorMinR * ColorMax;
        var minG = safeConfig.ColorMinG * ColorMax;
        var minB = safeConfig.ColorMinB * ColorMax;

        // Gamma lookup tables, rebuilt lazily when the exponent changes (a calibration edit, not a
        // per-frame event). Captured by reference outside the rebuild lock; a rebuild mid-frame at
        // worst mixes old and new tables for one frame - a calibration change, not a safety stage.
        var lutR = GetGammaLut(gammaR, ref _gammaLutR, ref _gammaLutBitsR);
        var lutG = GetGammaLut(gammaG, ref _gammaLutG, ref _gammaLutBitsG);
        var lutB = GetGammaLut(gammaB, ref _gammaLutB, ref _gammaLutBitsB);

        for (var i = 0; i < points.Count; i++)
        {
            var p = points[i];
            if (p.I == 0 && p.R == 0 && p.G == 0 && p.B == 0)
                continue;

            var r = (int)(Math.Max(p.R, 0) * safeConfig.ColorGainR);
            var g = (int)(Math.Max(p.G, 0) * safeConfig.ColorGainG);
            var b = (int)(Math.Max(p.B, 0) * safeConfig.ColorGainB);

            // A channel that gains up from zero must not glow: the minimum applies to the
            // pre-gain level, so an off channel stays off.
            if (p.R < minR)
                r = 0;
            if (p.G < minG)
                g = 0;
            if (p.B < minB)
                b = 0;

            if (!linear)
            {
                r = lutR[Math.Clamp(r, 0, ColorMax)];
                g = lutG[Math.Clamp(g, 0, ColorMax)];
                b = lutB[Math.Clamp(b, 0, ColorMax)];
            }

            r = Math.Min(r, ColorMax);
            g = Math.Min(g, ColorMax);
            b = Math.Min(b, ColorMax);

            if (r == p.R && g == p.G && b == p.B)
                continue;

            points[i] = p.I > 0 || r > 0 || g > 0 || b > 0
                            ? new LaserPoint(p.X, p.Y, r, g, b, p.I)
                            : LaserPoint.CreateBlanked(p.X, p.Y);
        }
    }

    private static readonly object GammaLutLock = new();
    private static ushort[] _gammaLutR = EmptyGammaLut();
    private static ushort[] _gammaLutG = EmptyGammaLut();
    private static ushort[] _gammaLutB = EmptyGammaLut();
    private static float _gammaLutBitsR, _gammaLutBitsG, _gammaLutBitsB;

    private static ushort[] EmptyGammaLut() => new ushort[ColorMax + 1];

    /// <summary>
    /// Returns the LUT for <paramref name="gamma"/> (out = in^(1/gamma)), rebuilding the cached
    /// table when the exponent changed. LUT indices cover 0..65535 so lookup replaces Math.Pow
    /// in the per-point path; exponent quantized to 1/1000 so slider noise does not rebuild.
    /// </summary>
    private static ushort[] GetGammaLut(float gamma, ref ushort[] cache, ref float cacheBits)
    {
        var quantized = MathF.Round(gamma * 1000f) / 1000f;
        lock (GammaLutLock)
        {
            if (cacheBits == quantized)
                return cache;

            var lut = new ushort[ColorMax + 1];
            var exponent = 1f / Math.Clamp(quantized, 0.1f, 8f);
            for (var v = 0; v <= ColorMax; v++)
            {
                lut[v] = (ushort)Math.Clamp((int)MathF.Round(MathF.Pow(v / (float)ColorMax, exponent) * ColorMax), 0, ColorMax);
            }

            cache = lut;
            cacheBits = quantized;
            return lut;
        }
    }

    /// <summary>
    /// Belt-and-suspenders final audit: a lit point outside the physical field is
    /// force-blanked rather than drawn. Should never trip after ClampToScanField - if it
    /// does, an earlier stage was bypassed and the counters make that visible.
    /// </summary>
    public static void DetectAndBlankOutOfBounds(List<LaserPoint> points, ref Stats stats)
    {
        for (var i = 0; i < points.Count; i++)
        {
            var p = points[i];
            if (p.I <= 0)
                continue;

            if (p.X < FieldMin || p.X > FieldMax || p.Y < FieldMin || p.Y > FieldMax)
            {
                points[i] = LaserPoint.CreateBlanked(p.X, p.Y);
                stats.OutOfBoundsBlanked++;
            }
        }
    }

    /// <summary>
    /// Copies source into destination with all color zeroed (positions preserved, so
    /// scanners keep moving the same path with the beam off). Used by output operators
    /// for E-stop and watchdog blanking. Iterates only to the shorter of the two arrays
    /// so a mismatch cannot cause an IndexOutOfRangeException.
    /// </summary>
    public static void BlankCopyInto(LaserPoint[] source, LaserPoint[] destination)
    {
        var count = Math.Min(source.Length, destination.Length);
        for (var i = 0; i < count; i++)
            destination[i] = LaserPoint.CreateBlanked(source[i].X, source[i].Y);
    }
}
