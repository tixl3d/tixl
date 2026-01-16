namespace Lib.flow.context;

[Guid("fdad077d-e919-4f40-a154-36e86245a585")]
public sealed class SetVec3Var : Instance<SetVec3Var>
{
    [Output(Guid = "55864a65-4227-4418-b86d-803fc4793676")]
    public readonly Slot<Command> Result = new();

    public SetVec3Var()
    {
        Result.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        var name = VariableName.GetValue(context);
        var newValue = Vec3Value.GetValue(context);
            
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
        }
        else
        {
            context.ObjectVariables[name] = newValue;
        }
    }
    
    [Input(Guid = "E1034127-63C9-42ED-9BDD-D1BC054BD103")]
    public readonly InputSlot<Vector3> Vec3Value = new();
    
    [Input(Guid = "0edf7837-4555-4e62-902f-930abf72e8b8")]
    public readonly InputSlot<string> VariableName = new();
        
    [Input(Guid = "a3b8a182-7510-4d89-befa-a35e7e0200ba")]
    public readonly InputSlot<Command> SubGraph = new();
    

}