using System;
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
    public Vector2 NearFarClip;
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
        f = Math.Clamp(f, 0f, 1f);

        var blendedPosition = MathUtils.Lerp(a.Position, b.Position, f);
        var blendedTarget = MathUtils.Lerp(a.Target, b.Target, f);

        var qa = ExtractCameraQuaternion(a);
        var qb = ExtractCameraQuaternion(b);

        // shortest path
        if (Quaternion.Dot(qa, qb) < 0)
            qb = Quaternion.Negate(qb);

        var q = Quaternion.Normalize(Quaternion.Slerp(qa, qb, f));

        Vector3 forward = Vector3.Transform(-Vector3.UnitZ, q);
        Vector3 up = Vector3.Transform(Vector3.UnitY, q);

        return new CameraDefinition
                   {
                       Position = blendedPosition,
                       Target = blendedPosition + forward, // RH camera
                       Up = Vector3.Normalize(up),
                       Roll = 0,

                       NearFarClip = MathUtils.Lerp(a.NearFarClip, b.NearFarClip, f),
                       LensShift = MathUtils.Lerp(a.LensShift, b.LensShift, f),
                       PositionOffset = MathUtils.Lerp(a.PositionOffset, b.PositionOffset, f),
                       AspectRatio = MathUtils.Lerp(a.AspectRatio, b.AspectRatio, f),
                       FieldOfView = MathUtils.Lerp(a.FieldOfView, b.FieldOfView, f),
                       RotationOffset = MathUtils.Lerp(a.RotationOffset, b.RotationOffset, f),

                       OffsetAffectsTarget = f < 0.5f ? a.OffsetAffectsTarget : b.OffsetAffectsTarget,
                   };
    }


    private static Quaternion ExtractCameraQuaternion(CameraDefinition cam)
    {
        cam.BuildProjectionMatrices(out _, out var worldToCamera);

        // Convert view -> world transform
        Matrix4x4.Invert(worldToCamera, out var cameraToWorld);

        // Extract the 3x3 rotation block
        var rot = new Matrix4x4(
                                cameraToWorld.M11, cameraToWorld.M12, cameraToWorld.M13, 0,
                                cameraToWorld.M21, cameraToWorld.M22, cameraToWorld.M23, 0,
                                cameraToWorld.M31, cameraToWorld.M32, cameraToWorld.M33, 0,
                                0, 0, 0, 1);

        // Orthonormalize to remove numerical drift
        var right = Vector3.Normalize(new Vector3(rot.M11, rot.M12, rot.M13));
        var up = Vector3.Normalize(new Vector3(rot.M21, rot.M22, rot.M23));
        var forward = Vector3.Normalize(new Vector3(rot.M31, rot.M32, rot.M33));

        // Rebuild orthogonal basis
        forward = Vector3.Normalize(forward);
        right = Vector3.Normalize(Vector3.Cross(up, forward));
        up = Vector3.Cross(forward, right);

        var clean = new Matrix4x4(
                                  right.X, right.Y, right.Z, 0,
                                  up.X, up.Y, up.Z, 0,
                                  forward.X, forward.Y, forward.Z, 0,
                                  0, 0, 0, 1);

        return Quaternion.CreateFromRotationMatrix(clean);
    }


    public void BuildProjectionMatrices(out Matrix4x4 camToClipSpace, out Matrix4x4 worldToCamera)
    {
        camToClipSpace = GraphicsMath.PerspectiveFovRH(FieldOfView, AspectRatio, NearFarClip.X, NearFarClip.Y);
        camToClipSpace.M31 = LensShift.X;
        camToClipSpace.M32 = LensShift.Y;

        var eye = Position;
        if (!OffsetAffectsTarget)
            eye += PositionOffset;

        var worldToCameraRoot = GraphicsMath.LookAtRH(eye, Target, Up);
        var rollRotation = Matrix4x4.CreateFromAxisAngle(Vector3.UnitZ, -Roll * MathUtils.ToRad);
        var additionalTranslation =
            OffsetAffectsTarget ? Matrix4x4.CreateTranslation(PositionOffset.X, PositionOffset.Y, PositionOffset.Z) : Matrix4x4.Identity;

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
    public CameraDefinition CameraDefinition => new(); // Not implemented
}