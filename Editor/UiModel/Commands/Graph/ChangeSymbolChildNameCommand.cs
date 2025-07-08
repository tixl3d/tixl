using T3.Core.Operator;

namespace T3.Editor.UiModel.Commands.Graph;

public sealed class ChangeSymbolChildNameCommand : ICommand
{
    public string Name => "Change Symbol Name";
    public bool IsUndoable => true;

    public ChangeSymbolChildNameCommand(SymbolUi.Child symbolChildUi, Symbol parentSymbol)
    {
        _symbolChildId = symbolChildUi.SymbolChild.Id;
        _parentSymbolId = parentSymbol.Id;
        NewName = _originalName = symbolChildUi.SymbolChild.Name;
    }

    public void Do()
    {
        AssignValue(NewName);
    }

    public void Undo()
    {
        AssignValue(_originalName);
    }

    private void AssignValue(string newName)
    {
        if (!SymbolUiRegistry.TryGetSymbolUi(_parentSymbolId, out var symbolUi))
            throw new Exception("Symbol not found: " + _parentSymbolId);
        
        var symbolParent = symbolUi.Symbol;
        var symbolChild = symbolParent.Children[_symbolChildId];
        symbolChild.Name = newName;
        symbolUi.FlagAsModified();
    }

    public string NewName { get; set; }
    private readonly string _originalName;
    private readonly Guid _symbolChildId;
    private readonly Guid _parentSymbolId;
}