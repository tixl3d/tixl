namespace Lib.numbers.@bool.logic;

[Guid("51648ecd-05ee-40b3-b562-8518ada70918")]
internal sealed class Not : Instance<Not>
{
    [Output(Guid = "0274f62a-b3a2-49e3-a486-043ee71f366b")]
    public readonly Slot<bool> Result = new();
        
    public Not()
    {
        Result.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        Result.Value = !BoolValue.GetValue(context);
    }
        
    [Input(Guid = "e5322b67-9c56-4afe-a398-79294858acc0")]
    public readonly InputSlot<bool> BoolValue = new();
}