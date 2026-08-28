namespace Lib.render.camera;

[Guid("1a8d2a8d-d189-472f-bab3-d645a63c7aff")]
internal sealed class ShiftCamera : Instance<ShiftCamera>
{
    [Output(Guid = "4525b575-31ee-4e6a-9f9b-4b0e3127e493")]
    public readonly Slot<Command> Output = new();
        
    public ShiftCamera()
    {
        Output.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
            
        //var pivot = Pivot.GetValue(context);
        var s = Scale.GetValue(context) * UniformScale.GetValue(context);
        // var r = Rotation.GetValue(context);
        // float yaw = MathUtil.DegreesToRadians(r.Y);
        // float pitch = MathUtil.DegreesToRadians(r.X);
        // float roll = MathUtil.DegreesToRadians(r.Z);
        var t = Translation.GetValue(context);
        // var objectToParentObject = Matrix.Transformation(
        //                                                  scalingCenter: pivot.ToSharpDx(), 
        //                                                  scalingRotation: Quaternion.Identity, 
        //                                                  scaling: s.ToSharpDx(), 
        //                                                  rotationCenter: pivot.ToSharpDx(),
        //                                                  rotation: Quaternion.RotationYawPitchRoll(yaw, pitch, roll), 
        //                                                  translation: t.ToSharpDx());
            
        var previous = context.CameraToClipSpace;
        var newCamToClip = previous;
        newCamToClip.M31 += t.X;
        newCamToClip.M32 += t.Y;
        newCamToClip.M33 += (float)((double)t.Z/1000.0);
        context.CameraToClipSpace = newCamToClip;
            
        //context.ObjectToWorld = Matrix.Multiply(objectToParentObject, context.ObjectToWorld);
        Command.GetValue(context);
            
            
        context.CameraToClipSpace = previous;
    }
        
        

    [Input(Guid = "b5df04f5-c648-4436-ae1c-b633fc5c16fa")]
    public readonly InputSlot<Command> Command = new();
        
    [Input(Guid = "3f580113-4117-4f87-9a7e-c151f26fa1ed")]
    public readonly InputSlot<Vector3> Translation = new();
        
    [Input(Guid = "b48ff02f-f8cf-4daf-9c57-5c74c491b05a")]
    public readonly InputSlot<Vector3> Scale = new();

    [Input(Guid = "22e755fd-235f-40ac-b9c3-4ae948164870")]
    public readonly InputSlot<float> UniformScale = new();
        
    // [Input(Guid = "dede4de5-a9e8-4bf1-b8f8-a99ff4263d84")]
    // public readonly InputSlot<System.Numerics.Vector3> Pivot = new();
}