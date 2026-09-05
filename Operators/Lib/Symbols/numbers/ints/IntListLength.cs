namespace Lib.numbers.ints;

[Guid("9a90890f-f5e2-4f96-9d6d-f5229f8b7531")]
internal sealed class IntListLength :Instance<IntListLength>{
    [Output(Guid = "35BB296E-4056-4C32-91BF-86F63183FDA7")]
    public readonly Slot<int> Length = new();

    public IntListLength()
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

    [Input(Guid = "6ACFD752-DBF6-409C-99A5-25FB684FE830")]
    public readonly InputSlot<List<int>> Input = new();
}