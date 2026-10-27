#nullable enable
using System.Diagnostics;
using ImGuiNET;
using T3.Core.Operator;
using T3.Editor.Gui.Interaction.Variations;
using T3.Editor.Gui.Interaction.Variations.Model;
using T3.Editor.Gui.MagGraph.Model;
using T3.Editor.Gui.MagGraph.States;
using T3.Editor.Gui.UiHelpers;
using T3.Editor.UiModel;
using T3.Editor.UiModel.Commands.Graph;
using T3.Editor.UiModel.Selection;
using Vector2 = System.Numerics.Vector2;

namespace T3.Editor.Gui.MagGraph.Interaction;

/// <summary>
/// Controls when and how a placeholder is shown and used to create new items / outputs etc.
/// </summary>
[HelpUiID("SymbolBrowser")]
internal sealed class PlaceholderCreation
{
    internal MagGraphItem? PlaceholderItem;

    internal void OpenToSplitHoveredConnections(GraphUiContext context)
    {
        if (context.ConnectionHovering.ConnectionHoversWhenClicked.Count == 0)
        {
            Log.Warning("No connections found? ");
            context.StateMachine.SetState(GraphStates.Default, context);
            return;
        }

        context.TempConnections.Clear();

        context.StartMacroCommand("Insert operator");
        var posOnCanvas = context.View.InverseTransformPositionFloat(ImGui.GetMousePos());

        var firstHover = context.ConnectionHovering.ConnectionHoversWhenClicked[0];
        var hoverConnection = firstHover.Connection;

        // For composition inputs the source slot is the parent's input, not in Outputs - fall back to line 0
        var outputLineIndex = hoverConnection.SourceItem.Variant == MagGraphItem.Variants.Input
                                  ? 0
                                  : Math.Max(0, hoverConnection.SourceItem.Instance?.Outputs.IndexOf(hoverConnection.SourceOutput) ?? 0);

        var styleToUse = MagGraphConnection.ConnectionStyles.Unknown;
        if (hoverConnection.Style is MagGraphConnection.ConnectionStyles.BottomToTop or MagGraphConnection.ConnectionStyles.RightToLeft)
        {
            styleToUse = hoverConnection.Style;
        }

        // Add temp connection into placeholder...
        var tempConnectionIn = new MagGraphConnection
                                   {
                                       Style = styleToUse,
                                       SourcePos = hoverConnection.SourcePos,
                                       SourceItem = hoverConnection.SourceItem,
                                       SourceOutput = hoverConnection.SourceOutput,
                                       TargetPos = default,
                                       TargetItem = null,
                                       OutputLineIndex = outputLineIndex,
                                       VisibleOutputIndex = 0,
                                       ConnectionHash = 0,
                                       IsTemporary = true,
                                   };
        context.TempConnections.Add(tempConnectionIn);

        PlaceholderItem = new MagGraphItem
                              {
                                  Selectable = _placeHolderSelectable,
                                  PosOnCanvas = posOnCanvas,
                                  Id = PlaceHolderId,
                                  Size = MagGraphItem.GridSize,
                                  Variant = MagGraphItem.Variants.Placeholder,
                              };

        context.Layout.Items[PlaceHolderId] = PlaceholderItem;

        PlaceHolderUi.Open(context, PlaceholderItem,
                           inputFilter: firstHover.Connection.Type,
                           outputFilter: firstHover.Connection.Type);

        PlaceHolderUi.Filter.UpdateIfNecessary(context.Selector, forceUpdate: true);
        context.StateMachine.SetState(GraphStates.Placeholder, context);
    }

