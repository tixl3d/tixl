using T3.Core.Utils;

namespace Types.Collections;

[Guid("db22a341-09f6-4953-b252-61b343159650")]
public sealed class FloatList : Instance<FloatList>
{
    [Output(Guid = "73bd6ce3-92ac-44af-b334-3f66fe31b160")]
    public readonly Slot<List<float>> Result = new(new List<float>(20));

    public FloatList()
    {
        Result.UpdateAction = Update;
    }

    private void Update(EvaluationContext context)
    {
        var list = List.GetValue(context);
        Result.Value = [..list ?? []];
    }
    
    [Input(Guid = "7D1BA868-209F-4419-962B-F13C36A7D19D")]
    public readonly InputSlot<List<float>> List = new();
}