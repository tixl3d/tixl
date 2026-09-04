namespace T3.Editor.UiModel.Commands.Graph;

public sealed class ChangeInstanceIsDisabledCommand : ICommand
{
    public string Name => "Disable/Enable";
    public bool IsUndoable => true;

    public ChangeInstanceIsDisabledCommand(SymbolUi.Child symbolChildUi, bool setDisabledTo)
    {
        _inputParentSymbolId = symbolChildUi.SymbolChild.Parent.Id;
        _childId = symbolChildUi.Id;
        _originalState = symbolChildUi.SymbolChild.IsDisabled;
        _newState = setDisabledTo;
    }

    public void Undo()
    {
        AssignValue(_originalState);
    }

    public void Do()
    {
        AssignValue(_newState);
    }

    private void AssignValue(bool shouldBeDisabled)
    {
        if (!SymbolUiRegistry.TryGetSymbolUi(_inputParentSymbolId, out var symbolUi))
            return;
            
        if (!symbolUi!.ChildUis.TryGetValue(_childId, out var childUi))
        {
            Log.Assert("Failed to find childUi");
            return;
        }

        childUi.SymbolChild.IsDisabled = shouldBeDisabled;
        symbolUi.FlagAsModified();
    }

    private readonly bool _newState;
    private readonly bool _originalState;
    private readonly Guid _inputParentSymbolId;
    private readonly Guid _childId;
}