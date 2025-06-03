using T3.Core.Utils;
using T3.Core.Utils.Geometry;

namespace T3.Core.Operator.Interfaces;

public interface ICamera
{
    Vector3 CameraPosition { get; set; }
    Vector3 CameraTarget { get; set; }
    float CameraRoll { get; set; }

    CameraDefinition CameraDefinition { get; }
    Matrix4x4 WorldToCamera { get; }
    Matrix4x4 CameraToClipSpace { get; }
}

public struct CameraDefinition
{
    public Vector2 ClipPlanes;
    public Vector2 LensShift;
    public Vector3 PositionOffset;
    public Vector3 Position;
    public Vector3 Target;
    public Vector3 Up;
    public float AspectRatio;
    public float FieldOfView;
    public float Roll;
    public Vector3 RotationOffset;
    public bool OffsetAffectsTarget;

    public static CameraDefinition Blend(CameraDefinition a, CameraDefinition b, float f)
    {
        return new CameraDefinition
                   {
                       ClipPlanes = MathUtils.Lerp(a.ClipPlanes, b.ClipPlanes, f),
                       LensShift = MathUtils.Lerp(a.LensShift, b.LensShift, f),
                       PositionOffset = MathUtils.Lerp(a.PositionOffset, b.PositionOffset, f),
                       Position = MathUtils.Lerp(a.Position, b.Position, f),
                       Target = MathUtils.Lerp(a.Target, b.Target, f),
                       Up = MathUtils.Lerp(a.Up, b.Up, f),
                       AspectRatio = MathUtils.Lerp(a.AspectRatio, b.AspectRatio, f),
                       FieldOfView = MathUtils.Lerp(a.FieldOfView, b.FieldOfView, f),
                       Roll = MathUtils.Lerp(a.Roll, b.Roll, f),
                       RotationOffset = MathUtils.Lerp(a.RotationOffset, b.RotationOffset, f),
                       OffsetAffectsTarget = f < 0.5 ? a.OffsetAffectsTarget : b.OffsetAffectsTarget,
                   };
    }

    public void BuildProjectionMatrices(out Matrix4x4 camToClipSpace, out Matrix4x4 worldToCamera)
    {
        camToClipSpace = GraphicsMath.PerspectiveFovRH(FieldOfView, AspectRatio, ClipPlanes.X, ClipPlanes.Y);
        camToClipSpace.M31 = LensShift.X;
        camToClipSpace.M32 = LensShift.Y;

        var eye = Position;
        if (!OffsetAffectsTarget)
            eye += PositionOffset;

        var worldToCameraRoot = GraphicsMath.LookAtRH(eye, Target, Up);
        var rollRotation = Matrix4x4.CreateFromAxisAngle(Vector3.UnitZ, -Roll * MathUtils.ToRad);
        var additionalTranslation = OffsetAffectsTarget ? Matrix4x4.CreateTranslation(PositionOffset.X, PositionOffset.Y, PositionOffset.Z) : Matrix4x4.Identity;

        var additionalRotation = Matrix4x4.CreateFromYawPitchRoll(MathUtils.ToRad * RotationOffset.Y,
                                                                  MathUtils.ToRad * RotationOffset.X,
                                                                  MathUtils.ToRad * RotationOffset.Z);

        worldToCamera = worldToCameraRoot * rollRotation * additionalRotation * additionalTranslation;
    }
}

// Mock view internal fallback camera (if no operator selected)
// Todo: Find a better location of this class
public class ViewCamera : ICamera
{
    public Vector3 CameraPosition { get; set; } = new(0, 0, GraphicsMath.DefaultCameraDistance);
    public Vector3 CameraTarget { get; set; }
    public float CameraRoll { get; set; }
    public Matrix4x4 WorldToCamera { get; }
    public Matrix4x4 CameraToClipSpace { get; }
    public CameraDefinition CameraDefinition => new();  // Not implemented
}