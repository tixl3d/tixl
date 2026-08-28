namespace Lib.numbers.@bool.convert;

[Guid("cd43942a-887e-4e34-bc54-0c2e5e8bc2af")]
internal sealed class BoolToInt : Instance<BoolToInt>
{
    [Output(Guid = "b0cfa6f9-3c3d-4499-b21a-5904d1cb3bd7")]
    public readonly Slot<int> Result = new();
        
    public BoolToInt()
    {
        Result.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        var valueForTrue = ResultForTrue.GetValue(context);
        var valueForFalse = ResultForFalse.GetValue(context);
            
        Result.Value = BoolValue.GetValue(context) 
                           ? valueForTrue
                           : valueForFalse;
    }
        
    [Input(Guid = "c644165f-3901-4dbf-8091-05f958e668e5")]
    public readonly InputSlot<bool> BoolValue = new();
        
    [Input(Guid = "9B64F287-D14A-493E-A1C7-DCBCDC703849")]
    public readonly InputSlot<int> ResultForFalse = new();
        
    [Input(Guid = "CBBB6B8A-0DC9-4A85-8ABC-E4C9C1C9C8BE")]
    public readonly InputSlot<int> ResultForTrue = new();


}