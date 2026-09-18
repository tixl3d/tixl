#nullable enable
using T3.Core.Output;
using Vector3 = System.Numerics.Vector3;
using Quaternion = System.Numerics.Quaternion;
using Matrix4x4 = System.Numerics.Matrix4x4;

namespace T3.Editor.Gui.Windows.OutputSetup;

/// <summary>
/// Poses surfaces in the stage without anyone typing a quaternion. A surface's own space is its plane: X right,
/// Y up, Z its normal — an unposed surface stands upright at the origin facing +Z.
/// </summary>
internal static class StagePlacing
{
    /// <summary>
    /// Stands <paramref name="wall"/> upright on the ground with its bottom-centre at <paramref name="bottomCentre"/>,
    /// facing along <paramref name="normal"/> (horizontal, unit length). Size is left alone.
    /// </summary>
    public static void PoseWall(Surface wall, Vector3 bottomCentre, Vector3 normal)
    {
        // Right follows from up and the facing so the frame stays right-handed. Rows are the images of the
        // local axes (row-vector convention).
        var up = Vector3.UnitY;
        var right = Vector3.Cross(up, normal);
        var frame = new Matrix4x4(right.X, right.Y, right.Z, 0,
                                  up.X, up.Y, up.Z, 0,
                                  normal.X, normal.Y, normal.Z, 0,
                                  0, 0, 0, 1);

        // The pose positions the anchor, which needn't be the bottom-centre that stands on the ground.
        var bottomCentreToAnchor = new Vector3(wall.AnchorInMeters.X - wall.SizeInMeters.X * 0.5f, wall.AnchorInMeters.Y, 0);
        var position = bottomCentre + Vector3.TransformNormal(bottomCentreToAnchor, frame);

        wall.Placement ??= new Surface.StagePlacement();
        wall.Placement.Pose = new Pose(position, Quaternion.Normalize(Quaternion.CreateFromRotationMatrix(frame)));
    }

    /// <summary>
    /// Lays <paramref name="floor"/> flat on the ground, its bottom-left corner at <paramref name="bottomLeft"/> and
    /// its own up pointing away from the viewer (−Z), so its normal points up.
    /// </summary>
    public static void PoseFloor(Surface floor, Vector3 bottomLeft)
    {
        var anchor = floor.AnchorInMeters;
        var position = bottomLeft + new Vector3(anchor.X, 0, -anchor.Y);
        floor.Placement ??= new Surface.StagePlacement();
        floor.Placement.Pose = new Pose(position, Quaternion.CreateFromAxisAngle(Vector3.UnitX, -MathF.PI * 0.5f));
    }

    /// <summary>Yaw (about Y), pitch (about X) and roll (about Z) in degrees, the way the card shows a pose.</summary>
    public static Vector3 ToYawPitchRollDegrees(Quaternion q)
    {
        // Matches Quaternion.CreateFromYawPitchRoll, which applies roll, then pitch, then yaw.
        var m = Matrix4x4.CreateFromQuaternion(q);
        float yaw, pitch, roll;
        var sinPitch = -m.M32;
        if (MathF.Abs(sinPitch) > 0.9999f)
        {
            // Looking straight up or down: yaw and roll share an axis, so all of it goes to yaw.
            pitch = MathF.CopySign(MathF.PI * 0.5f, sinPitch);
            yaw = MathF.Atan2(-m.M13, m.M11);
            roll = 0;
        }
        else
        {
            pitch = MathF.Asin(sinPitch);
            yaw = MathF.Atan2(m.M31, m.M33);
            roll = MathF.Atan2(m.M12, m.M22);
        }

        const float toDegrees = 180f / MathF.PI;
        return new Vector3(yaw * toDegrees, pitch * toDegrees, roll * toDegrees);
    }

    public static Quaternion FromYawPitchRollDegrees(Vector3 degrees)
    {
        const float toRadians = MathF.PI / 180f;
        return Quaternion.Normalize(Quaternion.CreateFromYawPitchRoll(degrees.X * toRadians, degrees.Y * toRadians, degrees.Z * toRadians));
    }
}
