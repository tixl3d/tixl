using T3.Core.Utils;

namespace Types.Collections;

[Guid("c35dc0e1-75cb-434e-b594-b2a4fb687d55")]
public sealed class IntList : Instance<IntList>
{
    [Output(Guid = "17714F77-DEB8-4D97-9045-76B351FA5A12")]
    public readonly Slot<List<int>> Result = new(new List<int>(20));

    public IntList()
    {
        Result.UpdateAction = Update;
    }

    private void Update(EvaluationContext context)
    {
        var list = List.GetValue(context);
        Result.Value = [..list ?? []];
    }
    
    [Input(Guid = "0E068DA1-1E4B-40AB-B86F-C05622D34717")]
    public readonly InputSlot<List<int>> List = new();
}