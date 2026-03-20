namespace Lib.render.@_;

[Guid("68e2031d-20ac-4a2e-a407-fc2cbd76aad2")]
internal sealed class ApplyCamTransform :Instance<ApplyCamTransform>{
    [Output(Guid = "928cca95-8d55-4c42-99d3-2f73747a508f")]
    public readonly Slot<Command> Output = new();

    public ApplyCamTransform()
    {
        Output.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        var obj = CamReference.GetValue(context);
        if (obj == null)
        {
            Log.Warning("Camera reference is undefined", this);
            return;
        }

        if (obj is not ICamera camera)
        {
            Log.Warning("Can't GetCamProperties from invalid reference type", this);
            return;
        }                   
        
        var camToClipSpace = camera.CameraToClipSpace;
        
        var worldToCam = camera.WorldToCamera;
        
        Matrix4x4.Invert(worldToCam, out var camToWorld);
        
        var scaleX = 1f / camToClipSpace.M11;
        var scaleY = 1f / camToClipSpace.M22;
        var camScale = Matrix4x4.CreateScale(scaleX, scaleY, 1f);
        // Apply scale in camera space before transforming to world space.
        var camToWorldScaled = Matrix4x4.Multiply(camScale, camToWorld);
        
        // Apply and evaluate
        var previousObjectToWorld = context.ObjectToWorld;
        context.ObjectToWorld = Matrix4x4.Multiply(camToWorldScaled, context.ObjectToWorld);
        Command.GetValue(context);
        context.ObjectToWorld = previousObjectToWorld;
    }

    [Input(Guid = "088fb8fd-df11-4de1-88a8-73cb8077eb9b")]
    public readonly InputSlot<Command> Command = new();

    [Input(Guid = "46C15ED4-D05A-4CAF-AEF7-80667D98FC3D")]
    public readonly InputSlot<object> CamReference = new();
}