#nullable enable
using T3.Core.Operator;
using T3.Core.Operator.Slots;
using T3.Editor.Gui.Window;
using T3.Editor.UiModel;
using T3.Editor.UiModel.Commands;
using T3.Editor.UiModel.ProjectHandling;

namespace T3.Editor.Gui.MagGraph.Interaction;

/*
 * Removes candidate anchors only when they have no incoming or outgoing connections at Do time.
 * Callers capture candidates before disconnecting and append cleanup to the same undo operation,
 * so undo restores anchors before reconnecting wires. The first successful Do fixes the snapshot
 * set for redo; IDs and copied state survive graph reloads without retaining live graph objects.
 */
internal sealed class RemoveDisconnectedReroutesCommand : ICommand
{
    // Retain slot settings and editor metadata so undo restores more than the anchor's topology.
    private sealed class ChildSnapshot
    {
        internal ChildSnapshot(SymbolUi.Child ui)
        {
            var child = ui.SymbolChild;
            var input = child.Inputs.Values.Single();
            var output = child.Outputs.Values.Single();
            SymbolId = child.Symbol.Id;
            ChildId = child.Id;
            Name = child.Name;
            IsBypassed = child.IsBypassed;
            InputId = input.Id;
            InputValue = input.Value.Clone();
            InputIsDefault = input.IsDefault;
            OutputId = output.OutputDefinition.Id;
            OutputIsDisabled = output.IsDisabled;
            OutputDirtyTrigger = output.DirtyFlagTrigger;
            Position = ui.PosOnCanvas;
            Size = ui.Size;
            SectionId = ui.SectionId;
            Style = ui.Style;
            Comment = ui.Comment;
            SnapshotGroupIndex = ui.SnapshotGroupIndex;
            SnapshotEnabledInputIds = ui.SnapshotEnabledInputIds?.ToArray();
            ConnectionStyles = ui.ConnectionStyleOverrides.ToArray();
        }

        internal bool Matches(Symbol definition)
        {
            return definition.Id == SymbolId && RerouteOperations.IsReroute(definition)
                   && definition.InputDefinitions[0].Id == InputId
                   && definition.InputDefinitions[0].ValueType == InputValue.ValueType
                   && definition.OutputDefinitions[0].Id == OutputId;
        }

        internal readonly Guid SymbolId;
        internal readonly Guid ChildId;
        internal readonly string Name;
        internal readonly bool IsBypassed;
        internal readonly Guid InputId;
        internal readonly InputValue InputValue;
        internal readonly bool InputIsDefault;
        internal readonly Guid OutputId;
        internal readonly bool OutputIsDisabled;
        internal readonly DirtyFlagTrigger OutputDirtyTrigger;
        internal readonly Vector2 Position;
        internal readonly Vector2 Size;
        internal readonly Guid SectionId;
        internal readonly SymbolUi.Child.Styles Style;
        internal readonly string? Comment;
        internal readonly int SnapshotGroupIndex;
        internal readonly Guid[]? SnapshotEnabledInputIds;
        internal readonly KeyValuePair<Guid, SymbolUi.Child.ConnectionStyles>[] ConnectionStyles;
    }

    public RemoveDisconnectedReroutesCommand(Guid compositionId, IEnumerable<Guid> candidateIds)
    {
        _compositionId = compositionId;
        _candidateIds = candidateIds.Distinct().ToArray();
    }

    public string Name => "Remove Disconnected Reroutes";
    public bool IsUndoable => true;
    // Nonzero only while deletion is applied; callers use this to avoid recording an empty cleanup.
    internal int AppliedCount { get; private set; }

    public void Do()
    {
        if (AppliedCount != 0 || !TryGetComposition(out var composition))
            return;

        var connected = GetConnectedChildren(composition!.Symbol);
        var snapshots = _snapshots;
        if (snapshots == null)
        {
            var candidates = new List<ChildSnapshot>();
            foreach (var id in _candidateIds)
            {
                if (connected.Contains(id) || !composition.ChildUis.TryGetValue(id, out var childUi)
                    || !composition.Symbol.Children.TryGetValue(id, out var child) || !RerouteOperations.IsReroute(child.Symbol))
                    continue;

                candidates.Add(new ChildSnapshot(childUi));
            }

            snapshots = candidates.ToArray();
        }
        else
        {
            foreach (var snapshot in snapshots)
            {
                if (connected.Contains(snapshot.ChildId) || !composition.ChildUis.ContainsKey(snapshot.ChildId)
                    || !composition.Symbol.Children.TryGetValue(snapshot.ChildId, out var child) || !snapshot.Matches(child.Symbol))
                {
                    Log.Warning("Disconnected reroute cleanup skipped because an anchor changed.");
                    return;
                }
            }
        }

        if (!TryResolveDefinitions(snapshots, out var definitions))
            return;

        try
        {
            foreach (var snapshot in snapshots)
                composition.RemoveChild(snapshot.ChildId);
        }
        catch (Exception e)
        {
            foreach (var snapshot in snapshots)
            {
                if (composition.Symbol.Children.ContainsKey(snapshot.ChildId) && composition.ChildUis.ContainsKey(snapshot.ChildId))
                    continue;

                RemovePartialChild(composition, snapshot.ChildId);
                Restore(composition, definitions[snapshot.SymbolId], snapshot);
            }

            Log.Warning($"Could not remove disconnected reroutes: {e.Message}");
            return;
        }

        _snapshots = snapshots;
        AppliedCount = snapshots.Length;
        if (AppliedCount > 0)
            RefreshViews(removeSelection: true);
    }

