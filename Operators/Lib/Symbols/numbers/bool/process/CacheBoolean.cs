using T3.Core.Utils;

namespace Lib.numbers.@bool.process;

[Guid("6502c40f-a38e-4bd9-8dbf-90e35079572d")]
internal sealed class CacheBoolean : Instance<CacheBoolean>
{
    [Output(Guid = "b0fa6fed-3478-493a-98b8-f4e680dc936e")]
    public readonly Slot<bool> Result = new();
        
    public CacheBoolean()
    {
        Result.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {

        if (!Value.HasInputConnections)
        {
            Result.Value = Value.GetValue(context);
            return;
        }

        if (Value.TryGetFirstConnection(out var connection)
            && connection is Slot<bool> boolValue)
        {
            Result.Value = boolValue.Value;
        }
    }
    

    [Input(Guid = "37e95081-96fb-4374-840f-3ecfee430181")]
    public readonly InputSlot<bool> Value = new();

    
}