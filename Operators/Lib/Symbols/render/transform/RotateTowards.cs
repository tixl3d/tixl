using T3.Core.Utils;
using T3.Core.Utils.Geometry;

namespace Lib.render.transform;

[Guid("8373c170-a140-4ce4-b59b-47f42fb71700")]
internal sealed class RotateTowards : Instance<RotateTowards>
{
    [Output(Guid = "772609fe-ec92-43a9-b8c6-9055bbf0310b")]
    public readonly Slot<Command> Output = new();
        
    public RotateTowards()
    {
        Output.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        var targetMode = LookTowards.GetEnumValue<Modes>(context);
        var targetPos = AlternativeTarget.GetValue(context);
            
        Vector3 targetPosDx;
        if (targetMode == Modes.TowardsCamera)
        {
            //TransformCallback?.Invoke(this, context); // this this is stupid stupid
            Matrix4x4.Invert(context.WorldToCamera, out var camToWorld);
            targetPosDx = Vector4.Transform(new Vector4(0f, 0f, 0f, 1f), camToWorld).ToVector3();
        }
        else
        {
            targetPosDx = targetPos;
        }
            
        var sourcePos = Vector4.Transform( new Vector4(0,0,0,1), context.ObjectToWorld).ToVector3();
            
        var lookAt = GraphicsMath.LookAtRH(Vector3.Zero , -targetPosDx + sourcePos, VectorT3.Up);
        Matrix4x4.Invert(lookAt, out lookAt);

        var rotationOffset = RotationOffset.GetValue(context);
        var rotateOffset = Matrix4x4.CreateFromYawPitchRoll(
                                                            rotationOffset.Y.ToRadians(),
                                                            rotationOffset.X.ToRadians(),
                                                            rotationOffset.Z.ToRadians());

        lookAt = Matrix4x4.Multiply( rotateOffset, lookAt);
            
            
        var previousWorldTobject = context.ObjectToWorld;
        context.ObjectToWorld = Matrix4x4.Multiply(lookAt, context.ObjectToWorld);
        Command.GetValue(context);
        context.ObjectToWorld = previousWorldTobject;
    }

    [Input(Guid = "ae8b45e6-e72f-40fe-8ff9-34cd9fffc164")]
    public readonly InputSlot<Command> Command = new();
        
    [Input(Guid = "1ce44784-db0d-42ab-b00b-09a1ef2e9042")]
    public readonly InputSlot<Vector3> AlternativeTarget = new();
        
    [Input(Guid = "a000747f-578f-4367-bc47-e8bb56252043")]
    public readonly InputSlot<Vector3> RotationOffset = new();
        
        
    [Input(Guid = "CCD2CC62-AD7B-420A-95D4-243257291619", MappedType = typeof(Modes))]
    public readonly InputSlot<int> LookTowards = new();

    private enum Modes
    {
        TowardsCamera,
        TowardsPosition,
    }
}