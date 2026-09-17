#nullable enable
using System.Diagnostics.CodeAnalysis;
using T3.Core.Model;
using T3.Core.Operator;
using T3.Core.Operator.Slots;
using T3.Editor.Gui.MagGraph.Model;
using T3.Editor.Gui.MagGraph.States;
using T3.Editor.UiModel;
using T3.Editor.UiModel.Helpers;
using T3.Editor.UiModel.Commands;
using T3.Editor.UiModel.Commands.Graph;

namespace T3.Editor.Gui.MagGraph.Interaction;

/// <summary>
/// Builds undoable cuts and typed reroute insertions from validated connection snapshots,
/// preserving source-slot groups, target input order, and duplicate wires.
/// </summary>
internal static class RerouteOperations
{
    /// <summary>Identifies one ordered wire; the multi-input index distinguishes duplicates and an empty child ID denotes the composition.</summary>
    /// <param name="SourceId">Source child ID, or Guid.Empty for a composition input.</param>
    /// <param name="SourceSlotId">Source output or composition input slot ID.</param>
    /// <param name="TargetId">Target child ID, or Guid.Empty for a composition output.</param>
    /// <param name="TargetSlotId">Target input or composition output slot ID.</param>
    /// <param name="MultiInputIndex">Occurrence index within the target input's ordered connections.</param>
    internal readonly record struct ConnectionOccurrence(Guid SourceId, Guid SourceSlotId, Guid TargetId, Guid TargetSlotId, int MultiInputIndex);
    /// <summary>Pairs a wire occurrence with its canvas-space crossing position.</summary>
    /// <param name="Occurrence">Exact ordered wire occurrence crossed by the stroke.</param>
    /// <param name="PositionOnCanvas">Accepted crossing position in canvas coordinates.</param>
    internal readonly record struct StrokeHit(ConnectionOccurrence Occurrence, Vector2 PositionOnCanvas);

    /// <summary>
    /// Reuses grouping storage while previewing a stroke. Each source slot gets the average of its
    /// distinct wire hits in canvas coordinates; overlapping groups are separated vertically.
    /// Preview and commit use the same calculation so the inserted anchors match the preview.
    /// </summary>
    internal sealed class AnchorPlacementBuffer
    {
        /// <summary>Reserves source-group and deduplication storage before preview updates.</summary>
        /// <param name="count">Maximum number of hits for which reusable placement storage should be reserved.</param>
        internal void EnsureCapacity(int count)
        {
            _groups.EnsureCapacity(count);
            _counts.EnsureCapacity(count);
            _seen.EnsureCapacity(count);
        }

        /// <summary>Averages distinct hits per source slot and separates overlapping groups using the shared placement rule.</summary>
        /// <param name="hits">Crossed occurrences and their canvas-space hit positions.</param>
        /// <param name="positions">Destination list replaced with one nonoverlapping anchor position per distinct source.</param>
        internal void Calculate(IReadOnlyList<StrokeHit> hits, List<Vector2> positions)
        {
            positions.Clear();
            _groups.Clear();
            _counts.Clear();
            _seen.Clear();
            for (var hitIndex = 0; hitIndex < hits.Count; hitIndex++)
            {
                var hit = hits[hitIndex];
                if (!_seen.Add(hit.Occurrence))
                    continue;

                var source = SourceOf(hit.Occurrence);
                if (!_groups.TryGetValue(source, out var index))
                {
                    index = positions.Count;
                    _groups.Add(source, index);
                    positions.Add(Vector2.Zero);
                    _counts.Add(0);
                }

                positions[index] += hit.PositionOnCanvas;
                _counts[index]++;
            }

            var spacing = MagGraphItem.RerouteSize + new Vector2(8);
            for (var index = 0; index < positions.Count; index++)
            {
                var position = positions[index] / _counts[index];
                for (var other = 0; other < index; other++)
                {
                    var distance = Vector2.Abs(position - positions[other]);
                    if (distance.X >= spacing.X || distance.Y >= spacing.Y)
                        continue;

                    position.Y = positions[other].Y + spacing.Y;
                    other = -1;
                }

                positions[index] = position;
            }
        }

