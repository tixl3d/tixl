namespace Lib.numbers.color;

[Guid("308bb0d1-337b-4c59-9233-6c7bdacf9633")]
internal sealed class CombineColorLists : Instance<CombineColorLists>
{
    [Output(Guid = "068501D2-DF29-4672-9492-BD4911E707F6")]
    public readonly Slot<List<Vector4>> Selected = new();

    public CombineColorLists()
    {
        Selected.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        Selected.Value??= [];
       
        var list = Selected.Value;
        list.Clear();

        var connections = InputLists.GetCollectedTypedInputs();
        if (connections == null || connections.Count == 0)
            return;

        foreach (var i in connections)
        {
            var inputList = i.GetValue(context);
            if(inputList is { Count: > 0 })
                list.AddRange(inputList);
        }
        
        InputLists.DirtyFlag.Clear();
    }
    
    

    [Input(Guid = "05F25C43-C2F3-4CC1-8EF5-BBD93DEB9BC2")]
    public readonly MultiInputSlot<List<Vector4>> InputLists = new();
}