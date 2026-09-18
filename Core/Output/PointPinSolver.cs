using System;
using System.Numerics;

namespace T3.Core.Output;

/// <summary>
/// Re-solves a corner pin so that reference points land where they were aimed. Each correspondence pairs
/// a point's current projection with the pixel it should project to; the pin's four corners are carried
/// through the transform that satisfies them. The transform is exactly as free as the points allow, so a
/// placed point is always hit precisely: one point shifts, two turn and scale, three shear, four and more
/// keystone (least squares beyond four — see the residual).
/// </summary>
public static class PointPinSolver
{
    /// <param name="from">Where each point projects to now, in output pixels.</param>
    /// <param name="to">Where each point should project to.</param>
    /// <param name="quad">The current corners; overwritten with the solved ones on success.</param>
    /// <param name="residualPx">The largest miss among the points after the solve (0 for up to four points).</param>
    public static bool TrySolve(ReadOnlySpan<Vector2> from, ReadOnlySpan<Vector2> to, Span<Vector2> quad, out float residualPx)
    {
        residualPx = 0;
        if (from.Length != to.Length || from.Length == 0 || quad.Length < 4)
            return false;

        Homography transform;
        switch (from.Length)
        {
            case 1:
                transform = Translation(to[0] - from[0]);
                break;
            case 2:
                if (!TrySimilarity(from, to, out transform))
                    return false;

                break;
            case 3:
                if (!TryAffine(from, to, out transform))
                    return false;

                break;
            default:
                if (!Homography.TryComputeLeastSquares(from, to, out transform))
                    return false;

                for (var i = 0; i < from.Length; i++)
                    residualPx = MathF.Max(residualPx, (transform.TransformPoint(from[i]) - to[i]).Length());

                break;
        }

        Span<Vector2> solved = stackalloc Vector2[4];
        for (var c = 0; c < 4; c++)
        {
            solved[c] = transform.TransformPoint(quad[c]);
            if (!float.IsFinite(solved[c].X) || !float.IsFinite(solved[c].Y))
                return false;
        }

        solved.CopyTo(quad);
        return true;
    }

    private static Homography Translation(Vector2 delta)
    {
        return new Homography { M11 = 1, M13 = delta.X, M22 = 1, M23 = delta.Y, M33 = 1 };
    }

    /// <summary>Rotation, uniform scale and translation through two pairs — a complex-number fit z' = a·z + b.</summary>
    private static bool TrySimilarity(ReadOnlySpan<Vector2> from, ReadOnlySpan<Vector2> to, out Homography transform)
    {
        transform = Homography.Identity;
        var d = from[1] - from[0];
        var lengthSquared = (double)d.X * d.X + (double)d.Y * d.Y;
        if (lengthSquared < 1e-9)
            return false;

        var e = to[1] - to[0];
        var aRe = (e.X * (double)d.X + e.Y * (double)d.Y) / lengthSquared;
        var aIm = (e.Y * (double)d.X - e.X * (double)d.Y) / lengthSquared;
        var bx = to[0].X - (aRe * from[0].X - aIm * from[0].Y);
        var by = to[0].Y - (aIm * from[0].X + aRe * from[0].Y);
        transform = new Homography { M11 = aRe, M12 = -aIm, M13 = bx, M21 = aIm, M22 = aRe, M23 = by, M33 = 1 };
        return true;
    }

    /// <summary>The affine map through three pairs: two 3×3 systems sharing the source matrix.</summary>
    private static bool TryAffine(ReadOnlySpan<Vector2> from, ReadOnlySpan<Vector2> to, out Homography transform)
    {
        transform = Homography.Identity;
        double x0 = from[0].X, y0 = from[0].Y, x1 = from[1].X, y1 = from[1].Y, x2 = from[2].X, y2 = from[2].Y;
        var det = x0 * (y1 - y2) - y0 * (x1 - x2) + (x1 * y2 - x2 * y1);
        if (Math.Abs(det) < 1e-9)
            return false;

        // Cramer's rule on [x y 1] rows for each target coordinate.
        Solve3(x0, y0, x1, y1, x2, y2, det, to[0].X, to[1].X, to[2].X, out var m11, out var m12, out var m13);
        Solve3(x0, y0, x1, y1, x2, y2, det, to[0].Y, to[1].Y, to[2].Y, out var m21, out var m22, out var m23);
        transform = new Homography { M11 = m11, M12 = m12, M13 = m13, M21 = m21, M22 = m22, M23 = m23, M33 = 1 };
        return true;
    }

    private static void Solve3(double x0, double y0, double x1, double y1, double x2, double y2, double det,
                               double r0, double r1, double r2, out double a, out double b, out double c)
    {
        a = (r0 * (y1 - y2) - y0 * (r1 - r2) + (r1 * y2 - r2 * y1)) / det;
        b = (x0 * (r1 - r2) - r0 * (x1 - x2) + (x1 * r2 - x2 * r1)) / det;
        c = (x0 * (y1 * r2 - y2 * r1) - y0 * (x1 * r2 - x2 * r1) + r0 * (x1 * y2 - x2 * y1)) / det;
    }
}
