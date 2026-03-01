namespace Lib.flow;

[Guid("3631c727-36a0-4f26-ae76-ee9c100efc33")]
internal sealed class Loop : Instance<Loop>
{
    [Output(Guid = "5685cbc4-fe19-4f0e-95a3-147d1fbbad15")]
    public readonly Slot<Command> Output = new();

    public Loop()
    {
        Output.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        var indexVariableName = IndexVariable.GetValue(context);
        var progressVariableName = ProgressVariable.GetValue(context);
            
        var end = Count.GetValue(context);
            
        // TODO: may restore context variable after iterating.
            
        for (var i = 0; i < end; i ++)
        {
            context.FloatVariables[indexVariableName] = i;
            context.IntVariables[indexVariableName] = i;
            if (end == 1)
            {
                context.FloatVariables[progressVariableName] = 0;
            }
            else
            {
                var value = i / ((float)(end - 1));
                context.FloatVariables[progressVariableName] = value;
            }

            DirtyFlag.GlobalInvalidationTick++;
            Command.InvalidateGraph();
            Command.GetValue(context);
        }
    }
        
    [Input(Guid = "49552a0c-2060-4f03-ad39-388293bb6871")]
    public readonly InputSlot<Command> Command = new();

    [Input(Guid = "1F6E2ADB-CFF8-4DC4-9CB4-A26E3AD8B087")]
    public readonly InputSlot<int> Count = new();
        
    [Input(Guid = "F9AEBE04-DD82-459F-8175-7139C7B2E468")]
    public readonly InputSlot<string> IndexVariable = new();
        
    [Input(Guid = "CDE7DD76-0356-48B1-9082-00828C0AF386")]
    public readonly InputSlot<string> ProgressVariable = new();
        
}