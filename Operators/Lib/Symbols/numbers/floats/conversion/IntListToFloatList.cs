namespace Lib.numbers.floats.conversion;

[Guid("0805764a-29d6-407a-99b4-5df62fe2a55d")]
internal sealed class IntListToFloatList : Instance<IntListToFloatList>
{

        [Input(Guid = "660de680-13a7-4902-8e48-1df5435ae979")]
        public readonly InputSlot<List<int>> IntList = new InputSlot<List<int>>();

        [Output(Guid = "1fb17816-fe44-4803-9ee0-3905e6efb765")]
        public readonly Slot<List<float>> Result = new Slot<List<float>>();
    public IntListToFloatList()
    {
        Result.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        var intValues = IntList.GetValue(context);
        if (intValues == null)
        {
            // Provide an empty list to downstream nodes if input is null
            Result.Value = new List<float>();
            return;
        }

        // Use LINQ's .Select() to efficiently convert each integer to a float.
        // The cast (float) is implicit, but it can be written explicitly for clarity.
        Result.Value = intValues.Select(i => (float)i).ToList();
    }

}