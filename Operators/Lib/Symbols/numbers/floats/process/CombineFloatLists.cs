namespace Lib.numbers.floats.process;

[Guid("2572b5f2-40af-442c-9354-7a9fa5bd378f")]
internal sealed class CombineFloatLists : Instance<CombineFloatLists>
{
    [Output(Guid = "ef43f4d0-0b7e-46bf-bb97-10e295cb9893")]
    public readonly Slot<List<float>> Selected = new();

    public CombineFloatLists()
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
    }
    
    

    [Input(Guid = "2101b2b6-da50-486e-b117-e533ad754fa3")]
    public readonly MultiInputSlot<List<float>> InputLists = new();
}