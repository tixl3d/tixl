using T3.Core.Utils;

namespace Types.Collections;

[Guid("b187906f-6d36-433f-843b-399b04987762")]
public sealed class StringList : Instance<StringList>
{
    [Output(Guid = "e639c851-7f8c-47c2-aa73-0f84c6f822c0")]
    public readonly Slot<List<string>> Result = new(new List<string>(20));

    public StringList()
    {
        Result.UpdateAction = Update;
    }

    private void Update(EvaluationContext context)
    {
        var list = List.GetValue(context);
        Result.Value = [..list ?? []];
    }
    
    [Input(Guid = "eaf49a9f-d704-4ecc-9df3-f8bda9ac5f3a")]
    public readonly InputSlot<List<string>> List = new();
}