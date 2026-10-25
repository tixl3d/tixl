using System.Diagnostics.CodeAnalysis;
using ImGuiNET;
using T3.Core.Operator;
using T3.Editor.Gui.MagGraph.Model;
using T3.Editor.Gui.MagGraph.States;
using T3.Editor.Gui.OutputUi;
using T3.Editor.UiModel;
using T3.Editor.UiModel.Commands;
using T3.Editor.UiModel.Commands.Sections;
using T3.Editor.UiModel.Commands.Graph;
using T3.Editor.UiModel.InputsAndTypes;
using T3.Editor.UiModel.Modification;
using T3.Editor.UiModel.Selection;

namespace T3.Editor.Gui.MagGraph.Interaction;

internal static class Modifications
{
    /** A connection to add after deleting operators, feeding a surviving target from a surviving source */
    private sealed record PendingBridge(MagGraphConnection InConnection, MagGraphConnection OutConnection, int MultiInputIndex);

    /// <summary>
    /// Duplicates the selected operators while preserving their external wiring: connections feeding the
    /// selection from the outside are recreated on the duplicates, and any external multi-inputs the originals
    /// fed get the duplicates' outputs appended — pushing snapped items below the new input line down, like an
    /// interactive connection drop. Single external inputs are left untouched so the duplicate doesn't steal
    /// the original's connection. Pastes at the mouse position and does not touch the clipboard.
    /// </summary>
    internal static void DuplicateWithConnections(GraphUiContext context)
    {
        var compositionOp = context.CompositionInstance;
        var compositionSymbolUi = compositionOp.GetSymbolUi();
        var compositionSymbol = compositionSymbolUi.Symbol;

        var selectedChildUis = context.Selector.GetSelectedChildUis().ToList();
        if (selectedChildUis.Count == 0)
            return;

        var selectedSections = context.Selector.GetSelectedNodes<Section>().ToList();
        var targetPosition = context.View.InverseTransformPositionFloat(ImGui.GetMousePos());

        var copyCommand = new CopySymbolChildrenCommand(compositionSymbolUi,
                                                        selectedChildUis,
                                                        selectedSections,
                                                        compositionSymbolUi,
                                                        targetPosition);
        // Authoritative set of duplicated children (includes any pulled in via collapsed sections).
        var oldToNewChildIds = copyCommand.OldToNewChildIds;

        // Connections feeding the selection from outside, kept with their original multi-input index so
        // inserting them ascending lands each one at its original slot among the copied internal connections.
        var inboundCommands = new List<(int multiInputIndex, AddConnectionCommand command)>();

        // Outputs of the selection feeding external multi-inputs, appended right after the original connection.
        // The original connection is kept for the push-down of snapped items below its input line.
        var outboundEntries = new List<(Symbol.Connection original, int multiInputIndex, AddConnectionCommand command)>();

        foreach (var connection in compositionSymbol.Connections)
        {
            var sourceDuplicated = connection.SourceParentOrChildId != Guid.Empty
                                   && oldToNewChildIds.ContainsKey(connection.SourceParentOrChildId);
            var targetDuplicated = connection.TargetParentOrChildId != Guid.Empty
                                   && oldToNewChildIds.ContainsKey(connection.TargetParentOrChildId);

            var multiInputIndex = compositionSymbol.GetMultiInputIndexFor(connection);

            if (targetDuplicated && !sourceDuplicated)
            {
                // External source -> duplicated target (also covers connections from the composition's own inputs).
                var newConnection = new Symbol.Connection(connection.SourceParentOrChildId,
                                                          connection.SourceSlotId,
                                                          oldToNewChildIds[connection.TargetParentOrChildId],
                                                          connection.TargetSlotId);
                inboundCommands.Add((multiInputIndex, new AddConnectionCommand(compositionSymbol, newConnection, multiInputIndex)));
            }
            else if (sourceDuplicated && !targetDuplicated && compositionSymbol.IsTargetMultiInput(connection))
            {
                // Duplicated source -> external multi-input. Single inputs are skipped on purpose: adding the
                // duplicate's output there would replace (steal) the original's connection.
                var newConnection = new Symbol.Connection(oldToNewChildIds[connection.SourceParentOrChildId],
                                                          connection.SourceSlotId,
                                                          connection.TargetParentOrChildId,
                                                          connection.TargetSlotId);
                outboundEntries.Add((connection, multiInputIndex, new AddConnectionCommand(compositionSymbol, newConnection, multiInputIndex + 1)));
            }
        }

        // Inbound ascending: each insert at the original index reconstructs the source order on duplicated inputs.
        inboundCommands.Sort((a, b) => a.multiInputIndex.CompareTo(b.multiInputIndex));

        // Outbound descending: inserting at (originalIndex + 1) high-to-low keeps earlier indices valid as the
        // multi-input grows.
        outboundEntries.Sort((a, b) => b.multiInputIndex.CompareTo(a.multiInputIndex));

        var macroCommand = context.StartMacroCommand("Duplicate with connections");
        macroCommand.AddAndExecCommand(copyCommand);
        foreach (var entry in inboundCommands)
        {
            macroCommand.AddAndExecCommand(entry.command);
        }

        foreach (var entry in outboundEntries)
        {
            macroCommand.AddAndExecCommand(entry.command);
        }

        // Each appended multi-input connection adds an input line to its target: push snapped items below
        // the new line down by one row, like an interactive connection drop. The layout still reflects the
        // pre-duplication state here, which is exactly the geometry the thresholds need.
        foreach (var entry in outboundEntries)
        {
            if (!context.Layout.Items.TryGetValue(entry.original.TargetParentOrChildId, out var targetItem))
                continue;

            var lines = targetItem.InputLines;
            var originalLineIndex = -1;
            for (var lineIndex = 0; lineIndex < lines.Length; lineIndex++)
            {
                if (lines[lineIndex].Id == entry.original.TargetSlotId
                    && lines[lineIndex].MultiInputIndex == entry.multiInputIndex)
                {
                    originalLineIndex = lineIndex;
                    break;
                }
            }

            if (originalLineIndex == -1)
                continue;

            var snappedItems = MagItemMovement.CollectSnappedItems(targetItem);
            snappedItems.Remove(targetItem);
            MagItemMovement.MoveSnappedItemsVertically(context,
                                                       snappedItems,
                                                       targetItem.PosOnCanvas.Y + MagGraphItem.GridSize.Y * (originalLineIndex + 0.5f),
                                                       MagGraphItem.GridSize.Y);
        }

        // Duplicating into a section near its bottom/right border grows the frame to fit
        SectionTree.GrowSectionToFitPlacedItems(compositionSymbolUi, targetPosition, copyCommand.NewSymbolChildIds, context.MacroCommand!, context.Selector);

        context.CompleteMacroCommand();

        // Select the duplicated nodes
        context.Selector.Clear();
        foreach (var newId in copyCommand.NewSymbolChildIds)
        {
            if (compositionSymbolUi.ChildUis.TryGetValue(newId, out var newChildUi)
                && compositionOp.Children.TryGetChildInstance(newId, out var instance))
            {
                context.Selector.AddSelection(newChildUi, instance);
            }
        }

        foreach (var newId in copyCommand.NewSymbolSectionIds)
        {
            if (compositionSymbolUi.Sections.TryGetValue(newId, out var section))
            {
                context.Selector.AddSelection(section);
            }
        }
    }

