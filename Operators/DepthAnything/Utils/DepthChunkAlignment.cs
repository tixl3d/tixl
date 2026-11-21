#nullable enable
using System;
using System.Collections.Generic;
using System.Numerics;

namespace Lib.io.video.depthanything.Utils;

/// <summary>A similarity transform: p' = Scale * Rotation * p + Translation.</summary>
internal readonly struct Sim3
{
    public Sim3(float scale, Quaternion rotation, Vector3 translation)
    {
        Scale = scale;
        Rotation = rotation;
        Translation = translation;
    }

    public readonly float Scale;
    public readonly Quaternion Rotation;
    public readonly Vector3 Translation;

    public static Sim3 Identity => new(1f, Quaternion.Identity, Vector3.Zero);

    public Vector3 Apply(Vector3 p) => Scale * Vector3.Transform(p, Rotation) + Translation;

    /** this(other(p)): other is applied first. */
    public Sim3 Compose(in Sim3 other) =>
        new(Scale * other.Scale, Quaternion.Normalize(Rotation * other.Rotation), Apply(other.Translation));
}

/// <summary>Weighted, trimmed least-squares similarity fit between corresponding points (Horn's quaternion method).</summary>
internal static class Sim3Fit
{
    private const int TrimIterations = 4;
    private const float KeepFraction = 0.7f;

    public static bool Fit(IReadOnlyList<Vector3> src, IReadOnlyList<Vector3> dst, IReadOnlyList<float> weight, out Sim3 result)
    {
        result = Sim3.Identity;
        var count = src.Count;
        if (count < 12)
            return false;

        var active = new float[count];
        for (var i = 0; i < count; i++)
            active[i] = weight[i];

        var residual = new float[count];
        for (var iteration = 0; iteration < TrimIterations; iteration++)
        {
            if (!FitOnce(src, dst, active, out result))
                return false;

            for (var i = 0; i < count; i++)
                residual[i] = Vector3.Distance(dst[i], result.Apply(src[i]));

            var sorted = (float[])residual.Clone();
            Array.Sort(sorted);
            var limit = sorted[(int)((count - 1) * KeepFraction)];
            for (var i = 0; i < count; i++)
                active[i] = residual[i] <= limit ? weight[i] : 0f;
        }

        return true;
    }

    /** Scale and translation only, for a rotation known from the camera poses; the points then cannot bend it. */
    public static bool FitWithRotation(IReadOnlyList<Vector3> src, IReadOnlyList<Vector3> dst, IReadOnlyList<float> weight, Quaternion rotation, out Sim3 result)
    {
        result = Sim3.Identity;
        var count = src.Count;
        if (count < 12)
            return false;

        var active = new float[count];
        for (var i = 0; i < count; i++)
            active[i] = weight[i];

        var residual = new float[count];
        for (var iteration = 0; iteration < TrimIterations; iteration++)
        {
            double sumW = 0, ax = 0, ay = 0, az = 0, bx = 0, by = 0, bz = 0;
            for (var i = 0; i < count; i++)
            {
                var rotated = Vector3.Transform(src[i], rotation);
                var wi = (double)active[i];
                sumW += wi;
                ax += wi * rotated.X; ay += wi * rotated.Y; az += wi * rotated.Z;
                bx += wi * dst[i].X; by += wi * dst[i].Y; bz += wi * dst[i].Z;
            }

            if (sumW < 1e-9)
                return false;

            ax /= sumW; ay /= sumW; az /= sumW; bx /= sumW; by /= sumW; bz /= sumW;
            double dot = 0, var = 0;
            for (var i = 0; i < count; i++)
            {
                var rotated = Vector3.Transform(src[i], rotation);
                var px = rotated.X - ax; var py = rotated.Y - ay; var pz = rotated.Z - az;
                dot += active[i] * (px * (dst[i].X - bx) + py * (dst[i].Y - by) + pz * (dst[i].Z - bz));
                var += active[i] * (px * px + py * py + pz * pz);
            }

            var scale = (float)(dot / var);
            if (!(scale > 1e-4f) || !float.IsFinite(scale))
                return false;

            result = new Sim3(scale, rotation, new Vector3((float)(bx - scale * ax), (float)(by - scale * ay), (float)(bz - scale * az)));
            for (var i = 0; i < count; i++)
                residual[i] = Vector3.Distance(dst[i], result.Apply(src[i]));

            var sorted = (float[])residual.Clone();
            Array.Sort(sorted);
            var limit = sorted[(int)((count - 1) * KeepFraction)];
            for (var i = 0; i < count; i++)
                active[i] = residual[i] <= limit ? weight[i] : 0f;
        }

        return true;
    }

