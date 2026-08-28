namespace Lib.numbers.@bool.combine;

[Guid("1446e61e-7f68-4655-99c8-5be390f64851")]
internal sealed class Any : Instance<Any>
{
    [Output(Guid = "9B2B6339-EA13-4A8B-8223-1C95266E59F1")]
    public readonly Slot<bool> Result = new();

    public Any()
    {
        Result.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        var result = false;
            
        foreach (var input in Input.GetCollectedTypedInputs())
        {
            result |= input.GetValue(context);
        }
            
        Result.Value = result;
    }

    [Input(Guid = "374AD549-676B-4BD0-AE6A-421892B92BDB")]
    public readonly MultiInputSlot<bool> Input = new();
}