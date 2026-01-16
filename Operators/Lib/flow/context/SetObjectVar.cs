namespace Lib.flow.context;

[Guid("a0e2c6dd-8728-4128-ab4d-dc277919e280")]
public sealed class SetObjectVar : Instance<SetObjectVar>
{
    [Output(Guid = "3F817582-E1BF-4ECD-A326-864A99E4255A")]
    public readonly Slot<Command> Output = new();

    public SetObjectVar()
    {
        Output.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        var name = VariableName.GetValue(context);
        var newValue = ObjectValue.GetValue(context);
        var clearAfterExecution = ClearAfterExecution.GetValue(context);
            
        if (string.IsNullOrEmpty(name))
        {
            Log.Warning($"Can't set variable with invalid name {name}", this);
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
            else if(!clearAfterExecution)
            {
                context.ObjectVariables.Remove(name);
            }
        }
        else
        {
            context.ObjectVariables[name] = newValue;
        }
    }
        
    [Input(Guid = "3B950A80-AA0D-47E4-AF3A-2696786B02E2")]
    public readonly InputSlot<Object> ObjectValue = new();
    
    [Input(Guid = "ba66d126-8faa-43df-9ade-78d346e8e3a2")]
    public readonly InputSlot<string> VariableName = new();
        
    [Input(Guid = "5ccbefac-d908-477c-8f42-1af325a6eed6")]
    public readonly InputSlot<Command> SubGraph = new();
        
    [Input(Guid = "538bf20c-45df-436e-9611-d6ccbdf05000")]
    public readonly InputSlot<bool> ClearAfterExecution = new ();
        

        
}