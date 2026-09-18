using System.Numerics;

namespace T3.Core.Output;

/// <summary>
/// Position + orientation of an entity in stage space (right-handed, Y-up, meters).
/// Shared by surfaces, projectors and cameras. Orientation is camera/entity → world.
/// </summary>
public struct Pose
{
    public Vector3 Position;
    public Quaternion Orientation;

    public static readonly Pose Identity = new() { Position = Vector3.Zero, Orientation = Quaternion.Identity };

    public Pose(Vector3 position, Quaternion orientation)
    {
        Position = position;
        Orientation = orientation;
    }

    /// <summary>Local → world transform (row-vector convention, like System.Numerics).</summary>
    public Matrix4x4 ToWorldMatrix()
    {
        return Matrix4x4.CreateFromQuaternion(Orientation) * Matrix4x4.CreateTranslation(Position);
    }

    /// <summary>World → local (view) transform. Cameras look down -Z in local space.</summary>
    public Matrix4x4 ToViewMatrix()
    {
        Matrix4x4.Invert(ToWorldMatrix(), out var view);
        return view;
    }
}
