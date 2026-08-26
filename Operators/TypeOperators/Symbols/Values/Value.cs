namespace Types.Values;

[Guid("5d7d61ae-0a41-4ffa-a51d-93bab665e7fe")]
public sealed class Value : Instance<Value>, IExtractedInput<float>
{
    [Output(Guid = "f83f1835-477e-4bb6-93f0-14bf273b8e94")]
    public readonly Slot<float> Result = new();

    public Value()
    {
        Result.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        Result.Value = Float.GetValue(context);
    }
        
    [Input(Guid = "7773837e-104a-4b3d-a41f-cadbd9249af2")]
    public readonly InputSlot<float> Float = new();

    public Slot<float> OutputSlot => Result;

    public void SetTypedInputValuesTo(float value, out IEnumerable<IInputSlot> changedInputs)
    {
        changedInputs = new[] { Float };
        Float.TypedInputValue.Value = value;
    }
}