using System;
using System.Numerics;

namespace T3.Core.Output;

/// <summary>
/// 3x3 projective 2D transform (row-major, double precision), used for corner-pin output
/// mappings and reference-image straightening. Ported from the validated projection-studio
/// prototype: Hartley-normalized 4-point solve with a w-sign fix so quads that cross the
/// horizon of the projective map don't flip.
/// </summary>
public struct Homography
{
    public double M11, M12, M13;
    public double M21, M22, M23;
    public double M31, M32, M33;

    public static readonly Homography Identity = new() { M11 = 1, M22 = 1, M33 = 1 };

    /// <summary>
    /// Computes the homography mapping the four source points onto the four destination
    /// points (same winding). Returns false for degenerate quads (three collinear points).
    /// </summary>
    public static bool TryComputeQuadToQuad(ReadOnlySpan<Vector2> source, ReadOnlySpan<Vector2> destination, out Homography result)
    {
        result = Identity;
        if (source.Length != 4 || destination.Length != 4)
            return false;

        var ns = NormalizeTransform(source);
        var nd = NormalizeTransform(destination);

        // 8x8 linear system on the normalized points, solved with Gauss-Jordan + partial pivoting
        Span<double> a = stackalloc double[8 * 8];
        Span<double> b = stackalloc double[8];
        for (var i = 0; i < 4; i++)
        {
            var s = ns.Forward.TransformPoint(source[i]);
            var d = nd.Forward.TransformPoint(destination[i]);
            double x = s.X, y = s.Y, u = d.X, v = d.Y;

            var r0 = i * 2 * 8;
            a[r0 + 0] = x; a[r0 + 1] = y; a[r0 + 2] = 1;
            a[r0 + 6] = -u * x; a[r0 + 7] = -u * y;
            b[i * 2] = u;

            var r1 = (i * 2 + 1) * 8;
            a[r1 + 3] = x; a[r1 + 4] = y; a[r1 + 5] = 1;
            a[r1 + 6] = -v * x; a[r1 + 7] = -v * y;
            b[i * 2 + 1] = v;
        }

        if (!TrySolve8(a, b, out var hn))
            return false;

        result = Finish(hn, ns.Forward, nd.Inverse, source);
        return true;
    }

    /// <summary>
    /// The least-squares homography through any number (≥4) of correspondences: the same inhomogeneous
    /// 8-parameter system as the exact solve, but through its normal equations, so extra pairs are averaged
    /// rather than refused. Exact for four. Returns false for fewer, or for a degenerate configuration.
    /// </summary>
    public static bool TryComputeLeastSquares(ReadOnlySpan<Vector2> source, ReadOnlySpan<Vector2> destination, out Homography result)
    {
        result = Identity;
        if (source.Length < 4 || source.Length != destination.Length)
            return false;

        var ns = NormalizeTransform(source);
        var nd = NormalizeTransform(destination);

        // Normal equations AᵀA h = Aᵀb, accumulated row by row so no N×8 matrix is ever built.
        Span<double> ata = stackalloc double[8 * 8];
        Span<double> atb = stackalloc double[8];
        Span<double> row = stackalloc double[8];
        for (var i = 0; i < source.Length; i++)
        {
            var sp = ns.Forward.TransformPoint(source[i]);
            var dp = nd.Forward.TransformPoint(destination[i]);
            double x = sp.X, y = sp.Y, u = dp.X, v = dp.Y;

            row.Clear();
            row[0] = x; row[1] = y; row[2] = 1; row[6] = -u * x; row[7] = -u * y;
            Accumulate(ata, atb, row, u);

            row.Clear();
            row[3] = x; row[4] = y; row[5] = 1; row[6] = -v * x; row[7] = -v * y;
            Accumulate(ata, atb, row, v);
        }

        if (!TrySolve8(ata, atb, out var hn))
            return false;

        result = Finish(hn, ns.Forward, nd.Inverse, source);
        return true;
    }

    private static void Accumulate(Span<double> ata, Span<double> atb, ReadOnlySpan<double> row, double rhs)
    {
        for (var r = 0; r < 8; r++)
        {
            if (row[r] == 0)
                continue;

            for (var c = 0; c < 8; c++)
                ata[r * 8 + c] += row[r] * row[c];

            atb[r] += row[r] * rhs;
        }
    }

    /// <summary>Gauss-Jordan with partial pivoting on an 8×8 system; the solution is the normalized map.</summary>
    private static bool TrySolve8(Span<double> a, Span<double> b, out Homography hn)
    {
        hn = Identity;
        for (var c = 0; c < 8; c++)
        {
            var pivot = c;
            for (var r = c + 1; r < 8; r++)
            {
                if (Math.Abs(a[r * 8 + c]) > Math.Abs(a[pivot * 8 + c]))
                    pivot = r;
            }

            if (Math.Abs(a[pivot * 8 + c]) < 1e-10)
                return false;

            if (pivot != c)
            {
                for (var k = 0; k < 8; k++)
                    (a[c * 8 + k], a[pivot * 8 + k]) = (a[pivot * 8 + k], a[c * 8 + k]);
                (b[c], b[pivot]) = (b[pivot], b[c]);
            }

            for (var r = 0; r < 8; r++)
            {
                if (r == c)
                    continue;

                var f = a[r * 8 + c] / a[c * 8 + c];
                for (var k = c; k < 8; k++)
                    a[r * 8 + k] -= f * a[c * 8 + k];

                b[r] -= f * b[c];
            }
        }

        hn = new Homography
                 {
                     M11 = b[0] / a[0 * 8 + 0], M12 = b[1] / a[1 * 8 + 1], M13 = b[2] / a[2 * 8 + 2],
                     M21 = b[3] / a[3 * 8 + 3], M22 = b[4] / a[4 * 8 + 4], M23 = b[5] / a[5 * 8 + 5],
                     M31 = b[6] / a[6 * 8 + 6], M32 = b[7] / a[7 * 8 + 7], M33 = 1,
                 };
        return true;
    }

