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
    /// Deletes the selected items and tries to collapse the gaps and patches the connection gaps is possible
    /// </summary>
    /// <param name="context"></param>
    internal static ChangeSymbol.SymbolModificationResults DeleteSelection(GraphUiContext context)
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

                //uiItems.Add(new ItemWithChildUi(item, childUi));
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

        var macroCommand = new MacroCommand("Delete items");
        if (deletedChildUis.Count > 0)
        {
            var collapsableConnectionPairs= MagItemMovement.FindLinkedVerticalCollapsableConnectionPairs(obsoleteConnections.ToList());

            // The bridge index must be computed before the deletion below removes
            // other connections into the same multi-input and shifts the positions.
            var deletedChildIds = new HashSet<Guid>();
            foreach (var childUi in deletedChildUis)
            {
                deletedChildIds.Add(childUi.Id);
            }

            var bridgeMultiInputIndices = new List<int>(collapsableConnectionPairs.Count);
            foreach (var pair in collapsableConnectionPairs)
            {
                bridgeMultiInputIndices.Add(ComputeMultiInputIndexAfterDeletion(context.CompositionInstance.Symbol, pair.Cb, deletedChildIds));
            }

            // Delete items...
            macroCommand.AddAndExecCommand(new DeleteSymbolChildrenCommand(compositionUi, deletedChildUis));

            // Collapse vertical gaps
            var relevantItems = MagItemMovement.CollectSnappedItems(deletedItems);
            var movableItems = new HashSet<MagGraphItem>(relevantItems.Except(deletedItems));

            for (var pairIndex = 0; pairIndex < collapsableConnectionPairs.Count; pairIndex++)
            {
                var pair = collapsableConnectionPairs[pairIndex];
                var mci = pair.Ca;
                var mco = pair.Cb;
                var affectedItems = MagItemMovement.MoveToCollapseVerticalGaps(mci, mco, movableItems, true);
                if (affectedItems.Count == 0)
                    continue;
                
                var affectedItemsAsNodes = movableItems.Select(i => i as ISelectableCanvasObject).ToList();
                var newMoveCommand = new ModifyCanvasElementsCommand(context.CompositionInstance.Symbol.Id, affectedItemsAsNodes, context.Selector);
                macroCommand.AddExecutedCommandForUndo(newMoveCommand);
            
                MagItemMovement.MoveToCollapseVerticalGaps(mci, mco, movableItems, dryRun:false);
                
                newMoveCommand.StoreCurrentValues();
            
                macroCommand.AddAndExecCommand(new AddConnectionCommand(context.CompositionInstance.Symbol,
                                                                        new Symbol.Connection(mci.SourceItem.Id,
                                                                                                  mci.SourceOutput.Id,
                                                                                                  mco.TargetItem.Id,
                                                                                                  mco.TargetInput.Id),
                                                                        bridgeMultiInputIndices[pairIndex]));
            }

            // Removing a connection can also free an input row on a surviving target
            // (e.g. an extra multi-input line): move snapped items below up to close the gap.
            var rebridgedConnections = new HashSet<MagGraphConnection>();
            foreach (var pair in collapsableConnectionPairs)
            {
                rebridgedConnections.Add(pair.Ca);
                rebridgedConnections.Add(pair.Cb);
            }

            foreach (var obsoleteConnection in obsoleteConnections)
            {
                if (rebridgedConnections.Contains(obsoleteConnection))
                    continue;

                var targetItem = obsoleteConnection.TargetItem;
                if (targetItem == null || deletedItems.Contains(targetItem))
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
                        && !deletedItems.Contains(snappedItem)
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
        context.Layout.FlagStructureAsChanged();
        return results;
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
            if (connection.TargetParentOrChildId != replacedConnection.TargetItem.Id
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