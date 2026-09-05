namespace Lib.flow.context;

[Guid("5204430b-6c56-4854-b59b-11467658d30b")]
public sealed class GetObjectVar : Instance<GetObjectVar>
,ICustomDropdownHolder
{
    [Output(Guid = "5306FBE2-4839-4BF3-9C20-E67718FDDB5A", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<Object> Result = new();

    public GetObjectVar()
    {
        Result.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        _contextVariableNames= context.ObjectVariables.Keys.ToList();
            
        var variableName = VariableName.GetValue(context);
        if (variableName != null && context.ObjectVariables.TryGetValue(variableName, out var value))
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

    [Input(Guid = "565e80b8-74b9-4081-9cc8-1d39c7c2eef1")]
    public readonly InputSlot<string> VariableName = new();
        
    [Input(Guid = "b25a15c9-9e51-492c-b563-2cfdf81df4a4")]
    public readonly InputSlot<object> FallbackDefault = new();
}