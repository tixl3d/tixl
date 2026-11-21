#nullable enable
using System;
using System.Collections.Generic;
using System.Numerics;

namespace Lib.io.video.depthanything.Utils;

/// <summary>
/// Temporal ego-motion from consecutive depth maps - recovers the camera trajectory that
/// the model's own depth (and optional confidence) implies. Needed because the community
/// ONNX exports carry an extrinsics output that does not respond to camera motion.
/// <para>Each frame's depth plane is unprojected with the intrinsics into a sparse point
/// cloud; the cur-to-keyframe transform is estimated by trimmed point-to-point Gauss-Newton
/// ICP over a voxel-hash grid, seeded with the previous estimate. Matching against a keyframe
/// instead of the previous frame keeps per-frame noise from accumulating while the camera holds
/// still; the keyframe advances only after real motion. The pose is a camToWorld transform in the
/// model's OpenCV-style frame (x right, y down, +Z forward, so the image-up axis is -Y).
/// Relative-depth scale wobble shows up as translation, which is why a pan/tilt camera should
/// track in <see cref="DepthCameraMotion.RotationOnly"/>; a failed match re-anchors instead of jumping.</para>
/// </summary>
internal sealed class DepthPoseTracker
{
    private const int MinSampleStride = 4;

    /** ICP cost grows with points x neighbours per grid cell; a few thousand well-spread samples track as well as 16k. */
    private const int TargetSampleCount = 4096;
    private const int MaxIterations = 8;
    private const float GridCell = 0.06f;
    private const float MinInlierRatio = 0.3f;
    private const float TrimKeepRatio = 0.7f;
    private const float ResidualGate = GridCell;

    /** A keyframe is replaced once the camera turned this far from it; beyond that the clouds overlap too little. */
    private const float KeyframeAngleDeg = 4f;

    /** Same for translation, in the depth's units (relative depth is around 1 for the scene). */
    private const float KeyframeDistance = 0.08f;

    /** Matches weaker than this still track, but the next frames match against the current view. */
    private const float KeyframeInlierRatio = 0.5f;

    public DepthCameraMotion Motion { get; set; } = DepthCameraMotion.Free;

    private readonly ImageRotationTracker _imageRotation = new();

    private Quaternion _worldRotation = Quaternion.Identity;
    private Vector3 _worldPosition = Vector3.Zero;
    private Quaternion _keyRotation = Quaternion.Identity;
    private Vector3 _keyPosition = Vector3.Zero;

    /** Keyframe cloud the current frame is matched against. */
    private float[] _prevPoints = [];
    private int _prevCount;
    private readonly Dictionary<long, int> _gridHeads = new();
    private int[] _gridNext = [];
    private float[] _pivotScratch = [];

    /** Previous accepted cur-to-keyframe transform; the next frame starts its search there. */
    private Quaternion _seedRotation = Quaternion.Identity;
    private Vector3 _seedTranslation = Vector3.Zero;

    public bool HasPose { get; private set; }
    public bool Tracking { get; private set; }

    /** How the last frame was tracked, for the pose log: image-based (features, intensity, predicted, lost) or depth. */
    public string Source => Motion == DepthCameraMotion.RotationOnly ? $"image-{_imageRotation.LastSource}({_imageRotation.LastInliers})" : "depth";
    public Vector3 Position => _worldPosition;
    public Vector3 Forward => Vector3.Transform(Vector3.UnitZ, _worldRotation);
    public Vector3 Up => Vector3.Transform(-Vector3.UnitY, _worldRotation);

    public void Reset()
    {
        _worldRotation = Quaternion.Identity;
        _worldPosition = Vector3.Zero;
        _keyRotation = Quaternion.Identity;
        _keyPosition = Vector3.Zero;
        _prevCount = 0;
        _prevPoints = [];
        _gridHeads.Clear();
        _seedRotation = Quaternion.Identity;
        _seedTranslation = Vector3.Zero;
        _imageRotation.Reset();
        HasPose = false;
        Tracking = false;
    }

