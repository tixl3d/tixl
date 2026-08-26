namespace Lib.render.transform;

[Guid("a6e12383-09b6-4bbd-a4bb-8908598c3409")]
internal sealed class RotateAroundAxis : Instance<RotateAroundAxis>
{
    [Output(Guid = "1974f1bc-c3e3-4ea8-8b1e-72e9e1032a68")]
    public readonly Slot<Command> Output = new();
        

    public RotateAroundAxis()
    {
        Output.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        var vector3 = Axis.GetValue(context);
        var angle = Angle.GetValue(context) / 180 * MathF.PI;
            
        Matrix4x4 m = Matrix4x4.CreateFromAxisAngle(vector3, angle);
            
        var previousWorldTobject = context.ObjectToWorld;
        context.ObjectToWorld = Matrix4x4.Multiply(m, context.ObjectToWorld);
        Command.GetValue(context);
        context.ObjectToWorld = previousWorldTobject;
    }

    [Input(Guid = "7fee8751-6784-49d2-9862-c8fd3759e82f")]
    public readonly InputSlot<Command> Command = new();
        
    [Input(Guid = "12E6EA95-F7E9-450B-815F-5891E9CEEAF7")]
    public readonly InputSlot<float> Angle = new();
        
    [Input(Guid = "28e60531-4d5a-4de1-9f8b-5e885220292c")]
    public readonly InputSlot<Vector3> Axis = new();
        
}