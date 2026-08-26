namespace Lib.numbers.data._obsolete;

[Guid("8b285708-3f20-4957-9eb2-bb40e0d320ee")]
internal sealed class IterateList : Instance<IterateList>
{
    [Output(Guid = "6FD6E8AB-7F90-4693-BE5C-391DE9027362")]
    public readonly Slot<Command> Result = new();

    public IterateList()
    {
        Result.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        if (context.IteratedList != null)
        {
            Log.Warning("Nesting multiple [IterateList] Operators is not possible yet", this);
            return;
        }
            
        var list = List.GetValue(context);
        if (list == null || list.NumElements == 0)
            return;

        context.IteratedList = list;

        foreach (var initCommand in SetupCommands.CollectedInputs)
        {
            initCommand.InvalidateGraph();
            initCommand.GetValue(context);
        }
            
        for (int index = 0; index < list.NumElements; index++)
        {
            context.IteratedListIndex = index;
            context.FloatVariables["iterator"] = index;
            DirtyFlag.GlobalInvalidationTick++;
            foreach (var c in IterateCommands.CollectedInputs)
            {
                c.InvalidateGraph();
                c.GetValue(context);
                    
            }
        }

        context.IteratedList = null;
    }

    [Input(Guid = "A5E1551B-697E-4316-A95E-315A39B69C76")]
    public readonly InputSlot<StructuredList> List = new();

    [Input(Guid = "452B30A4-D0B8-43CC-B5D2-B6D338CA4DEB")]
    public readonly MultiInputSlot<Command> SetupCommands = new();

    [Input(Guid = "1D09C5E2-19D1-4622-A06D-4FFD3124343C")]
    public readonly MultiInputSlot<Command> IterateCommands = new();
}