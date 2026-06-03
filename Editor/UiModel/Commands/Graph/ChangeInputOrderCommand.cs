#nullable enable
using T3.Editor.UiModel.Modification;

namespace T3.Editor.UiModel.Commands.Graph;

/// <summary>
/// Reorders an editable operator's input parameters. Each Do/Undo reorders the in-memory
/// input definitions and recompiles the operator source to match, so this is a heavy command.
/// Slot ids are unchanged, so connections and animations survive both directions.
/// </summary>
internal sealed class ChangeInputOrderCommand : ICommand
{
    public string Name => "Reorder Inputs";
    public bool IsUndoable => true;

    public ChangeInputOrderCommand(Guid symbolId, IReadOnlyList<Guid> oldOrder, IReadOnlyList<Guid> newOrder)
    {
        _symbolId = symbolId;
        _oldOrder = oldOrder.ToArray();
        _newOrder = newOrder.ToArray();
    }

    public void Do() => ApplyOrder(_newOrder);
    public void Undo() => ApplyOrder(_oldOrder);

    private void ApplyOrder(Guid[] order)
    {
        if (!SymbolUiRegistry.TryGetSymbolUi(_symbolId, out var symbolUi))
        {
            Log.Warning($"Can't reorder inputs - symbol {_symbolId} is no longer available.");
            return;
        }

        var symbol = symbolUi.Symbol;

        var current = symbol.InputDefinitions.ToList();
        symbol.InputDefinitions.Clear();
        foreach (var id in order)
        {
            var def = current.Find(d => d.Id == id);
            if (def != null)
                symbol.InputDefinitions.Add(def);
        }

        // Keep any definition not covered by the captured order (defensive against drift)
        foreach (var def in current)
        {
            if (!symbol.InputDefinitions.Contains(def))
                symbol.InputDefinitions.Add(def);
        }

        symbol.SortInputSlotsByDefinitionOrder();
        InputsAndOutputs.AdjustInputOrderOfSymbol(symbol);
    }

    private readonly Guid _symbolId;
    private readonly Guid[] _oldOrder;
    private readonly Guid[] _newOrder;
}
