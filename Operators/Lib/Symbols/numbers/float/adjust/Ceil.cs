namespace Lib.numbers.@float.adjust;


[Guid("7129684e-bf84-4c8b-996e-baba68094585")]
internal sealed class Ceil : Instance<Ceil>
{
    [Output(Guid = "bc97d61a-0e23-469c-83d9-4a687c8e0016")]
    public readonly Slot<float> Result = new();

    public Ceil()
    {
        Result.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        var v = Value.GetValue(context);
        Result.Value = (float)Math.Ceiling(v);
    }
        
    [Input(Guid = "a0f6f860-9561-4af8-9660-05cfb2f995a0")]
    public readonly InputSlot<float> Value = new();
}