        /// <summary>Maps each source slot to its placement index in encounter order.</summary>
        private readonly Dictionary<Endpoint, int> _groups = new();
        /// <summary>Number of distinct wire hits contributing to each source-group average.</summary>
        private readonly List<int> _counts = new();
        /// <summary>Deduplicates ordered wire occurrences within one placement calculation.</summary>
        private readonly HashSet<ConnectionOccurrence> _seen = new();
    }

    /// <summary>Identifies a child slot or a composition boundary slot without retaining live objects.</summary>
    /// <param name="ChildId">Child ID, or Guid.Empty for a composition interface slot.</param>
    /// <param name="SlotId">Slot ID within the child or composition interface.</param>
    private readonly record struct Endpoint(Guid ChildId, Guid SlotId);
    /// <summary>Sources is an ordered list, including duplicates, rather than a set of upstream endpoints.</summary>
    /// <param name="Target">Target input whose incoming connection order is captured.</param>
    /// <param name="Sources">Source endpoints in their original order, including duplicates.</param>
    private sealed record TargetSnapshot(Endpoint Target, Endpoint[] Sources);
    /// <summary>Describes a child or wire mutation with the IDs needed to validate its replay.</summary>
    /// <param name="Command">Undoable child or wire mutation represented by this step.</param>
    /// <param name="Connection">Ordered wire occurrence for a wire step, or null for a child step.</param>
    /// <param name="AddsConnection">Whether a wire step adds rather than removes its occurrence.</param>
    /// <param name="ChildId">Child identity used to verify a child mutation.</param>
    /// <param name="SymbolId">Operator definition identity used to verify a child mutation.</param>
    /// <param name="AddsChild">Whether a child step adds rather than removes its child.</param>
    private sealed record CommandStep(ICommand Command, ConnectionOccurrence? Connection, bool AddsConnection, Guid ChildId, Guid SymbolId,
                                      bool AddsChild = true);

    /// <summary>Copies the displayed wire endpoints and target occurrence index for later validation.</summary>
    /// <param name="connection">Displayed wire whose endpoint IDs and multi-input ordinal are captured.</param>
    /// <returns>Value snapshot identifying this exact ordered connection occurrence.</returns>
    internal static ConnectionOccurrence Capture(MagGraphConnection connection)
    {
        return new ConnectionOccurrence(connection.SourceParentOrChildId, connection.SourceOutput.Id,
                                        connection.TargetParentOrChildId, connection.TargetInput.Id, connection.MultiInputIndex);
    }

    /// <summary>Executes pending cleanup and appends it last so undo restores anchors before their connections.</summary>
    /// <returns>Whether cleanup removed anchors and the initiating layout needs a refresh.</returns>
    /// <param name="cleanup">Cleanup captured before the edit, or null when no cleanup was prepared.</param>
    /// <param name="macro">Enclosing undo group to which applied cleanup is appended.</param>
    internal static bool CompleteCleanup(RemoveDisconnectedReroutesCommand? cleanup, MacroCommand macro)
    {
        if (cleanup == null)
            return false;

        cleanup.Do();
        if (cleanup.AppliedCount == 0)
            return false;

        macro.AddExecutedCommandForUndo(cleanup);
        return true;
    }

    /// <summary>Captures connected anchors before editing so cleanup leaves deliberately blank anchors alone.</summary>
    /// <param name="symbol">Composition whose currently connected reroutes are captured.</param>
    /// <returns>IDs of recognized reroute children with at least one incoming or outgoing connection.</returns>
    internal static HashSet<Guid> CaptureConnectedReroutes(Symbol symbol)
    {
        var connectedChildren = new HashSet<Guid>();
        foreach (var connection in symbol.Connections)
        {
            if (connection.SourceParentOrChildId != Guid.Empty)
                connectedChildren.Add(connection.SourceParentOrChildId);
            if (connection.TargetParentOrChildId != Guid.Empty)
                connectedChildren.Add(connection.TargetParentOrChildId);
        }

        var reroutes = new HashSet<Guid>();
        foreach (var (id, child) in symbol.Children)
        {
            if (connectedChildren.Contains(id) && SymbolAnalysis.IsReroute(child.Symbol))
                reroutes.Add(id);
        }

        return reroutes;
    }

