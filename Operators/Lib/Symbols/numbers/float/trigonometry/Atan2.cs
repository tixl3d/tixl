namespace Lib.numbers.@float.trigonometry;

[Guid("7636bb3c-9f12-4323-b9be-e05f0eb561c5")]
internal sealed class Atan2 : Instance<Atan2>
{
    [Output(Guid = "ce5e7140-442d-4541-8596-c24a58e74111")]
    public readonly Slot<float> Result = new();

    public Atan2()
    {
        Result.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        var v = Vector.GetValue(context);
        Result.Value = MathF.Atan2(v.X, v.Y);
    }
        
    [Input(Guid = "00770DC9-40A7-421C-A2FB-2D6BC1845387")]
    public readonly InputSlot<Vector2> Vector = new();

}