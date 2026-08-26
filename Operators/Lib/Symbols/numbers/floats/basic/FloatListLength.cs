namespace Lib.numbers.floats.basic;

[Guid("affe33d0-b86a-4327-aae7-880553b4b7dc")]
internal sealed class FloatListLength : Instance<FloatListLength>
{
    [Output(Guid = "f74b00d3-5585-48bd-be56-f0abfe4b6665")]
    public readonly Slot<int> Length = new();

    public FloatListLength()
    {
        Length.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        var list = Input.GetValue(context);
        if (list == null)
        {
            Length.Value = 0;
            return;
        }
            
        Length.Value = list.Count;
    }

    [Input(Guid = "237CDC7A-053C-4D60-8370-5EEE2B5F611E")]
    public readonly InputSlot<List<float>> Input = new();
}