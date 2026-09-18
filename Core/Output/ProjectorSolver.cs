using System;
using System.Collections.Generic;
using System.Numerics;
using T3.Core.DataTypes.Vector;

namespace T3.Core.Output;

/// <summary>
/// Solves a projector's camera (Pose + Projection) from CalibrationPoints via normalized DLT.
/// Points must span more than one plane: from a single plane, standoff distance and field of
/// view cannot be separated (near-wide and far-tele look identical on that plane).
/// </summary>
public static class ProjectorSolver
{
    public readonly struct SolveResult
    {
        public readonly Pose Pose;
        public readonly Projection Lens;
        public readonly float MeanResidualPx;
        public readonly float MaxResidualPx;

        internal SolveResult(Pose pose, Projection lens, float meanResidualPx, float maxResidualPx)
        {
            Pose = pose;
            Lens = lens;
            MeanResidualPx = meanResidualPx;
            MaxResidualPx = maxResidualPx;
        }
    }

    public const int MinPointCount = 6;

    /// <summary>
    /// Projects a stage point to output-canvas pixels (top-left origin, y down) through a
    /// camera model. This is the forward model the solver inverts — keep them in sync.
    /// Returns false when the point is behind the camera.
    /// </summary>
    public static bool TryProjectToPixel(in Pose pose, in Projection lens, Int2 canvasResolution, Vector3 stagePosition, out Vector2 pixel)
    {
        pixel = Vector2.Zero;
        var aspect = canvasResolution.Width / (float)canvasResolution.Height;
        var viewProjection = pose.ToViewMatrix() * lens.GetMatrix(aspect);
        var clip = Vector4.Transform(new Vector4(stagePosition, 1), viewProjection);
        if (clip.W <= 1e-6f)
            return false;

        var ndcX = clip.X / clip.W;
        var ndcY = clip.Y / clip.W;
        pixel = new Vector2((ndcX * 0.5f + 0.5f) * canvasResolution.Width,
                            (0.5f - ndcY * 0.5f) * canvasResolution.Height);
        return true;
    }

    /// <summary>
    /// Estimates pose and lens from &gt;= 6 stage↔pixel correspondences spanning two planes.
    /// Fails (returns false) on too few points or a degenerate (coplanar) configuration.
    /// </summary>
    public static bool TrySolve(IReadOnlyList<CalibrationPoint> points, Int2 canvasResolution, out SolveResult result)
    {
        result = default;
        if (points == null || points.Count < MinPointCount)
            return false;

        // Hartley normalization of both point sets
        var centroid3 = Vector3.Zero;
        var centroid2 = Vector2.Zero;
        for (var i = 0; i < points.Count; i++)
        {
            centroid3 += points[i].StagePosition;
            centroid2 += points[i].OutputPixel;
        }

        centroid3 /= points.Count;
        centroid2 /= points.Count;

        double meanDistance3 = 0, meanDistance2 = 0;
        for (var i = 0; i < points.Count; i++)
        {
            meanDistance3 += (points[i].StagePosition - centroid3).Length();
            meanDistance2 += (points[i].OutputPixel - centroid2).Length();
        }

        meanDistance3 /= points.Count;
        meanDistance2 /= points.Count;
        if (meanDistance3 < 1e-9 || meanDistance2 < 1e-9)
            return false;

        var scale3 = Math.Sqrt(3) / meanDistance3;
        var scale2 = Math.Sqrt(2) / meanDistance2;

        // Accumulate AᵀA of the 2n x 12 DLT system directly (12x12, symmetric)
        var ata = new double[12 * 12];
        Span<double> row = stackalloc double[12];
        for (var i = 0; i < points.Count; i++)
        {
            var p3 = points[i].StagePosition;
            double x = (p3.X - centroid3.X) * scale3;
            double y = (p3.Y - centroid3.Y) * scale3;
            double z = (p3.Z - centroid3.Z) * scale3;
            double u = (points[i].OutputPixel.X - centroid2.X) * scale2;
            double v = (points[i].OutputPixel.Y - centroid2.Y) * scale2;

            row.Clear();
            row[0] = x; row[1] = y; row[2] = z; row[3] = 1;
            row[8] = -u * x; row[9] = -u * y; row[10] = -u * z; row[11] = -u;
            AccumulateOuterProduct(ata, row);

            row.Clear();
            row[4] = x; row[5] = y; row[6] = z; row[7] = 1;
            row[8] = -v * x; row[9] = -v * y; row[10] = -v * z; row[11] = -v;
            AccumulateOuterProduct(ata, row);
        }

        if (!TryFindSmallestEigenvector(ata, out var p))
            return false;

        // Denormalize: P = T2dInv * Pn * T3d
        var pn = new double[3, 4];
        for (var r = 0; r < 3; r++)
        {
            for (var c = 0; c < 4; c++)
                pn[r, c] = p[r * 4 + c];
        }

        var denormalized = DenormalizeProjectionMatrix(pn, centroid3, scale3, centroid2, scale2);
        if (!TryDecompose(denormalized, canvasResolution, out var pose, out var lens))
            return false;

        // Residuals against the forward model (not the raw P) so decomposition errors count too
        double residualSum = 0, residualMax = 0;
        for (var i = 0; i < points.Count; i++)
        {
            if (!TryProjectToPixel(pose, lens, canvasResolution, points[i].StagePosition, out var reprojected))
                return false;

            var distance = (reprojected - points[i].OutputPixel).Length();
            residualSum += distance;
            residualMax = Math.Max(residualMax, distance);
        }

        result = new SolveResult(pose, lens, (float)(residualSum / points.Count), (float)residualMax);
        return true;
    }

