namespace Lib.numbers.data._obsolete;

[Guid("dd201330-6eab-43b9-b601-2e491ab18feb")]
internal sealed class Iterator : Instance<Iterator>
{
    //[Output(Guid = "6a7857cf-902a-4a26-bb76-7e2dd83717fd")]
    //public readonly Slot<StructuredList> Result = new Slot<StructuredList>();

    [Output(Guid = "6f69d72f-5a89-436f-87c9-5c2085c2f69a")]
    public readonly Slot<Command> Result = new();

    public Iterator()
    {
        Result.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        //var connectedLists = Lists.CollectedInputs.Select(c => c.GetValue(context)).Where(c => c != null).ToList();
        //Lists.DirtyFlag.Clear();
        var list = List.GetValue(context);
        if (list == null || list.NumElements == 0)
            return;

        for (int index = 0; index < list.NumElements; index++)
        {
            context.FloatVariables["iterator"] = index;
            DirtyFlag.GlobalInvalidationTick++;
            foreach (var c in SubTree.CollectedInputs)
            {
                //Log.Debug($"  {index} {c}", this);
                c.InvalidateGraph();
                c.GetValue(context);
                    
            }
        }
        //Resul
    }

    [Input(Guid = "6cb78896-c571-4ee8-a624-46de9a917f4b")]
    public readonly InputSlot<StructuredList> List = new();

    [Input(Guid = "5802fa92-a8b2-44ec-b4cc-47e63d41b345")]
    public readonly MultiInputSlot<Command> SubTree = new();
}