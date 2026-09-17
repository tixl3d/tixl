#nullable enable
using T3.Core.Operator;
using T3.Core.Operator.Slots;
using T3.Editor.Gui.Window;
using T3.Editor.UiModel;
using T3.Editor.UiModel.Commands;
using T3.Editor.UiModel.Helpers;
using T3.Editor.UiModel.ProjectHandling;

namespace T3.Editor.Gui.MagGraph.Interaction;

/// <summary>
/// Removes previously connected candidate anchors only when fully disconnected at execution time.
/// Appending cleanup last lets undo restore anchors before their wires; IDs and copied state survive reloads.
/// The first successful execution fixes the snapshot set for redo.
/// </summary>
internal sealed class RemoveDisconnectedReroutesCommand : ICommand
{
    /// <summary>Retain slot settings and editor metadata so undo restores more than the anchor's topology.</summary>
    private sealed class ChildSnapshot
    {
        /// <summary>Copies the anchor identity, slot values, and editor state without retaining its live child.</summary>
        /// <param name="ui">Child UI whose editable metadata and runtime defaults are captured for undo.</param>
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

        /// <summary>Checks that the current definition still has the captured reroute slot IDs and value type.</summary>
        /// <param name="definition">Current symbol definition to compare with the captured slot contract.</param>
        /// <returns>True when the current input and output definitions match the captured contract.</returns>
        internal bool Matches(Symbol definition)
        {
            return definition.Id == SymbolId && SymbolAnalysis.IsReroute(definition)
                   && definition.InputDefinitions[0].Id == InputId
                   && definition.InputDefinitions[0].ValueType == InputValue.ValueType
                   && definition.OutputDefinitions[0].Id == OutputId;
        }

        /// <summary>Definition identity used to resolve the current symbol during replay.</summary>
        internal readonly Guid SymbolId;
        /// <summary>Persistent child identity restored by undo.</summary>
        internal readonly Guid ChildId;
        /// <summary>Child name captured before removal.</summary>
        internal readonly string Name;
        /// <summary>Captured bypass state restored with the child.</summary>
        internal readonly bool IsBypassed;
        /// <summary>Persistent input slot identity required when restoring the value.</summary>
        internal readonly Guid InputId;
        /// <summary>Cloned input value retained independently of the removed child.</summary>
        internal readonly InputValue InputValue;
        /// <summary>Whether the captured input uses its definition default.</summary>
        internal readonly bool InputIsDefault;
        /// <summary>Persistent output slot identity required when restoring settings.</summary>
        internal readonly Guid OutputId;
        /// <summary>Captured output disable state.</summary>
        internal readonly bool OutputIsDisabled;
        /// <summary>Captured output invalidation trigger.</summary>
        internal readonly DirtyFlagTrigger OutputDirtyTrigger;
        /// <summary>Saved top-left position in canvas coordinates.</summary>
        internal readonly Vector2 Position;
        /// <summary>Saved child size, including the compact reroute dimensions.</summary>
        internal readonly Vector2 Size;
        /// <summary>Saved section membership.</summary>
        internal readonly Guid SectionId;
        /// <summary>Saved child display style.</summary>
        internal readonly SymbolUi.Child.Styles Style;
        /// <summary>Saved optional child comment.</summary>
        internal readonly string? Comment;
        /// <summary>Saved parameter snapshot group.</summary>
        internal readonly int SnapshotGroupIndex;
        /// <summary>Copied input IDs enabled for parameter snapshots, preserving an unset selection.</summary>
        internal readonly Guid[]? SnapshotEnabledInputIds;
        /// <summary>Copied per-connection display overrides.</summary>
        internal readonly KeyValuePair<Guid, SymbolUi.Child.ConnectionStyles>[] ConnectionStyles;
    }

    /// <summary>Captures distinct candidate IDs before editing; removal is deferred until macro completion.</summary>
    /// <param name="compositionId">ID of the composition to resolve whenever the command executes.</param>
    /// <param name="candidateIds">IDs of reroutes that were connected before the enclosing edit.</param>
    public RemoveDisconnectedReroutesCommand(Guid compositionId, IEnumerable<Guid> candidateIds)
    {
        _compositionId = compositionId;
        _candidateIds = candidateIds.Distinct().ToArray();
    }

    /// <summary>Label shown in undo history.</summary>
    public string Name => "Remove Disconnected Reroutes";
    /// <summary>Cleanup can restore its captured children through undo.</summary>
    public bool IsUndoable => true;
    /// <summary>Nonzero only while deletion is applied; callers use this to avoid recording an empty cleanup.</summary>
    internal int AppliedCount { get; private set; }

    /// <summary>Removes newly isolated candidates and fixes the first successful snapshot set for redo.</summary>
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
                    || !composition.Symbol.Children.TryGetValue(id, out var child) || !SymbolAnalysis.IsReroute(child.Symbol))
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

    /// <summary>Restores captured anchors and metadata before earlier macro commands reconnect their wires.</summary>
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

    /// <summary>Resolves the current editable composition by ID and reports an unavailable graph.</summary>
    /// <param name="composition">Resolved editable composition UI when true; not usable when false.</param>
    /// <returns>True when the composition is still registered and editable.</returns>
    private bool TryGetComposition(out SymbolUi? composition)
    {
        if (SymbolUiRegistry.TryGetSymbolUi(_compositionId, out composition) && !composition.ReadOnly
            && !composition.Symbol.SymbolPackage.IsReadOnly)
            return true;

        Log.Warning("Disconnected reroute cleanup skipped because its editable graph is unavailable.");
        return false;
    }

    /// <summary>Resolves and validates every captured definition before any replay mutation.</summary>
    /// <param name="snapshots">Captured children whose operator definitions are needed for restoration.</param>
    /// <param name="definitions">Definitions resolved by symbol ID; may be incomplete when false.</param>
    /// <returns>True when every required definition exists and matches its captured slot contract.</returns>
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

    /// <summary>Collects both endpoints so either incoming or outgoing wiring prevents cleanup.</summary>
    /// <param name="composition">Composition whose wires are inspected for connected children.</param>
    /// <returns>IDs of children appearing at either endpoint of at least one connection.</returns>
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

    /// <summary>Recreates an anchor with its original identity, slot settings, and editor metadata.</summary>
    /// <param name="composition">Composition UI into which the child is restored.</param>
    /// <param name="definition">Current operator definition used to recreate the child.</param>
    /// <param name="snapshot">Captured identity, presentation, input values, and output settings to restore.</param>
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

    /// <summary>Removes any model or UI fragment left by a failed child restore.</summary>
    /// <param name="composition">Composition UI containing a partially restored child.</param>
    /// <param name="id">ID of the partial child to remove after a failed restore.</param>
    private static void RemovePartialChild(SymbolUi composition, Guid id)
    {
        if (composition.Symbol.Children.ContainsKey(id) || composition.ChildUis.ContainsKey(id))
            composition.RemoveChild(id);
    }

    /// <summary>
    /// Refreshes the focused view and every registered graph window showing this composition.
    /// This includes the initiating canvas; selection removal is limited to the anchors this command deleted.
    /// </summary>
    /// <param name="removeSelection">True to remove deleted anchors from selection while refreshing all matching graph views.</param>
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

    /// <summary>Composition identity used to resolve live state on each execution.</summary>
    private readonly Guid _compositionId;
    /// <summary>Previously connected anchor IDs eligible for removal after editing.</summary>
    private readonly Guid[] _candidateIds;
    /// <summary>Snapshot set fixed by the first successful deletion and reused for undo and redo.</summary>
    private ChildSnapshot[]? _snapshots;
}
