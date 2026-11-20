using System;
using System.Collections.Generic;
using System.Numerics;

namespace T3.Core.Utils.Splines;

/// <summary>Turns cubic bezier segments into polylines that stay within a chord tolerance.</summary>
public static class BezierFlattening
{
    /// <summary>
    /// Appends points approximating the segment p0..p3 after p0 (which the caller adds): straight segments add only
    /// the end, curved ones are subdivided until the chord deviates less than <paramref name="tolerance"/>.
    /// </summary>
    /// <param name="includeEnd">False for the closing segment of a loop, whose end is the first point again.</param>
    public static void FlattenSegment(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float tolerance, List<Vector3> target, bool includeEnd)
    {
        // Straight when both handles lie on the chord within tolerance - the common case for polylines and glyph corners
        var chord = p3 - p0;
        var chordLengthSq = chord.LengthSquared();
        var flat = chordLengthSq < 1e-12f
                   || (DistanceToLineSq(p1, p0, chord, chordLengthSq) <= tolerance * tolerance
                       && DistanceToLineSq(p2, p0, chord, chordLengthSq) <= tolerance * tolerance);
        if (flat)
        {
            if (includeEnd)
                target.Add(p3);

            return;
        }

        // Subdivision count from the control polygon's deviation (Wang's bound)
        var deviation = MathF.Max((p0 - 2 * p1 + p2).Length(), (p1 - 2 * p2 + p3).Length());
        var steps = Math.Clamp((int)MathF.Ceiling(MathF.Sqrt(0.75f * deviation / MathF.Max(tolerance, 1e-6f))), 1, 64);
        var lastStep = includeEnd ? steps : steps - 1;
        for (var i = 1; i <= lastStep; i++)
        {
            target.Add(Bezier.GetPoint(p0, p1, p2, p3, i / (float)steps));
        }
    }

    private static float DistanceToLineSq(Vector3 p, Vector3 origin, Vector3 direction, float directionLengthSq)
    {
        var d = p - origin;
        var t = Vector3.Dot(d, direction) / directionLengthSq;
        return (d - direction * t).LengthSquared();
    }
}