    private static bool FitOnce(IReadOnlyList<Vector3> src, IReadOnlyList<Vector3> dst, float[] w, out Sim3 result)
    {
        result = Sim3.Identity;
        double sumW = 0;
        double ax = 0, ay = 0, az = 0, bx = 0, by = 0, bz = 0;
        for (var i = 0; i < src.Count; i++)
        {
            var wi = (double)w[i];
            sumW += wi;
            ax += wi * src[i].X; ay += wi * src[i].Y; az += wi * src[i].Z;
            bx += wi * dst[i].X; by += wi * dst[i].Y; bz += wi * dst[i].Z;
        }

        if (sumW < 1e-9)
            return false;

        ax /= sumW; ay /= sumW; az /= sumW; bx /= sumW; by /= sumW; bz /= sumW;

        double sxx = 0, sxy = 0, sxz = 0, syx = 0, syy = 0, syz = 0, szx = 0, szy = 0, szz = 0, varSrc = 0;
        for (var i = 0; i < src.Count; i++)
        {
            var wi = (double)w[i];
            var px = src[i].X - ax; var py = src[i].Y - ay; var pz = src[i].Z - az;
            var qx = dst[i].X - bx; var qy = dst[i].Y - by; var qz = dst[i].Z - bz;
            sxx += wi * px * qx; sxy += wi * px * qy; sxz += wi * px * qz;
            syx += wi * py * qx; syy += wi * py * qy; syz += wi * py * qz;
            szx += wi * pz * qx; szy += wi * pz * qy; szz += wi * pz * qz;
            varSrc += wi * (px * px + py * py + pz * pz);
        }

        if (varSrc < 1e-12)
            return false;

        var n = new double[4, 4]
        {
            { sxx + syy + szz, syz - szy, szx - sxz, sxy - syx },
            { syz - szy, sxx - syy - szz, sxy + syx, szx + sxz },
            { szx - sxz, sxy + syx, -sxx + syy - szz, syz + szy },
            { sxy - syx, szx + sxz, syz + szy, -sxx - syy + szz },
        };

        var q = LargestEigenvector(n);
        var rotation = Quaternion.Normalize(new Quaternion((float)q[1], (float)q[2], (float)q[3], (float)q[0]));

        // Umeyama scale: how much the rotated source must stretch to meet the destination
        double dot = 0;
        for (var i = 0; i < src.Count; i++)
        {
            var rotated = Vector3.Transform(new Vector3((float)(src[i].X - ax), (float)(src[i].Y - ay), (float)(src[i].Z - az)), rotation);
            dot += w[i] * (rotated.X * (dst[i].X - bx) + rotated.Y * (dst[i].Y - by) + rotated.Z * (dst[i].Z - bz));
        }

        var scale = (float)(dot / varSrc);
        if (!(scale > 1e-4f) || !float.IsFinite(scale))
            return false;

        var centroidSrc = Vector3.Transform(new Vector3((float)ax, (float)ay, (float)az), rotation) * scale;
        result = new Sim3(scale, rotation, new Vector3((float)bx, (float)by, (float)bz) - centroidSrc);
        return true;
    }

    /** Jacobi eigen decomposition of a symmetric 4x4 matrix; returns the eigenvector of the largest eigenvalue. */
    private static double[] LargestEigenvector(double[,] a)
    {
        var v = new double[4, 4];
        for (var i = 0; i < 4; i++)
            v[i, i] = 1;

        for (var sweep = 0; sweep < 50; sweep++)
        {
            double off = 0;
            for (var p = 0; p < 3; p++)
            {
                for (var q = p + 1; q < 4; q++)
                    off += a[p, q] * a[p, q];
            }

            if (off < 1e-24)
                break;

            for (var p = 0; p < 3; p++)
            {
                for (var q = p + 1; q < 4; q++)
                {
                    if (Math.Abs(a[p, q]) < 1e-30)
                        continue;

                    var theta = (a[q, q] - a[p, p]) / (2 * a[p, q]);
                    var t = Math.Sign(theta == 0 ? 1 : theta) / (Math.Abs(theta) + Math.Sqrt(theta * theta + 1));
                    var c = 1 / Math.Sqrt(t * t + 1);
                    var s = t * c;
                    for (var k = 0; k < 4; k++)
                    {
                        var akp = a[k, p];
                        var akq = a[k, q];
                        a[k, p] = c * akp - s * akq;
                        a[k, q] = s * akp + c * akq;
                    }

                    for (var k = 0; k < 4; k++)
                    {
                        var apk = a[p, k];
                        var aqk = a[q, k];
                        a[p, k] = c * apk - s * aqk;
                        a[q, k] = s * apk + c * aqk;
                    }

                    for (var k = 0; k < 4; k++)
                    {
                        var vkp = v[k, p];
                        var vkq = v[k, q];
                        v[k, p] = c * vkp - s * vkq;
                        v[k, q] = s * vkp + c * vkq;
                    }
                }
            }
        }

        var best = 0;
        for (var i = 1; i < 4; i++)
        {
            if (a[i, i] > a[best, best])
                best = i;
        }

        return [v[0, best], v[1, best], v[2, best], v[3, best]];
    }
}

