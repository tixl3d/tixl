namespace Types.Values;

[Guid("5880cbc3-a541-4484-a06a-0e6f77cdbe8e")]
public sealed class String : Instance<String>, IExtractedInput<string>
{
    [Output(Guid = "dd9d8718-addc-49b1-bd33-aac22b366f94")]
    public readonly Slot<string?> Result = new();

    public String()
    {
        Result.UpdateAction += UpdateString;
    }

    private void UpdateString(EvaluationContext context)
    {
        Result.Value = InputString.GetValue(context);
    }
        
    [Input(Guid = "ceeae47b-d792-471d-a825-49e22749b7b9")]
    public readonly InputSlot<string> InputString = new();
        
    public Slot<string> OutputSlot => Result!;  // Bypassing nullable is not ideal

    public void SetTypedInputValuesTo(string value, out IEnumerable<IInputSlot> changedInputs)
    {
        changedInputs = [InputString];
        InputString.TypedInputValue.Value = value;
    }
}