    /// <summary>Feeds one grayscale frame of a pan/tilt camera: the rotation comes from image features, not from depth.</summary>
    public void UpdateFromImage(byte[] gray, int width, int height, float fx, float fy, float cx, float cy)
    {
        _imageRotation.Update(gray, width, height, fx, fy, cx, cy);
        _worldRotation = _imageRotation.Rotation;
        _worldPosition = Vector3.Zero;
        HasPose = _imageRotation.HasPose;
        Tracking = _imageRotation.Tracking;
    }

    /// <summary>Feeds one depth plane and updates the accumulated camToWorld pose.</summary>
    public void Update(float[] depth, float[]? confidence, int width, int height,
                       float fx, float fy, float cx, float cy)
    {
        if (Motion == DepthCameraMotion.Static)
        {
            HasPose = true;
            Tracking = true;
            return;                       // a fixed camera keeps the identity pose
        }

        var points = BuildCloud(depth, confidence, width, height, fx, fy, cx, cy, out var count);
        if (count < 64)
        {
            Tracking = false;
            return;
        }

        if (_prevCount < 64)
        {
            PromoteKeyframe(points, count);
            HasPose = true;
            Tracking = true;
            return;                       // first frame anchors at the identity pose
        }

        var (deltaRotation, deltaTranslation, inlierRatio, residual) =
            EstimateDelta(points, count);
        var accepted = inlierRatio >= MinInlierRatio && residual <= ResidualGate;
        if (accepted)
        {
            // C = C_key o delta, with delta mapping cur-camera points into the keyframe camera:
            // p_world = R_key (R_delta p + t_delta) + t_key, so R_delta is applied first.
            _worldPosition = _keyPosition + Vector3.Transform(deltaTranslation, _keyRotation);
            _worldRotation = Quaternion.Normalize(Quaternion.Multiply(_keyRotation, deltaRotation));
            _seedRotation = deltaRotation;
            _seedTranslation = deltaTranslation;

            var turnedDeg = 2f * MathF.Acos(Math.Clamp(MathF.Abs(deltaRotation.W), 0f, 1f)) * 180f / MathF.PI;
            if (turnedDeg > KeyframeAngleDeg || deltaTranslation.Length() > KeyframeDistance || inlierRatio < KeyframeInlierRatio)
                PromoteKeyframe(points, count);
        }
        else
        {
            PromoteKeyframe(points, count);   // re-anchor on the current view, keep the world pose
        }

        Tracking = accepted;
        HasPose = true;
    }

    /** Makes the current view the reference for the next frames, at the current world pose. */
    private void PromoteKeyframe(float[] points, int count)
    {
        _keyRotation = _worldRotation;
        _keyPosition = _worldPosition;
        _seedRotation = Quaternion.Identity;
        _seedTranslation = Vector3.Zero;
        SwapCloud(points, count);
    }

    private float[] BuildCloud(float[] depth, float[]? confidence, int width, int height,
                               float fx, float fy, float cx, float cy, out int count)
    {
        var sampleStride = Math.Max(MinSampleStride, (int)MathF.Ceiling(MathF.Sqrt((float)width * height / TargetSampleCount)));
        var columns = (width + sampleStride - 1) / sampleStride;
        var rows = (height + sampleStride - 1) / sampleStride;
        var points = new float[columns * rows * 3];

        float threshold = 0f;
        if (confidence != null)
        {
            var samples = new List<float>(columns * rows);
            for (var y = 0; y < height; y += sampleStride)
            for (var x = 0; x < width; x += sampleStride)
            {
                var v = confidence[y * width + x];
                if (float.IsFinite(v))
                    samples.Add(v);
            }

            samples.Sort();
            threshold = samples.Count > 0 ? samples[samples.Count / 2] : 0f;
        }

        var n = 0;
        for (var y = 0; y < height; y += sampleStride)
        {
            var row = y * width;
            for (var x = 0; x < width; x += sampleStride)
            {
                var index = row + x;
                var z = depth[index];
                if (!float.IsFinite(z) || z <= 0f)
                    continue;
                if (confidence != null && (confidence[index] < threshold || !float.IsFinite(confidence[index])))
                    continue;

                points[n * 3 + 0] = (x - cx) / fx * z;
                points[n * 3 + 1] = (y - cy) / fy * z;
                points[n * 3 + 2] = z;
                n++;
            }
        }

        count = n;
        return points;
    }

