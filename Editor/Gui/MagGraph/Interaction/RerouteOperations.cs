#nullable enable
using System.Reflection;
using T3.Core.Model;
using T3.Core.Operator;
using T3.Core.Operator.Slots;
using T3.Editor.Gui.MagGraph.Model;
using T3.Editor.Gui.MagGraph.States;
using T3.Editor.UiModel;
using T3.Editor.UiModel.Commands;
using T3.Editor.UiModel.Commands.Graph;

namespace T3.Editor.Gui.MagGraph.Interaction;

internal static class RerouteOperations
{
    internal readonly record struct ConnectionOccurrence(Guid SourceId, Guid SourceSlotId, Guid TargetId, Guid TargetSlotId, int MultiInputIndex);
    internal readonly record struct StrokeHit(ConnectionOccurrence Occurrence, Vector2 PositionOnCanvas);

    internal sealed class AnchorPlacementBuffer
    {
        internal void EnsureCapacity(int count)
        {
            _groups.EnsureCapacity(count);
            _counts.EnsureCapacity(count);
            _seen.EnsureCapacity(count);
        }

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

        private readonly Dictionary<Endpoint, int> _groups = new();
        private readonly List<int> _counts = new();
        private readonly HashSet<ConnectionOccurrence> _seen = new();
    }

    private readonly record struct Endpoint(Guid ChildId, Guid SlotId);
    private readonly record struct Definition(Guid SymbolId, Guid InputId, Guid OutputId);
    private sealed record TargetSnapshot(Endpoint Target, Endpoint[] Sources);
    private sealed record CommandStep(ICommand Command, ConnectionOccurrence? Connection, bool AddsConnection, Guid ChildId, Guid SymbolId,
                                      bool AddsChild = true);
    private sealed record CollapseGuard(Guid DraggedId, Definition DraggedDefinition, Guid TargetId, Definition TargetDefinition,
                                        ConnectionOccurrence[] Before, ConnectionOccurrence[] After);

    internal static ConnectionOccurrence Capture(MagGraphConnection connection)
    {
        return new ConnectionOccurrence(connection.SourceParentOrChildId, connection.SourceOutput.Id,
                                        connection.TargetParentOrChildId, connection.TargetInput.Id, connection.MultiInputIndex);
    }

