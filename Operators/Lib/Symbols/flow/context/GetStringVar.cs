namespace Lib.flow.context;

[Guid("15012739-97b0-49e6-8bdc-cd45cd262560")]
public sealed class GetStringVar : Instance<GetStringVar>
,ICustomDropdownHolder
{
    [Output(Guid = "1a3ed40a-655b-4604-868d-1f037f7306c7", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
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

    [Input(Guid = "d19b0483-cdd4-4684-852d-ef5ee8393dfc")]
    public readonly InputSlot<string> VariableName = new();
        
    [Input(Guid = "fafdbd5d-ec66-4210-beab-6b9ac010191c")]
    public readonly InputSlot<string> FallbackDefault = new InputSlot<string>("");
}