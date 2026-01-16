namespace Lib.flow.context;

[Guid("9c9274c1-3f85-4f57-9eb5-1e0a0b1cce00")]
public sealed class SetStringVar : Instance<SetStringVar>
{
    [Output(Guid = "d45a4915-fdd4-4adc-8083-fcd3d7466215")]
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
        
    [Input(Guid = "64453e52-84eb-487a-b69c-6c5c793dc057")]
    public readonly InputSlot<string> StringValue = new();
    
    [Input(Guid = "5be2939b-27f5-4af8-b78e-3ae220d6b1a4")]
    public readonly InputSlot<string> VariableName = new();
        
    [Input(Guid = "77d3ebbd-9a07-445d-8990-6b9d91d0e79d")]
    public readonly InputSlot<Command> SubGraph = new();
        
    [Input(Guid = "41b7154b-20b4-4cb2-9bcb-507a3ecb6807")]
    public readonly InputSlot<bool> ClearAfterExecution = new ();
        

        
}