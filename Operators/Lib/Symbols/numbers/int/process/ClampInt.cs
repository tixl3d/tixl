using T3.Core.Utils;

namespace Lib.numbers.@int.process;

[Guid("5f734c25-9f1a-436c-b56c-7e0a1e07fdda")]
internal sealed class ClampInt : Instance<ClampInt>
{
    [Output(Guid = "E6AAE72F-8C22-4133-BA0D-C3635751D715")]
    public readonly Slot<int> Result = new();

    public ClampInt()
    {
        Result.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        var v = Value.GetValue(context);
        var min = Min.GetValue(context);
        var max = Max.GetValue(context);
        Result.Value = MathUtils.Clamp(v, min, max);
    }
        
    [Input(Guid = "75A09454-6CDE-458B-9314-05A99B2E5919")]
    public readonly InputSlot<int> Value = new();

    [Input(Guid = "E715919D-F3E3-4708-90A6-B55EFB379257")]
    public readonly InputSlot<int> Min = new();
        
    [Input(Guid = "23E55B5D-B469-4D0F-A495-7E87FE65CCCF")]
    public readonly InputSlot<int> Max = new();

        
}