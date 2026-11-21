#nullable enable
using T3.Core.Operator;

namespace T3.Editor.UiModel.Commands.Graph;

internal sealed class AddConnectionCommand : ICommand
{
    public string Name => "Add Connection";
    public bool IsUndoable => true;

    internal AddConnectionCommand(Symbol compositionSymbol, Symbol.Connection connectionToAdd, int multiInputIndex)
    {
        _addedConnection = connectionToAdd;
        _compositionSymbolId = compositionSymbol.Id;
        _multiInputIndex = multiInputIndex;
    }

    public void Do()
    {
        if (!SymbolUiRegistry.TryGetSymbolUi(_compositionSymbolId, out var compositionSymbolUi))
        {
            Log.Warning($"Could not find symbol with id {_compositionSymbolId} - was it removed?");
            return;
        }
            
        compositionSymbolUi.Symbol.AddConnection(_addedConnection, _multiInputIndex);
        compositionSymbolUi.FlagAsModified();
    }

    public void Undo()
    {
        if (!SymbolUiRegistry.TryGetSymbolUi(_compositionSymbolId, out var compositionSymbolUi))
        {
            Log.Warning($"Could not find symbol with id {_compositionSymbolId} - was it removed?");
            return;
        }
            
        compositionSymbolUi.Symbol.RemoveConnection(_addedConnection, _multiInputIndex);
        compositionSymbolUi.FlagAsModified();
    }

    private readonly Guid _compositionSymbolId;
    private readonly Symbol.Connection _addedConnection;
    private readonly int _multiInputIndex;

    // Read access for the collab wire encoder; does not participate in undo semantics.
    internal Guid CompositionSymbolIdForCollab => _compositionSymbolId;
    internal Symbol.Connection ConnectionForCollab => _addedConnection;
    internal int MultiInputIndexForCollab => _multiInputIndex;
}