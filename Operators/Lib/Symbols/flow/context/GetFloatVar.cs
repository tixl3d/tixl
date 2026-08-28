namespace Lib.flow.context;

[Guid("e6072ecf-30d2-4c52-afa1-3b195d61617b")]
public sealed class GetFloatVar : Instance<GetFloatVar>, ICustomDropdownHolder
{
    [Output(Guid = "e368ba33-827e-4e08-aa19-ba894b40906a", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<float> Result = new();

    public GetFloatVar()
    {
        Result.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        //if (VariableName.DirtyFlag.IsDirty && !VariableName.HasInputConnections)
        _contextVariableNames= context.FloatVariables.Keys.ToList();
            
        var variableName = VariableName.GetValue(context);
        if (variableName != null && context.FloatVariables.TryGetValue(variableName, out var value))
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

    [Input(Guid = "015d1ea0-ea51-4038-893a-4af2f8584631")]
    public readonly InputSlot<string> VariableName = new();
        
    [Input(Guid = "AE76829B-D17D-4443-9CF1-63E3C44B90C8")]
    public readonly InputSlot<float> FallbackDefault = new();
}