    private static void AccumulateOuterProduct(double[] ata, ReadOnlySpan<double> row)
    {
        for (var r = 0; r < 12; r++)
        {
            if (row[r] == 0)
                continue;

            for (var c = 0; c < 12; c++)
                ata[r * 12 + c] += row[r] * row[c];
        }
    }

    /// <summary>
    /// Jacobi eigenvalue iteration on the symmetric 12x12 AᵀA; the eigenvector of the
    /// smallest eigenvalue is the least-squares null vector of A. Avoids the p34=1
    /// inhomogeneous shortcut, which degenerates when the principal plane nears the origin.
    /// </summary>
    private static bool TryFindSmallestEigenvector(double[] m, out double[] eigenvector)
    {
        const int n = 12;
        var a = (double[])m.Clone();
        var v = new double[n * n];
        for (var i = 0; i < n; i++)
            v[i * n + i] = 1;

        for (var sweep = 0; sweep < 100; sweep++)
        {
            double offDiagonal = 0;
            for (var r = 0; r < n; r++)
            {
                for (var c = r + 1; c < n; c++)
                    offDiagonal += Math.Abs(a[r * n + c]);
            }

            if (offDiagonal < 1e-15)
                break;

            for (var r = 0; r < n; r++)
            {
                for (var c = r + 1; c < n; c++)
                {
                    var apq = a[r * n + c];
                    if (Math.Abs(apq) < 1e-20)
                        continue;

                    var app = a[r * n + r];
                    var aqq = a[c * n + c];
                    var theta = (aqq - app) / (2 * apq);
                    var t = Math.Sign(theta) / (Math.Abs(theta) + Math.Sqrt(theta * theta + 1));
                    if (theta == 0)
                        t = 1;

                    var cos = 1 / Math.Sqrt(t * t + 1);
                    var sin = t * cos;

                    for (var k = 0; k < n; k++)
                    {
                        var akp = a[k * n + r];
                        var akq = a[k * n + c];
                        a[k * n + r] = cos * akp - sin * akq;
                        a[k * n + c] = sin * akp + cos * akq;
                    }

                    for (var k = 0; k < n; k++)
                    {
                        var apk = a[r * n + k];
                        var aqk = a[c * n + k];
                        a[r * n + k] = cos * apk - sin * aqk;
                        a[c * n + k] = sin * apk + cos * aqk;
                    }

                    for (var k = 0; k < n; k++)
                    {
                        var vkp = v[k * n + r];
                        var vkq = v[k * n + c];
                        v[k * n + r] = cos * vkp - sin * vkq;
                        v[k * n + c] = sin * vkp + cos * vkq;
                    }
                }
            }
        }

        var smallestIndex = 0;
        for (var i = 1; i < n; i++)
        {
            if (a[i * n + i] < a[smallestIndex * n + smallestIndex])
                smallestIndex = i;
        }

        eigenvector = new double[n];
        double norm = 0;
        for (var i = 0; i < n; i++)
        {
            eigenvector[i] = v[i * n + smallestIndex];
            norm += eigenvector[i] * eigenvector[i];
        }

        if (norm < 1e-20)
            return false;

        norm = Math.Sqrt(norm);
        for (var i = 0; i < n; i++)
            eigenvector[i] /= norm;

        return true;
    }

