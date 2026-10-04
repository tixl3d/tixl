using System;
using System.Numerics;

namespace T3.Core.Output;

/// <summary>
/// Recovers a surface's rectilinear space from lines drawn across features that are known to be physically
/// horizontal or vertical.
/// <para>The lines are traced in <b>surface space</b>, judged on the wall: you nudge a line until its
/// projection lies along a real mortar joint or door frame, and its surface coordinates then encode a known
/// physical direction. If the corner-pin were correct, every horizontal line would be parallel to every
/// other; where they actually meet — their vanishing point — measures exactly how wrong it is. Two vanishing
/// points give the line at infinity (affine rectification), and forcing the two directions apart gives a
/// shear-free space.</para>
/// <para>What this <i>cannot</i> recover is the aspect ratio: directions say nothing about how long a meter
/// is along each axis. The solve therefore preserves the surface's current width and height, leaving scale
/// and aspect to the measured lengths. Straighten fixes the keystone; lengths fix the metric.</para>
/// </summary>
public static class LineRectifier
{
    /// <summary>Two lines per axis is the minimum that determines a vanishing point.</summary>
    public const int MinLinesPerAxis = 2;

    /// <summary>
    /// Which axis a line claims, and by how much it currently misses it. Read from the line's angle every
    /// time it is asked for and never stored, so nudging an endpoint re-classifies at once — a line dragged
    /// from flat to upright simply becomes a vertical one. Shared by the editor overlay and the projected
    /// pass so both agree on what a line is.
    /// </summary>
    public static bool IsHorizontal(Vector2 p1, Vector2 p2, out float deviationInDegrees)
    {
        var delta = p2 - p1;
        var angle = MathF.Atan2(delta.Y, delta.X) * 180f / MathF.PI;
        if (angle < 0)
            angle += 180;

        var fromHorizontal = MathF.Min(angle, 180 - angle);
        var fromVertical = MathF.Abs(90 - angle);
        var horizontal = fromHorizontal <= fromVertical;
        deviationInDegrees = horizontal ? fromHorizontal : fromVertical;
        return horizontal;
    }

    /// <summary>Alignment good enough to stop nudging.</summary>
    public const float AlignedDegrees = 0.15f;

    /// <summary>Alignment bad enough to read as "not straightened yet".</summary>
    public const float MisalignedDegrees = 4f;

    /// <summary>
    /// Solves the homography taking the surface's current (distorted) space to a rectilinear one, normalized
    /// to start at the origin and keep <paramref name="size"/>. Lines are (x1, y1, x2, y2) in surface space.
    /// </summary>
    public static bool TrySolve(ReadOnlySpan<Vector4> horizontal, ReadOnlySpan<Vector4> vertical, Vector2 size,
                                out Homography rectify)
    {
        rectify = Homography.Identity;
        if (horizontal.Length < MinLinesPerAxis || vertical.Length < MinLinesPerAxis
            || size.X <= 0.0001f || size.Y <= 0.0001f)
        {
            return false;
        }

        if (!TryGetVanishingPoint(horizontal, out var vanishX) || !TryGetVanishingPoint(vertical, out var vanishY))
            return false;

        // The line through both vanishing points is the image of the line at infinity; sending it back there
        // is what makes physically parallel features parallel again.
        var horizon = Vector3.Cross(vanishX, vanishY);
        if (MathF.Abs(horizon.Z) < 1e-9f)
            return false;

        var affine = Homography.Identity;
        affine.M31 = horizon.X / horizon.Z;
        affine.M32 = horizon.Y / horizon.Z;

        // Both vanishing points are now at infinity, so what is left of them is a pure direction. Mapping
        // those two directions onto the axes removes the remaining shear.
        var dirX = TransformToDirection(affine, vanishX);
        var dirY = TransformToDirection(affine, vanishY);

        // A point at infinity is the same point negated, so the recovered direction may come back pointing
        // the wrong way. Taking it as-is builds a mirror into the result, which straightens the lines
        // perfectly and flips the content — so pin each direction to its own axis first.
        if (dirX.X < 0)
            dirX = -dirX;

        if (dirY.Y < 0)
            dirY = -dirY;

        var determinant = dirX.X * dirY.Y - dirX.Y * dirY.X;
        if (MathF.Abs(determinant) < 1e-9f)
            return false;

        var deshear = Homography.Identity;
        deshear.M11 = dirY.Y / determinant;
        deshear.M12 = -dirY.X / determinant;
        deshear.M21 = -dirX.Y / determinant;
        deshear.M22 = dirX.X / determinant;

        var solved = Homography.Multiply(deshear, affine);

        // The de-shear normalized both directions to unit length, which is arbitrary — so rescale back onto
        // the surface's current extent. This is the deliberate hand-off to "apply lengths": nothing here
        // claims to know the true aspect.
        Span<Vector2> corners =
            [
                Vector2.Zero,
                new Vector2(size.X, 0),
                new Vector2(size.X, size.Y),
                new Vector2(0, size.Y),
            ];

        var min = new Vector2(float.MaxValue);
        var max = new Vector2(float.MinValue);
        for (var i = 0; i < 4; i++)
        {
            var p = solved.TransformPoint(corners[i]);
            min = Vector2.Min(min, p);
            max = Vector2.Max(max, p);
        }

        var extent = max - min;
        if (extent.X < 1e-6f || extent.Y < 1e-6f)
            return false;

        var scale = new Vector2(size.X / extent.X, size.Y / extent.Y);
        var normalize = Homography.Identity;
        normalize.M11 = scale.X;
        normalize.M22 = scale.Y;
        normalize.M13 = -min.X * scale.X;
        normalize.M23 = -min.Y * scale.Y;

        rectify = Homography.Multiply(normalize, solved);
        return true;
    }

