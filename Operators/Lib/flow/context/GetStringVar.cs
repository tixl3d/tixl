namespace Lib.flow.context;

[Guid("a21368fe-ae46-4945-9187-12cc97ea0345")]
public sealed class GetStringVar : Instance<GetStringVar>
,ICustomDropdownHolder
{
    [Output(Guid = "eadbd15b-06d0-4893-a2d2-22f8eab03119", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<string> Result = new();

    public GetStringVar()
    {
        Result.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        //if (VariableName.DirtyFlag.IsDirty && !VariableName.HasInputConnections)
        _contextVariableNames= context.StringVariables.Keys.ToList();
            
        var variableName = VariableName.GetValue(context);
        if (variableName != null && context.StringVariables.TryGetValue(variableName, out var value))
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

    [Input(Guid = "a7d3fb83-b0d3-4a33-b33b-5dfdc26533a3")]
    public readonly InputSlot<string> VariableName = new();
        
    [Input(Guid = "810792bf-8741-4e35-a57e-563f9b08f6aa")]
    public readonly InputSlot<string> FallbackDefault = new();
}