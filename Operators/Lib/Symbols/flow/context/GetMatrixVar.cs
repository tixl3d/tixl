namespace Lib.flow.context;

[Guid("a3700115-a15b-493a-8a07-dab16b4ee0af")]
public class GetMatrixVar : Instance<GetMatrixVar>
                          , ICustomDropdownHolder, IStatusProvider
{
    [Output(Guid = "1EEAB949-C741-4ABE-A5AA-BE0819097CDA", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<Vector4[]> Result = new();

    public GetMatrixVar()
    {
        Result.UpdateAction = Update;
    }

    private void Update(EvaluationContext context)
    {
        if (VariableName.DirtyFlag.IsDirty)
        {
            _contextVariableNames = context.ObjectVariables
                                           .Where(k => k.Value is Vector4[])
                                           .Select(k => k.Key)
                                           .ToList();
        }

        var variableName = VariableName.GetValue(context);

        if (context.ObjectVariables.TryGetValue(variableName, out var currentValue))
        {
            if (currentValue is Vector4[] vector4Array)
            {
                Result.Value = vector4Array;
                _statusMessage = string.Empty;
            }
            else
            {
                _statusMessage = $"Can't cast {variableName} to Vector4[]";
                Result.Value = _identity;
            }
        }
        else
        {
            Result.Value = _identity;
            _statusMessage = $"Can't read undefined Vector4[] {variableName}.";
        }
    }

    [Input(Guid = "6ff40ed6-68d9-46b8-aaf3-d4276bedfd81")]
    public readonly InputSlot<string> VariableName = new();

    #region ICustomDropdownHolder
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

    #region status provider
    IStatusProvider.StatusLevel IStatusProvider.GetStatusLevel() =>
        string.IsNullOrEmpty(_statusMessage) ? IStatusProvider.StatusLevel.Success : IStatusProvider.StatusLevel.Warning;

    string IStatusProvider.GetStatusMessage() => _statusMessage;
    private string _statusMessage = string.Empty;
    #endregion

    private static readonly Vector4[] _identity =
        {
            new(1, 0, 0, 0),
            new(0, 1, 0, 0),
            new(0, 0, 1, 0),
            new(0, 0, 0, 1)
        };
}