    /// <summary>Calculates canvas-space anchor positions using the same source-group rule as insertion.</summary>
    /// <param name="hits">Crossed occurrences and their canvas-space hit positions.</param>
    /// <param name="positions">Destination list replaced with the resulting anchor positions.</param>
    internal static void GetAnchorPositions(IReadOnlyList<StrokeHit> hits, List<Vector2> positions)
    {
        GetAnchorPositions(hits, positions, new AnchorPlacementBuffer());
    }

    /// <summary>Calculates canvas-space anchor positions using the same source-group rule as insertion.</summary>
    /// <param name="hits">Crossed occurrences and their canvas-space hit positions.</param>
    /// <param name="positions">Destination list replaced with the resulting anchor positions.</param>
    /// <param name="buffer">Reusable grouping and placement storage owned by the caller.</param>
    internal static void GetAnchorPositions(IReadOnlyList<StrokeHit> hits, List<Vector2> positions, AnchorPlacementBuffer buffer)
    {
        buffer.Calculate(hits, positions);
    }

    /// <summary>
    /// Validates every captured occurrence before changing the graph. A cut removes only crossed
    /// occurrences; insertion replaces their source at the same target index and adds one anchor
    /// per source slot. Only a successfully applied edit is added to the undo stack.
    /// </summary>
    /// <param name="context">Graph context providing the current composition, layout, selection, and interaction state.</param>
    /// <param name="cut">True to delete hit occurrences; false to route them through new typed anchors.</param>
    /// <param name="hits">Crossed wire occurrences and their canvas-space hit positions.</param>
    /// <param name="error">Validation or execution failure explanation; empty on success.</param>
    /// <returns>True when the complete routing or cutting edit succeeds.</returns>
    internal static bool TryApply(GraphUiContext context, bool cut, IReadOnlyList<StrokeHit> hits, out string error)
    {
        error = string.Empty;
        var composition = context.ProjectView.CompositionInstance;
        if (composition == null || context.PreventInteraction || composition.Symbol.SymbolPackage.IsReadOnly)
        {
            error = "This graph cannot be edited.";
            return false;
        }

        var symbol = composition.Symbol;
        if (!SymbolUiRegistry.TryGetSymbolUi(symbol.Id, out _))
        {
            error = "The graph is no longer available.";
            return false;
        }

        var occurrences = new List<ConnectionOccurrence>();
        var seen = new HashSet<ConnectionOccurrence>();
        var groupTypes = new Dictionary<Endpoint, Type>();
        foreach (var hit in hits)
        {
            var occurrence = hit.Occurrence;
            if (!seen.Add(occurrence))
                continue;

            if (occurrence.MultiInputIndex < 0 || !MatchesOccurrence(symbol, occurrence))
            {
                error = "A crossed connection changed. Try the gesture again.";
                return false;
            }

            if (!TryGetConnectionType(symbol, occurrence, !cut, out var type, out error))
                return false;

            if (!float.IsFinite(hit.PositionOnCanvas.X) || !float.IsFinite(hit.PositionOnCanvas.Y))
            {
                error = "The anchor position is invalid.";
                return false;
            }

            groupTypes.TryAdd(SourceOf(occurrence), type);
            occurrences.Add(occurrence);
        }

        if (occurrences.Count == 0)
            return false;

        var definitions = new Dictionary<Type, SymbolAnalysis.RerouteDefinition>();
        if (!cut && !CollectDefinitions(groupTypes.Values, definitions, out error))
            return false;

        var steps = new List<CommandStep>();
        var targets = new Dictionary<Endpoint, List<Endpoint>>();
        foreach (var occurrence in occurrences)
        {
            var target = TargetOf(occurrence);
            if (!targets.ContainsKey(target))
                targets.Add(target, GetSources(symbol, target));
        }

        var before = SnapshotTargets(targets);
        if (cut)
        {
            occurrences.Sort(CompareForDeletion);
            foreach (var occurrence in occurrences)
            {
                steps.Add(ConnectionStep(symbol, occurrence, false));
                targets[TargetOf(occurrence)].RemoveAt(occurrence.MultiInputIndex);
            }

            var isolatedReroutes = CaptureConnectedReroutes(symbol);
            var targetOrdinals = new Dictionary<Endpoint, int>();
            foreach (var connection in symbol.Connections)
            {
                var target = new Endpoint(connection.TargetParentOrChildId, connection.TargetSlotId);
                var ordinal = targetOrdinals.GetValueOrDefault(target);
                targetOrdinals[target] = ordinal + 1;
                var occurrence = new ConnectionOccurrence(connection.SourceParentOrChildId, connection.SourceSlotId,
                                                          connection.TargetParentOrChildId, connection.TargetSlotId, ordinal);
                if (seen.Contains(occurrence))
                    continue;

                isolatedReroutes.Remove(connection.SourceParentOrChildId);
                isolatedReroutes.Remove(connection.TargetParentOrChildId);
            }

            foreach (var id in isolatedReroutes)
            {
                var cleanup = new RemoveDisconnectedReroutesCommand(symbol.Id, [id]);
                steps.Add(new CommandStep(cleanup, null, false, id, symbol.Children[id].Symbol.Id, AddsChild: false));
            }
        }
        else
        {
            var positions = new List<Vector2>();
            GetAnchorPositions(hits, positions);
            var reroutes = new Dictionary<Endpoint, (Guid childId, SymbolAnalysis.RerouteDefinition definition)>();
            var groupIndex = 0;
            foreach (var (source, type) in groupTypes)
            {
                var definition = definitions[type];
                var addChild = new AddSymbolChildCommand(symbol, definition.SymbolId)
                                   {
                                       PosOnCanvas = positions[groupIndex++] - MagGraphItem.RerouteSize / 2,
                                       Size = MagGraphItem.RerouteSize,
                                   };
                steps.Add(new CommandStep(addChild, null, false, addChild.AddedChildId, definition.SymbolId));
                var input = new ConnectionOccurrence(source.ChildId, source.SlotId, addChild.AddedChildId, definition.InputId, 0);
                steps.Add(ConnectionStep(symbol, input, true));
                targets.Add(TargetOf(input), [source]);
                reroutes.Add(source, (addChild.AddedChildId, definition));
            }

            foreach (var occurrence in occurrences)
            {
                var (childId, definition) = reroutes[SourceOf(occurrence)];
                var replacement = occurrence with { SourceId = childId, SourceSlotId = definition.OutputId };
                steps.Add(ConnectionStep(symbol, occurrence, false));
                steps.Add(ConnectionStep(symbol, replacement, true));
                targets[TargetOf(occurrence)][occurrence.MultiInputIndex] = SourceOf(replacement);
            }
        }

        var command = new RoutingCommand(symbol.Id, cut ? "Cut Connections" : "Reroute Connections", steps.ToArray(), before, SnapshotTargets(targets));
        if (!command.TryExecute(false, out error))
            return false;

        UndoRedoStack.Add(command);
        context.Layout.FlagStructureAsChanged();
        if (!cut)
        {
            context.Selector.Clear();
            foreach (var step in steps)
            {
                if (step.ChildId != Guid.Empty)
                    context.Selector.TrySelectCompositionChild(composition, step.ChildId);
            }
        }
        else
        {
            for (var index = context.Selector.Selection.Count - 1; index >= 0; index--)
            {
                var selected = context.Selector.Selection[index];
                if (selected is SymbolUi.Child && !symbol.Children.ContainsKey(selected.Id))
                    context.Selector.DeselectNode(selected);
            }
        }

        return true;
    }

