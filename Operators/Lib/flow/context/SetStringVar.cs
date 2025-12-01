namespace Lib.flow.context;

[Guid("ce9de603-46c6-493b-b9d4-626f69fa9f44")]
public sealed class SetStringVar : Instance<SetStringVar>
{
    [Output(Guid = "9aff5db6-f137-4c3c-b765-b61d45ba820b")]
    public readonly Slot<Command> Output = new();

    public SetStringVar()
    {
        Output.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        var name = VariableName.GetValue(context);
        var newValue = StringValue.GetValue(context);
        var clearAfterExecution = ClearAfterExecution.GetValue(context);
            
        if (string.IsNullOrEmpty(name))
        {
            Log.Warning($"Can't set variable with invalid name {name}", this);
            return;
        }

        if (SubGraph.HasInputConnections)
        {
            var hadPreviousValue = context.StringVariables.TryGetValue(name, out var previous);
            context.StringVariables[name] = newValue;

            SubGraph.GetValue(context);

            if (hadPreviousValue)
            {
                context.StringVariables[name] = previous;
            }
            else if(!clearAfterExecution)
            {
                context.StringVariables.Remove(name);
            }
        }
        else
        {
            context.StringVariables[name] = newValue;
        }
    }
        
    [Input(Guid = "2a785e9f-696b-4a85-8656-aa3638559bf2")]
    public readonly InputSlot<string> StringValue = new();
    
    [Input(Guid = "605c45f5-b7d5-4e52-bd2e-89f38e5e6f7e")]
    public readonly InputSlot<string> VariableName = new();
        
    [Input(Guid = "0d8ab5a7-5178-4adc-82b2-a73fcc5b52d8")]
    public readonly InputSlot<Command> SubGraph = new();
        
    [Input(Guid = "72623619-1288-4ccc-b343-426acb9287e2")]
    public readonly InputSlot<bool> ClearAfterExecution = new ();
        

        
}