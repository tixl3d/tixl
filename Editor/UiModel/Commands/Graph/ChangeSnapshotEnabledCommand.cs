namespace T3.Editor.UiModel.Commands.Graph;

/// <summary>
/// Toggles `EnabledForSnapshots` (backing: `SnapshotGroupIndex`) on a set of children. Stores each
/// child's pre-change group index so undo restores the exact previous state rather than a flat
/// enable/disable.
/// </summary>
internal sealed class ChangeSnapshotEnabledCommand : ICommand
{
    public string Name => "Toggle snapshot enabled";
    public bool IsUndoable => true;

    private const int GroupIndexForSnapshots = 1;

    private readonly Guid _parentSymbolId;
    private readonly List<Entry> _changes = [];

    internal ChangeSnapshotEnabledCommand(Guid parentSymbolId, IEnumerable<SymbolUi.Child> children, bool newEnabled)
    {
        _parentSymbolId = parentSymbolId;
        var newIndex = newEnabled ? GroupIndexForSnapshots : 0;
        foreach (var child in children)
        {
            var originalEnabledInputIds = child.SnapshotEnabledInputIds == null ? null : new HashSet<Guid>(child.SnapshotEnabledInputIds);
            _changes.Add(new Entry(child.Id, child.SnapshotGroupIndex, originalEnabledInputIds, newIndex));
        }
    }

    public void Do() => Apply(useNew: true);
    public void Undo() => Apply(useNew: false);

    private void Apply(bool useNew)
    {
        if (!SymbolUiRegistry.TryGetSymbolUi(_parentSymbolId, out var symbolUi))
            return;

        foreach (var change in _changes)
        {
            if (!symbolUi.ChildUis.TryGetValue(change.ChildId, out var child))
                continue;

            child.SnapshotGroupIndex = useNew ? change.NewGroupIndex : change.OriginalGroupIndex;

            // The bulk toggle resets per-parameter selection: enable means all parameters
            child.SnapshotEnabledInputIds = useNew || change.OriginalEnabledInputIds == null
                                                ? null
                                                : [..change.OriginalEnabledInputIds];
        }

        symbolUi.FlagAsModified();
    }

    private readonly record struct Entry(Guid ChildId, int OriginalGroupIndex, HashSet<Guid>? OriginalEnabledInputIds, int NewGroupIndex);
}