    private void SwapCloud(float[] points, int count)
    {
        _prevPoints = points;
        _prevCount = count;
        _gridHeads.Clear();
        if (_gridNext.Length < count)
            _gridNext = new int[count];
        for (var i = 0; i < count; i++)
        {
            var key = CellKey(_prevPoints[i * 3], _prevPoints[i * 3 + 1], _prevPoints[i * 3 + 2]);
            _gridNext[i] = _gridHeads.TryGetValue(key, out var head) ? head : -1;
            _gridHeads[key] = i;
        }
    }

    private static long CellKey(float x, float y, float z)
    {
        return PackCells((int)MathF.Floor(x / GridCell),
                         (int)MathF.Floor(y / GridCell),
                         (int)MathF.Floor(z / GridCell));
    }

    private static long PackCells(int ix, int iy, int iz)
    {
        return ((long)(ix & 0x1FFFFF) << 42) | ((long)(iy & 0x1FFFFF) << 21) | (iz & 0x1FFFFF);
    }

    // ---------------- ICP ----------------

    /// <summary>
    /// Estimates the cur-to-prev transform: starts from the previous accepted delta,
    /// matches the transformed points against the voxel grid, trims to the best 70% of
    /// the matches, and refines with Gauss-Newton on the point-to-point residuals.
    /// </summary>
    private (Quaternion Rotation, Vector3 Translation, float InlierRatio, float Residual)
        EstimateDelta(float[] cur, int count)
    {
        var rotation = _seedRotation;
        var translation = _seedTranslation;

        var inlierRatio = 0f;
        var residual = float.MaxValue;
        var pivot = float.MaxValue;
        Span<int> partners = count <= 8192 ? stackalloc int[count] : new int[count];
        Span<float> dists = count <= 8192 ? stackalloc float[count] : new float[count];

        for (var iteration = 0; iteration < MaxIterations; iteration++)
        {
            var matched = 0;
            var distSum = 0f;
            for (var i = 0; i < count; i++)
            {
                var moved = Vector3.Transform(new Vector3(cur[i * 3], cur[i * 3 + 1], cur[i * 3 + 2]), rotation) + translation;
                FindNearest(moved, out var best, out var bestIndex);
                dists[i] = best;
                partners[i] = bestIndex;
                if (bestIndex >= 0)
                {
                    matched++;
                    distSum += best;
                }
            }

            if (matched < count * 0.25f)
                break;

            var keep = Math.Max(16, (int)(matched * TrimKeepRatio));
            pivot = SelectPivot(dists, count, keep);

            var inliers = 0;
            for (var i = 0; i < count; i++)
            {
                if (partners[i] >= 0 && dists[i] <= pivot)
                    inliers++;
            }
            inlierRatio = (float)inliers / count;

            if (!GaussNewtonStep(cur, count, partners, dists, pivot, ref rotation, ref translation))
                break;

            distSum = 0f;
        }

        // final residual over the trimmed inliers at the final estimate
        var finalSum = 0f;
        var finalCount = 0;
        for (var i = 0; i < count; i++)
        {
            if (partners[i] < 0 || dists[i] > pivot)
                continue;
            var moved = Vector3.Transform(new Vector3(cur[i * 3], cur[i * 3 + 1], cur[i * 3 + 2]), rotation) + translation;
            var qx = moved.X - _prevPoints[partners[i] * 3];
            var qy = moved.Y - _prevPoints[partners[i] * 3 + 1];
            var qz = moved.Z - _prevPoints[partners[i] * 3 + 2];
            finalSum += (float)Math.Sqrt(qx * qx + qy * qy + qz * qz);
            finalCount++;
        }

        residual = finalCount > 0 ? finalSum / finalCount : float.MaxValue;
        return (rotation, translation, inlierRatio, residual);
    }

