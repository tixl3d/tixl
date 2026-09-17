#nullable enable
using T3.Core.Operator;

namespace T3.Editor.UiModel.Commands.Graph;

/// <summary>
/// Removes a child whose symbol is missing (see <see cref="Symbol.UnresolvedChildren"/>), so it is no
/// longer written back to the file. Its layout entry stays on the <see cref="SymbolUi"/> - the writer
/// skips it while the child is gone - so undo only has to restore the symbol data.
/// </summary>
internal sealed class DeleteMissingOperatorCommand : ICommand
{
    public string Name { get; }
    public bool IsUndoable => true;

    internal DeleteMissingOperatorCommand(Guid compositionSymbolId, Guid childId, string label)
    {
        _compositionSymbolId = compositionSymbolId;
        _childId = childId;
        Name = $"Delete Missing {label}";
    }

    public void Do()
    {
        if (!TryGetComposition(out var symbolUi))
            return;

        if (!symbolUi.Symbol.TryRemoveUnresolvedChild(_childId, out _removed))
        {
            Log.Warning($"Can't delete missing operator {_childId}: it is no longer part of [{symbolUi.Symbol.Name}]");
            return;
        }

        symbolUi.FlagAsModified();
    }

    public void Undo()
    {
        if (_removed == null || !TryGetComposition(out var symbolUi))
            return;

        symbolUi.Symbol.RestoreUnresolvedChild(_removed);
        _removed = null;
        symbolUi.FlagAsModified();
    }

    private bool TryGetComposition([System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out SymbolUi? symbolUi)
    {
        if (SymbolUiRegistry.TryGetSymbolUi(_compositionSymbolId, out symbolUi))
            return true;

        Log.Warning($"Can't find symbol {_compositionSymbolId} for '{Name}' - was it unloaded?");
        return false;
    }

    private readonly Guid _compositionSymbolId;
    private readonly Guid _childId;
    private Symbol.RemovedUnresolvedChild? _removed;
}