    private static double[,] DenormalizeProjectionMatrix(double[,] pn, Vector3 centroid3, double scale3, Vector2 centroid2, double scale2)
    {
        // T3d maps stage coords into normalized space: x' = (x - cx) * s3
        var t3 = new double[4, 4];
        t3[0, 0] = scale3; t3[0, 3] = -centroid3.X * scale3;
        t3[1, 1] = scale3; t3[1, 3] = -centroid3.Y * scale3;
        t3[2, 2] = scale3; t3[2, 3] = -centroid3.Z * scale3;
        t3[3, 3] = 1;

        // T2dInv maps normalized pixels back: u = u'/s2 + cu
        var t2Inv = new double[3, 3];
        t2Inv[0, 0] = 1 / scale2; t2Inv[0, 2] = centroid2.X;
        t2Inv[1, 1] = 1 / scale2; t2Inv[1, 2] = centroid2.Y;
        t2Inv[2, 2] = 1;

        var tmp = new double[3, 4];
        for (var r = 0; r < 3; r++)
        {
            for (var c = 0; c < 4; c++)
            {
                double sum = 0;
                for (var k = 0; k < 4; k++)
                    sum += pn[r, k] * t3[k, c];
                tmp[r, c] = sum;
            }
        }

        var result = new double[3, 4];
        for (var r = 0; r < 3; r++)
        {
            for (var c = 0; c < 4; c++)
            {
                double sum = 0;
                for (var k = 0; k < 3; k++)
                    sum += t2Inv[r, k] * tmp[k, c];
                result[r, c] = sum;
            }
        }

        return result;
    }

    /// <summary>
    /// Decomposes P = K [R|t] (column-vector convention, pixel space with y down) into the
    /// forward model of <see cref="TryProjectToPixel"/>: RQ via Givens rotations, sign fixes
    /// so fx &gt; 0 and fy &lt; 0 (y-down pixels), then fov/lens-shift from K and the
    /// camera-to-world pose from R and the camera center.
    /// </summary>
    private static bool TryDecompose(double[,] p, Int2 canvasResolution, out Pose pose, out Projection lens)
    {
        pose = Pose.Identity;
        lens = default;

        // Camera center: right null vector of P, from  M * C = -p4
        var m = new double[3, 3];
        for (var r = 0; r < 3; r++)
        {
            for (var c = 0; c < 3; c++)
                m[r, c] = p[r, c];
        }

        if (!TrySolve3x3(m, new[] { -p[0, 3], -p[1, 3], -p[2, 3] }, out var center))
            return false;

        // RQ decomposition of M into upper-triangular K and rotation R
        var k = (double[,])m.Clone();
        var r3 = new double[3, 3];
        r3[0, 0] = 1; r3[1, 1] = 1; r3[2, 2] = 1;

        ApplyGivensFromRight(k, r3, 2, 1); // zero k[2,1]
        ApplyGivensFromRight(k, r3, 2, 0); // zero k[2,0]
        ApplyGivensFromRight(k, r3, 1, 0); // zero k[1,0]

        if (Math.Abs(k[2, 2]) < 1e-12)
            return false;

        // Normalize and fix signs: our convention needs fx > 0, fy < 0, k33 > 0
        var kScale = 1.0 / k[2, 2];
        for (var r = 0; r < 3; r++)
        {
            for (var c = 0; c < 3; c++)
                k[r, c] *= kScale;
        }

        FlipColumnIfNeeded(k, r3, 0, k[0, 0] < 0);
        FlipColumnIfNeeded(k, r3, 1, k[1, 1] > 0);

        // The pixel model uses z' = -z_camera, so the decomposed R̂ is diag(1,1,-1) times the
        // world→camera rotation. Undo the flip FIRST — R̂ itself has det -1 in the good case —
        // then use the DLT's global sign ambiguity to make the result a proper rotation.
        var mv = new double[3, 3];
        for (var r = 0; r < 3; r++)
        {
            for (var c = 0; c < 3; c++)
                mv[r, c] = r == 2 ? -r3[r, c] : r3[r, c];
        }

        var det = mv[0, 0] * (mv[1, 1] * mv[2, 2] - mv[1, 2] * mv[2, 1])
                  - mv[0, 1] * (mv[1, 0] * mv[2, 2] - mv[1, 2] * mv[2, 0])
                  + mv[0, 2] * (mv[1, 0] * mv[2, 1] - mv[1, 1] * mv[2, 0]);
        if (det < 0)
        {
            for (var r = 0; r < 3; r++)
            {
                for (var c = 0; c < 3; c++)
                    mv[r, c] = -mv[r, c];
            }
        }

        var fx = k[0, 0];
        var fy = k[1, 1];
        var cx = k[0, 2];
        var cy = k[1, 2];

        var width = (double)canvasResolution.Width;
        var height = (double)canvasResolution.Height;

        var fovY = 2 * Math.Atan(height / (2 * Math.Abs(fy)));
        var shiftX = 2 * cx / width - 1;
        var shiftY = 1 - 2 * cy / height;

        // mv is the world→camera rotation in column convention; as a row-vector (numerics)
        // matrix the same numbers read as the camera→world map — exactly Pose's orientation.
        var cameraToWorld = new Matrix4x4
                                {
                                    M11 = (float)mv[0, 0], M12 = (float)mv[0, 1], M13 = (float)mv[0, 2],
                                    M21 = (float)mv[1, 0], M22 = (float)mv[1, 1], M23 = (float)mv[1, 2],
                                    M31 = (float)mv[2, 0], M32 = (float)mv[2, 1], M33 = (float)mv[2, 2],
                                    M44 = 1,
                                };
        var orientation = Quaternion.Normalize(Quaternion.CreateFromRotationMatrix(cameraToWorld));

        pose = new Pose(new Vector3((float)center[0], (float)center[1], (float)center[2]), orientation);
        lens = Projection.CreatePerspective((float)fovY, new Vector2((float)shiftX, (float)shiftY));

        // Square pixels imply fx == |fy|; strong anisotropy or skew means the correspondences
        // were bad or coplanar — report failure, not garbage.
        var anisotropy = Math.Abs(fx - Math.Abs(fy)) / Math.Max(Math.Abs(fy), 1e-9);
        return anisotropy < 0.1;
    }

