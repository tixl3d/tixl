using T3.Core.Utils;

namespace Lib.numbers.vec3;

[Guid("a40cb2a4-5e4c-41ef-b70d-40fa04adafbb")]
internal sealed class LerpVec3 : Instance<LerpVec3>
{
    [Output(Guid = "D5116FBE-1D04-48CE-B948-56EF184A5FD6")]
    public readonly Slot<Vector3> Result = new();

    public LerpVec3()
    {
        Result.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        var f = F.GetValue(context);
        if (Clamp.GetValue(context))
        {
            f = f.Clamp(0, 1);
        }
        Result.Value =  Vector3.Lerp(A.GetValue(context), B.GetValue(context), f);
    }


    [Input(Guid = "A7FE900A-958D-4F79-9DF7-D30B31BFB71B")]
    public readonly InputSlot<Vector3> A = new();

    [Input(Guid = "41159B82-5215-4915-B433-1F2B3F18547F")]
    public readonly InputSlot<Vector3> B = new();
        
    [Input(Guid = "86af9964-8ee4-4d94-b96f-f8b61e821563")]
    public readonly InputSlot<float> F = new();
        
    [Input(Guid = "f79d512c-b066-49d1-96c5-bf320f0082a6")]
    public readonly InputSlot<bool> Clamp = new();

        
}