    internal static ChangeSymbol.SymbolModificationResults AlignSelectionToLeft(GraphUiContext context)
    {
        var results = ChangeSymbol.SymbolModificationResults.Nothing;
        
        if (context.Selector.Selection.Count == 0)
            return results;

        if(!SymbolUiRegistry.TryGetSymbolUi(context.CompositionInstance.Symbol.Id, out var compositionUi))
        {
            Log.Warning("Can't find composition ui?");
            return results;
        }

        var minX = float.PositiveInfinity;
        foreach (var i in context.Selector.Selection)
        {
            minX = MathF.Min(minX, i.PosOnCanvas.X);
        }
        
        var moveCommand = new ModifyCanvasElementsCommand(compositionUi.Symbol.Id, context.Selector.Selection, context.Selector );
        
        foreach (var i in context.Selector.Selection)
        {
            var pos = i.PosOnCanvas;
            pos.X = minX;
            i.PosOnCanvas= pos;
        }
        
        moveCommand.StoreCurrentValues();
        UndoRedoStack.Add(moveCommand);
        return ChangeSymbol.SymbolModificationResults.Nothing;
    }
    
    
    /// <summary>
    /// Deletes the selected items. With <paramref name="keepConnections"/> the deleted operators are dissolved:
    /// their downstream targets get reconnected to the upstream source (when the types match) and gaps in
    /// snapped stacks are closed. Afterwards the item snapped above or left of the deleted ones gets selected,
    /// so repeated deletes keep working.
    /// </summary>
    internal static ChangeSymbol.SymbolModificationResults DeleteSelection(GraphUiContext context, bool keepConnections = true)
    {
        var results = ChangeSymbol.SymbolModificationResults.Nothing;
        
        if (context.Selector.Selection.Count == 0)
            return results;

        if(!SymbolUiRegistry.TryGetSymbolUi(context.CompositionInstance.Symbol.Id, out var compositionUi))
        {
            Log.Warning("Can't find composition ui?");
            return results;
        }

        var deletedItems = new List<MagGraphItem>();
        var deletedChildUis = new List<SymbolUi.Child>();
        var deletedInputUis = new List<IInputUi>();
        var deletedOutputUis = new List<IOutputUi>();
        var deletedSections = new List<MagGraphSection>();
        
        foreach (var s in context.Selector.Selection)
        {
            if (context.Layout.Items.TryGetValue(s.Id, out var item))
            {
                if (item.Variant != MagGraphItem.Variants.Operator)
                {
                    if (compositionUi.Symbol.SymbolPackage.IsReadOnly)
                    {
                        Log.Warning("Can't delete inputs or outputs from a read only symbol.");
                        continue;
                    }

                    if (item.Variant == MagGraphItem.Variants.Input)
                    {
                        deletedInputUis.Add(item.Selectable as IInputUi);
                    }
                    else if (item.Variant == MagGraphItem.Variants.Output)
                    {
                        deletedOutputUis.Add(item.Selectable as IOutputUi);
                    }

                    continue;
                }

                if (!compositionUi.ChildUis.TryGetValue(item.Id, out var childUi))
                {
                    Log.Warning("Can't find symbol child for " + item);
                    continue;
                }

                deletedItems.Add(item);
                deletedChildUis.Add(childUi);
            }
            else if (s is Section section)
            {
                if (!context.Layout.Sections.TryGetValue(s.Id, out var mga))
                {
                    Log.Warning("Can't find section: " + s.Id);
                    continue;
                }

                deletedSections.Add(mga);
            }
            else
            {
                Log.Warning("Can't find selectable item " + s);
                continue;
            }
        }

        // Deleting a collapsed frame deletes its hidden contents with it - once the
        // header is gone they would have no visible representation left on the canvas
        if (deletedSections.Count > 0)
        {
            var hiddenOps = new List<SymbolUi.Child>();
            var hiddenSections = new List<Section>();
            foreach (var magSection in deletedSections.ToArray())
            {
                if (magSection.Section.Collapsed)
                {
                    SectionTree.CollectHiddenContents(compositionUi, magSection.Section, hiddenOps, hiddenSections);
                }
            }

            foreach (var hiddenOp in hiddenOps)
            {
                if (deletedChildUis.Contains(hiddenOp))
                    continue;

                deletedChildUis.Add(hiddenOp);
                if (context.Layout.Items.TryGetValue(hiddenOp.Id, out var hiddenItem))
                {
                    deletedItems.Add(hiddenItem);
                }
            }

            foreach (var hiddenSection in hiddenSections)
            {
                if (!context.Layout.Sections.TryGetValue(hiddenSection.Id, out var hiddenMagSection))
                {
                    Log.Warning("Can't find hidden nested section: " + hiddenSection.Id);
                    continue;
                }

                if (!deletedSections.Contains(hiddenMagSection))
                {
                    deletedSections.Add(hiddenMagSection);
                }
            }
        }

        // find obsolete connections...
        var obsoleteConnections = new HashSet<MagGraphConnection>();
        foreach (var i in deletedItems)
        {
            foreach (var inLine in i.InputLines)
            {
                if (inLine.ConnectionIn != null)
                    obsoleteConnections.Add(inLine.ConnectionIn);
            }

            foreach (var outLine in i.OutputLines)
            {
                obsoleteConnections.UnionWith(outLine.ConnectionsOut);
            }
        }
        
        if (deletedChildUis.Count == 0 && deletedInputUis.Count == 0 && deletedOutputUis.Count == 0
            && deletedSections.Count==0)
            return results;

        var deletedItemSet = new HashSet<MagGraphItem>(deletedItems);
        var itemToSelect = FindSnappedUpstreamSurvivor(deletedItems, deletedItemSet);

        var macroCommand = new MacroCommand("Delete items");
        if (deletedChildUis.Count > 0)
        {
            var symbol = context.CompositionInstance.Symbol;
            var deletedChildIds = new HashSet<Guid>();
            foreach (var childUi in deletedChildUis)
            {
                deletedChildIds.Add(childUi.Id);
            }

            // Bridge indices must be computed before the deletion below removes
            // other connections into the same multi-input and shifts the positions.
            var bridges = keepConnections
                              ? CollectBridges(symbol, obsoleteConnections, deletedItems, deletedItemSet, deletedChildIds)
                              : [];

            // Delete items...
            macroCommand.AddAndExecCommand(new DeleteSymbolChildrenCommand(compositionUi, deletedChildUis));

            CollapseSnappedGaps(context, macroCommand, bridges, deletedItems);

            foreach (var bridge in bridges)
            {
                macroCommand.AddAndExecCommand(new AddConnectionCommand(symbol,
                                                                        new Symbol.Connection(bridge.InConnection.SourceParentOrChildId,
                                                                                              bridge.InConnection.SourceOutput.Id,
                                                                                              bridge.OutConnection.TargetParentOrChildId,
                                                                                              bridge.OutConnection.TargetInput.Id),
                                                                        bridge.MultiInputIndex));
            }

            // Removing a connection can also free an input row on a surviving target
            // (e.g. an extra multi-input line): move snapped items below up to close the gap.
            var rebridgedConnections = new HashSet<MagGraphConnection>();
            foreach (var bridge in bridges)
            {
                rebridgedConnections.Add(bridge.OutConnection);
            }

            foreach (var obsoleteConnection in obsoleteConnections)
            {
                if (rebridgedConnections.Contains(obsoleteConnection))
                    continue;

                var targetItem = obsoleteConnection.TargetItem;
                if (targetItem == null || deletedItemSet.Contains(targetItem))
                    continue;

                var lines = targetItem.InputLines;
                var lineIndex = obsoleteConnection.InputLineIndex;
                if (lineIndex >= lines.Length)
                    continue;

                var line = lines[lineIndex];
                var linesForSlot = 0;
                foreach (var otherLine in lines)
                {
                    if (otherLine.Id == line.Id)
                        linesForSlot++;
                }

                // The row only disappears for extra multi-input lines and for optional inputs shown while connected
                var freesInputLine = linesForSlot > 1
                                     || (targetItem.Variant == MagGraphItem.Variants.Operator
                                         && line.InputUi is { Relevancy: Relevancy.Optional }
                                         && lineIndex > 0);
                if (!freesInputLine)
                    continue;

                var snappedItems = MagItemMovement.CollectSnappedItems(targetItem);
                var yThreshold = targetItem.PosOnCanvas.Y + MagGraphItem.GridSize.Y * (lineIndex - 0.5f);
                var itemsBelow = new List<ISelectableCanvasObject>();
                foreach (var snappedItem in snappedItems)
                {
                    if (snappedItem != targetItem
                        && !deletedItemSet.Contains(snappedItem)
                        && snappedItem.PosOnCanvas.Y > yThreshold)
                    {
                        itemsBelow.Add(snappedItem);
                    }
                }

                if (itemsBelow.Count == 0)
                    continue;

                var moveUpCommand = new ModifyCanvasElementsCommand(context.CompositionInstance.Symbol.Id, itemsBelow, context.Selector);
                macroCommand.AddExecutedCommandForUndo(moveUpCommand);
                foreach (var itemBelow in itemsBelow)
                {
                    var pos = itemBelow.PosOnCanvas;
                    pos.Y -= MagGraphItem.GridSize.Y;
                    itemBelow.PosOnCanvas = pos;
                }

                moveUpCommand.StoreCurrentValues();
            }
        }


        if (deletedInputUis.Count > 0 || deletedOutputUis.Count > 0)
        {
            // Executed after the child deletes above, so the command only captures the connections
            // still present (child-side connections were already removed by DeleteSymbolChildrenCommand).
            macroCommand.AddAndExecCommand(new RemoveInputsOrOutputsCommand(compositionUi.Symbol.Id,
                                                                            deletedInputUis.Select(entry => entry.Id).ToArray(),
                                                                            deletedOutputUis.Select(entry => entry.Id).ToArray()));
        }

        if (deletedSections.Count > 0)
        {
            foreach (var a in deletedSections)
            {
                macroCommand.AddAndExecCommand(new DeleteSectionCommand(compositionUi, a.Section));
            }
        }

        UndoRedoStack.Add(macroCommand);
        
        context.Layout.FlagStructureAsChanged();
        context.Selector.Clear();
        itemToSelect?.Select(context.Selector);
        return results;
    }