    public void Undo()
    {
        if (AppliedCount == 0 || _snapshots == null || !TryGetComposition(out var composition))
            return;

        foreach (var snapshot in _snapshots)
        {
            if (composition!.Symbol.Children.ContainsKey(snapshot.ChildId) || composition.ChildUis.ContainsKey(snapshot.ChildId))
            {
                Log.Warning("Disconnected reroute restore skipped because a child ID is already in use.");
                return;
            }
        }

        if (!TryResolveDefinitions(_snapshots, out var definitions))
            return;

        try
        {
            foreach (var snapshot in _snapshots)
                Restore(composition!, definitions[snapshot.SymbolId], snapshot);
        }
        catch (Exception e)
        {
            foreach (var snapshot in _snapshots)
                RemovePartialChild(composition!, snapshot.ChildId);

            Log.Warning($"Could not restore disconnected reroutes: {e.Message}");
            return;
        }

        AppliedCount = 0;
        RefreshViews(removeSelection: false);
    }

    private bool TryGetComposition(out SymbolUi? composition)
    {
        if (SymbolUiRegistry.TryGetSymbolUi(_compositionId, out composition) && !composition.ReadOnly
            && !composition.Symbol.SymbolPackage.IsReadOnly)
            return true;

        Log.Warning("Disconnected reroute cleanup skipped because its editable graph is unavailable.");
        return false;
    }

    private static bool TryResolveDefinitions(ChildSnapshot[] snapshots, out Dictionary<Guid, Symbol> definitions)
    {
        definitions = new Dictionary<Guid, Symbol>();
        foreach (var snapshot in snapshots)
        {
            if (!SymbolUiRegistry.TryGetSymbolUi(snapshot.SymbolId, out var definitionUi) || !snapshot.Matches(definitionUi.Symbol))
            {
                Log.Warning("Disconnected reroute cleanup skipped because an operator definition changed or was unloaded.");
                return false;
            }

            definitions.TryAdd(snapshot.SymbolId, definitionUi.Symbol);
        }

        return true;
    }

    private static HashSet<Guid> GetConnectedChildren(Symbol composition)
    {
        var connected = new HashSet<Guid>();
        foreach (var connection in composition.Connections)
        {
            connected.Add(connection.SourceParentOrChildId);
            connected.Add(connection.TargetParentOrChildId);
        }

        return connected;
    }

    private static void Restore(SymbolUi composition, Symbol definition, ChildSnapshot snapshot)
    {
        var ui = composition.AddChild(definition, snapshot.ChildId, snapshot.Position, snapshot.Size, snapshot.Name, snapshot.IsBypassed);
        ui.SectionId = snapshot.SectionId;
        ui.Style = snapshot.Style;
        ui.Comment = snapshot.Comment;
        ui.SnapshotGroupIndex = snapshot.SnapshotGroupIndex;
        ui.SnapshotEnabledInputIds = snapshot.SnapshotEnabledInputIds == null ? null : [..snapshot.SnapshotEnabledInputIds];
        foreach (var (id, style) in snapshot.ConnectionStyles)
            ui.ConnectionStyleOverrides.Add(id, style);

        var child = ui.SymbolChild;
        var input = child.Inputs[snapshot.InputId];
        input.Value.Assign(snapshot.InputValue.Clone());
        input.IsDefault = snapshot.InputIsDefault;
        var output = child.Outputs[snapshot.OutputId];
        output.IsDisabled = snapshot.OutputIsDisabled;
        output.DirtyFlagTrigger = snapshot.OutputDirtyTrigger;

        // AddChild may have created live slots before the saved settings were restored.
        foreach (var parent in composition.Symbol.InstancesOfSelf)
        {
            if (!parent.Children.TryGetChildInstance(snapshot.ChildId, out var instance, false))
                continue;

            instance.Inputs[0].DirtyFlag.ForceInvalidate();
            var slot = instance.Outputs[0];
            slot.DirtyFlag.Trigger = snapshot.OutputDirtyTrigger;
            slot.IsDisabled = snapshot.OutputIsDisabled;
            slot.DirtyFlag.ForceInvalidate();
        }

        composition.FlagAsModified();
    }

    private static void RemovePartialChild(SymbolUi composition, Guid id)
    {
        if (composition.Symbol.Children.ContainsKey(id) || composition.ChildUis.ContainsKey(id))
            composition.RemoveChild(id);
    }

    private void RefreshViews(bool removeSelection)
    {
        var focused = ProjectView.Focused;
        Refresh(focused);
        foreach (var window in GraphWindow.GraphWindowInstances)
        {
            if (window.ProjectView != focused)
                Refresh(window.ProjectView);
        }

        return;

        void Refresh(ProjectView? view)
        {
            if (view?.CompositionInstance?.Symbol.Id != _compositionId)
                return;

            if (removeSelection)
            {
                var selection = view.NodeSelection;
                for (var index = selection.Selection.Count - 1; index >= 0; index--)
                {
                    if (selection.Selection[index] is not SymbolUi.Child child)
                        continue;

                    foreach (var snapshot in _snapshots!)
                    {
                        if (snapshot.ChildId != child.Id)
                            continue;

                        selection.DeselectNode(child);
                        break;
                    }
                }
            }

            view.FlagChanges(ProjectView.ChangeTypes.Children);
        }
    }

    private readonly Guid _compositionId;
    private readonly Guid[] _candidateIds;
    private ChildSnapshot[]? _snapshots;
}
