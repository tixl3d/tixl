namespace Lib.numbers.@bool.convert;

[Guid("9db2fcbf-54b9-4222-878b-80d1a0dc6edf")]
internal sealed class BoolToFloat : Instance<BoolToFloat>
{
    [Output(Guid = "F0321A54-E844-482F-A161-7F137ABC54B0")]
    public readonly Slot<float> Result = new();
        
    public BoolToFloat()
    {
        Result.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        var trueValue = ForTrue.GetValue(context);
        var falseValue = ForFalse.GetValue(context);
        Result.Value = BoolValue.GetValue(context) 
                           ? trueValue 
                           : falseValue;
    }
        
    [Input(Guid = "253b9ae4-fac5-4641-bf0c-d8614606a840")]
    public readonly InputSlot<bool> BoolValue = new();
        
    [Input(Guid = "24FFA0A7-9195-4B38-9C88-37CF4C3AFC36")]
    public readonly InputSlot<float> ForFalse = new();

    [Input(Guid = "0A53A4FF-4DFB-455A-B70B-0D7EED5E5F22")]
    public readonly InputSlot<float> ForTrue = new();
}