    internal void OpenOnCanvas(GraphUiContext context, Vector2 posOnCanvas, Type? inputTypeFilter = null, Type? outputTypeFilter = null,
                               bool onlyMultiInputs = false)
    {
        // Might continue a macro command started by a preceding connection drag...
        context.StartOrContinueMacroCommand("Insert Operator");

        PlaceholderItem = new MagGraphItem
                              {
                                  Selectable = _placeHolderSelectable,
                                  PosOnCanvas = posOnCanvas,
                                  Id = PlaceHolderId,
                                  Size = MagGraphItem.GridSize,
                                  Variant = MagGraphItem.Variants.Placeholder,
                              };

        context.Layout.Items[PlaceHolderId] = PlaceholderItem;
        PlaceHolderUi.Open(context, PlaceholderItem, inputFilter: inputTypeFilter, outputFilter: outputTypeFilter, onlyMultiInputs: onlyMultiInputs);
    }

    internal void OpenForItemOutput(GraphUiContext context,
                              MagGraphItem item,
                              MagGraphItem.OutputLine outputLine,
                              MagGraphItem.Directions direction = MagGraphItem.Directions.Vertical)
    {
        Debug.Assert(item.OutputLines.Length > 0);
        context.StartMacroCommand("Insert Operator");

        // Within a stack the part above (or left of) the insertion makes room, so the downstream
        // block keeps its place and the spacing to its neighbours doesn't grow with every insert.
        // Keep for after creation because inserted node might exceed unit height and further pushing is required... 
        _snappedItems = MagItemMovement.CollectSnappedItems(item);
        _upstreamItems = CollectUpstreamItems(item, outputLine, direction, _snappedItems);
        if (_upstreamItems != null)
        {
            var offset = direction == MagGraphItem.Directions.Vertical
                             ? new Vector2(0, -MagGraphItem.GridSize.Y)
                             : new Vector2(-MagGraphItem.GridSize.X, 0);
            MagItemMovement.MoveItems(context, _upstreamItems, offset);
        }

        var focusedItemPosOnCanvas = direction == MagGraphItem.Directions.Vertical
                                         ? item.PosOnCanvas + new Vector2(0, item.Size.Y)
                                         : item.PosOnCanvas + new Vector2(item.Size.X, MagGraphItem.GridSize.Y * outputLine.VisibleIndex);

        PlaceholderItem = new MagGraphItem
                              {
                                  Selectable = _placeHolderSelectable,
                                  PosOnCanvas = focusedItemPosOnCanvas,
                                  Id = PlaceHolderId,
                                  Size = MagGraphItem.GridSize,
                                  Variant = MagGraphItem.Variants.Placeholder,
                              };

        context.Selector.Selection.Clear();
        context.Layout.Items[PlaceHolderId] = PlaceholderItem;

        var outputValueType = SnappedOutputValueType(outputLine, direction);
        PlaceHolderUi.Open(context,
                           PlaceholderItem,
                           direction,
                           outputLine.Output.ValueType,
                           outputValueType);

        _snappedSourceOutputLine = outputLine;
        _snappedSourceItem = item;
        _connectionOrientation = direction;
    }

    /// <summary>
    /// If the insertion cuts a snapped connection, returns the items on the item's side of it, including those
    /// snapped beside them (e.g. a multi-input block hanging below a horizontal row). Returns null if nothing is
    /// snapped beyond the insertion point, so there is no stack to open.
    /// </summary>
    private static HashSet<MagGraphItem>? CollectUpstreamItems(MagGraphItem item, MagGraphItem.OutputLine outputLine,
                                                              MagGraphItem.Directions direction, HashSet<MagGraphItem> snappedItems)
    {
        var isVertical = direction == MagGraphItem.Directions.Vertical;
        MagGraphConnection? cutConnection = null;
        if (isVertical)
        {
            foreach (var line in item.OutputLines)
            {
                foreach (var c in line.ConnectionsOut)
                {
                    if (c.Style is MagGraphConnection.ConnectionStyles.MainOutToMainInSnappedVertical
                        or MagGraphConnection.ConnectionStyles.AdditionalOutToMainInputSnappedVertical)
                        cutConnection = c;
                }
            }
        }
        else
        {
            foreach (var c in outputLine.ConnectionsOut)
            {
                if (c.Style is MagGraphConnection.ConnectionStyles.MainOutToMainInSnappedHorizontal
                    or MagGraphConnection.ConnectionStyles.MainOutToInputSnappedHorizontal)
                    cutConnection = c;
            }
        }

        if (cutConnection == null)
            return null;

        var upstreamItems = MagItemMovement.CollectSnappedItems(item, ignoreConnectionHash: cutConnection.ConnectionHash);
        if (!upstreamItems.Contains(cutConnection.TargetItem))
            return upstreamItems;

        // Both sides are linked by other snaps around the insertion point: split by position instead
        upstreamItems.Clear();
        var threshold = isVertical
                            ? item.PosOnCanvas.Y + item.Size.Y - MagGraphItem.GridSize.Y / 2
                            : item.PosOnCanvas.X + item.Size.X - MagGraphItem.GridSize.X / 2;
        foreach (var snappedItem in snappedItems)
        {
            var itemStart = isVertical ? snappedItem.PosOnCanvas.Y : snappedItem.PosOnCanvas.X;
            if (itemStart < threshold)
                upstreamItems.Add(snappedItem);
        }

        return upstreamItems;
    }

