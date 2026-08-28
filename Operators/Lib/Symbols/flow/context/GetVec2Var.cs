namespace Lib.flow.context;

[Guid("411d3dd4-778b-4012-a19f-05d855fa1baf")]
public sealed class GetVec2Var : Instance<GetVec2Var>
,ICustomDropdownHolder
{
    [Output(Guid = "2231668d-dc24-4966-8582-632bfd771899", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<Vector2> Result = new();

    public GetVec2Var()
    {
        Result.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        if (VariableName.DirtyFlag.IsDirty && !VariableName.HasInputConnections)
            _contextVariableNames = context.ObjectVariables.Keys.ToList();

        var variableName = VariableName.GetValue(context);
        if (variableName != null && context.ObjectVariables.TryGetValue(variableName, out var value)
            && value is Vector2 vec2)
        {
            Result.Value = vec2;
        }
        else
        {
            Result.Value = FallbackDefault.GetValue(context);
        }
    }

    #region implementation of ICustomDropdownHolder
    string ICustomDropdownHolder.GetValueForInput(Guid inputId)
    {
        return VariableName.Value;
    }

    IEnumerable<string> ICustomDropdownHolder.GetOptionsForInput(Guid inputId)
    {
        return _contextVariableNames;
    }

    void ICustomDropdownHolder.HandleResultForInput(Guid inputId, string selected, bool isAListItem)
    {
        if (inputId != VariableName.Input.InputDefinition.Id)
        {
            Log.Warning("Unexpected input id {inputId} in HandleResultForInput", inputId);
            return;
        }
        // Update the list of available variables when dropdown is shown
        VariableName.DirtyFlag.Invalidate();
        VariableName.SetTypedInputValue(selected);
    }
    #endregion


    private List<string> _contextVariableNames = new ();

    [Input(Guid = "05d0cc68-4f6b-4e03-871c-4de0e6ea0b24")]
    public readonly InputSlot<string> VariableName = new();

    [Input(Guid = "82944208-d8dc-4da4-8d17-8b9036c551b0")]
    public readonly InputSlot<Vector2> FallbackDefault = new();
}