    /// <summary>
    /// Refines a corner pin until the reference lines come out level and plumb.
    /// <para>Lines are given in <b>output pixels</b> — that is the invariant the user established by aiming
    /// each one at a physical feature, and moving the corners must not move it. Each candidate quad is scored
    /// by mapping those fixed observations back into surface space and summing their squared angular error.</para>
    /// <para>Coordinate descent with a halving step, plus a pull back toward the starting quad. That
    /// regularization is what lets this work where <see cref="TrySolve"/> can't: with only one line on an
    /// axis the constraints leave degrees of freedom open, and without it the quad would wander off toward
    /// whatever squarer shape also satisfies them.</para>
    /// <para>Every candidate is held to the starting quad's scale and aspect (see <see cref="KeepGauge"/>),
    /// and each line's say is weighted by its length (see <see cref="ScoreQuad"/>). Both keep this honest to
    /// what lines can actually tell: a direction each, and nothing about the metric.</para>
    /// </summary>
    public static bool TryRefineQuad(ReadOnlySpan<Vector4> linesInOutput, Vector2 size, ReadOnlySpan<Vector2> quad,
                                     Span<Vector2> refined)
    {
        if (linesInOutput.Length == 0 || quad.Length < 4 || refined.Length < 4
            || size.X <= 0.0001f || size.Y <= 0.0001f)
        {
            return false;
        }

        Span<Vector2> rect =
            [
                Vector2.Zero,
                new Vector2(size.X, 0),
                new Vector2(size.X, size.Y),
                new Vector2(0, size.Y),
            ];

        Span<Vector2> start = stackalloc Vector2[4];
        Span<Vector2> current = stackalloc Vector2[4];
        Span<Vector2> trial = stackalloc Vector2[4];
        for (var i = 0; i < 4; i++)
        {
            start[i] = quad[i];
            current[i] = quad[i];
        }

        var diagonal = MathF.Max((quad[2] - quad[0]).Length(), 1e-4f);
        var best = ScoreQuad(current, start, rect, linesInOutput, diagonal);
        var step = diagonal * 0.004f;
        var limit = diagonal * 5e-6f;

        ReadOnlySpan<Vector2> directions = [new Vector2(1, 0), new Vector2(-1, 0), new Vector2(0, 1), new Vector2(0, -1)];

        for (var iteration = 0; iteration < 200 && step > limit; iteration++)
        {
            var improved = false;
            for (var corner = 0; corner < 4; corner++)
            {
                foreach (var direction in directions)
                {
                    current.CopyTo(trial);
                    trial[corner] += direction * step;
                    KeepGauge(trial, start);

                    var score = ScoreQuad(trial, start, rect, linesInOutput, diagonal);
                    if (score >= best - 1e-12f)
                        continue;

                    best = score;
                    trial.CopyTo(current);
                    improved = true;
                }
            }

            if (!improved)
                step *= 0.5f;
        }

        current[..4].CopyTo(refined);
        return true;
    }

    /// <summary>
    /// Squared angular error of the lines under this quad, each weighted by its length, plus the pull toward
    /// the start. A line states a direction by where its two ends sit, so the same hand-width of imprecision
    /// at the ends is a large angle on a short line and a small one on a long line: the shorter line is the
    /// weaker measurement, and its say is scaled by the square of its length to match. The weights are
    /// normalized to average one, so lengths decide the lines' say relative to each other while the pull
    /// toward the start keeps the footing it was tuned against.
    /// </summary>
    private static float ScoreQuad(ReadOnlySpan<Vector2> candidate, ReadOnlySpan<Vector2> start, ReadOnlySpan<Vector2> rect,
                                   ReadOnlySpan<Vector4> linesInOutput, float diagonal)
    {
        if (!Homography.TryComputeQuadToQuad(candidate, rect, out var outputToSurface))
            return float.MaxValue;

        // Lengths are read in surface space, where the lines were judged, against the surface's own diagonal.
        var reference = MathF.Max(rect[2].Length(), 1e-4f);
        var weighted = 0f;
        var weights = 0f;
        foreach (var line in linesInOutput)
        {
            var a = outputToSurface.TransformPoint(new Vector2(line.X, line.Y));
            var b = outputToSurface.TransformPoint(new Vector2(line.Z, line.W));
            IsHorizontal(a, b, out var deviation);

            var relativeLength = (b - a).Length() / reference;
            var weight = relativeLength * relativeLength;
            weighted += weight * deviation * deviation;
            weights += weight;
        }

        var score = weights > 1e-9f ? weighted / weights * linesInOutput.Length : 0f;

        for (var i = 0; i < 4; i++)
        {
            var moved = (candidate[i] - start[i]).Length() / diagonal;
            score += 50 * moved * moved;
        }

        return score;
    }