    /// <summary>De-normalizes, fixes the w sign so the sources stay on the positive side, and scales M33 to 1.</summary>
    private static Homography Finish(in Homography normalized, in Homography sourceForward, in Homography destinationInverse, ReadOnlySpan<Vector2> source)
    {
        var h = Multiply(destinationInverse, Multiply(normalized, sourceForward));

        double wSum = 0;
        foreach (var p in source)
            wSum += h.M31 * p.X + h.M32 * p.Y + h.M33;

        if (wSum < 0)
            h = h.Scaled(-1);

        if (Math.Abs(h.M33) > 1e-9)
            h = h.Scaled(1.0 / Math.Abs(h.M33));

        return h;
    }

    public static Homography Multiply(in Homography a, in Homography b)
    {
        return new Homography
                   {
                       M11 = a.M11 * b.M11 + a.M12 * b.M21 + a.M13 * b.M31,
                       M12 = a.M11 * b.M12 + a.M12 * b.M22 + a.M13 * b.M32,
                       M13 = a.M11 * b.M13 + a.M12 * b.M23 + a.M13 * b.M33,
                       M21 = a.M21 * b.M11 + a.M22 * b.M21 + a.M23 * b.M31,
                       M22 = a.M21 * b.M12 + a.M22 * b.M22 + a.M23 * b.M32,
                       M23 = a.M21 * b.M13 + a.M22 * b.M23 + a.M23 * b.M33,
                       M31 = a.M31 * b.M11 + a.M32 * b.M21 + a.M33 * b.M31,
                       M32 = a.M31 * b.M12 + a.M32 * b.M22 + a.M33 * b.M32,
                       M33 = a.M31 * b.M13 + a.M32 * b.M23 + a.M33 * b.M33,
                   };
    }

    public readonly bool TryInvert(out Homography inverse)
    {
        var cofactor11 = M22 * M33 - M23 * M32;
        var cofactor12 = M13 * M32 - M12 * M33;
        var cofactor13 = M12 * M23 - M13 * M22;
        var cofactor21 = M23 * M31 - M21 * M33;
        var cofactor22 = M11 * M33 - M13 * M31;
        var cofactor23 = M13 * M21 - M11 * M23;
        var cofactor31 = M21 * M32 - M22 * M31;
        var cofactor32 = M12 * M31 - M11 * M32;
        var cofactor33 = M11 * M22 - M12 * M21;

        var det = M11 * cofactor11 + M12 * cofactor21 + M13 * cofactor31;
        if (Math.Abs(det) < 1e-12)
        {
            inverse = Identity;
            return false;
        }

        var invDet = 1.0 / det;
        inverse = new Homography
                      {
                          M11 = cofactor11 * invDet, M12 = cofactor12 * invDet, M13 = cofactor13 * invDet,
                          M21 = cofactor21 * invDet, M22 = cofactor22 * invDet, M23 = cofactor23 * invDet,
                          M31 = cofactor31 * invDet, M32 = cofactor32 * invDet, M33 = cofactor33 * invDet,
                      };
        return true;
    }

    public readonly Vector2 TransformPoint(Vector2 p)
    {
        var w = M31 * p.X + M32 * p.Y + M33;
        return new Vector2((float)((M11 * p.X + M12 * p.Y + M13) / w),
                           (float)((M21 * p.X + M22 * p.Y + M23) / w));
    }

    /// <summary>
    /// Embeds the projective 2D map into a 4x4 acting on (x, y, w) with z passing through,
    /// for GPU use (row-vector convention). Callers must clip against w &lt;= 0 before
    /// projecting — near-horizon points blow up otherwise.
    /// </summary>
    public readonly Matrix4x4 ToMatrix4x4()
    {
        return new Matrix4x4
                   {
                       M11 = (float)M11, M21 = (float)M12, M41 = (float)M13,
                       M12 = (float)M21, M22 = (float)M22, M42 = (float)M23,
                       M33 = 1f,
                       M14 = (float)M31, M24 = (float)M32, M44 = (float)M33,
                   };
    }

    private readonly Homography Scaled(double factor)
    {
        return new Homography
                   {
                       M11 = M11 * factor, M12 = M12 * factor, M13 = M13 * factor,
                       M21 = M21 * factor, M22 = M22 * factor, M23 = M23 * factor,
                       M31 = M31 * factor, M32 = M32 * factor, M33 = M33 * factor,
                   };
    }

    /// <summary>
    /// Hartley normalization: translate the centroid to the origin and scale the mean
    /// distance to sqrt(2). Keeps the 8x8 solve well-conditioned for pixel-space inputs.
    /// </summary>
    private static (Homography Forward, Homography Inverse) NormalizeTransform(ReadOnlySpan<Vector2> points)
    {
        double cx = 0, cy = 0;
        foreach (var p in points)
        {
            cx += p.X;
            cy += p.Y;
        }

        cx /= points.Length;
        cy /= points.Length;

        double meanDistance = 0;
        foreach (var p in points)
            meanDistance += Math.Sqrt((p.X - cx) * (p.X - cx) + (p.Y - cy) * (p.Y - cy));

        meanDistance /= points.Length;
        var s = meanDistance > 1e-9 ? Math.Sqrt(2) / meanDistance : 1.0;

        var forward = new Homography { M11 = s, M13 = -s * cx, M22 = s, M23 = -s * cy, M33 = 1 };
        var inverse = new Homography { M11 = 1 / s, M13 = cx, M22 = 1 / s, M23 = cy, M33 = 1 };
        return (forward, inverse);
    }
}
