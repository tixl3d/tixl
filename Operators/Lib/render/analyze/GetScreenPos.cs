namespace Lib.render.analyze;

[Guid("56779460-e3e2-401b-8836-f11917b55431")]
internal sealed class GetScreenPos : Instance<GetScreenPos>
{
    [Output(Guid = "caf0c601-8462-4f33-8983-f860214e4f24")]
    public readonly Slot<Command> UpdateCommand = new();

    [Output(Guid = "b0cfdbbd-e07a-411d-9095-b774a9dd80dd")]
    public readonly Slot<Vector3> Position = new();

    public GetScreenPos()
    {
        UpdateCommand.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        var worldPosition = LocalPosition.GetValue(context);
        
        // 1. World to View Space
        var worldPositionHomogeneous = new Vector4(worldPosition, 1.0f);
        var viewPosition = Vector4.Transform(worldPositionHomogeneous, context.ObjectToWorld * context.WorldToCamera);

        // 2. View to Clip Space
        var clipPosition = Vector4.Transform(viewPosition, context.CameraToClipSpace);

        // 3. Perspective Division
        var ndcPosition = new Vector3(
                                      clipPosition.X / clipPosition.W,
                                      clipPosition.Y / clipPosition.W,
                                      clipPosition.Z / clipPosition.W
                                     );

        // 4. Viewport Transformation to your desired range
        var aspectRatio = context.CameraToClipSpace.M22 / context.CameraToClipSpace.M11;
        
        var screenX = ndcPosition.X * aspectRatio;
        var screenY = ndcPosition.Y;
        var screenZ = SetDepthToZero.GetValue(context)
                      ? 0f
                      : ndcPosition.Z;

        var screenPosition = new Vector3(screenX, screenY, screenZ);
        var changed = Vector3.Distance(screenPosition, _lastPosition) > 0.0001f;

        if (changed)
        {
            _lastPosition = screenPosition;
            Position.Value = screenPosition;
            Position.DirtyFlag.ForceInvalidate();
        }
    }

    private Vector3 _lastPosition;
    
    [Input(Guid = "B597DC38-72BB-4929-B939-C33247EF98EB")]
    public readonly InputSlot<Vector3> LocalPosition = new();
    
    [Input(Guid = "9E55A7AD-2CF0-414A-8D7B-7D7C63B9FBB2")]
    public readonly InputSlot<bool> SetDepthToZero = new();
}