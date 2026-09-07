using System.Runtime.InteropServices;

namespace T3.Core.Rendering;

[StructLayout(LayoutKind.Explicit, Size = 4 * 4 * 4 * 10)]
public struct TransformBufferLayout
{
    public TransformBufferLayout(Matrix4x4 cameraToClipSpace, Matrix4x4 worldToCamera, Matrix4x4 objectToWorld)
    {
        Matrix4x4.Invert(cameraToClipSpace, out var clipSpaceToCamera);
        Matrix4x4.Invert(worldToCamera, out var cameraToWorld);
        Matrix4x4.Invert(objectToWorld, out var worldToObject);

        WorldToClipSpace = Matrix4x4.Multiply(worldToCamera, cameraToClipSpace);
        ClipSpaceToWorld = Matrix4x4.Multiply(clipSpaceToCamera, cameraToWorld);
        ObjectToCamera = Matrix4x4.Multiply(objectToWorld, worldToCamera);
        ObjectToClipSpace = Matrix4x4.Multiply(ObjectToCamera, cameraToClipSpace);

        CameraToClipSpace = Matrix4x4.Transpose(cameraToClipSpace);
        // transpose all as mem layout in hlsl constant buffer is row based
        CameraToClipSpace = Matrix4x4.Transpose(cameraToClipSpace);
        ClipSpaceToCamera = Matrix4x4.Transpose(clipSpaceToCamera);
        WorldToCamera = Matrix4x4.Transpose(worldToCamera);
        CameraToWorld = Matrix4x4.Transpose(cameraToWorld);
        WorldToClipSpace = Matrix4x4.Transpose(WorldToClipSpace);
        ClipSpaceToWorld = Matrix4x4.Transpose(ClipSpaceToWorld);
        ObjectToWorld = Matrix4x4.Transpose(objectToWorld);
        WorldToObject = Matrix4x4.Transpose(worldToObject);
        ObjectToCamera = Matrix4x4.Transpose(ObjectToCamera);
        ObjectToClipSpace = Matrix4x4.Transpose(ObjectToClipSpace);
    }

    [FieldOffset(0)]
    public Matrix4x4 CameraToClipSpace;

    [FieldOffset(64)]
    public Matrix4x4 ClipSpaceToCamera;

    [FieldOffset(128)]
    public Matrix4x4 WorldToCamera;

    [FieldOffset(192)]
    public Matrix4x4 CameraToWorld;

    [FieldOffset(256)]
    public Matrix4x4 WorldToClipSpace;

    [FieldOffset(320)]
    public Matrix4x4 ClipSpaceToWorld;

    [FieldOffset(384)]
    public Matrix4x4 ObjectToWorld;

    [FieldOffset(448)]
    public Matrix4x4 WorldToObject;

    [FieldOffset(512)]
    public Matrix4x4 ObjectToCamera;

    [FieldOffset(576)]
    public Matrix4x4 ObjectToClipSpace;
}