// using System.Numerics;
// using T3.Core.DataTypes;
// using T3.Core.Logging;
// using T3.Core.Operator;
// using T3.Core.Operator.Attributes;
// using T3.Core.Operator.Slots;
// using T3.Core.Utils.Geometry;
// using Vector3 = System.Numerics.Vector3;
// using Vector4 = System.Numerics.Vector4;

namespace Lib.render.camera;

[Guid("f0c38f0f-36ef-4562-a993-96a175cd03cd")]
public class CurrentCamMatrices : Instance<CurrentCamMatrices>
{
    [Output(Guid = "3a6ab1e9-98d0-4a51-87e2-2436f7d5fba1", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<Command> Command = new();

    [Output(Guid = "79C3C087-1D6A-4AA8-AB60-10BC2B2E3E8D", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<Vector4[]> WorldToClipSpace = new();

    public CurrentCamMatrices()
    {
        Command.UpdateAction = Update;
        WorldToClipSpace.UpdateAction = Update;
    }

    private void Update(EvaluationContext context)
    {
        var worldToCam = context.WorldToCamera;
        var camToClip = context.CameraToClipSpace;
        var worldToClip = worldToCam * camToClip;
        worldToClip = Matrix4x4.Transpose(worldToClip);

        WorldToClipSpace.Value = new[]
                                     {
                                         new Vector4(worldToClip.M11, worldToClip.M12, worldToClip.M13, worldToClip.M14),
                                         new Vector4(worldToClip.M21, worldToClip.M22, worldToClip.M23, worldToClip.M24),
                                         new Vector4(worldToClip.M31, worldToClip.M32, worldToClip.M33, worldToClip.M34),
                                         new Vector4(worldToClip.M41, worldToClip.M42, worldToClip.M43, worldToClip.M44),
                                     };
    }
}