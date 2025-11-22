namespace Lib.numbers.ints;

[Guid("df218352-52ae-4d11-a0f1-24fce0964af5")]
internal sealed class PickIntFromList : Instance<PickIntFromList>
{
    [Output(Guid = "E3BDE7C8-3D12-4979-8CE1-0CCD0A4AC181")]
    public readonly Slot<int> Selected = new();

    public PickIntFromList()
    {
        Selected.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        var list = Input.GetValue(context);
        if (list == null || list.Count == 0)
        {
            Selected.Value = default; 
            return;
        }

        var index = Index.GetValue(context) % list.Count;
        if (index < 0)
            index += list.Count;

        Selected.Value = list[index];
    }

    [Input(Guid = "087CCE9C-054C-4DEA-976A-D0CA1DD8269F")]
    public readonly InputSlot<List<int>> Input = new(new List<int>(20));

    [Input(Guid = "5dfaa21c-5b6e-4943-b787-170b29809f49")]
    public readonly InputSlot<int> Index = new(0);
}