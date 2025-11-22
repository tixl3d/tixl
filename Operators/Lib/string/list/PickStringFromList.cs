namespace Lib.@string.list;

[Guid("ef357e66-24e9-4f54-8d86-869db74602f4")]
internal sealed class PickStringFromList : Instance<PickStringFromList>
{
    [Output(Guid = "467bb46e-3391-48a7-b0eb-f7fd9d77b60f")]
    public readonly Slot<string> Selected = new();

    [Output(Guid = "83009BD4-5257-44A2-8091-92B7D2FA5E35")]
    public readonly Slot<int> Count = new();

        
    public PickStringFromList()
    {
        Selected.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        var list = Input.GetValue(context);
        if (list == null || list.Count == 0)
        {
            Selected.Value = string.Empty;
            Count.Value = 0;
            return;
        }

        Count.Value = list.Count;

        var index = Index.GetValue(context) % list.Count;
        if (index < 0)
            index += list.Count;

        Selected.Value = list[index];
    }

    [Input(Guid = "8d5e77a6-1ec4-4979-ad26-f7862049bce1")]
    public readonly InputSlot<List<string>> Input = new(new List<string>(20));

    [Input(Guid = "12ce5fe3-750f-47ed-9507-416cb327a615")]
    public readonly InputSlot<int> Index = new(0);
}