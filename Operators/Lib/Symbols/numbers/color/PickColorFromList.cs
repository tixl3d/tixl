using T3.Core.Utils;

namespace Lib.numbers.color;

[Guid("95817796-c125-4a9c-8996-ee8b6f089156")]
internal sealed class PickColorFromList : Instance<PickColorFromList>
{
    [Output(Guid = "187BBA48-6466-4ACB-912F-494868933135")]
    public readonly Slot<Vector4> Selected = new();

    public PickColorFromList()
    {
        Selected.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        var list = Input.GetValue(context);
        if (list == null || list.Count == 0)
            return;
        
        var index = Index.GetValue(context).Mod(list.Count);
        Selected.Value = list[index];
    }

    [Input(Guid = "6AB7E3FE-955F-44D2-A99D-E743D7F05F45")]
    public readonly InputSlot<List<Vector4>> Input = new();

    [Input(Guid = "ed09c102-d3e9-4d8d-9f37-654f96e26c38")]
    public readonly InputSlot<int> Index = new(0);
}