    private static bool HasHorizontalSnapRight(MagGraphItem.OutputLine outputLine)
    {
        foreach (var c in outputLine.ConnectionsOut)
        {
            if (c.Style is MagGraphConnection.ConnectionStyles.MainOutToMainInSnappedHorizontal
                or MagGraphConnection.ConnectionStyles.MainOutToInputSnappedHorizontal)
                return true;
        }

        return false;
    }

    private static Type? SnappedOutputValueType(MagGraphItem.OutputLine outputLine, MagGraphItem.Directions direction)
    {
        if (outputLine.ConnectionsOut.Count == 0)
            return null;

        foreach (var c in outputLine.ConnectionsOut)
        {
            if (direction == MagGraphItem.Directions.Vertical)
            {
                if (c.Style == MagGraphConnection.ConnectionStyles.MainOutToMainInSnappedVertical)
                    return c.SourceOutput.ValueType;
            }
            else
            {
                if (c.Style == MagGraphConnection.ConnectionStyles.MainOutToInputSnappedHorizontal
                    || c.Style == MagGraphConnection.ConnectionStyles.MainOutToMainInSnappedHorizontal)
                {
                    return c.SourceOutput.ValueType;
                }
            }
        }

        return null;
    }

    internal void OpenForItemInput(GraphUiContext context,
                                   MagGraphItem targetItem,
                                   Guid selectedInputId,
                                   MagGraphItem.Directions direction)
    {
        Debug.Assert(targetItem.Instance != null);

        var input = targetItem.Instance.Inputs.FirstOrDefault(i => i.Id == selectedInputId);
        if (input == null)
            return;

        context.StartMacroCommand("Insert Operator");


        int insertionLineIndex;
        Vector2 focusedItemPosOnCanvas;

        var shouldPushDown = false;
        
        if (direction == MagGraphItem.Directions.Horizontal)
        {
            // Find insertion index
            var visibleInputLines = targetItem.InputLines;
            var allInputSlots = targetItem.Instance.Inputs;

            insertionLineIndex = InputPicking.GetInsertionLineIndex(allInputSlots, visibleInputLines, selectedInputId, out  shouldPushDown);
            focusedItemPosOnCanvas = targetItem.PosOnCanvas + new Vector2(-targetItem.Size.X, MagGraphItem.GridSize.Y * insertionLineIndex);
        }
        else
        {
            insertionLineIndex = 0;
            focusedItemPosOnCanvas = targetItem.PosOnCanvas + new Vector2(0, -MagGraphItem.GridSize.Y);
        }

        
        PlaceholderItem = new MagGraphItem
                              {
                                  Selectable = _placeHolderSelectable,
                                  PosOnCanvas = focusedItemPosOnCanvas,
                                  Id = PlaceHolderId,
                                  Size = MagGraphItem.GridSize,
                                  Variant = MagGraphItem.Variants.Placeholder,
                              };

        _snappedTargetItem = targetItem;
        _snappedTargetInputId = selectedInputId;

        // Keep for after creation because inserted node might exceed unit height and further pushing is required... 
        _snappedItems = MagItemMovement.CollectSnappedItems(targetItem, includeRoot: false);

        if (shouldPushDown)
        {
            MagItemMovement
               .MoveSnappedItemsVertically(context,
                                           _snappedItems,
                                           targetItem.PosOnCanvas.Y + (insertionLineIndex - 0.5f) * MagGraphItem.GridSize.Y,
                                           MagGraphItem.GridSize.Y);
        }

        context.Selector.Selection.Clear();
        context.Layout.Items[PlaceHolderId] = PlaceholderItem;

        context.TempConnections.Add(new MagGraphConnection
                                        {
                                            Style = direction == MagGraphItem.Directions.Vertical ? MagGraphConnection.ConnectionStyles.MainOutToMainInSnappedVertical :  MagGraphConnection.ConnectionStyles.Unknown,
                                            SourcePos = default,
                                            SourceOutput = null,
                                            SourceItem = PlaceholderItem,
                                            TargetPos = default,
                                            TargetItem = targetItem,
                                            InputLineIndex = insertionLineIndex,
                                            MultiInputIndex = 0,
                                            OutputLineIndex = 0,
                                            VisibleOutputIndex = 0,
                                            ConnectionHash = 0,
                                            IsTemporary = true,
                                        });

        context.DisconnectedInputHashes.Add(MagGraphConnection.GetItemInputHash(targetItem.Id, selectedInputId,
                                                                                0)); // keep input visible until state is complete
        context.Layout.FlagStructureAsChanged();

        PlaceHolderUi.Open(context,
                           PlaceholderItem,
                           direction,
                           null,
                           input.ValueType);

        //_snappedSourceOutputLine = null;//outputLine;
        //_snappedSourceItem = null;
        _connectionOrientation = direction;

    }

