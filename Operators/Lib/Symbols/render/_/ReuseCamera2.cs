namespace Lib.render.@_;

[Guid("1de05a51-4a22-44cd-a584-6f1ae1c0e8d1")]
internal sealed class ReuseCamera2 : Instance<ReuseCamera2>
{
    [Output(Guid = "04c676d4-012b-44ef-b3b2-6b7d7f09d490")]
    public readonly Slot<Command> Output = new();
        
    public ReuseCamera2()
    {
        Output.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        var obj = CameraReference.GetValue(context);
        if (obj == null)
        {
            Log.Warning("Camera reference is undefined", this);
            return;
        }

        if (obj is not ICamera camera)
        {
            Log.Warning($"Can't GetCamProperties from invalid reference type: {obj}", this);
            return;
        }                   
            
        // Set properties and evaluate sub tree
        var prevWorldToCamera = context.WorldToCamera;
        var prevCameraToClipSpace = context.CameraToClipSpace;
            
        context.WorldToCamera = camera.WorldToCamera;
        context.CameraToClipSpace = camera.CameraToClipSpace;
            
        Command.GetValue(context);
            
        context.CameraToClipSpace = prevCameraToClipSpace;
        context.WorldToCamera = prevWorldToCamera;
    }

    [Input(Guid = "dfc3c909-ae13-4364-b9db-c594dad1bee4")]
    public readonly InputSlot<Command> Command = new();
        
    [Input(Guid = "8cac9f22-c6a1-4ced-9733-fe366eafb5c4")]
    public readonly InputSlot<Object> CameraReference = new();
}