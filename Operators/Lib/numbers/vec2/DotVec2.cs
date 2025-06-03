namespace Lib.numbers.vec2;

[Guid("0db4fd9b-7996-47e1-a4cc-debfd04a18f4")]
internal sealed class DotVec2 : Instance<DotVec2>
{
    [Input(Guid = "bac6bef8-1bf3-4f74-9198-c06dec48610c")]
    public readonly InputSlot<Vector2> Input1 = new();
    
    [Input(Guid = "3f28ec3a-e7ab-41f5-95d4-e3cc5b6357f9")]
    public readonly InputSlot<Vector2> Input2 = new();

    [Output(Guid = "18591f1a-87f0-49fc-8608-b30b98364b67")]
    public readonly Slot<float> Result = new();

    public DotVec2()
    {
        Result.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        Result.Value = Vector2.Dot(Input1.GetValue(context), Input2.GetValue(context));
    }
}