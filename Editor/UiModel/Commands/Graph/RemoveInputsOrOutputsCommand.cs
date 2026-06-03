#nullable enable
using T3.Core.Animation;
using T3.Core.Operator;
using T3.Core.Operator.Slots;
using T3.Editor.UiModel.Modification;

namespace T3.Editor.UiModel.Commands.Graph;

/// <summary>
/// Removes inputs and/or outputs from an editable operator. Removing a slot recompiles the operator
/// (which also prunes the connections that referenced it). Undo re-adds the slots with their original
/// ids, default values, and connections.
/// </summary>
/// <remarks>
/// Connections are captured in <see cref="Do"/> rather than the constructor: when this runs inside a
/// macro alongside a child deletion, the child command removes its own connections first, so capturing
/// here avoids double-restoring a connection that both commands touched.
/// </remarks>
internal sealed class RemoveInputsOrOutputsCommand : ICommand
{
    public string Name => "Remove Inputs/Outputs";
    public bool IsUndoable => true;

    public RemoveInputsOrOutputsCommand(Guid symbolId, IReadOnlyList<Guid> inputIdsToRemove, IReadOnlyList<Guid> outputIdsToRemove)
    {
        _symbolId = symbolId;

        if (!SymbolUiRegistry.TryGetSymbolUi(symbolId, out var symbolUi))
            return;

        var symbol = symbolUi.Symbol;

        foreach (var id in inputIdsToRemove)
        {
            var def = symbol.InputDefinitions.FirstOrDefault(i => i.Id == id);
            if (def == null)
                continue;

            _inputs.Add(new InputEntry(def.Id, def.Name, def.ValueType, def.IsMultiInput, def.DefaultValue?.Clone()));
        }

        foreach (var id in outputIdsToRemove)
        {
            var def = symbol.OutputDefinitions.FirstOrDefault(o => o.Id == id);
            if (def == null)
                continue;

            var isTimeClip = def.OutputDataType == typeof(TimeClip);
            _outputs.Add(new OutputEntry(def.Id, def.Name, def.ValueType, isTimeClip));
        }
    }

    public void Do()
    {
        if (!SymbolUiRegistry.TryGetSymbolUi(_symbolId, out var symbolUi))
        {
            Log.Warning($"Can't remove inputs/outputs - symbol {_symbolId} is no longer available.");
            return;
        }

        var symbol = symbolUi.Symbol;

        _removedConnections.Clear();
        foreach (var con in symbol.Connections)
        {
            var willBePruned = _inputs.Any(i => i.Id == con.SourceSlotId) || _outputs.Any(o => o.Id == con.TargetSlotId);
            if (!willBePruned)
                continue;

            _removedConnections.Add(new ConnectionEntry(con, symbol.GetMultiInputIndexFor(con)));
        }

        _removedConnections.Reverse(); // restore in reverse to preserve multi-input order

        InputsAndOutputs.RemoveInputsAndOutputsFromSymbol(_inputs.Select(i => i.Id).ToArray(),
                                                          _outputs.Select(o => o.Id).ToArray(),
                                                          symbol);
    }

    public void Undo()
    {
        if (!SymbolUiRegistry.TryGetSymbolUi(_symbolId, out var symbolUi))
        {
            Log.Warning($"Can't restore inputs/outputs - symbol {_symbolId} is no longer available.");
            return;
        }

        foreach (var e in _inputs)
            InputsAndOutputs.AddInputToSymbol(e.Id, e.Name, e.MultiInput, e.Type, symbolUi.Symbol);

        foreach (var e in _outputs)
            InputsAndOutputs.AddOutputToSymbol(e.Id, e.Name, e.IsTimeClip, e.Type, symbolUi.Symbol);

        // Re-resolve after the recompiles, then restore default values and connections.
        if (!SymbolUiRegistry.TryGetSymbolUi(_symbolId, out symbolUi))
            return;

        var symbol = symbolUi.Symbol;

        foreach (var e in _inputs)
        {
            if (e.DefaultValue == null)
                continue;

            var def = symbol.InputDefinitions.FirstOrDefault(i => i.Id == e.Id);
            if (def != null)
                def.DefaultValue = e.DefaultValue.Clone();
        }

        foreach (var ce in _removedConnections)
        {
            if (!symbol.Connections.Contains(ce.Connection))
                symbol.AddConnection(ce.Connection, ce.MultiInputIndex);
        }

        symbolUi.FlagAsModified();
    }

    private readonly Guid _symbolId;
    private readonly List<InputEntry> _inputs = [];
    private readonly List<OutputEntry> _outputs = [];
    private readonly List<ConnectionEntry> _removedConnections = [];

    private sealed record InputEntry(Guid Id, string Name, Type Type, bool MultiInput, InputValue? DefaultValue);
    private sealed record OutputEntry(Guid Id, string Name, Type Type, bool IsTimeClip);
    private readonly record struct ConnectionEntry(Symbol.Connection Connection, int MultiInputIndex);
}
