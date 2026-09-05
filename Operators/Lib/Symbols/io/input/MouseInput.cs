using T3.Core.Utils;
using T3.Core.Utils.Geometry;

namespace Lib.io.input;

[Guid("eff2ffff-dc39-4b90-9b1c-3c0a9a0108c6")]
internal sealed class MouseInput : Instance<MouseInput>
{
    [Output(Guid = "CDC87CE1-FAB8-4B96-9137-9965E064BFA3", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<Vector2> Position = new();

    [Output(Guid = "78CAABCF-9C3B-4E50-9D80-BDCBABAEB003", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<bool> IsLeftButtonDown = new();

    [Output(Guid = "BE90EED3-26BF-4DC3-9771-073C04D359BC", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<Vector3> Position3d = new();

    public MouseInput()
    {
        Position.UpdateAction += Update;
        Position3d.UpdateAction += Update;
        IsLeftButtonDown.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        var mode = OutputMode.GetEnumValue<OutputModes>(context);
        var scale = Scale.GetValue(context);
        var aspectRatio = (float)context.RequestedResolution.Width / context.RequestedResolution.Height;

        var lastPosition = T3.SystemUi.MouseInput.LastPosition;

        switch (mode)
        {
            case OutputModes.Normalized:
                Position.Value = lastPosition;
                Position3d.Value = new Vector3(lastPosition.X, lastPosition.Y, 0);
                break;
            case OutputModes.SignedPosition:
                Position.Value = (lastPosition - new Vector2(0.5f, 0.5f)) * new Vector2(aspectRatio, -1) * scale;
                Position3d.Value = new Vector3(lastPosition.X, lastPosition.Y, 0);
                break;
            case OutputModes.OnWorldXYPlane:
            case OutputModes.OnWorldFloorPlane:
            {
                Matrix4x4.Invert(context.CameraToClipSpace, out var clipSpaceToCamera);
                Matrix4x4.Invert(context.WorldToCamera, out var cameraToWorld);
                var clipSpaceToWorld = clipSpaceToCamera * cameraToWorld;

                var posInClip = (lastPosition - new Vector2(0.5f, 0.5f)) * new Vector2(2, -2);
                var posInWorld = GraphicsMath.TransformCoordinate(new Vector3(posInClip, 0f), clipSpaceToWorld);
                var targetInWorld = GraphicsMath.TransformCoordinate(new Vector3(posInClip, 1f), clipSpaceToWorld);
                var ray = new Ray(posInWorld, targetInWorld - posInWorld);

                var planeNormal = mode == OutputModes.OnWorldXYPlane ? Vector3.UnitZ : Vector3.UnitY;
                var xyPlane = PlaneExtensions.CreateFromPointAndNormal(Vector3.Zero, planeNormal);
                if (xyPlane.Intersects(ray, out Vector3 intersectionPoint))
                {
                    Position.Value = new Vector2(intersectionPoint.X, intersectionPoint.Y);
                    Position3d.Value = intersectionPoint;
                }

                break;
            }
            default:
                throw new ArgumentOutOfRangeException(nameof(mode));
        }

        IsLeftButtonDown.Value = T3.SystemUi.MouseInput.IsLeftButtonDown;
        Position3d.DirtyFlag.Clear();
        Position.DirtyFlag.Clear();
        IsLeftButtonDown.DirtyFlag.Clear();
    }

    [Input(Guid = "49775CC2-35B7-4C9F-A502-59FE8FBBE2A7")]
    public readonly InputSlot<bool> DoUpdate = new();

    [Input(Guid = "1327525C-716C-43E4-A5D1-58CF35440462", MappedType = typeof(OutputModes))]
    public readonly InputSlot<int> OutputMode = new();

    [Input(Guid = "6B1E81BD-2430-4439-AD0C-859B2433C38B")]
    public readonly InputSlot<float> Scale = new();

    private enum OutputModes
    {
        Normalized,
        SignedPosition,
        OnWorldXYPlane,
        OnWorldFloorPlane
    }
}