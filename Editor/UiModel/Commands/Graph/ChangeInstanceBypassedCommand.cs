using T3.Core.Operator;

namespace T3.Editor.UiModel.Commands.Graph;

public sealed class ChangeInstanceBypassedCommand : ICommand
{
    public string Name => "Bypass";
    public bool IsUndoable => true;

    public ChangeInstanceBypassedCommand(Symbol.Child symbolChild, bool setBypassedTo)
    {
        _inputParentSymbolId = symbolChild.Parent.Id;
        _childId = symbolChild.Id;
        _originalState = symbolChild.IsBypassed;
        _newState = setBypassedTo;
    }

    public void Undo()
    {
        AssignValue(_originalState);
    }

    public void Do()
    {
        AssignValue(_newState);
    }

    private void AssignValue(bool shouldBeBypassed)
    {
        if (!SymbolUiRegistry.TryGetSymbolUi(_inputParentSymbolId, out var symbolUi))
            return;
            
        var symbol = symbolUi.Symbol;
        if (!symbol.Children.TryGetValue(_childId, out var child))
        {
            Log.Assert("Failed to find child");
            return;
        }

        try
        {
            child.IsBypassed = shouldBeBypassed;
            symbolUi.FlagAsModified();
        }
        catch (Exception e)
        {
            Log.Error($"Failed to set bypass state: {e.Message}");
        }
        
    }

    private readonly bool _newState;
    private readonly bool _originalState;
    private readonly Guid _inputParentSymbolId;
    private readonly Guid _childId;
}