    /// <summary>
    /// Finds the connections that replace the deleted operators: each surviving target of a deleted operator's
    /// primary output gets fed by the first surviving source found by walking up the primary inputs.
    /// </summary>
    private static List<PendingBridge> CollectBridges(Symbol symbol, HashSet<MagGraphConnection> obsoleteConnections,
                                                      List<MagGraphItem> deletedItems, HashSet<MagGraphItem> deletedItemSet,
                                                      HashSet<Guid> deletedChildIds)
    {
        var bridges = new List<PendingBridge>();
        var bridgedOutConnections = new HashSet<MagGraphConnection>();

        // Snapped vertical stacks first, so their gaps are closed and reconnected as one unit
        var stackPairs = MagItemMovement.FindLinkedVerticalCollapsableConnectionPairs(obsoleteConnections.ToList());
        foreach (var pair in stackPairs)
        {
            if (deletedItemSet.Contains(pair.Ca.SourceItem) || deletedItemSet.Contains(pair.Cb.TargetItem))
                continue;

            bridges.Add(new PendingBridge(pair.Ca, pair.Cb, ComputeMultiInputIndexAfterDeletion(symbol, pair.Cb, deletedChildIds)));
            bridgedOutConnections.Add(pair.Cb);
        }

        foreach (var deletedItem in deletedItems)
        {
            if (deletedItem.OutputLines.Length == 0)
                continue;

            foreach (var outConnection in deletedItem.OutputLines[0].ConnectionsOut)
            {
                if (bridgedOutConnections.Contains(outConnection)
                    || outConnection.TargetItem == null
                    || deletedItemSet.Contains(outConnection.TargetItem))
                    continue;

                if (!TryFindSurvivingUpstreamConnection(deletedItem, deletedItemSet, out var inConnection)
                    || inConnection.Type != outConnection.Type)
                    continue;

                bridges.Add(new PendingBridge(inConnection, outConnection,
                                              ComputeMultiInputIndexAfterDeletion(symbol, outConnection, deletedChildIds)));
                bridgedOutConnections.Add(outConnection);
            }
        }

        // Several bridges into one multi-input: each earlier insertion shifts the later ones by one
        var orderedBridges = bridges.OrderBy(b => b.OutConnection.TargetItem.Id)
                                    .ThenBy(b => b.OutConnection.TargetInput.Id)
                                    .ThenBy(b => b.MultiInputIndex)
                                    .ToList();
        for (var index = 1; index < orderedBridges.Count; index++)
        {
            var previous = orderedBridges[index - 1];
            var current = orderedBridges[index];
            if (previous.OutConnection.TargetItem == current.OutConnection.TargetItem
                && previous.OutConnection.TargetInput.Id == current.OutConnection.TargetInput.Id
                && current.MultiInputIndex <= previous.MultiInputIndex)
            {
                orderedBridges[index] = current with { MultiInputIndex = previous.MultiInputIndex + 1 };
            }
        }

        return orderedBridges;
    }

