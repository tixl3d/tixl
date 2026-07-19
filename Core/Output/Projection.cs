using System;
using System.Numerics;

namespace T3.Core.Output;

/// <summary>
/// Lens/projection half of a camera or projector. Projectors are off-axis cameras:
/// lens shift is the norm, so the shift is a first-class field, not a special case.
/// </summary>
public struct Projection
{
    public ProjectionKind Kind;

    /// <summary>Vertical field of view in radians (perspective only).</summary>
    public float FieldOfViewY;

    /// <summary>
    /// Principal-point offset in NDC units (1 = half the image size). A projector mounted
    /// below a screen projecting upward has a positive Y shift.
    /// </summary>
    public Vector2 LensShift;

    /// <summary>Visible height in meters (orthographic only).</summary>
    public float OrthographicHeight;

    public float NearZ;
    public float FarZ;

    public static Projection CreatePerspective(float fieldOfViewY, Vector2 lensShift, float nearZ = 0.1f, float farZ = 1000f)
    {
        return new Projection
                   {
                       Kind = ProjectionKind.Perspective,
                       FieldOfViewY = fieldOfViewY,
                       LensShift = lensShift,
                       NearZ = nearZ,
                       FarZ = farZ,
                   };
    }

    public static Projection CreateOrthographic(float heightInMeters, float nearZ = 0.1f, float farZ = 1000f)
    {
        return new Projection
                   {
                       Kind = ProjectionKind.Orthographic,
                       OrthographicHeight = heightInMeters,
                       NearZ = nearZ,
                       FarZ = farZ,
                   };
    }

    /// <summary>
    /// Right-handed projection matrix (row-vector convention). For perspective, the lens
    /// shift lands in M31/M32 so it stays constant in NDC regardless of depth.
    /// </summary>
    public Matrix4x4 GetMatrix(float aspect)
    {
        if (Kind == ProjectionKind.Orthographic)
        {
            var height = MathF.Max(OrthographicHeight, 1e-5f);
            return Matrix4x4.CreateOrthographic(height * aspect, height, NearZ, FarZ);
        }

        var fov = Math.Clamp(FieldOfViewY, 1e-4f, MathF.PI - 1e-4f);
        var yScale = 1.0f / MathF.Tan(fov * 0.5f);
        var xScale = yScale / MathF.Max(aspect, 1e-5f);
        var zRange = NearZ - FarZ;

        var m = new Matrix4x4
                    {
                        M11 = xScale,
                        M22 = yScale,
                        M31 = -LensShift.X,
                        M32 = -LensShift.Y,
                        M33 = FarZ / zRange,
                        M34 = -1f,
                        M43 = NearZ * FarZ / zRange,
                    };
        return m;
    }
}

public enum ProjectionKind
{
    Perspective,
    Orthographic,
}