    private static void ApplyGivensFromRight(double[,] k, double[,] r, int row, int column)
    {
        // Zeroes k[row, column] by rotating columns (column, row); accumulates the rotation in r
        var a = k[row, row];
        var b = k[row, column];
        var radius = Math.Sqrt(a * a + b * b);
        if (radius < 1e-15)
            return;

        var cos = a / radius;
        var sin = b / radius;

        for (var i = 0; i < 3; i++)
        {
            var ki = k[i, column];
            var kj = k[i, row];
            k[i, column] = cos * ki - sin * kj;
            k[i, row] = sin * ki + cos * kj;
        }

        for (var i = 0; i < 3; i++)
        {
            var ri = r[column, i];
            var rj = r[row, i];
            r[column, i] = cos * ri - sin * rj;
            r[row, i] = sin * ri + cos * rj;
        }
    }

    private static void FlipColumnIfNeeded(double[,] k, double[,] r, int index, bool flip)
    {
        if (!flip)
            return;

        for (var i = 0; i < 3; i++)
            k[i, index] = -k[i, index];

        for (var i = 0; i < 3; i++)
            r[index, i] = -r[index, i];
    }

    private static bool TrySolve3x3(double[,] m, double[] b, out double[] x)
    {
        x = new double[3];
        var a = new double[3, 4];
        for (var r = 0; r < 3; r++)
        {
            for (var c = 0; c < 3; c++)
                a[r, c] = m[r, c];
            a[r, 3] = b[r];
        }

        for (var c = 0; c < 3; c++)
        {
            var pivot = c;
            for (var r = c + 1; r < 3; r++)
            {
                if (Math.Abs(a[r, c]) > Math.Abs(a[pivot, c]))
                    pivot = r;
            }

            if (Math.Abs(a[pivot, c]) < 1e-12)
                return false;

            if (pivot != c)
            {
                for (var k = 0; k < 4; k++)
                    (a[c, k], a[pivot, k]) = (a[pivot, k], a[c, k]);
            }

            for (var r = 0; r < 3; r++)
            {
                if (r == c)
                    continue;

                var f = a[r, c] / a[c, c];
                for (var k = c; k < 4; k++)
                    a[r, k] -= f * a[c, k];
            }
        }

        for (var i = 0; i < 3; i++)
            x[i] = a[i, 3] / a[i, i];

        return true;
    }
}
