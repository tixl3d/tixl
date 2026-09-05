namespace Lib.numbers.@float.adjust;


[Guid("cd9f16bc-5306-458c-aff8-1cca3bb24469")]
internal sealed class Abs : Instance<Abs>
{
    [Output(Guid = "29ed2f76-d86e-43b9-aa2b-1712823baa29")]
    public readonly Slot<float> Result = new();

    public Abs()
    {
        Result.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        var v = Value.GetValue(context);
            
        Result.Value = v > 0 ? v: (-1*v);
            
    }
        
    [Input(Guid = "9ca014a9-5abc-4d83-ac30-bb85c5d913b7")]
    public readonly InputSlot<float> Value = new();
}