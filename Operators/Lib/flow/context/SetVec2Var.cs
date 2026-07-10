namespace Lib.flow.context;

[Guid("332d7496-09ec-45b7-85b4-0653bb93dd62")]
public sealed class SetVec2Var : Instance<SetVec2Var>
{
    [Output(Guid = "bca971ce-39fd-43f8-8516-838149b142ee")]
    public readonly Slot<Command> Result = new();

    public SetVec2Var()
    {
        Result.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        var name = VariableName.GetValue(context);
        var newValue = Vec2Value.GetValue(context);

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

    [Input(Guid = "e8752eec-0036-41db-957c-8a1fe649fcca")]
    public readonly InputSlot<Vector2> Vec2Value = new();

    [Input(Guid = "903c7fc3-dfa4-4a01-8f7d-53a94f8a93ab")]
    public readonly InputSlot<string> VariableName = new();

    [Input(Guid = "861f826a-9e0d-446e-8f80-ad51d7d9a09b")]
    public readonly InputSlot<Command> SubGraph = new();
}
