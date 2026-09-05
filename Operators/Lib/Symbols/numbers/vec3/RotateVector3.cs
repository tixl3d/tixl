namespace Lib.numbers.vec3;

[Guid("ce7c2103-3669-4c7a-ba61-a10428b9d467")]
internal sealed class RotateVector3 : Instance<RotateVector3>
{
    [Output(Guid = "473ef336-d525-4d33-921b-d4cdaf11c73b")]
    public readonly Slot<Vector3> Result = new();

        
    public RotateVector3()
    {
        Result.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        var vec = VectorA.GetValue(context);
        var axis = Axis.GetValue(context);
        var angle = Angle.GetValue(context) / 180 * MathF.PI;
            
        var m = Matrix4x4.CreateFromAxisAngle(axis, angle);
        Result.Value = Vector3.TransformNormal(vec, m) * Scale.GetValue(context);
    }
        
    [Input(Guid = "56229f73-cbe2-4279-a659-d70d32e0df59")]
    public readonly InputSlot<Vector3> VectorA = new();

    [Input(Guid = "991f39df-e4c1-4d43-aa5b-09b223aa14d2")]
    public readonly InputSlot<float> Angle = new();
        
    [Input(Guid = "351B4E46-397D-41DD-AD89-6CE200E36E2D")]
    public readonly InputSlot<Vector3> Axis = new();
        
    [Input(Guid = "F23112F4-265A-4CCA-9EEA-D701D9FE447F")]
    public readonly InputSlot<float> Scale = new();

}