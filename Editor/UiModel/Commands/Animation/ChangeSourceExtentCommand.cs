#nullable enable
using T3.Core.Animation;
using T3.Core.Logging;
using T3.Editor.Gui.Windows.TimeLine;

namespace T3.Editor.UiModel.Commands.Animation;

/// <summary>
/// Sets or clears a symbol's authored source extent (<see cref="TimelineState.SourceExtent"/>).
/// The symbol is resolved by id at call time so the command survives operator-package reloads.
/// </summary>
internal sealed class ChangeSourceExtentCommand : ICommand
{
    public string Name => "Change Source Extent";
    public bool IsUndoable => true;

    internal ChangeSourceExtentCommand(Guid symbolId, TimeRange? originalExtent)
    {
        _symbolId = symbolId;
        _originalExtent = originalExtent;
        _newExtent = originalExtent;
    }

    internal void StoreNewExtent(TimeRange? newExtent)
    {
        _newExtent = newExtent;
    }

    public void Do() => Apply(_newExtent);
    public void Undo() => Apply(_originalExtent);

    private void Apply(TimeRange? extent)
    {
        if (!SymbolUiRegistry.TryGetSymbolUi(_symbolId, out var symbolUi))
        {
            Log.Warning($"Can't change source extent: symbol {_symbolId} is no longer available");
            return;
        }

        symbolUi.TimelineState ??= new TimelineState();
        symbolUi.TimelineState.SourceExtent = extent;
        if (!symbolUi.ReadOnly)
            symbolUi.FlagAsModified();
    }

    private readonly Guid _symbolId;
    private readonly TimeRange? _originalExtent;
    private TimeRange? _newExtent;
}