    internal static bool IsReroute(Symbol symbol)
    {
        return TryGetDefinition(symbol, out _);
    }

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
            if (connectedChildren.Contains(id) && IsReroute(child.Symbol))
                reroutes.Add(id);
        }

        return reroutes;
    }

    internal static void GetAnchorPositions(IReadOnlyList<StrokeHit> hits, List<Vector2> positions)
    {
        GetAnchorPositions(hits, positions, new AnchorPlacementBuffer());
    }

    internal static void GetAnchorPositions(IReadOnlyList<StrokeHit> hits, List<Vector2> positions, AnchorPlacementBuffer buffer)
    {
        buffer.Calculate(hits, positions);
    }

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

            groupTypes.TryAdd(SourceOf(occurrence), type!);
            occurrences.Add(occurrence);
        }

        if (occurrences.Count == 0)
            return false;

        var definitions = new Dictionary<Type, Definition>();
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
            var reroutes = new Dictionary<Endpoint, (Guid childId, Definition definition)>();
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

    internal static bool TryCollapse(GraphUiContext context, Guid draggedId, Guid targetId, out string error)
    {
        error = string.Empty;
        var composition = context.ProjectView.CompositionInstance;
        var macro = context.MacroCommand;
        if (composition == null || context.PreventInteraction || composition.Symbol.SymbolPackage.IsReadOnly || macro == null)
        {
            error = "The graph has no active editable move.";
            return false;
        }

        var symbol = composition.Symbol;
        if (draggedId == targetId
            || !SymbolUiRegistry.TryGetSymbolUi(symbol.Id, out var ui)
            || ui.ReadOnly
            || !symbol.Children.TryGetValue(draggedId, out var dragged)
            || !symbol.Children.TryGetValue(targetId, out var target)
            || !ui.ChildUis.ContainsKey(draggedId) || !ui.ChildUis.ContainsKey(targetId)
            || !TryGetDefinition(dragged.Symbol, out var draggedDefinition)
            || !TryGetDefinition(target.Symbol, out var targetDefinition)
            || dragged.Symbol.InputDefinitions[0].ValueType != target.Symbol.InputDefinitions[0].ValueType)
        {
            error = "Only two available reroutes of the same type can collapse.";
            return false;
        }

        var draggedInput = new Endpoint(draggedId, draggedDefinition.InputId);
        var targetInput = new Endpoint(targetId, targetDefinition.InputId);
        var targets = new Dictionary<Endpoint, List<Endpoint>>
                          {
                              [draggedInput] = GetSources(symbol, draggedInput),
                              [targetInput] = GetSources(symbol, targetInput),
                          };
        if (targets[draggedInput].Count > 1 || targets[targetInput].Count > 1)
        {
            error = "A reroute input has more than one connection.";
            return false;
        }

        // Resolve a direct chain through either anchor before choosing the surviving input.
        var sources = targets[targetInput].Count > 0 ? targets[targetInput] : targets[draggedInput];
        Endpoint? source = sources.Count == 0 ? null : sources[0];
        var visited = new HashSet<Guid>();
        while (source is { } endpoint && (endpoint.ChildId == draggedId || endpoint.ChildId == targetId))
        {
            var definition = endpoint.ChildId == draggedId ? draggedDefinition : targetDefinition;
            if (!visited.Add(endpoint.ChildId) || endpoint.SlotId != definition.OutputId)
            {
                error = "The reroutes contain a cyclic or invalid connection.";
                return false;
            }

            sources = targets[new Endpoint(endpoint.ChildId, definition.InputId)];
            source = sources.Count == 0 ? null : sources[0];
        }

        foreach (var connection in symbol.Connections)
        {
            if (connection.SourceParentOrChildId != draggedId && connection.SourceParentOrChildId != targetId)
                continue;

            var endpoint = new Endpoint(connection.TargetParentOrChildId, connection.TargetSlotId);
            if (!targets.ContainsKey(endpoint))
                targets.Add(endpoint, GetSources(symbol, endpoint));
        }

        var before = SnapshotTargets(targets);
        var beforeIncident = GetIncidentConnections(symbol, draggedId, targetId);
        targets[draggedInput].Clear();
        targets[targetInput].Clear();
        if (source is { } externalSource)
            targets[targetInput].Add(externalSource);

        var targetOutput = new Endpoint(targetId, targetDefinition.OutputId);
        foreach (var (endpoint, targetSources) in targets)
        {
            if (endpoint == draggedInput || endpoint == targetInput)
                continue;

            for (var index = 0; index < targetSources.Count; index++)
            {
                if (targetSources[index].ChildId == draggedId)
                    targetSources[index] = targetOutput;
            }
        }

        var steps = new List<CommandStep>();
        foreach (var snapshot in before)
        {
            var desired = targets[snapshot.Target];
            if (snapshot.Sources.Length == desired.Count)
            {
                for (var index = 0; index < desired.Count; index++)
                {
                    if (snapshot.Sources[index] == desired[index])
                        continue;

                    steps.Add(ConnectionStep(symbol, Occurrence(snapshot.Sources[index], snapshot.Target, index), false));
                    steps.Add(ConnectionStep(symbol, Occurrence(desired[index], snapshot.Target, index), true));
                }
            }
            else
            {
                for (var index = snapshot.Sources.Length - 1; index >= 0; index--)
                    steps.Add(ConnectionStep(symbol, Occurrence(snapshot.Sources[index], snapshot.Target, index), false));
                for (var index = 0; index < desired.Count; index++)
                    steps.Add(ConnectionStep(symbol, Occurrence(desired[index], snapshot.Target, index), true));
            }
        }

        var cleanup = new RemoveDisconnectedReroutesCommand(symbol.Id, [draggedId]);
        steps.Add(new CommandStep(cleanup, null, false, draggedId, draggedDefinition.SymbolId, AddsChild: false));
        var after = SnapshotTargets(targets);
        var guard = new CollapseGuard(draggedId, draggedDefinition, targetId, targetDefinition, beforeIncident,
                                      GetIncidentConnections(symbol, draggedId, targetId, after));
        var command = new RoutingCommand(symbol.Id, "Collapse Reroutes", steps.ToArray(), before, after, guard);
        if (!command.TryExecute(false, out error))
            return false;

        macro.AddExecutedCommandForUndo(command);
        context.Layout.FlagStructureAsChanged();
        context.Selector.TrySelectCompositionChild(composition, targetId);
        return true;
    }

    private static bool TryGetDefinition(Symbol symbol, out Definition definition)
    {
        definition = default;
        if (symbol.SymbolPackage.Id != TypeOperatorsPackageId || symbol.InputDefinitions.Count != 1 || symbol.OutputDefinitions.Count != 1
            || symbol.InputDefinitions[0].IsMultiInput || symbol.InputDefinitions[0].ValueType != symbol.OutputDefinitions[0].ValueType
            || symbol.OutputDefinitions[0].OutputDataType != null || symbol.Children.Count != 0)
            return false;

        var marked = false;
        foreach (var marker in symbol.InstanceType.GetInterfaces())
        {
            if (marker.FullName == MarkerName && marker.Assembly == symbol.InstanceType.Assembly)
            {
                marked = true;
                break;
            }
        }

        if (!marked)
            return false;

        var inputs = 0;
        var outputs = 0;
        foreach (var field in symbol.InstanceType.GetFields(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
        {
            if (!typeof(ISlot).IsAssignableFrom(field.FieldType))
                continue;

            if (!field.FieldType.IsGenericType || field.FieldType.GetGenericArguments()[0] != symbol.InputDefinitions[0].ValueType)
                return false;

            var genericType = field.FieldType.GetGenericTypeDefinition();
            if (genericType == typeof(InputSlot<>))
            {
                inputs++;
            }
            else if (genericType == typeof(Slot<>))
            {
                outputs++;
            }
            else
            {
                return false;
            }
        }

        if (inputs != 1 || outputs != 1)
            return false;

        definition = new Definition(symbol.Id, symbol.InputDefinitions[0].Id, symbol.OutputDefinitions[0].Id);
        return true;
    }

    private static bool CollectDefinitions(IEnumerable<Type> types, Dictionary<Type, Definition> definitions, out string error)
    {
        error = string.Empty;
        foreach (var package in SymbolPackage.AllPackages)
        {
            if (package.Id != TypeOperatorsPackageId)
                continue;

            foreach (var symbol in package.Symbols.Values)
            {
                if (!TryGetDefinition(symbol, out var definition) || !SymbolUiRegistry.TryGetSymbolUi(symbol.Id, out _))
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

    private static bool TryGetConnectionType(Symbol symbol, ConnectionOccurrence occurrence, bool merging, out Type? type, out string error,
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
                if (merging && input.IsMultiInput)
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
            if (merging && output?.OutputDataType != null)
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

    private static CommandStep ConnectionStep(Symbol symbol, ConnectionOccurrence occurrence, bool add)
    {
        var connection = new Symbol.Connection(occurrence.SourceId, occurrence.SourceSlotId, occurrence.TargetId, occurrence.TargetSlotId);
        ICommand command = add
                               ? new AddConnectionCommand(symbol, connection, occurrence.MultiInputIndex)
                               : new DeleteConnectionCommand(symbol, connection, occurrence.MultiInputIndex);
        return new CommandStep(command, occurrence, add, Guid.Empty, Guid.Empty);
    }

    private static Endpoint SourceOf(ConnectionOccurrence occurrence) => new(occurrence.SourceId, occurrence.SourceSlotId);
    private static Endpoint TargetOf(ConnectionOccurrence occurrence) => new(occurrence.TargetId, occurrence.TargetSlotId);
    private static ConnectionOccurrence Occurrence(Endpoint source, Endpoint target, int index)
        => new(source.ChildId, source.SlotId, target.ChildId, target.SlotId, index);

    private static List<ConnectionOccurrence> GetConnections(Symbol symbol, TargetSnapshot[]? replacements = null)
    {
        var replacedTargets = replacements == null ? null : new HashSet<Endpoint>(replacements.Select(s => s.Target));
        var ordinals = new Dictionary<Endpoint, int>();
        var connections = new List<ConnectionOccurrence>();
        foreach (var connection in symbol.Connections)
        {
            var target = new Endpoint(connection.TargetParentOrChildId, connection.TargetSlotId);
            if (replacedTargets?.Contains(target) == true)
                continue;

            var index = ordinals.GetValueOrDefault(target);
            ordinals[target] = index + 1;
            connections.Add(Occurrence(new Endpoint(connection.SourceParentOrChildId, connection.SourceSlotId), target, index));
        }

        if (replacements != null)
        {
            foreach (var snapshot in replacements)
            {
                for (var index = 0; index < snapshot.Sources.Length; index++)
                    connections.Add(Occurrence(snapshot.Sources[index], snapshot.Target, index));
            }
        }

        return connections;
    }

    private static ConnectionOccurrence[] GetIncidentConnections(Symbol symbol, Guid firstId, Guid secondId, TargetSnapshot[]? replacements = null)
    {
        var connections = GetConnections(symbol, replacements);
        connections.RemoveAll(c => c.SourceId != firstId && c.SourceId != secondId && c.TargetId != firstId && c.TargetId != secondId);
        connections.Sort(CompareForDeletion);
        return connections.ToArray();
    }

    private static bool IsAcyclic(Symbol symbol, TargetSnapshot[] replacements)
    {
        var downstream = new Dictionary<Guid, List<Guid>>();
        var remainingInputs = new Dictionary<Guid, int>();
        foreach (var connection in GetConnections(symbol, replacements))
        {
            if (connection.SourceId == Guid.Empty || connection.TargetId == Guid.Empty)
                continue;

            if (!downstream.TryGetValue(connection.SourceId, out var targets))
                downstream.Add(connection.SourceId, targets = new List<Guid>());
            targets.Add(connection.TargetId);
            remainingInputs.TryAdd(connection.SourceId, 0);
            remainingInputs[connection.TargetId] = remainingInputs.GetValueOrDefault(connection.TargetId) + 1;
        }

        var ready = new Queue<Guid>(remainingInputs.Where(pair => pair.Value == 0).Select(pair => pair.Key));
        var visited = 0;
        while (ready.TryDequeue(out var id))
        {
            visited++;
            if (!downstream.TryGetValue(id, out var targets))
                continue;

            foreach (var target in targets)
            {
                if (--remainingInputs[target] == 0)
                    ready.Enqueue(target);
            }
        }

        return visited == remainingInputs.Count;
    }

    private static int CompareForDeletion(ConnectionOccurrence a, ConnectionOccurrence b)
    {
        var comparison = a.TargetId.CompareTo(b.TargetId);
        if (comparison != 0)
            return comparison;

        comparison = a.TargetSlotId.CompareTo(b.TargetSlotId);
        return comparison != 0 ? comparison : b.MultiInputIndex.CompareTo(a.MultiInputIndex);
    }

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

    private static TargetSnapshot[] SnapshotTargets(Dictionary<Endpoint, List<Endpoint>> targets)
    {
        var snapshots = new TargetSnapshot[targets.Count];
        var index = 0;
        foreach (var (target, sources) in targets)
            snapshots[index++] = new TargetSnapshot(target, sources.ToArray());
        return snapshots;
    }

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

    private sealed class RoutingCommand : ICommand
    {
        public string Name { get; }
        public bool IsUndoable => true;

        internal RoutingCommand(Guid compositionId, string name, CommandStep[] steps, TargetSnapshot[] before, TargetSnapshot[] after,
                                CollapseGuard? collapse = null)
        {
            _compositionId = compositionId;
            Name = name;
            _steps = steps;
            _before = before;
            _after = after;
            _collapse = collapse;
        }

        public void Do()
        {
            if (!TryExecute(false, out var error))
                Log.Warning($"{Name}: {error}");
        }

        public void Undo()
        {
            if (!TryExecute(true, out var error))
                Log.Warning($"Undo {Name}: {error}");
        }

        internal bool TryExecute(bool undo, out string error)
        {
            error = string.Empty;
            if (_isApplied != undo)
                return true;

            if (!SymbolUiRegistry.TryGetSymbolUi(_compositionId, out var ui) || ui.Symbol.SymbolPackage.IsReadOnly || _collapse != null && ui.ReadOnly)
            {
                error = "The editable graph is no longer available.";
                return false;
            }

            if (!MatchesState(ui, undo) || !ValidateSlotContracts(ui.Symbol))
            {
                error = "The affected connections or reroute definitions changed.";
                return false;
            }

            if (_collapse != null && !IsAcyclic(ui.Symbol, undo ? _before : _after))
            {
                error = "Collapsing these reroutes would create a cycle.";
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

        private bool MatchesState(SymbolUi ui, bool applied)
        {
            if (_collapse is { } collapse)
            {
                if (!ui.Symbol.Children.TryGetValue(collapse.TargetId, out var target) || !ui.ChildUis.ContainsKey(collapse.TargetId)
                    || !TryGetDefinition(target.Symbol, out var definition) || definition != collapse.TargetDefinition
                    || !SymbolUiRegistry.TryGetSymbolUi(collapse.DraggedDefinition.SymbolId, out var draggedUi)
                    || !TryGetDefinition(draggedUi.Symbol, out var draggedDefinition) || draggedDefinition != collapse.DraggedDefinition
                    || draggedUi.Symbol.InputDefinitions[0].ValueType != target.Symbol.InputDefinitions[0].ValueType
                    || !GetIncidentConnections(ui.Symbol, collapse.DraggedId, collapse.TargetId).SequenceEqual(applied ? collapse.After : collapse.Before))
                    return false;
            }

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

                if (!SymbolUiRegistry.TryGetSymbolUi(step.SymbolId, out var definitionUi) || !IsReroute(definitionUi.Symbol))
                    return false;

                var shouldExist = step.AddsChild == applied;
                var exists = ui.Symbol.Children.TryGetValue(step.ChildId, out var child);
                if (exists != shouldExist || ui.ChildUis.ContainsKey(step.ChildId) != shouldExist || exists && child!.Symbol.Id != step.SymbolId)
                    return false;
            }

            return true;
        }

        private bool ValidateSlotContracts(Symbol symbol)
        {
            var plannedChildren = new Dictionary<Guid, Symbol>();
            var merging = _collapse != null;
            foreach (var step in _steps)
            {
                if (step.ChildId == Guid.Empty)
                    continue;

                if (!SymbolUiRegistry.TryGetSymbolUi(step.SymbolId, out var definitionUi))
                    return false;

                plannedChildren.Add(step.ChildId, definitionUi.Symbol);
                merging |= step.AddsChild;
            }

            foreach (var step in _steps)
            {
                if (step.Connection is { } connection
                    && !TryGetConnectionType(symbol, connection, merging, out _, out _, plannedChildren))
                    return false;
            }

            if (_collapse is { } collapse)
            {
                foreach (var connection in collapse.Before.Concat(collapse.After))
                {
                    if (!TryGetConnectionType(symbol, connection, true, out _, out _, plannedChildren))
                        return false;
                }
            }

            return true;
        }

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

        private readonly Guid _compositionId;
        private readonly CommandStep[] _steps;
        private readonly TargetSnapshot[] _before;
        private readonly TargetSnapshot[] _after;
        private readonly CollapseGuard? _collapse;
        private bool _isApplied;
    }

    private const string MarkerName = "Types.Routing.IRerouteNode";
    private static readonly Guid TypeOperatorsPackageId = new("c8a53b12-ded3-4327-86d2-bd731b25de22");
}