    private void FindNearest(Vector3 p, out float distSq, out int index)
    {
        var best = float.MaxValue;
        var bestIndex = -1;
        var cx = (int)MathF.Floor(p.X / GridCell);
        var cy = (int)MathF.Floor(p.Y / GridCell);
        var cz = (int)MathF.Floor(p.Z / GridCell);
        for (var dz = -1; dz <= 1; dz++)
        for (var dy = -1; dy <= 1; dy++)
        for (var dx = -1; dx <= 1; dx++)
        {
            var key = PackCells(cx + dx, cy + dy, cz + dz);
            for (var j = _gridHeads.TryGetValue(key, out var head) ? head : -1;
                 j >= 0;
                 j = _gridNext[j])
            {
                var qx = _prevPoints[j * 3] - p.X;
                var qy = _prevPoints[j * 3 + 1] - p.Y;
                var qz = _prevPoints[j * 3 + 2] - p.Z;
                var d2 = qx * qx + qy * qy + qz * qz;
                if (d2 < best)
                {
                    best = d2;
                    bestIndex = j;
                    if (d2 == 0f)
                        goto done;
                }
            }
        }

        done:
        distSq = best;
        index = bestIndex;
    }

    /// <summary>The <paramref name="keep"/>-th smallest matched squared distance (the largest one when fewer matched).</summary>
    private float SelectPivot(Span<float> dists, int count, int keep)
    {
        if (_pivotScratch.Length < count)
            _pivotScratch = new float[count];

        var n = 0;
        for (var i = 0; i < count; i++)
        {
            var v = dists[i];
            if (v < float.MaxValue)
                _pivotScratch[n++] = v;
        }

        if (n == 0)
            return float.MaxValue;

        return QuickSelect(_pivotScratch.AsSpan(0, n), Math.Min(n, keep) - 1);
    }

    /** Iterative Hoare quickselect: O(n) on average, reorders <paramref name="values"/> in place. */
    private static float QuickSelect(Span<float> values, int k)
    {
        var left = 0;
        var right = values.Length - 1;
        while (left < right)
        {
            var pivot = values[left + (right - left) / 2];
            var i = left;
            var j = right;
            while (i <= j)
            {
                while (values[i] < pivot)
                    i++;
                while (values[j] > pivot)
                    j--;
                if (i <= j)
                {
                    (values[i], values[j]) = (values[j], values[i]);
                    i++;
                    j--;
                }
            }

            if (k <= j)
            {
                right = j;
            }
            else if (k >= i)
            {
                left = i;
            }
            else
            {
                break;
            }
        }

        return values[k];
    }

    /// <summary>
    /// One Gauss-Newton step on the trimmed point-to-point residuals. Solves the 6x6
    /// normal equations for a (small-angle rotation, translation) increment and composes
    /// both onto the running estimate. Returns false when the solve is singular.
    /// </summary>
    private bool GaussNewtonStep(float[] cur, int count, Span<int> partners, Span<float> dists,
                                 float pivot, ref Quaternion rotation, ref Vector3 translation)
    {
        Span<float> h = stackalloc float[36];
        Span<float> g = stackalloc float[6];
        h.Clear();
        g.Clear();

        // Allocated once: a stackalloc inside the point loop is only released on return and overflows the stack.
        Span<float> jwx = stackalloc float[6];
        Span<float> jwy = stackalloc float[6];
        Span<float> jwz = stackalloc float[6];
        jwx[3] = 1f;
        jwy[4] = 1f;
        jwz[5] = 1f;

        for (var i = 0; i < count; i++)
        {
            if (partners[i] < 0 || dists[i] > pivot)
                continue;
            var p = Vector3.Transform(new Vector3(cur[i * 3], cur[i * 3 + 1], cur[i * 3 + 2]), rotation);
            var rx = p.X + translation.X - _prevPoints[partners[i] * 3];
            var ry = p.Y + translation.Y - _prevPoints[partners[i] * 3 + 1];
            var rz = p.Z + translation.Z - _prevPoints[partners[i] * 3 + 2];

            // dr/dw for increment w applied as R(w) * p: w x p  ->
            //   drx/dw = ( 0,  pz, -py),  dry/dw = (-pz, 0, px),  drz/dw = ( py, -px, 0)
            var ax = p.X; var ay = p.Y; var az = p.Z;
            jwx[1] = az;
            jwx[2] = -ay;
            jwy[0] = -az;
            jwy[2] = ax;
            jwz[0] = ay;
            jwz[1] = -ax;
            AccumulateNormalEquations(jwx, -rx, h, g);
            AccumulateNormalEquations(jwy, -ry, h, g);
            AccumulateNormalEquations(jwz, -rz, h, g);
        }

        if (Motion == DepthCameraMotion.RotationOnly)
        {
            // Pan/tilt cameras turn about a fixed point: pin the translation unknowns to zero
            for (var d = 3; d < 6; d++)
            {
                for (var k = 0; k < 6; k++)
                {
                    h[d * 6 + k] = 0f;
                    h[k * 6 + d] = 0f;
                }

                h[d * 6 + d] = 1f;
                g[d] = 0f;
            }
        }

        // damped solve: H * delta = g, with a tiny Tikhonov term for degenerate geometry
        var lambda = 1e-6f * (h[0] + h[7] + h[14] + h[21] + h[28] + h[35]) / 6f + 1e-9f;
        for (var d = 0; d < 6; d++)
        {
            h[d * 6 + d] += lambda;
        }

        if (!Solve6(h, g, out var wx, out var wy, out var wz, out var tx, out var ty, out var tz))
            return false;

        // compose the small-angle increment:  T'(p) = R(w) * (T(p)) + dt
        var angle = MathF.Sqrt(wx * wx + wy * wy + wz * wz);
        if (angle > 1e-8f)
        {
            var axis = new Vector3(wx / angle, wy / angle, wz / angle);
            var deltaQ = Quaternion.Normalize(Quaternion.CreateFromAxisAngle(axis, angle));
            rotation = Quaternion.Normalize(Quaternion.Multiply(deltaQ, rotation));
            translation = Vector3.Transform(translation, deltaQ) + new Vector3(tx, ty, tz);
        }
        else
        {
            translation += new Vector3(tx, ty, tz);
        }

        return true;
    }