    internal void Cancel(GraphUiContext context)
    {
        if (context.MacroCommand != null)
            context.CancelMacroCommand();

        if (_snappedSourceItem != null)
        {
            context.Selector.SetSelection(_snappedSourceItem.Selectable, _snappedSourceItem.Instance);
        }

        Reset(context);
    }

    internal void Reset(GraphUiContext context)
    {
        if (context.MacroCommand != null)
        {
            Log.Debug("cancelling placeholder command... " + context.MacroCommand);
            context.CancelMacroCommand();
        }

        _snappedSourceItem = null;
        _snappedTargetItem = null;
        _snappedTargetInputId = Guid.Empty;
        context.DisconnectedInputHashes.Clear();
        context.Layout.FlagStructureAsChanged();
        context.ConnectionHovering.ConnectionHoversWhenClicked.Clear();
        context.TempConnections.Clear();

        PlaceHolderUi.Reset();

        if (PlaceholderItem == null)
            return;

        context.Layout.Items.Remove(PlaceHolderId);
        PlaceholderItem = null;

        DrawUtils.RestoreImGuiKeyboardNavigation();
    }

    internal void Update(GraphUiContext context)
    {
        var uiResult = PlaceHolderUi.Draw(context, out var selectedUi);
        if (uiResult.HasFlag(PlaceHolderUi.UiResults.Create) && selectedUi != null)
        {
            CreateInstance(context, selectedUi.Symbol, PlaceHolderUi.SelectedPreset);
            if (uiResult.HasFlag(PlaceHolderUi.UiResults.EditParameters))
                ParameterPrompt.Open(context);
        }
        else if (uiResult.HasFlag(PlaceHolderUi.UiResults.Cancel))
        {
            Cancel(context);
        }
    }