    /// <summary>
    /// Walks up the primary inputs through deleted operators and returns the connection coming from the first surviving item.
    /// Gives up on multi-inputs with several connections, where the upstream source would be ambiguous.
    /// </summary>
    private static bool TryFindSurvivingUpstreamConnection(MagGraphItem deletedItem, HashSet<MagGraphItem> deletedItemSet,
                                                           [NotNullWhen(true)] out MagGraphConnection? connection)
    {
        connection = null;
        var item = deletedItem;
        for (var step = 0; step <= deletedItemSet.Count; step++)
        {
            if (!item.TryGetPrimaryInConnection(out var inConnection))
                return false;

            var connectedLinesForPrimarySlot = 0;
            foreach (var line in item.InputLines)
            {
                if (line.Id == item.InputLines[0].Id && line.ConnectionIn != null)
                    connectedLinesForPrimarySlot++;
            }

            if (connectedLinesForPrimarySlot > 1)
                return false;

            if (!deletedItemSet.Contains(inConnection.SourceItem))
            {
                connection = inConnection;
                return true;
            }

            if (inConnection.OutputLineIndex != 0)
                return false;

            item = inConnection.SourceItem;
        }

        return false;
    }

    /// <summary>
    /// The item snapped above or left of the first deleted operator, looking through deleted chains.
    /// </summary>
    private static MagGraphItem? FindSnappedUpstreamSurvivor(List<MagGraphItem> deletedItems, HashSet<MagGraphItem> deletedItemSet)
    {
        foreach (var deletedItem in deletedItems)
        {
            var item = deletedItem;
            for (var step = 0; step <= deletedItemSet.Count; step++)
            {
                if (!item.TryGetPrimaryInConnection(out var inConnection) || !inConnection.IsSnapped)
                    break;

                if (!deletedItemSet.Contains(inConnection.SourceItem))
                    return inConnection.SourceItem;

                item = inConnection.SourceItem;
            }
        }

        return null;
    }