    private static void AccumulateNormalEquations(Span<float> j, float negResidual, Span<float> h, Span<float> g)
    {
        for (var a = 0; a < 6; a++)
        {
            for (var b = 0; b < 6; b++)
            {
                h[a * 6 + b] += j[a] * j[b];
            }
            g[a] += j[a] * negResidual;
        }
    }

    /// <summary>Gaussian elimination with partial pivoting on a 6x6 system.</summary>
    private static bool Solve6(Span<float> h, Span<float> g,
                               out float wx, out float wy, out float wz,
                               out float tx, out float ty, out float tz)
    {
        var a = new float[36];
        var b = new float[6];
        h.CopyTo(a);
        g.CopyTo(b);

        for (var col = 0; col < 6; col++)
        {
            var pivotRow = col;
            for (var r = col + 1; r < 6; r++)
            {
                if (MathF.Abs(a[r * 6 + col]) > MathF.Abs(a[pivotRow * 6 + col]))
                    pivotRow = r;
            }

            if (MathF.Abs(a[pivotRow * 6 + col]) < 1e-10f)
            {
                wx = wy = wz = tx = ty = tz = 0f;
                return false;
            }

            if (pivotRow != col)
            {
                for (var k = 0; k < 6; k++)
                {
                    (a[col * 6 + k], a[pivotRow * 6 + k]) = (a[pivotRow * 6 + k], a[col * 6 + k]);
                }
                (b[col], b[pivotRow]) = (b[pivotRow], b[col]);
            }

            for (var r = col + 1; r < 6; r++)
            {
                var f = a[r * 6 + col] / a[col * 6 + col];
                if (f == 0f)
                    continue;
                for (var k = col; k < 6; k++)
                {
                    a[r * 6 + k] -= f * a[col * 6 + k];
                }
                b[r] -= f * b[col];
            }
        }

        Span<float> x = stackalloc float[6];
        for (var r = 5; r >= 0; r--)
        {
            var s = b[r];
            for (var k = r + 1; k < 6; k++)
            {
                s -= a[r * 6 + k] * x[k];
            }
            x[r] = s / a[r * 6 + r];
        }

        wx = x[0]; wy = x[1]; wz = x[2]; tx = x[3]; ty = x[4]; tz = x[5];
        return true;
    }
}

/// <summary>How the physical camera moves, which constrains what the depth tracker may estimate.</summary>
public enum DepthCameraMotion
{
    /** Hand-held or moving: full rotation and translation. */
    Free,

    /** Pan/tilt (PTZ) or tripod: rotation only, so depth-scale wobble cannot drift the position. */
    RotationOnly,

    /** Fixed camera: the pose stays at the reference. */
    Static,
}
