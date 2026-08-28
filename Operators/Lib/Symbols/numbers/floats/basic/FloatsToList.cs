namespace Lib.numbers.floats.basic;

[Guid("5c5d855c-3167-40a3-b4c3-e7b27b0d61cf")]
internal sealed class FloatsToList : Instance<FloatsToList>
{
    [Output(Guid = "{9140BD66-3257-498A-80CD-C516C128F7E5}")]
    public readonly Slot<List<float>> Result = new(new List<float>(20));

    public FloatsToList()
    {
        Result.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        Result.Value.Clear();
        foreach (var input in Input.GetCollectedTypedInputs())
        {
            Result.Value.Add(input.GetValue(context));
        }
    }
        
    [Input(Guid = "{15874509-FABB-44CA-93A1-858FF95FB5F5}")]
    public readonly MultiInputSlot<float> Input = new();
}