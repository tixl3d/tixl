using T3.Core.Operator;

namespace T3.Editor.UiModel.Commands.Graph;

internal sealed class DeleteConnectionCommand : ICommand
{
    public string Name => "Delete Connection";
    public bool IsUndoable => true;

    internal DeleteConnectionCommand(Symbol compositionSymbol, Symbol.Connection connectionToRemove, int multiInputIndex)
    {
        _removedConnection = connectionToRemove.Clone();
        _compositionSymbolId = compositionSymbol.Id;
        _multiInputIndex = multiInputIndex;
    }

    public void Do()
    {
        if(!SymbolUiRegistry.TryGetSymbolUi(_compositionSymbolId, out var symbolUi))
            throw new Exception("Symbol not found: " + _compositionSymbolId);
            
        var compositionSymbol = symbolUi.Symbol;
        compositionSymbol.RemoveConnection(_removedConnection, _multiInputIndex);

        symbolUi.FlagAsModified();
    }

    public void Undo()
    {
        if(!SymbolUiRegistry.TryGetSymbolUi(_compositionSymbolId, out var symbolUi))
            throw new Exception("Symbol not found: " + _compositionSymbolId);
            
        var compositionSymbol = symbolUi.Symbol;
        compositionSymbol.AddConnection(_removedConnection, _multiInputIndex);
        symbolUi.FlagAsModified();
    }

    private readonly Guid _compositionSymbolId;
    private readonly Symbol.Connection _removedConnection;
    private readonly int _multiInputIndex;
}