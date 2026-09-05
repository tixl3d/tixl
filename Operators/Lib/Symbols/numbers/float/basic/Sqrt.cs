namespace Lib.numbers.@float.basic;

[Guid("24b56330-b9c5-4454-a398-0500b0422ce1")]
internal sealed class Sqrt : Instance<Sqrt>
{
    [Output(Guid = "915a7042-4bdc-4238-a59e-04eed3020f12")]
    public readonly Slot<float> Result = new();

    public Sqrt()
    {
        Result.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        var v = Value.GetValue(context);
        Result.Value = MathF.Sqrt(v);
    }
        
    [Input(Guid = "a91ef6c9-4d5d-4fb5-9e83-c2f4fbb9769f")]
    public readonly InputSlot<float> Value = new();
}