    /// <summary>
    /// Puts a candidate back on the starting quad's gauge: its centre, and its span along each of its own two
    /// axes. Angles cannot see scale — the same quad stretched scores exactly the same — so the descent would
    /// otherwise wander along that freedom, and every press of Straighten would stretch the picture a little
    /// further and change its aspect. Keystone is what the lines determine; the metric stays with the measured
    /// lengths (see the class summary).
    /// </summary>
    private static void KeepGauge(Span<Vector2> quad, ReadOnlySpan<Vector2> start)
    {
        // The quad's own frame, so a pin that is rotated on the canvas is corrected along its edges rather
        // than along the canvas' axes.
        var u = (quad[1] - quad[0] + quad[2] - quad[3]) * 0.5f;
        var v = (quad[3] - quad[0] + quad[2] - quad[1]) * 0.5f;
        var determinant = u.X * v.Y - u.Y * v.X;
        if (MathF.Abs(determinant) < 1e-9f)
            return;

        var wanted = AxisSpans(start);
        var have = AxisSpans(quad);
        if (have.X < 1e-6f || have.Y < 1e-6f)
            return;

        var scale = new Vector2(wanted.X / have.X, wanted.Y / have.Y);
        var centre = Centre(quad);
        var target = Centre(start);
        for (var i = 0; i < 4; i++)
        {
            // Split the corner into its u and v shares, scale each, and put it back together.
            var offset = quad[i] - centre;
            var along = (offset.X * v.Y - offset.Y * v.X) / determinant * scale.X;
            var across = (u.X * offset.Y - u.Y * offset.X) / determinant * scale.Y;
            quad[i] = target + u * along + v * across;
        }
    }

    /// <summary>A quad's span along each of its axes: the mean of its two opposite edges, so a keystone reads
    /// as one number per axis.</summary>
    private static Vector2 AxisSpans(ReadOnlySpan<Vector2> quad)
    {
        return new Vector2(((quad[1] - quad[0]).Length() + (quad[2] - quad[3]).Length()) * 0.5f,
                           ((quad[3] - quad[0]).Length() + (quad[2] - quad[1]).Length()) * 0.5f);
    }

    private static Vector2 Centre(ReadOnlySpan<Vector2> quad)
    {
        return (quad[0] + quad[1] + quad[2] + quad[3]) * 0.25f;
    }

    /// <summary>
    /// Where a set of physically parallel lines meets. Averaged over every pair rather than taking the first
    /// two, so a shakily traced line doesn't decide the answer on its own. Homogeneous throughout: lines that
    /// are already parallel meet at infinity, which is the correct answer, not a failure.
    /// </summary>
    private static bool TryGetVanishingPoint(ReadOnlySpan<Vector4> lines, out Vector3 vanishing)
    {
        vanishing = Vector3.Zero;
        var reference = Vector3.Zero;
        var count = 0;

        for (var i = 0; i < lines.Length; i++)
        {
            var a = LineThrough(lines[i]);
            for (var j = i + 1; j < lines.Length; j++)
            {
                var intersection = Vector3.Cross(a, LineThrough(lines[j]));
                var length = intersection.Length();
                if (length < 1e-12f)
                    continue;

                intersection /= length;

                // Homogeneous points are sign-ambiguous; averaging without aligning them would cancel out.
                if (count == 0)
                    reference = intersection;
                else if (Vector3.Dot(intersection, reference) < 0)
                    intersection = -intersection;

                vanishing += intersection;
                count++;
            }
        }

        if (count == 0 || vanishing.Length() < 1e-9f)
            return false;

        vanishing /= count;
        return true;
    }

    private static Vector3 LineThrough(Vector4 segment)
    {
        return Vector3.Cross(new Vector3(segment.X, segment.Y, 1), new Vector3(segment.Z, segment.W, 1));
    }

    /// <summary>The direction a point at infinity carries once <paramref name="h"/> has put it there.</summary>
    private static Vector2 TransformToDirection(in Homography h, Vector3 p)
    {
        return new Vector2((float)(h.M11 * p.X + h.M12 * p.Y + h.M13 * p.Z),
                           (float)(h.M21 * p.X + h.M22 * p.Y + h.M23 * p.Z));
    }
}
