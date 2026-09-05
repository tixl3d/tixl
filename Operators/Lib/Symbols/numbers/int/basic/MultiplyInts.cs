namespace Lib.numbers.@int.basic;

[Guid("318072b7-9d7b-4ad3-b163-484c3a987895")]
internal sealed class MultiplyInts : Instance<MultiplyInts>
{
    [Output(Guid = "F4E00844-C5A7-4F57-9D75-14EE2143EB77")]
    public readonly Slot<int> Result = new();

    public MultiplyInts()
    {
        Result.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        Result.Value = 1;
        var connectedCount = 0;
        var total = 1;
        foreach (var input in InputValues.GetCollectedTypedInputs())
        {
            total *= input.GetValue(context);
            connectedCount++;
        }

        Result.Value = connectedCount == 0 ? 0 : total;
        
        InputValues.DirtyFlag.Clear();
    }

    [Input(Guid = "20BAC17B-0C5E-4A35-8FC1-7C20D5E101D2")]
    public readonly MultiInputSlot<int> InputValues = new();
}