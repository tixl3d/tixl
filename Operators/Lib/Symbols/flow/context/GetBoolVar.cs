namespace Lib.flow.context;

[Guid("604bfb46-fe8f-4c8b-896b-1b7bc827137b")]
public sealed class GetBoolVar : Instance<GetBoolVar>
,ICustomDropdownHolder
{
    [Output(Guid = "B5BB6CD9-58DD-4C9C-AAB3-AE0E21C81822", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<bool> Result = new();

    public GetBoolVar()
    {
        Result.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        if (VariableName.DirtyFlag.IsDirty && !VariableName.HasInputConnections)
            _contextVariableNames= context.BoolVariables.Keys.ToList();
            
        var variableName = VariableName.GetValue(context);
        if (variableName != null && context.BoolVariables.TryGetValue(variableName, out var value))
        {
            Result.Value = value;
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
        
        
    private  List<string> _contextVariableNames = new ();

    [Input(Guid = "b0821091-68c0-4e34-9f8b-926c0b6ebf94")]
    public readonly InputSlot<string> VariableName = new();
        
    [Input(Guid = "992EE524-633B-4705-A9D7-9DBF14B01EAB")]
    public readonly InputSlot<bool> FallbackDefault = new();
}