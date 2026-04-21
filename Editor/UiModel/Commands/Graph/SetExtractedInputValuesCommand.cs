using T3.Core.Operator.Slots;

namespace T3.Editor.UiModel.Commands.Graph;

/// <summary>
/// Applies a set of extracted input values to a freshly-created child and resets them to default on
/// undo. Assumes the targeted inputs were at their default value before extraction — true for the
/// "Extract as connected operator" flow where the child is newly added in the same macro.
/// </summary>
internal sealed class SetExtractedInputValuesCommand : ICommand
{
    public string Name => "Apply extracted values";
    public bool IsUndoable => true;

    internal SetExtractedInputValuesCommand(Guid compositionId, Guid childId, Dictionary<Guid, InputValue> newValues)
    {
        _compositionId = compositionId;
        _childId = childId;
        _newValues = newValues;
    }

    public void Do()
    {
        ForEachInput((input, inputId) =>
                     {
                         input.Value.Assign(_newValues[inputId]);
                         input.IsDefault = false;
                     });
    }

    public void Undo()
    {
        ForEachInput((input, _) => input.ResetToDefault());
    }

    private void ForEachInput(Action<Core.Operator.Symbol.Child.Input, Guid> action)
    {
        if (!SymbolUiRegistry.TryGetSymbolUi(_compositionId, out var compositionUi))
            return;

        var composition = compositionUi.Symbol;
        if (!composition.Children.TryGetValue(_childId, out var child))
            return;

        foreach (var inputId in _newValues.Keys)
        {
            if (!child.Inputs.TryGetValue(inputId, out var input))
                continue;

            action(input, inputId);

            foreach (var parentInstance in composition.InstancesOfSelf)
            {
                if (!parentInstance.Children.TryGetChildInstance(_childId, out var instance))
                    continue;

                for (var i = 0; i < instance.Inputs.Count; i++)
                {
                    var slot = instance.Inputs[i];
                    if (slot.Id != inputId)
                        continue;

                    slot.DirtyFlag.ForceInvalidate();
                    break;
                }
            }
        }

        compositionUi.FlagAsModified();
    }

    private readonly Guid _compositionId;
    private readonly Guid _childId;
    private readonly Dictionary<Guid, InputValue> _newValues;
}
