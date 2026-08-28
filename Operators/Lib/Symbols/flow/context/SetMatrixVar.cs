namespace Lib.flow.context;

[Guid("b822a197-9cf6-4bda-abda-02cf98ed5b90")]
public class SetMatrixVar : Instance<SetMatrixVar>
{
    [Output(Guid = "82686968-5e65-428f-91da-d15d55b37300")]
    public readonly Slot<Command> Output = new();

    public SetMatrixVar()
    {
        Output.UpdateAction = Update;
    }
        
        
    private void Update(EvaluationContext context)
    {
        var name = VariableName.GetValue(context);
        var newValue = Value.GetValue(context);
        var clearAfterExecution = ClearAfterExecution.GetValue(context);

            
        if (string.IsNullOrEmpty(name))
        {
            return;
        }

        if (SubGraph.HasInputConnections)
        {
            var hadPreviousValue = context.ObjectVariables.TryGetValue(name, out var previous);
                
            context.ObjectVariables[name] = newValue;

            SubGraph.GetValue(context);

            if (hadPreviousValue)
            {
                context.ObjectVariables[name] = previous;
            }
            else
            {
                if(!clearAfterExecution)
                    context.ObjectVariables.Remove(name);
            }
        }
        else if(!clearAfterExecution)
        {
            context.ObjectVariables[name] = newValue;
        }
    }

        
    [Input(Guid = "6ac6caf5-f65b-4740-9a35-3ac0e5bfbdde")]
    public readonly InputSlot<Command> SubGraph = new();
        
    [Input(Guid = "3e968af2-a08e-4c4b-a822-724c66164b5f")]
    public readonly InputSlot<string> VariableName = new();
        
        
    [Input(Guid = "4BD58603-46B7-4C66-BCD6-43468400D489")]
    public readonly InputSlot<Vector4[]> Value = new();
        
    [Input(Guid = "34f9fde3-7030-4d1a-a48d-e71984634709")]
    public readonly InputSlot<bool> ClearAfterExecution = new ();

    private enum LogLevels
    {
        None,
        Warnings,
        Changes,
        AllUpdates,
    }
}