    /// <summary>Finds one registered, validated anchor definition for each required value type.</summary>
    /// <param name="types">Distinct slot value types that require a supported reroute definition.</param>
    /// <param name="definitions">Destination map populated with the validated definition for each requested type.</param>
    /// <param name="error">Explanation of an unsupported or missing definition; empty on success.</param>
    /// <returns>True when every requested value type has a valid reroute definition.</returns>
    private static bool CollectDefinitions(IEnumerable<Type> types, Dictionary<Type, SymbolAnalysis.RerouteDefinition> definitions, out string error)
    {
        error = string.Empty;
        foreach (var package in SymbolPackage.AllPackages)
        {
            if (package.Id != SymbolAnalysis.TypeOperatorsPackageId)
                continue;

            foreach (var symbol in package.Symbols.Values)
            {
                if (!SymbolAnalysis.TryGetRerouteDefinition(symbol, out var definition) || !SymbolUiRegistry.TryGetSymbolUi(symbol.Id, out _))
                    continue;

                var type = symbol.InputDefinitions[0].ValueType;
                if (!definitions.TryAdd(type, definition))
                {
                    error = $"More than one reroute definition is available for {type.Name}.";
                    return false;
                }
            }
        }

        foreach (var type in types)
        {
            if (!definitions.ContainsKey(type))
            {
                error = $"No reroute is available for {type.Name}.";
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Reroute insertion requires value-only wiring: a scalar reroute cannot preserve a composition
    /// multi-input bundle or output metadata. Cutting may remove either. plannedChildren supplies
    /// definitions for anchors that the command will create or restore but that are not live yet.
    /// </summary>
    /// <param name="symbol">Composition owning the current or planned connection.</param>
    /// <param name="occurrence">Ordered occurrence whose endpoint slot types are checked.</param>
    /// <param name="insertingReroutes">Whether the check must enforce the constraints for inserting reroutes.</param>
    /// <param name="type">Matching slot value type when true; not usable when false.</param>
    /// <param name="error">Explanation of a missing or incompatible endpoint; empty on success.</param>
    /// <param name="plannedChildren">Definitions for children planned by the edit but not yet present, or null when checking only existing children.</param>
    /// <returns>True when both endpoint contracts permit the requested operation.</returns>
    private static bool TryGetConnectionType(Symbol symbol, ConnectionOccurrence occurrence, bool insertingReroutes, [NotNullWhen(true)] out Type? type, out string error,
                                             IReadOnlyDictionary<Guid, Symbol>? plannedChildren = null)
    {
        type = null;
        error = string.Empty;
        Symbol sourceSymbol;
        if (occurrence.SourceId == Guid.Empty)
        {
            sourceSymbol = symbol;
        }
        else if (symbol.Children.TryGetValue(occurrence.SourceId, out var sourceChild))
        {
            sourceSymbol = sourceChild.Symbol;
        }
        else if (plannedChildren != null && plannedChildren.TryGetValue(occurrence.SourceId, out var plannedSource))
        {
            sourceSymbol = plannedSource;
        }
        else
        {
            error = "A connection source is no longer available.";
            return false;
        }

        if (occurrence.SourceId == Guid.Empty)
        {
            var input = sourceSymbol.InputDefinitions.Find(i => i.Id == occurrence.SourceSlotId);
            if (input != null)
            {
                if (insertingReroutes && input.IsMultiInput)
                {
                    error = "A composition multi-input bundle cannot be rerouted.";
                    return false;
                }

                type = input.ValueType;
            }
        }
        else
        {
            var output = sourceSymbol.OutputDefinitions.Find(o => o.Id == occurrence.SourceSlotId);
            if (insertingReroutes && output?.OutputDataType != null)
            {
                error = "Connections carrying output metadata cannot be rerouted.";
                return false;
            }

            type = output?.ValueType;
        }

        Type? targetType = null;
        if (occurrence.TargetId == Guid.Empty)
        {
            if (occurrence.MultiInputIndex == 0)
                targetType = symbol.OutputDefinitions.Find(o => o.Id == occurrence.TargetSlotId)?.ValueType;
        }
        else
        {
            var targetSymbol = symbol.Children.TryGetValue(occurrence.TargetId, out var targetChild)
                                   ? targetChild.Symbol
                                   : plannedChildren?.GetValueOrDefault(occurrence.TargetId);
            var targetInput = targetSymbol?.InputDefinitions.Find(i => i.Id == occurrence.TargetSlotId);
            if (targetInput != null && (targetInput.IsMultiInput || occurrence.MultiInputIndex == 0))
                targetType = targetInput.ValueType;
        }

        if (type == null || targetType != type)
        {
            error = "A connection slot is missing or its type changed.";
            return false;
        }

        return true;
    }

    /// <summary>Creates an add or delete command for the exact target occurrence index.</summary>
    /// <param name="symbol">Composition on which the wire command operates.</param>
    /// <param name="occurrence">Exact occurrence to add or remove.</param>
    /// <param name="add">True to add the occurrence; false to remove it.</param>
    /// <returns>Replay step containing the wire command and its expected topology change.</returns>
    private static CommandStep ConnectionStep(Symbol symbol, ConnectionOccurrence occurrence, bool add)
    {
        var connection = new Symbol.Connection(occurrence.SourceId, occurrence.SourceSlotId, occurrence.TargetId, occurrence.TargetSlotId);
        ICommand command = add
                               ? new AddConnectionCommand(symbol, connection, occurrence.MultiInputIndex)
                               : new DeleteConnectionCommand(symbol, connection, occurrence.MultiInputIndex);
        return new CommandStep(command, occurrence, add, Guid.Empty, Guid.Empty);
    }

    /// <summary>Returns the source slot identity of a wire occurrence.</summary>
    /// <param name="occurrence">Occurrence whose source identity is requested.</param>
    /// <returns>Source child/interface ID and slot ID.</returns>
    private static Endpoint SourceOf(ConnectionOccurrence occurrence) => new(occurrence.SourceId, occurrence.SourceSlotId);
    /// <summary>Returns the target slot identity of a wire occurrence.</summary>
    /// <param name="occurrence">Occurrence whose target identity is requested.</param>
    /// <returns>Target child/interface ID and slot ID.</returns>
    private static Endpoint TargetOf(ConnectionOccurrence occurrence) => new(occurrence.TargetId, occurrence.TargetSlotId);
    /// <summary>Orders deletions by target and descending input index so removals do not shift later occurrences.</summary>
    /// <param name="a">First occurrence to order for deletion.</param>
    /// <param name="b">Second occurrence to order for deletion.</param>
    /// <returns>Comparison result grouping target inputs and deleting higher ordinals before lower ones.</returns>
    private static int CompareForDeletion(ConnectionOccurrence a, ConnectionOccurrence b)
    {
        var comparison = a.TargetId.CompareTo(b.TargetId);
        if (comparison != 0)
            return comparison;

        comparison = a.TargetSlotId.CompareTo(b.TargetSlotId);
        return comparison != 0 ? comparison : b.MultiInputIndex.CompareTo(a.MultiInputIndex);
    }

    /// <summary>Reads upstream endpoints in target-input order, retaining duplicates.</summary>
    /// <param name="symbol">Composition containing the target input's connections.</param>
    /// <param name="target">Input endpoint whose incoming connections are read.</param>
    /// <returns>Source endpoints in the target input's connection order, retaining duplicates.</returns>
    private static List<Endpoint> GetSources(Symbol symbol, Endpoint target)
    {
        var sources = new List<Endpoint>();
        foreach (var connection in symbol.Connections)
        {
            if (connection.IsTargetOf(target.ChildId, target.SlotId))
                sources.Add(new Endpoint(connection.SourceParentOrChildId, connection.SourceSlotId));
        }

        return sources;
    }

    /// <summary>Copies ordered input lists into immutable replay snapshots.</summary>
    /// <param name="targets">Target inputs mapped to their ordered incoming source endpoints.</param>
    /// <returns>Snapshots of each target and its source order.</returns>
    private static TargetSnapshot[] SnapshotTargets(Dictionary<Endpoint, List<Endpoint>> targets)
    {
        var snapshots = new TargetSnapshot[targets.Count];
        var index = 0;
        foreach (var (target, sources) in targets)
            snapshots[index++] = new TargetSnapshot(target, sources.ToArray());
        return snapshots;
    }

    /// <summary>Checks that the source still occupies the captured target-input index.</summary>
    /// <param name="symbol">Composition whose current topology is checked.</param>
    /// <param name="occurrence">Expected wire endpoints and target ordinal.</param>
    /// <returns>True when the specified source still occupies the target occurrence.</returns>
    private static bool MatchesOccurrence(Symbol symbol, ConnectionOccurrence occurrence)
    {
        var index = 0;
        foreach (var connection in symbol.Connections)
        {
            if (!connection.IsTargetOf(occurrence.TargetId, occurrence.TargetSlotId))
                continue;

            if (index++ == occurrence.MultiInputIndex)
                return connection.IsSourceOf(occurrence.SourceId, occurrence.SourceSlotId);
        }

        return false;
    }

    /// <summary>
    /// Replays a routing edit using IDs and ordered before/after snapshots, resolving live symbols
    /// on each Do/Undo. _isApplied selects the expected state; undo reverses the step order.
    /// Stale contracts are rejected before mutation, each step's result is verified, and failures
    /// attempt to roll back completed steps rather than leaving an unchecked partial edit.
    /// </summary>
    private sealed class RoutingCommand : ICommand
    {
        /// <summary>Label shown in routing undo history.</summary>
        public string Name { get; }
        /// <summary>Routing edits retain enough ordered state to replay in either direction.</summary>
        public bool IsUndoable => true;

        /// <summary>Stores the ordered edit steps and before/after state under the composition ID.</summary>
        /// <param name="compositionId">ID of the composition to resolve on each execution.</param>
        /// <param name="name">Label shown for the edit in undo history.</param>
        /// <param name="steps">Child and wire mutations in forward execution order.</param>
        /// <param name="before">Expected target input orders before applying the edit.</param>
        /// <param name="after">Expected target input orders after applying the edit.</param>
        internal RoutingCommand(Guid compositionId, string name, CommandStep[] steps, TargetSnapshot[] before, TargetSnapshot[] after)
        {
            _compositionId = compositionId;
            Name = name;
            _steps = steps;
            _before = before;
            _after = after;
        }

        /// <summary>Applies or replays the routing edit and reports a rejected or failed execution.</summary>
        public void Do()
        {
            if (!TryExecute(false, out var error))
                Log.Warning($"{Name}: {error}");
        }

        /// <summary>Reverses the routing edit and reports a rejected or failed restoration.</summary>
        public void Undo()
        {
            if (!TryExecute(true, out var error))
                Log.Warning($"Undo {Name}: {error}");
        }

        /// <summary>Validates current state, executes in the requested order, and rolls back completed steps on failure.</summary>
        /// <param name="undo">True to undo completed steps in reverse order; false to apply them forward.</param>
        /// <param name="error">Failure explanation if validation or execution fails; empty on success.</param>
        /// <returns>True when the requested replay succeeds or is already in the requested state.</returns>
        internal bool TryExecute(bool undo, out string error)
        {
            error = string.Empty;
            if (_isApplied != undo)
                return true;

            if (!SymbolUiRegistry.TryGetSymbolUi(_compositionId, out var ui) || ui.Symbol.SymbolPackage.IsReadOnly)
            {
                error = "The editable graph is no longer available.";
                return false;
            }

            if (!MatchesState(ui, undo) || !ValidateSlotContracts(ui.Symbol))
            {
                error = "The affected connections or reroute definitions changed.";
                return false;
            }

            var completed = new List<CommandStep>(_steps.Length);
            try
            {
                for (var order = 0; order < _steps.Length; order++)
                {
                    var step = _steps[undo ? _steps.Length - order - 1 : order];
                    var countBefore = ui.Symbol.Connections.Count;
                    var childBefore = ui.Symbol.Children.ContainsKey(step.ChildId);
                    try
                    {
                        ExecuteStep(ui, step, undo);
                        completed.Add(step);
                    }
                    catch
                    {
                        // A subcommand can mutate its definition before failing while updating instances.
                        if (ui.Symbol.Connections.Count != countBefore || ui.Symbol.Children.ContainsKey(step.ChildId) != childBefore)
                            completed.Add(step);
                        throw;
                    }
                }

                if (!MatchesState(ui, !undo))
                    throw new InvalidOperationException("The resulting connections do not match the requested edit.");

                _isApplied = !undo;
                return true;
            }
            catch (Exception exception)
            {
                error = $"The graph edit could not be completed: {exception.Message}";
                for (var index = completed.Count - 1; index >= 0; index--)
                {
                    try
                    {
                        ExecuteStep(ui, completed[index], !undo);
                    }
                    catch (Exception rollbackException)
                    {
                        Log.Error($"Could not restore a routing edit: {rollbackException.Message}");
                    }
                }

                if (!MatchesState(ui, undo))
                    error += " Some changes could not be restored; inspect the graph before continuing.";
                return false;
            }
        }

        /// <summary>Checks ordered target inputs and created anchor identities against the expected replay state.</summary>
        /// <param name="ui">Current composition UI against which replay state is checked.</param>
        /// <param name="applied">True to require the post-edit state; false to require the pre-edit state.</param>
        /// <returns>True when target connection orders and anchor identities match the requested state.</returns>
        private bool MatchesState(SymbolUi ui, bool applied)
        {
            foreach (var snapshot in applied ? _after : _before)
            {
                var sources = GetSources(ui.Symbol, snapshot.Target);
                if (!sources.SequenceEqual(snapshot.Sources))
                    return false;
            }

            foreach (var step in _steps)
            {
                if (step.ChildId == Guid.Empty)
                    continue;

                if (!SymbolUiRegistry.TryGetSymbolUi(step.SymbolId, out var definitionUi) || !SymbolAnalysis.IsReroute(definitionUi.Symbol))
                    return false;

                var shouldExist = step.AddsChild == applied;
                var exists = ui.Symbol.Children.TryGetValue(step.ChildId, out var child);
                if (exists != shouldExist || ui.ChildUis.ContainsKey(step.ChildId) != shouldExist || exists && child!.Symbol.Id != step.SymbolId)
                    return false;
            }

            return true;
        }

        /// <summary>Checks current and planned child definitions before replaying wire mutations.</summary>
        /// <param name="symbol">Current composition whose existing and planned child slots are validated.</param>
        /// <returns>True when every wire step still has compatible endpoint contracts.</returns>
        private bool ValidateSlotContracts(Symbol symbol)
        {
            var plannedChildren = new Dictionary<Guid, Symbol>();
            var addsReroutes = false;
            foreach (var step in _steps)
            {
                if (step.ChildId == Guid.Empty)
                    continue;

                if (!SymbolUiRegistry.TryGetSymbolUi(step.SymbolId, out var definitionUi))
                    return false;

                plannedChildren.Add(step.ChildId, definitionUi.Symbol);
                addsReroutes |= step.AddsChild;
            }

            foreach (var step in _steps)
            {
                if (step.Connection is { } connection
                    && !TryGetConnectionType(symbol, connection, addsReroutes, out _, out _, plannedChildren))
                    return false;
            }

            return true;
        }

        /// <summary>Runs one child or wire command and verifies that its expected topology change occurred.</summary>
        /// <param name="ui">Current composition UI used to verify the step's effect.</param>
        /// <param name="step">Child or wire mutation to execute.</param>
        /// <param name="undo">True to invoke the reverse mutation; false to invoke the forward mutation.</param>
        private static void ExecuteStep(SymbolUi ui, CommandStep step, bool undo)
        {
            if (step.Connection is not { } connection)
            {
                if (undo)
                {
                    step.Command.Undo();
                }
                else
                {
                    step.Command.Do();
                }

                var shouldExist = step.AddsChild != undo;
                if (ui.Symbol.Children.ContainsKey(step.ChildId) != shouldExist || ui.ChildUis.ContainsKey(step.ChildId) != shouldExist)
                    throw new InvalidOperationException("The reroute child was not updated.");
                return;
            }

            var add = step.AddsConnection != undo;
            if (!TryGetConnectionType(ui.Symbol, connection, false, out _, out var error))
                throw new InvalidOperationException(error);

            var target = TargetOf(connection);
            var before = GetSources(ui.Symbol, target);
            var expected = new List<Endpoint>(before);
            if (add)
            {
                if (connection.MultiInputIndex > expected.Count)
                    throw new InvalidOperationException("The connection insertion index changed.");

                expected.Insert(connection.MultiInputIndex, SourceOf(connection));
            }
            else
            {
                if (connection.MultiInputIndex >= expected.Count || expected[connection.MultiInputIndex] != SourceOf(connection))
                    throw new InvalidOperationException("The connection to remove changed.");

                expected.RemoveAt(connection.MultiInputIndex);
            }

            if (undo)
            {
                step.Command.Undo();
            }
            else
            {
                step.Command.Do();
            }

            if (!GetSources(ui.Symbol, target).SequenceEqual(expected))
                throw new InvalidOperationException("A connection command did not produce the expected input order.");
        }

        /// <summary>Composition identity resolved afresh for each execution.</summary>
        private readonly Guid _compositionId;
        /// <summary>Ordered mutations applied forward and undone in reverse.</summary>
        private readonly CommandStep[] _steps;
        /// <summary>Ordered target-input state required before applying the edit.</summary>
        private readonly TargetSnapshot[] _before;
        /// <summary>Ordered target-input state required before undoing the edit.</summary>
        private readonly TargetSnapshot[] _after;
        /// <summary>Selects the expected topology and prevents duplicate execution in the same direction.</summary>
        private bool _isApplied;
    }
}
