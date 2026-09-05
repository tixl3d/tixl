using T3.Core.Utils;

namespace Lib.flow.context;

[Guid("470db771-c7f2-4c52-8897-d3a9b9fc6a4e")]
public sealed class GetIntVar : Instance<GetIntVar>, ICustomDropdownHolder
{
    [Output(Guid = "B306B216-630C-4611-90FD-52FF322EBD00", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<int> Result = new();

    public GetIntVar()
    {
        Result.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        if (VariableName.DirtyFlag.IsDirty)
        {
            _contextVariableNames = context.IntVariables.Keys.ToList();
        }
            
        var logUpdates = LogUpdates.GetEnumValue<LogLevels>(context);
        var fallbackValue = FallbackValue.GetValue(context);
        var variableName = VariableName.GetValue(context);

        if (variableName == null)
            return;
        
        if (context.IntVariables.TryGetValue(variableName, out int currentValue))
        {
            Result.Value = currentValue;

            if ((int)logUpdates >= (int)LogLevels.AllUpdates)
                Log.Debug($"int {variableName} is {currentValue}", this);
        }
        else
        {
            Result.Value = fallbackValue;

            var complain = ((int)logUpdates >= (int)LogLevels.Warnings && !_complainedOnce)
                           || logUpdates == LogLevels.AllUpdates;

            if (!complain)
                return;

            Log.Warning($"Can't read undefined int {variableName}.", this);
            _complainedOnce = true;
        }
    }

    private bool _complainedOnce;

    [Input(Guid = "d7662b65-f249-4887-a319-dc2cf7d192f2")]
    public readonly InputSlot<string> VariableName = new();

    [Input(Guid = "C78E0D72-1296-430C-98FB-078CBE2E9596")]
    public readonly InputSlot<int> FallbackValue = new();

    [Input(Guid = "70EF2D92-CD3E-4C14-BA9C-E7E29B2478C8", MappedType = typeof(LogLevels))]
    public readonly InputSlot<int> LogUpdates = new();

    private enum LogLevels
    {
        None,
        Warnings,
        Changes,
        AllUpdates,
    }
        
    #region  ICustomDropdownHolder
        
    string ICustomDropdownHolder.GetValueForInput(Guid inputId)
    {
        return VariableName.Value;
    }
        
    IEnumerable<string> ICustomDropdownHolder.GetOptionsForInput(Guid inputId)
    {
        if (inputId != VariableName.Input.InputDefinition.Id)
        {
            Log.Warning("Unexpected input id {inputId} in GetOptionsForInput", inputId);
            return new List<string>(); 
        }
            
        // Update the list of available variables when dropdown is shown
        VariableName.DirtyFlag.Invalidate();
        return _contextVariableNames;
    }
        
    void ICustomDropdownHolder.HandleResultForInput(Guid inputId, string selected, bool isAListItem)
    {
        if (inputId != VariableName.Input.InputDefinition.Id)
        {
            Log.Warning("Unexpected input id {inputId} in HandleResultForInput", inputId);
            return;
        }
        VariableName.SetTypedInputValue(selected);
    }
        
    private List<string> _contextVariableNames = new();
        
    #endregion
}