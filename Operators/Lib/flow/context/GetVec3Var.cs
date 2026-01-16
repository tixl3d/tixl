namespace Lib.flow.context;

[Guid("f21de2e1-6af8-4651-90a0-6c662bbb23af")]
public sealed class GetVec3Var : Instance<GetVec3Var>
,ICustomDropdownHolder
{
    [Output(Guid = "F26C6DFE-AFC5-4824-9580-92FF5CD8F086", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<Vector3> Result = new();

    public GetVec3Var()
    {
        Result.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        if (VariableName.DirtyFlag.IsDirty && !VariableName.HasInputConnections)
            _contextVariableNames= context.BoolVariables.Keys.ToList();
            
        var variableName = VariableName.GetValue(context);
        if (variableName != null && context.ObjectVariables.TryGetValue(variableName, out var value)
            && value is Vector3 vec3)
        {
            Result.Value = vec3;
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

    [Input(Guid = "d8a9d923-232f-4cd4-9e24-fadbf40fe1d1")]
    public readonly InputSlot<string> VariableName = new();
        
    [Input(Guid = "5CCE784C-020A-4E51-881B-EDD48846B0FC")]
    public readonly InputSlot<Vector3> FallbackDefault = new();
}