    /// <summary>
    /// Moves the remaining snapped items together where a bridge replaces a snapped chain of deleted operators.
    /// </summary>
    private static void CollapseSnappedGaps(GraphUiContext context, MacroCommand macroCommand, List<PendingBridge> bridges,
                                            List<MagGraphItem> deletedItems)
    {
        var snappedBridges = new List<PendingBridge>();
        foreach (var bridge in bridges)
        {
            if (bridge.InConnection.IsSnapped && bridge.OutConnection.IsSnapped)
                snappedBridges.Add(bridge);
        }

        if (snappedBridges.Count == 0)
            return;

        var movableItems = MagItemMovement.CollectSnappedItems(deletedItems);
        movableItems.ExceptWith(deletedItems);
        if (movableItems.Count == 0)
            return;

        var movedItems = new List<ISelectableCanvasObject>(movableItems);
        var moveCommand = new ModifyCanvasElementsCommand(context.CompositionInstance.Symbol.Id, movedItems, context.Selector);

        // Closing the lowest / rightmost gap first keeps the cached positions of the remaining gaps valid
        var anythingMoved = false;
        foreach (var bridge in snappedBridges.OrderByDescending(b => b.InConnection.SourcePos.Y))
        {
            if (IsVertical(bridge.InConnection) && IsVertical(bridge.OutConnection))
                anythingMoved |= MagItemMovement.MoveToCollapseVerticalGaps(bridge.InConnection, bridge.OutConnection, movableItems, dryRun: false).Count > 0;
        }

        foreach (var bridge in snappedBridges.OrderByDescending(b => b.InConnection.SourcePos.X))
        {
            if (!IsVertical(bridge.InConnection) && !IsVertical(bridge.OutConnection))
                anythingMoved |= MoveToCollapseHorizontalGaps(bridge.InConnection, bridge.OutConnection, movableItems);
        }

        if (!anythingMoved)
            return;

        moveCommand.StoreCurrentValues();
        macroCommand.AddExecutedCommandForUndo(moveCommand);
        return;

        static bool IsVertical(MagGraphConnection c) => c.Style is MagGraphConnection.ConnectionStyles.MainOutToMainInSnappedVertical
                                                                  or MagGraphConnection.ConnectionStyles.AdditionalOutToMainInputSnappedVertical;
    }

