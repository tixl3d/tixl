namespace Lib.flow.context;

[Guid("9a843835-d39c-428f-b996-6334323e8106")]
public sealed class SetBoolVar : Instance<SetBoolVar>
{
    [Output(Guid = "DF5E416D-8BE3-4DBE-ABF3-68A5139FDB7A")]
    public readonly Slot<Command> Result = new();

    public SetBoolVar()
    {
        Result.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        var name = VariableName.GetValue(context);
        var newValue = BoolValue.GetValue(context);
            
        if (string.IsNullOrEmpty(name))
        {
            Log.Warning($"Can't set variable with invalid name {name}", this);
            return;
        }

        if (SubGraph.HasInputConnections)
        {
            var hadPreviousValue = context.BoolVariables.TryGetValue(name, out var previous);
            context.BoolVariables[name] = newValue;

            SubGraph.GetValue(context);

            if (hadPreviousValue)
            {
                context.BoolVariables[name] = previous;
            }
        }
        else
        {
            context.BoolVariables[name] = newValue;
        }
    }
    
    [Input(Guid = "85CDC809-E835-4851-8956-BB0217B71F1A")]
    public readonly InputSlot<bool> BoolValue = new();
    
    [Input(Guid = "BFDFCD6E-3B31-4B26-AFF4-3023A6B72810")]
    public readonly InputSlot<string> VariableName = new();
        
    [Input(Guid = "7698ECCB-0319-4C03-8F4E-486CC959D50C")]
    public readonly InputSlot<Command> SubGraph = new();
    

}