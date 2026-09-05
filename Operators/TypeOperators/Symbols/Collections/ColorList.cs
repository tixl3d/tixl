using T3.Core.Utils;

namespace Types.Collections;

[Guid("578d282a-597e-4b11-b4cc-cc42cc80b87f")]
public sealed class ColorList : Instance<ColorList>
{
    [Output(Guid = "ea0a37f8-52b4-4931-99bd-61ae21483df0")]
    public readonly Slot<List<Vector4>> Result = new(new List<Vector4>(20));

    public ColorList()
    {
        Result.UpdateAction = Update;
    }

    private void Update(EvaluationContext context)
    {
        var list = List.GetValue(context);
        Result.Value = [..list ?? []];
    }
    
    [Input(Guid = "5ad31347-a09d-4fce-8168-c80a1793f2c1")]
    public readonly InputSlot<List<Vector4>> List = new();
}