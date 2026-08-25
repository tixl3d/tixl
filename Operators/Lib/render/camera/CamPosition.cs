
using T3.Core.Animation;

namespace Lib.render.camera;

[Guid("2ed26fb7-fe66-4ed6-8b8d-230d87ae5c77")]
internal sealed class CamPosition : Instance<CamPosition>
{
        
    [Output(Guid = "51BEC9E0-2E6E-49B6-885C-2AA0F3AC37E3")]
    public readonly Slot<Command> Command = new();
        
    [Output(Guid = "20E33049-C2FA-4C9F-8607-318B279B72EC")]
    public readonly Slot<Vector3> Position = new();
        
    [Output(Guid = "5A38F00B-342A-46E5-9410-FBF403F2313E")]
    public readonly Slot<Vector3> Direction = new();

    [Output(Guid = "A2213E94-0FE5-4CA6-A13D-9A265D50E707")]
    public readonly Slot<float> AspectRatio = new();

        
    public CamPosition()
    {
        Command.UpdateAction += Update;
        Position.UpdateAction += Update;
        Direction.UpdateAction += Update;
        AspectRatio.UpdateAction += Update;
        // Note: We only want to call update for the execution path. 
    }

    private void Update(EvaluationContext context)
    {
        if (_lastUpdateFrame >= Playback.FrameCount)
        {
            return;
        }

        _lastUpdateFrame = Playback.FrameCount;
        var worldToCamera = context.WorldToCamera;
        var cameraToClipSpace = context.CameraToClipSpace;

        if (CamReference.GetValue(context) is ICamera camReference)
        {
            worldToCamera = camReference.WorldToCamera;
            cameraToClipSpace = camReference.CameraToClipSpace;
        }
        Matrix4x4.Invert(worldToCamera, out var camToWorld);
            
        var pos = Vector4.Transform(new Vector4(0f, 0f, 0f, 1f), camToWorld);
        Position.Value = new Vector3(pos.X, pos.Y, pos.Z);
            
        var dir = pos -Vector4.Transform(new Vector4(0f, 0f, 1f, 1f), camToWorld);
        Direction.Value = new Vector3(dir.X, dir.Y, dir.Z);

        float aspect = cameraToClipSpace.M22 / cameraToClipSpace.M11;
        AspectRatio.Value = aspect;
            
        Command.DirtyFlag.Clear();
        Position.DirtyFlag.Clear();
        Direction.DirtyFlag.Clear();
        AspectRatio.DirtyFlag.Clear();
    }

    private int _lastUpdateFrame = -1;
    
    [Input(Guid = "51A79F01-9B63-4217-B396-10E3C6F22C80")]
    public readonly InputSlot<object> CamReference = new();
}