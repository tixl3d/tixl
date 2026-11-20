using System;

namespace T3.Core.Utils.Math3d;

public static class GraphicsMath
{
    /// <summary>
    /// System.Numerics implementation of SharpDX's LookAtRH method
    /// </summary>
    public static Matrix4x4 LookAtRH(Vector3 eye, Vector3 target, Vector3 up)
    {
        var zAxis = Vector3.Normalize(eye - target);
        var xAxis = Vector3.Normalize(Vector3.Cross(up, zAxis));
        var yAxis = Vector3.Cross(zAxis, xAxis);

        return new Matrix4x4(
                             xAxis.X, yAxis.X, zAxis.X, 0,
                             xAxis.Y, yAxis.Y, zAxis.Y, 0,
                             xAxis.Z, yAxis.Z, zAxis.Z, 0,
                             -Vector3.Dot(xAxis, eye), -Vector3.Dot(yAxis, eye), -Vector3.Dot(zAxis, eye), 1
                            );
    }

    /// <summary>
    /// System.Numerics implementation of SharpDX's PerspectiveFovRH method
    /// </summary>
    public static Matrix4x4 PerspectiveFovRH(float fov, float aspect, float zNear, float zFar)
    {
        fov = MathF.Max(FovEpsilon, MathF.Min(fov, MaxFov));
        aspect = MathF.Max(FovEpsilon, aspect);
        zNear = MathF.Max(FovEpsilon, zNear);
        zFar = MathF.Max(zNear + FovEpsilon, zFar);

        var yScale = 1.0f / MathF.Tan(fov * 0.5f);
        var xScale = yScale / aspect;
        var differenceZNearZFar = zNear - zFar;

        Matrix4x4 result;

        result.M11 = xScale;
        result.M12 = result.M13 = result.M14 = 0.0f;

        result.M22 = yScale;
        result.M21 = result.M23 = result.M24 = 0.0f;

        result.M33 = zFar / differenceZNearZFar;
        result.M31 = result.M32 = 0.0f;
        result.M34 = -1.0f;

        result.M41 = result.M42 = result.M44 = 0.0f;
        result.M43 = zNear * zFar / differenceZNearZFar;

        return result;
    }

    public static Matrix4x4 CreateTransformationMatrix(in Vector3 scalingCenter,
                                                       in Quaternion scalingRotation,
                                                       in Vector3 scaling,
                                                       in Vector3 rotationCenter,
                                                       in Quaternion rotation,
                                                       in Vector3 translation)
    {
        // Create rotation matrices from quaternions
        var scalingRotationMatrix = Matrix4x4.CreateFromQuaternion(scalingRotation);
        var rotationMatrix = Matrix4x4.CreateFromQuaternion(rotation);

        // Create inverse rotation matrix from the conjugate of the quaternion
        var inverseScalingRotationMatrix = Matrix4x4.CreateFromQuaternion(Quaternion.Conjugate(scalingRotation));

        // Create translation matrices for the rotation and scaling centers
        var scalingCenterTranslation = Matrix4x4.CreateTranslation(-scalingCenter);
        var rotationCenterTranslation = Matrix4x4.CreateTranslation(-rotationCenter);

        // Create the inverse translations
        var inverseScalingCenterTranslation = Matrix4x4.CreateTranslation(scalingCenter);
        var inverseRotationCenterTranslation = Matrix4x4.CreateTranslation(rotationCenter);

        // Create the scaling matrix
        var scalingMatrix = Matrix4x4.CreateScale(scaling);

        // Create the final translation matrix
        var finalTranslationMatrix = Matrix4x4.CreateTranslation(translation);

        // Combine the matrices to form the transformation matrix
        var transformationMatrix =
            scalingCenterTranslation *
            inverseScalingRotationMatrix *
            scalingMatrix *
            scalingRotationMatrix *
            inverseScalingCenterTranslation *
            rotationCenterTranslation *
            rotationMatrix *
            inverseRotationCenterTranslation *
            finalTranslationMatrix;

        return transformationMatrix;
    }
    
    /// <summary>
    /// Reimplementation of SharpDX's Vector3.TransformCoordinate method
    /// </summary>
    public static Vector3 TransformCoordinate(in Vector3 coordinate, in Matrix4x4 matrix)
    {
        var vector = new Vector4(coordinate, 1);
        var transformed = Vector4.Transform(vector, matrix);
        return new Vector3(transformed.X, transformed.Y, transformed.Z) / transformed.W;
    }

    private const float FovEpsilon = 0.0001f;
    private const float MaxFov = MathF.PI - FovEpsilon;
    
    
    public static float DefaultCamFovDegrees = 45;
    public static readonly float DefaultCameraDistance = 1f / MathF.Tan(DefaultCamFovDegrees * MathF.PI / 360f);
}