    private void CreateInstance(GraphUiContext context, Symbol symbol, Variation? preset)
    {
        if (context.MacroCommand == null || PlaceholderItem == null)
        {
            Log.Warning("Macro command missing for insertion");
            return;
        }

        var parentSymbol = context.CompositionInstance.Symbol;
        var parentSymbolUi = parentSymbol.GetSymbolUi();

        var addSymbolChildCommand = new AddSymbolChildCommand(parentSymbol, symbol.Id) { PosOnCanvas = PlaceholderItem.PosOnCanvas };
        context.MacroCommand.AddAndExecCommand(addSymbolChildCommand);

        // Get created node
        if (!parentSymbolUi.ChildUis.TryGetValue(addSymbolChildCommand.AddedChildId, out var newChildUi))
        {
            Log.Warning($"Unable to create new operator - failed to retrieve new child ui \"{addSymbolChildCommand.AddedChildId}\" " +
                        $"from parent symbol ui {parentSymbolUi.Symbol}");
            return;
        }

        var newSymbolChild = newChildUi.SymbolChild;

        Instance newInstance;
        try
        {
            newInstance = context.CompositionInstance.Children[newChildUi.Id];
        }
        catch (Exception e)
        {
            // This can happen if compilation of instance failed (e.g. because there is syntax error in the
            // operator's c# code. Failing gracefully here will reveal these compilation errors in the console log.
            Log.Error($"Failed to access newly created symbol child {newChildUi.Id} {e.Message}");
            return;
        }
        
        
        //var newInstance = context.CompositionInstance.Children[newChildUi.Id];
        context.Selector.SetSelection(newChildUi, newInstance);

        // Connect to focus node...
        if (_snappedSourceItem != null)
        {
            var hasSplitConnection = false;
            var connectedInputId = Guid.Empty;
            if (newInstance.Inputs.Count > 0)
            {
                if (_snappedSourceOutputLine.ConnectionsOut.Count > 0 && newInstance.Outputs.Count > 0)
                {
                    var newItemOutput = newInstance.Outputs[0];

                    // Below the lowest op of a stack the new op becomes the stack's end, so it takes over
                    // the connections leaving to the right. Within a stack only the part below moves on.
                    var hasConnectionBelow = false;
                    foreach (var mc in _snappedSourceOutputLine.ConnectionsOut)
                    {
                        if (mc.Style is MagGraphConnection.ConnectionStyles.BottomToTop
                            or MagGraphConnection.ConnectionStyles.MainOutToMainInSnappedVertical)
                        {
                            hasConnectionBelow = true;
                            break;
                        }
                    }

                    // Reroute original connections...
                    foreach (var mc in _snappedSourceOutputLine.ConnectionsOut)
                    {
                        if (newItemOutput.ValueType != mc.Type)
                        {
                            Log.Warning("Skipping mismatched connection type");
                            continue;
                        }

                        var splitVertically = _connectionOrientation == MagGraphItem.Directions.Vertical
                                              && (mc.Style == MagGraphConnection.ConnectionStyles.BottomToTop
                                                  || (!hasConnectionBelow && mc.Style == MagGraphConnection.ConnectionStyles.RightToLeft));

                        var splitHorizontal = _connectionOrientation == MagGraphItem.Directions.Horizontal
                                              && mc.Style == MagGraphConnection.ConnectionStyles.RightToLeft;

                        if (!splitVertically && !splitHorizontal)
                            continue;

                        hasSplitConnection = true;

                        context.MacroCommand
                               .AddAndExecCommand(new DeleteConnectionCommand(context.CompositionInstance.Symbol,
                                                                              mc.AsSymbolConnection(),
                                                                              mc.MultiInputIndex));

                        context.MacroCommand
                               .AddAndExecCommand(new AddConnectionCommand(context.CompositionInstance.Symbol,
                                                                           new Symbol.Connection(newInstance.SymbolChildId,
                                                                                                 newItemOutput.Id,
                                                                                                 mc.TargetParentOrChildId,
                                                                                                 mc.TargetInput.Id
                                                                                                ),
                                                                           mc.MultiInputIndex));
                    }
                }

                // Create new Connection
                // Find first input with matching type
                var outputType = _snappedSourceOutputLine.Output.ValueType;
                var matchingInputSlot = newInstance.Inputs.FirstOrDefault(i => i.ValueType == outputType);
                if (matchingInputSlot == null)
                {
                    Log.Debug($"Can't find input with type {outputType}");
                }
                else if (matchingInputSlot.ValueType != outputType)
                {
                    Log.Warning("Skipping connection with mismatching types");
                }
                else
                {
                    var sourceId = _snappedSourceItem.Variant == MagGraphItem.Variants.Input ? Guid.Empty : _snappedSourceItem.Id;
                    var sourceOutputId = _snappedSourceItem.Variant == MagGraphItem.Variants.Input ? _snappedSourceItem.Id : _snappedSourceOutputLine.Id;
                    context.MacroCommand
                           .AddAndExecCommand(new AddConnectionCommand(context.CompositionInstance.Symbol,
                                                                       new Symbol.Connection(sourceId,
                                                                                             sourceOutputId,
                                                                                             newInstance.SymbolChildId,
                                                                                             matchingInputSlot.Id
                                                                                            ),
                                                                       0));
                    connectedInputId = matchingInputSlot.Id;
                }
            }

            // A connected parameter row (e.g. a value feeding "Scale") can only snap horizontally. At the end of a
            // chain or stack the new op goes right of the source, moved up so that row lines up with the output.
            var newItemLines = CollectNewItemLines(context, symbol, newChildUi, newInstance);
            var shiftedRows = 0;
            if (!hasSplitConnection && connectedInputId != Guid.Empty)
            {
                var connectedRow = 0;
                for (var lineIndex = 0; lineIndex < newItemLines.inputLines.Count; lineIndex++)
                {
                    if (newItemLines.inputLines[lineIndex].Id != connectedInputId)
                        continue;

                    connectedRow = lineIndex;
                    break;
                }

                var placeRight = connectedRow > 0
                                 && (_connectionOrientation == MagGraphItem.Directions.Horizontal
                                     || (_upstreamItems == null && !HasHorizontalSnapRight(_snappedSourceOutputLine)));
                if (placeRight)
                {
                    var outputRowPos = _snappedSourceItem.PosOnCanvas
                                       + new Vector2(_snappedSourceItem.Size.X, MagGraphItem.GridSize.Y * _snappedSourceOutputLine.VisibleIndex);

                    var moveNewChildCommand = new ModifyCanvasElementsCommand(parentSymbol.Id, [newChildUi], context.Selector);
                    context.MacroCommand.AddExecutedCommandForUndo(moveNewChildCommand);
                    newChildUi.PosOnCanvas = outputRowPos - new Vector2(0, MagGraphItem.GridSize.Y * connectedRow);
                    moveNewChildCommand.StoreCurrentValues();
                    shiftedRows = connectedRow;
                }
            }

            // Push snapped ops further down if new op exceed initial default height
            {
                var newHeight = newItemLines.inputLines.Count + newItemLines.outputLines.Count - 1 - shiftedRows;
                if (newHeight > 1)
                {
                    var extraHeight = MagGraphItem.GridSize.Y * (newHeight - 1);
                    var yThreshold = _snappedSourceItem.PosOnCanvas.Y + _snappedSourceItem.Size.Y - MagGraphItem.GridSize.Y / 2;
                    if (_connectionOrientation == MagGraphItem.Directions.Vertical)
                    {
                        // Outside a stack nothing is snapped below the new op that would need to make room
                        if (_upstreamItems != null)
                        {
                            MagItemMovement.MoveItems(context, _upstreamItems, new Vector2(0, -extraHeight));

                            var moveNewChildCommand = new ModifyCanvasElementsCommand(parentSymbol.Id, [newChildUi], context.Selector);
                            context.MacroCommand.AddExecutedCommandForUndo(moveNewChildCommand);
                            newChildUi.PosOnCanvas -= new Vector2(0, extraHeight);
                            moveNewChildCommand.StoreCurrentValues();
                        }
                    }
                    else
                    {
                        MagItemMovement.MoveSnappedItemsVertically(context, _snappedItems, yThreshold, extraHeight);
                    }
                }
            }
        }
        else if (_snappedTargetItem != null)
        {
            var inputLine = _snappedTargetItem.InputLines.FirstOrDefault(i => i.Id == _snappedTargetInputId);

            var getMatchingOutput = newInstance.Outputs.FirstOrDefault(o2 => o2.ValueType == inputLine.Type);
            if (getMatchingOutput == null)
            {
                Log.Warning("Can't find input?");
                return;
            }

            //var newItemOutput = newInstance.Outputs[0];
            context.MacroCommand
                   .AddAndExecCommand(new AddConnectionCommand(context.CompositionInstance.Symbol,
                                                               new Symbol.Connection(newInstance.SymbolChildId,
                                                                                     getMatchingOutput.Id,
                                                                                     _snappedTargetItem.Id,
                                                                                     _snappedTargetInputId
                                                                                    ),
                                                               0));
            var newHeight = ComputeNewItemUnitsHeight(context, symbol, newChildUi, newInstance);
            if (newHeight > 1)
            {
                MagItemMovement
                   .MoveSnappedItemsVertically(context,
                                               _snappedItems,
                                               newChildUi.PosOnCanvas.Y + MagGraphItem.GridSize.Y / 2,
                                               MagGraphItem.GridSize.Y * (newHeight - 1));
            }
        }
        else if (context.TempConnections.Count <= 1 && context.TryGetActiveOutputLine(out _))
        {
            var primaryInput = newInstance.Inputs.FirstOrDefault(i => i.ValueType == context.DraggedPrimaryOutputType);
            if (primaryInput != null)
            {
                var connectionToAdd = new Symbol.Connection(context.ActiveSourceItem!.Id,
                                                            context.ActiveSourceOutputId,
                                                            newInstance.SymbolChildId,
                                                            primaryInput.Id);

                context.MacroCommand
                       .AddAndExecCommand(new AddConnectionCommand(context.CompositionInstance.Symbol,
                                                                   connectionToAdd,
                                                                   0));
            }
        }
        // Wire connect temp connections
        else if (context.TempConnections.Count > 0 || context.ConnectionHovering.ConnectionHoversWhenClicked.Count > 0)
        {
            foreach (var h in context.ConnectionHovering.ConnectionHoversWhenClicked)
            {
                // Remove current connections
                context.MacroCommand
                       .AddAndExecCommand
                            (new DeleteConnectionCommand(context.CompositionInstance.Symbol,
                                                         h.Connection.AsSymbolConnection(),
                                                         h.Connection.MultiInputIndex
                                                        ));

                var tempConnectionOut = new MagGraphConnection
                                            {
                                                Style = MagGraphConnection.ConnectionStyles.Unknown,
                                                SourcePos = default,
                                                SourceOutput = null,
                                                SourceItem = null,
                                                TargetPos = default,
                                                TargetItem = h.Connection.TargetItem,
                                                InputLineIndex = h.Connection.InputLineIndex,
                                                MultiInputIndex = h.Connection.MultiInputIndex,
                                                OutputLineIndex = 0,
                                                VisibleOutputIndex = 0,
                                                ConnectionHash = 0,
                                                IsTemporary = true,
                                            };

                context.TempConnections.Add(tempConnectionOut);
            }

            foreach (var tc in context.TempConnections)
            {
                if (!tc.IsTemporary)
                    continue;

                if (tc.SourceItem != null && tc.TargetItem == null)
                {
                    var tcSourceOutput = tc.SourceOutput;
                    var matchingInput = newInstance.Inputs.FirstOrDefault(i => i.ValueType == tcSourceOutput.ValueType);
                    if (matchingInput != null)
                    {
                        var connectionToAdd = new Symbol.Connection(tc.SourceParentOrChildId,
                                                                    tcSourceOutput.Id,
                                                                    newInstance.SymbolChildId,
                                                                    matchingInput.Id);
                        context.MacroCommand
                               .AddAndExecCommand(new AddConnectionCommand(context.CompositionInstance.Symbol,
                                                                           connectionToAdd,
                                                                           tc.MultiInputIndex));
                    }
                }
                else if (tc.SourceItem == null && tc.TargetItem != null)
                {
                    var matchingOutput = newInstance.Outputs.FirstOrDefault(o => o.ValueType == tc.Type);
                    if (matchingOutput != null)
                    {
                        var connectionToAdd = new Symbol.Connection(newInstance.SymbolChildId,
                                                                    matchingOutput.Id,
                                                                    tc.TargetParentOrChildId,
                                                                    tc.TargetInput.Id);
                        context.MacroCommand
                               .AddAndExecCommand(new AddConnectionCommand(context.CompositionInstance.Symbol,
                                                                           connectionToAdd,
                                                                           tc.MultiInputIndex));
                    }
                }
            }
        }

        if (preset != null && SymbolVariationPool.TryCreateApplyPresetCommand(newInstance, preset, out var applyPresetCommand))
        {
            context.MacroCommand.AddAndExecCommand(applyPresetCommand);
            VariationHandling.GetOrLoadVariations(symbol.Id).SetActiveVariationWithoutApply(preset);
        }

        ParameterPopUp.NodeIdRequestedForParameterWindowActivation = newSymbolChild.Id;
        context.Layout.FlagStructureAsChanged();

        Complete(context);
    }