    /// <summary>
    /// Moves items right of each free column between the two connections one column to the left.
    /// </summary>
    private static bool MoveToCollapseHorizontalGaps(MagGraphConnection ca, MagGraphConnection cb, HashSet<MagGraphItem> movableItems)
    {
        var minX = ca.SourcePos.X;
        var maxX = cb.TargetPos.X;
        var columnWidth = MagGraphItem.GridSize.X;

        var columnCount = (int)((maxX - minX) / columnWidth + 0.5f);
        var anythingMoved = false;
        for (var columnIndex = columnCount - 1; columnIndex >= 0; columnIndex--)
        {
            var middleColumnX = minX + (0.5f + columnIndex) * columnWidth;

            var isColumnBlocked = false;
            foreach (var item in movableItems)
            {
                if (item.Area.Min.X >= middleColumnX || item.Area.Max.X <= middleColumnX)
                    continue;

                isColumnBlocked = true;
                break;
            }

            if (isColumnBlocked)
                continue;

            foreach (var item in movableItems)
            {
                if (item.PosOnCanvas.X < middleColumnX)
                    continue;

                item.PosOnCanvas -= new Vector2(columnWidth, 0);
                anythingMoved = true;
            }
        }

        return anythingMoved;
    }

    //private record ItemWithChildUi(MagGraphItem Item, SymbolUi.Child UiChild);

    /// <summary>
    /// Computes the multi-input index a bridging connection should be inserted at once the
    /// given children are deleted: the number of surviving connections into the same target
    /// slot that precede the replaced connection.
    /// </summary>
    private static int ComputeMultiInputIndexAfterDeletion(Symbol symbol, MagGraphConnection replacedConnection, HashSet<Guid> deletedChildIds)
    {
        var survivingIndex = 0;
        var slotConnectionIndex = 0;
        foreach (var connection in symbol.Connections)
        {
            if (connection.TargetParentOrChildId != replacedConnection.TargetParentOrChildId
                || connection.TargetSlotId != replacedConnection.TargetInput.Id)
                continue;

            if (slotConnectionIndex == replacedConnection.MultiInputIndex)
                break;

            slotConnectionIndex++;

            if (!deletedChildIds.Contains(connection.SourceParentOrChildId)
                && !deletedChildIds.Contains(connection.TargetParentOrChildId))
            {
                survivingIndex++;
            }
        }

        return survivingIndex;
    }
}