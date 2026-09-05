namespace Types.Values;

[Guid("cc07b314-4582-4c2c-84b8-bb32f59fc09b")]
public sealed class IntValue : Instance<IntValue>, IExtractedInput<int>
{
    [Output(Guid = "8A65B34B-40BE-4DBF-812C-D4C663464C7F")]
    public readonly Slot<int> Result = new();

    public IntValue()
    {
        Result.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        Result.Value = Int.GetValue(context);
    }
        
    [Input(Guid = "4515C98E-05BC-4186-8773-4D2B31A8C323")]
    public readonly InputSlot<int> Int = new();

    public Slot<int> OutputSlot => Result;

    public void SetTypedInputValuesTo(int value, out IEnumerable<IInputSlot> changedInputs)
    {
        changedInputs = new[] { Int };
        Int.TypedInputValue.Value = value;
    }
}