/// <summary>One window of frames inferred together: depth, confidence, w2c extrinsics and intrinsics, all in the window's own frame.</summary>
internal sealed class DepthChunk
{
    public DepthChunk(int views, int size, float[] depth, float[] confidence, float[] extrinsics, float[] intrinsics)
    {
        Views = views;
        Size = size;
        Depth = depth;
        Confidence = confidence;
        Extrinsics = extrinsics;
        Intrinsics = intrinsics;
    }

    public readonly int Views;
    public readonly int Size;
    public readonly float[] Depth;        // [views, size, size]
    public readonly float[] Confidence;   // [views, size, size]
    public readonly float[] Extrinsics;   // [views, 3, 4] world-to-camera, view 0 is the reference
    public readonly float[] Intrinsics;   // [views, 3, 3] in pixels of the window's input size

    /** Camera-to-world rotation (row-major 3x3 as a Matrix4x4's upper part) and position of a view in the window frame. */
    public void CameraToWorld(int view, out Matrix4x4 rotation, out Vector3 position)
    {
        var e = view * 12;
        // w2c is [R|t] and a camera point maps to the world as R^T (x - t). With Vector3.Transform's row-vector
        // convention, v * R is exactly R^T v, so R itself is the matrix to hand over.
        var r = new Matrix4x4(Extrinsics[e + 0], Extrinsics[e + 1], Extrinsics[e + 2], 0,
                              Extrinsics[e + 4], Extrinsics[e + 5], Extrinsics[e + 6], 0,
                              Extrinsics[e + 8], Extrinsics[e + 9], Extrinsics[e + 10], 0,
                              0, 0, 0, 1);
        rotation = r;
        var t = new Vector3(Extrinsics[e + 3], Extrinsics[e + 7], Extrinsics[e + 11]);
        position = -Vector3.Transform(t, r);
    }

    /** Window-frame points on a pixel grid: the same (u, v) samples for every view, so views of one frame in two windows pair up by index. */
    public void SamplePoints(int view, int stride, float minConfidence, List<Vector3> points, List<float> weights)
    {
        CameraToWorld(view, out var rotation, out var position);
        var k = view * 9;
        var fx = Intrinsics[k + 0];
        var fy = Intrinsics[k + 4];
        var cx = Intrinsics[k + 2];
        var cy = Intrinsics[k + 5];
        var plane = view * Size * Size;
        for (var v = stride / 2; v < Size; v += stride)
        {
            for (var u = stride / 2; u < Size; u += stride)
            {
                var d = Depth[plane + v * Size + u];
                var c = Confidence[plane + v * Size + u];
                if (!(d > 0f) || !float.IsFinite(d) || c < minConfidence)
                {
                    points.Add(default);
                    weights.Add(0f);
                    continue;
                }

                var camera = new Vector3((u - cx) / fx * d, (v - cy) / fy * d, d);
                points.Add(Vector3.Transform(camera, rotation) + position);
                weights.Add(c);
            }
        }
    }
}

/// <summary>A frame placed in the common world frame: its camera pose and the factor that scales its window depth into world units.</summary>
internal readonly struct PlacedFrame
{
    public PlacedFrame(int chunk, int view, Matrix4x4 rotation, Vector3 position, float depthScale)
    {
        Chunk = chunk;
        View = view;
        Rotation = rotation;
        Position = position;
        DepthScale = depthScale;
    }

    public readonly int Chunk;
    public readonly int View;
    public readonly Matrix4x4 Rotation;   // camera-to-world, row-vector convention (Vector3.Transform(cameraPoint, Rotation))
    public readonly Vector3 Position;
    public readonly float DepthScale;
}

/// <summary>
/// Chains overlapping windows into one world frame the way DA3-Streaming does: the frames two windows share are
/// unprojected in both, and a similarity transform fitted on those point pairs carries the new window onto the one before.
/// Rotation comes from the shared frames' camera poses (point clouds barely constrain it for a rotating camera); scale and
/// translation come from the points.
/// <para>New windows are always placed on the last <i>key</i> window, the one that advanced the frame ring, never on the
/// previous window: while the camera holds still the same ring is inferred again and again, and re-chaining each of those
/// windows onto the one before would compound a small error every time.</para>
/// </summary>
internal sealed class DepthChunkChain
{
    private const int SampleStride = 8;
    private const float MinConfidence = 0.2f;

    private int _chunkIndex;
    private bool _hasKey;
    private Sim3 _keyToWorld = Sim3.Identity;
    private List<Vector3>[]? _keyPoints;
    private List<float>[]? _keyWeights;
    private Quaternion[]? _keyRotations;

