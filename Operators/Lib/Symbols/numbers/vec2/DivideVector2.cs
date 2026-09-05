namespace Lib.numbers.vec2;

[Guid("c20452bc-ff82-4684-bc49-77fe59d2df46")]
internal sealed class DivideVector2 : Instance<DivideVector2>
{
    [Output(Guid = "ce997e45-80f7-4a19-bb43-5ee125d09f3b")]
    public readonly Slot<Vector2> Result = new Slot<Vector2>();
        
    public DivideVector2()
    {
        Result.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        var a = A.GetValue(context);
        var b = B.GetValue(context);
        var u = UniformScale.GetValue(context);
        //Result.Value = new System.Numerics.Vector2(a.X * b.X, a.Y * b.Y);
        Result.Value = (a / b) / u;
    }
        


    [Input(Guid = "d21dbb6f-db32-4a79-80a0-ad36af9306e0")]
    public readonly InputSlot<Vector2> A = new InputSlot<Vector2>();

    [Input(Guid = "cab9a7a3-e32d-42ea-a6b4-247b5ed79b5f")]
    public readonly InputSlot<Vector2> B = new InputSlot<Vector2>();
        
    [Input(Guid = "65B844DE-7ED6-4D3A-9ECD-E499F1523FB5")]
    public readonly InputSlot<float> UniformScale = new();
        
}