    private static int ComputeNewItemUnitsHeight(GraphUiContext context, Symbol symbol, SymbolUi.Child newChildUi,
                                                 Instance newInstance)
    {
        var (inputLines, outputLines) = CollectNewItemLines(context, symbol, newChildUi, newInstance);
        return inputLines.Count + outputLines.Count - 1;
    }

    private static (List<MagGraphItem.InputLine> inputLines, List<MagGraphItem.OutputLine> outputLines)
        CollectNewItemLines(GraphUiContext context, Symbol symbol, SymbolUi.Child newChildUi, Instance newInstance)
    {
        var newItem = new MagGraphItem
                          {
                              Variant = MagGraphItem.Variants.Operator,
                              Selectable = newChildUi,
                              Size = default,
                              SymbolUi = symbol.GetSymbolUi(),
                              SymbolChild = newChildUi.SymbolChild,
                              InstancePath = newInstance.InstancePath,
                              
                          };

        List<MagGraphItem.InputLine> inputLines = [];
        List<MagGraphItem.OutputLine> outputLines = [];
        MagGraphLayout.CollectVisibleLines(context, newItem, inputLines, outputLines);
        return (inputLines, outputLines);
    }

    private void Complete(GraphUiContext context)
    {
        context.CompleteMacroCommand();
        Reset(context);
    }

    private sealed class PlaceholderSelectable : ISelectableCanvasObject
    {
        public Guid Id => Guid.Empty;
        public Vector2 PosOnCanvas { get; set; }
        public Vector2 Size { get; set; }
    }

    private static readonly PlaceholderSelectable _placeHolderSelectable = new();

    internal static Guid PlaceHolderId = Guid.Parse("ffffffff-eeee-47C7-A17F-E297672EE1F3");

    /** Required For resetting selection and post creation layout changes */
    private MagGraphItem? _snappedSourceItem;

    /** Used when extracting inputs from parameter window */
    private MagGraphItem? _snappedTargetItem;

    private Guid _snappedTargetInputId;

    private static MagGraphItem.Directions _connectionOrientation = MagGraphItem.Directions.Horizontal;
    private MagGraphItem.OutputLine _snappedSourceOutputLine;
    private HashSet<MagGraphItem> _snappedItems = [];
    private HashSet<MagGraphItem>? _upstreamItems;
}