    public Sim3 LastChunkToWorld { get; private set; } = Sim3.Identity;
    public int LastPairCount { get; private set; }
    public float LastResidualBefore { get; private set; }
    public float LastResidualAfter { get; private set; }

    /** How far a pure point-based rotation fit would have turned from the pose-based rotation, for diagnostics. */
    public float LastPointRotationDegrees { get; private set; }

    private static float QuaternionAngle(Quaternion q) => 2f * MathF.Acos(Math.Clamp(MathF.Abs(q.W), 0f, 1f)) * 180f / MathF.PI;

    /**
     * Places a window whose view j is view j + <paramref name="keyOffset"/> of the key window (1 for a window that slides
     * one frame at a time). With <paramref name="advance"/> the window becomes the new key window. Returns the frames to use:
     * all of the first window, afterwards the views that are new. Null when the shared frames cannot be fitted.
     */
    public List<PlacedFrame>? Add(DepthChunk chunk, int keyOffset, bool advance)
    {
        var views = chunk.Views;
        var shared = views - keyOffset;
        var chunkToWorld = Sim3.Identity;
        if (_hasKey)
        {
            var src = new List<Vector3>();
            var dst = new List<Vector3>();
            var weight = new List<float>();
            var accumulated = new Quaternion(0, 0, 0, 0);
            var reference = Quaternion.Identity;
            for (var j = 0; j < shared; j++)
            {
                var points = new List<Vector3>();
                var weights = new List<float>();
                chunk.SamplePoints(j, SampleStride, MinConfidence, points, weights);
                var keyIndex = j + keyOffset;
                for (var i = 0; i < points.Count; i++)
                {
                    var w = MathF.Min(weights[i], _keyWeights![keyIndex][i]);
                    if (w <= 0f)
                        continue;

                    src.Add(points[i]);
                    dst.Add(_keyPoints![keyIndex][i]);
                    weight.Add(w);
                }

                // p_key = Q * p_new, so Q = Q_key * Q_new^-1 for this shared frame
                chunk.CameraToWorld(j, out var newMatrix, out _);
                var q = _keyRotations![keyIndex] * Quaternion.Inverse(Quaternion.CreateFromRotationMatrix(newMatrix));
                if (j == 0)
                    reference = q;
                else if (Quaternion.Dot(reference, q) < 0f)
                    q = new Quaternion(-q.X, -q.Y, -q.Z, -q.W);

                accumulated = new Quaternion(accumulated.X + q.X, accumulated.Y + q.Y, accumulated.Z + q.Z, accumulated.W + q.W);
            }

            LastPairCount = src.Count;
            var poseRotation = Quaternion.Normalize(accumulated);
            if (Sim3Fit.Fit(src, dst, weight, out var pointFit))
                LastPointRotationDegrees = QuaternionAngle(Quaternion.Concatenate(Quaternion.Inverse(poseRotation), pointFit.Rotation));

            if (!Sim3Fit.FitWithRotation(src, dst, weight, poseRotation, out var newToKey))
                return null;

            var before = 0f;
            var after = 0f;
            for (var i = 0; i < src.Count; i++)
            {
                before += Vector3.Distance(src[i], dst[i]);
                after += Vector3.Distance(newToKey.Apply(src[i]), dst[i]);
            }

            LastResidualBefore = before / src.Count;
            LastResidualAfter = after / src.Count;
            chunkToWorld = _keyToWorld.Compose(newToKey);
        }

        LastChunkToWorld = chunkToWorld;
        var placed = new List<PlacedFrame>();
        for (var view = _hasKey ? shared : 0; view < views; view++)
        {
            chunk.CameraToWorld(view, out var rotation, out var position);
            var worldPosition = chunkToWorld.Apply(position);
            var worldRotation = rotation * Matrix4x4.CreateFromQuaternion(chunkToWorld.Rotation);
            placed.Add(new PlacedFrame(_chunkIndex, view, worldRotation, worldPosition, chunkToWorld.Scale));
        }

        if (!_hasKey || advance)
        {
            _keyToWorld = chunkToWorld;
            _keyPoints = new List<Vector3>[views];
            _keyWeights = new List<float>[views];
            _keyRotations = new Quaternion[views];
            for (var view = 0; view < views; view++)
            {
                _keyPoints[view] = new List<Vector3>();
                _keyWeights[view] = new List<float>();
                chunk.SamplePoints(view, SampleStride, MinConfidence, _keyPoints[view], _keyWeights[view]);
                chunk.CameraToWorld(view, out var matrix, out _);
                _keyRotations[view] = Quaternion.CreateFromRotationMatrix(matrix);
            }

            _hasKey = true;
        }

        _chunkIndex++;
        return placed;
    }
}
