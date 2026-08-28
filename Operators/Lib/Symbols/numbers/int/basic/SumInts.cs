namespace Lib.numbers.@int.basic;

[Guid("bc7594df-68f0-4678-aec7-523474b3b36a")]
internal sealed class SumInts : Instance<SumInts>
{
    [Output(Guid = "FF5F8839-527F-419E-A140-B9F03033C509")]
    public readonly Slot<int> Result = new();

    public SumInts()
    {
        Result.UpdateAction = Update;
    }

    private void Update(EvaluationContext context)
    {
        Result.Value = 0;
        var connectedCount = 0;
        foreach (var input in InputValues.GetCollectedTypedInputs())
        {
            Result.Value += input.GetValue(context);
            connectedCount++;
        }

        if (connectedCount == 0)
        {
            Result.Value = InputValues.GetValue(context);
        }
            
        InputValues.DirtyFlag.Clear();
    }

    [Input(Guid = "F61859A4-FC6B-47DC-82F1-2EB7142B6B36")]
    public readonly MultiInputSlot<int> InputValues = new();
}