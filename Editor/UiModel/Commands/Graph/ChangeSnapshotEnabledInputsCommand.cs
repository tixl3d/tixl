namespace T3.Editor.UiModel.Commands.Graph;

/// <summary>
/// Changes which of a child's inputs are enabled for snapshot control, including the per-op
/// group index (toggling the first/last parameter also enables/disables the op). Stores the
/// previous state so undo restores it exactly, and resolves the child by guid per call so the
/// command survives package reloads.
/// </summary>
internal sealed class ChangeSnapshotEnabledInputsCommand : ICommand
{
    public string Name => "Change parameter snapshot control";
    public bool IsUndoable => true;

    internal ChangeSnapshotEnabledInputsCommand(Guid compositionSymbolId, SymbolUi.Child childUi, int newGroupIndex, HashSet<Guid>? newEnabledInputIds)
    {
        _compositionSymbolId = compositionSymbolId;
        _childId = childUi.Id;
        _originalGroupIndex = childUi.SnapshotGroupIndex;
        _originalEnabledInputIds = childUi.SnapshotEnabledInputIds == null ? null : [..childUi.SnapshotEnabledInputIds];
        _newGroupIndex = newGroupIndex;
        _newEnabledInputIds = newEnabledInputIds == null ? null : [..newEnabledInputIds];
    }

    public void Do() => Apply(_newGroupIndex, _newEnabledInputIds);
    public void Undo() => Apply(_originalGroupIndex, _originalEnabledInputIds);

    private void Apply(int groupIndex, HashSet<Guid>? enabledInputIds)
    {
        if (!SymbolUiRegistry.TryGetSymbolUi(_compositionSymbolId, out var symbolUi))
        {
            Log.Warning($"Can't change snapshot inputs for missing symbol {_compositionSymbolId}");
            return;
        }

        if (!symbolUi.ChildUis.TryGetValue(_childId, out var childUi))
        {
            Log.Warning($"Can't change snapshot inputs for missing child {_childId}");
            return;
        }

        childUi.SnapshotGroupIndex = groupIndex;
        childUi.SnapshotEnabledInputIds = enabledInputIds == null ? null : [..enabledInputIds];
        symbolUi.FlagAsModified();
    }

    private readonly Guid _compositionSymbolId;
    private readonly Guid _childId;
    private readonly int _originalGroupIndex;
    private readonly HashSet<Guid>? _originalEnabledInputIds;
    private readonly int _newGroupIndex;
    private readonly HashSet<Guid>? _newEnabledInputIds;
}
