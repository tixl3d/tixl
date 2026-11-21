#nullable enable
using System;
using System.Numerics;

namespace Lib.Utils;

/// <summary>
/// Camera-space projection into the laser scanner field (ILDA 16-bit, ±32767) shared by the
/// operators that turn 3D geometry into laser points. All of it culls instead of clamping and
/// treats NaN or infinite input as "not drawable", so a diverged simulation can never reach the
/// scanner as a garbage coordinate.
/// </summary>
internal static class LaserProjection
{
    /// <summary>Half the ILDA range: NDC ±1 maps to ±FieldMax.</summary>
    public const float FieldMax = 32767f;

    /// <summary>Smallest clip-space W (distance in front of the camera) that still projects.</summary>
    private const float MinClipW = 0.0001f;

    public static float FiniteOrZero(float value) => float.IsFinite(value) ? value : 0f;

    public static bool IsFinite(Vector3 v) => float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z);

    /// <summary>
    /// Projects a point to ILDA units. Divides by the clip space W (the distance in front of the
    /// camera), which is what makes the picture follow the lens - near/far planes and lens shift
    /// included. Points outside the near/far planes or the frame are not drawn: clamping them to
    /// the border would slide content along the edge instead of leaving the frame, and is what
    /// morphs shapes near it.
    /// </summary>
    public static bool TryProject(Vector3 position, Matrix4x4 objectToClip, out int x, out int y)
    {
        x = 0;
        y = 0;
        if (!IsFinite(position))
            return false;

        var clip = Vector4.Transform(new Vector4(position, 1f), objectToClip);
        if (!(clip.W > MinClipW) || clip.Z < 0f || clip.Z > clip.W)
            return false;

        var ndcX = clip.X / clip.W;
        var ndcY = clip.Y / clip.W;
        if (!(ndcX >= -1f && ndcX <= 1f && ndcY >= -1f && ndcY <= 1f))
            return false;

        x = (int)MathF.Round(ndcX * FieldMax);
        y = (int)MathF.Round(ndcY * FieldMax);
        return true;
    }

    /// <summary>
    /// Projects a segment and clips it to the camera frustum. Clamping the two endpoints instead
    /// would pin whatever is outside onto the border, bending straight edges along it (the
    /// "morphing" seen when content approaches the field edge); clipping cuts the segment where
    /// it leaves the frame, and a segment entirely outside is dropped.
    /// </summary>
    public static bool TryProjectSegment(Vector3 a, Vector3 b, Matrix4x4 objectToClip,
                                         out float ax, out float ay, out float bx, out float by)
    {
        ax = ay = bx = by = 0f;
        if (!IsFinite(a) || !IsFinite(b))
            return false;

        var ca = Vector4.Transform(new Vector4(a, 1f), objectToClip);
        var cb = Vector4.Transform(new Vector4(b, 1f), objectToClip);

        // Near and far planes in clip space, where the segment is still straight (a lerp):
        // 0 <= z <= w.
        var tStart = 0f;
        var tEnd = 1f;
        if (!ClipPlane(ca.Z, cb.Z, ref tStart, ref tEnd)
            || !ClipPlane(ca.W - ca.Z, cb.W - cb.Z, ref tStart, ref tEnd))
            return false;

        var start = Vector4.Lerp(ca, cb, tStart);
        var end = Vector4.Lerp(ca, cb, tEnd);
        if (!(start.W > MinClipW) || !(end.W > MinClipW))
            return false;

        var x0 = start.X / start.W;
        var y0 = start.Y / start.W;
        var x1 = end.X / end.W;
        var y1 = end.Y / end.W;

        // Liang-Barsky against the unit square. A projected 3D line stays straight in NDC, so
        // clipping here is exact.
        var t0 = 0f;
        var t1 = 1f;
        var dx = x1 - x0;
        var dy = y1 - y0;
        if (!ClipAgainstEdge(-dx, x0 + 1f, ref t0, ref t1)
            || !ClipAgainstEdge(dx, 1f - x0, ref t0, ref t1)
            || !ClipAgainstEdge(-dy, y0 + 1f, ref t0, ref t1)
            || !ClipAgainstEdge(dy, 1f - y0, ref t0, ref t1))
            return false;

        ax = (x0 + dx * t0) * FieldMax;
        ay = (y0 + dy * t0) * FieldMax;
        bx = (x0 + dx * t1) * FieldMax;
        by = (y0 + dy * t1) * FieldMax;
        return float.IsFinite(ax + ay + bx + by);
    }

    /// <summary>
    /// World-space position of the camera defined by a rigid view matrix (rotation-translation,
    /// as the LookAt and aim paths build them): the view origin transformed back into the world,
    /// for tests that compare geometry directions against the eye point.
    /// </summary>
    public static Vector3 GetViewOrigin(Matrix4x4 worldToCamera)
    {
        var tx = worldToCamera.M41;
        var ty = worldToCamera.M42;
        var tz = worldToCamera.M43;
        return new Vector3(
            -(tx * worldToCamera.M11 + ty * worldToCamera.M21 + tz * worldToCamera.M31),
            -(tx * worldToCamera.M12 + ty * worldToCamera.M22 + tz * worldToCamera.M32),
            -(tx * worldToCamera.M13 + ty * worldToCamera.M23 + tz * worldToCamera.M33));
    }

    /// <summary>RGB in 0..1 times brightness to the 16-bit laser color range; NaN becomes dark.</summary>
    public static void ToColor(float red, float green, float blue, float brightness, out int r, out int g, out int b)
    {
        r = (int)(Saturate(red * brightness) * 65535f);
        g = (int)(Saturate(green * brightness) * 65535f);
        b = (int)(Saturate(blue * brightness) * 65535f);
    }

    /// <summary>NaN maps to 0, which a plain Math.Clamp would let through.</summary>
    private static float Saturate(float value) => value > 0f ? (value < 1f ? value : 1f) : 0f;

    /// <summary>Keeps the part of the parameter range [t0, t1] where a plane distance that runs
    /// linearly from <paramref name="distanceA"/> to <paramref name="distanceB"/> is not negative.</summary>
    private static bool ClipPlane(float distanceA, float distanceB, ref float t0, ref float t1)
    {
        if (distanceA < 0f && distanceB < 0f)
            return false;

        if (distanceA < 0f)
            t0 = MathF.Max(t0, distanceA / (distanceA - distanceB));
        else if (distanceB < 0f)
            t1 = MathF.Min(t1, distanceA / (distanceA - distanceB));

        return t0 <= t1;
    }

    /// <summary>One Liang-Barsky half-plane test: p * t &lt;= q.</summary>
    private static bool ClipAgainstEdge(float p, float q, ref float t0, ref float t1)
    {
        if (MathF.Abs(p) < 1e-9f)
            return q >= 0f;

        var r = q / p;
        if (p < 0f)
        {
            if (r > t1)
                return false;
            if (r > t0)
                t0 = r;
        }
        else
        {
            if (r < t0)
                return false;
            if (r < t1)
                t1 = r;
        }

        return true;
    }
}
