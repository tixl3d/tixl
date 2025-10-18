#nullable enable
using System.Diagnostics;
using ImGuiNET;
using T3.Core.Operator;
using T3.Editor.Gui.Graph;
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

        var outputLineIndex = hoverConnection.SourceItem.Instance?.Outputs.IndexOf(hoverConnection.SourceOutput) ?? 0;

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

    internal void OpenOnCanvas(GraphUiContext context, Vector2 posOnCanvas, Type? inputTypeFilter = null)
    {
        context.StartMacroCommand("Insert Operator");

        PlaceholderItem = new MagGraphItem
                              {
                                  Selectable = _placeHolderSelectable,
                                  PosOnCanvas = posOnCanvas,
                                  Id = PlaceHolderId,
                                  Size = MagGraphItem.GridSize,
                                  Variant = MagGraphItem.Variants.Placeholder,
                              };

        context.Layout.Items[PlaceHolderId] = PlaceholderItem;
        PlaceHolderUi.Open(context, PlaceholderItem, inputFilter: inputTypeFilter);
    }

    internal void OpenForItemOutput(GraphUiContext context,
                              MagGraphItem item,
                              MagGraphItem.OutputLine outputLine,
                              MagGraphItem.Directions direction = MagGraphItem.Directions.Vertical)
    {
        Debug.Assert(item.OutputLines.Length > 0);
        context.StartMacroCommand("Insert Operator");

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

        // Make space vertically
        if (direction == MagGraphItem.Directions.Vertical)
        {
            // Keep for after creation because inserted node might exceed unit height and further pushing is required... 
            _snappedItems = MagItemMovement.CollectSnappedItems(item);

            MagItemMovement
               .MoveSnappedItemsVertically(context,
                                           _snappedItems,
                                           item.PosOnCanvas.Y + item.Size.Y - MagGraphItem.GridSize.Y / 2,
                                           MagGraphItem.GridSize.Y);
        }
        else
        {
            // Keep for after creation because inserted node might exceed unit height and further pushing is required... 
            _snappedItems = MagItemMovement.CollectSnappedItems(item);

            MagItemMovement
               .MoveSnappedItemsHorizontally(context,
                                             _snappedItems,
                                             item.PosOnCanvas.X + item.Size.X - MagGraphItem.GridSize.X / 2,
                                             MagGraphItem.GridSize.X);
        }

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
            CreateInstance(context, selectedUi.Symbol);
        }
        else if (uiResult.HasFlag(PlaceHolderUi.UiResults.Cancel))
        {
            Cancel(context);
        }
    }

    private void CreateInstance(GraphUiContext context, Symbol symbol)
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
            if (newInstance.Inputs.Count > 0)
            {
                if (_snappedSourceOutputLine.ConnectionsOut.Count > 0)
                {
                    var newItemOutput = newInstance.Outputs[0];

                    // Reroute original connections...
                    foreach (var mc in _snappedSourceOutputLine.ConnectionsOut)
                    {
                        if (newItemOutput.ValueType != mc.Type)
                        {
                            Log.Warning("Skipping mismatched connection type");
                            continue;
                        }

                        var splitVertically = _connectionOrientation == MagGraphItem.Directions.Vertical
                                              && mc.Style == MagGraphConnection.ConnectionStyles.BottomToTop;

                        var splitHorizontal = _connectionOrientation == MagGraphItem.Directions.Horizontal
                                              && mc.Style == MagGraphConnection.ConnectionStyles.RightToLeft;

                        if (!splitVertically && !splitHorizontal)
                            continue;

                        context.MacroCommand
                               .AddAndExecCommand(new DeleteConnectionCommand(context.CompositionInstance.Symbol,
                                                                              mc.AsSymbolConnection(),
                                                                              mc.MultiInputIndex));

                        context.MacroCommand
                               .AddAndExecCommand(new AddConnectionCommand(context.CompositionInstance.Symbol,
                                                                           new Symbol.Connection(newInstance.SymbolChildId,
                                                                                                 newItemOutput.Id,
                                                                                                 mc.TargetItem.Id,
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
                }
            }

            // Push snapped ops further down if new op exceed initial default height
            {
                var newHeight = ComputeNewItemUnitsHeight(context, symbol, newChildUi, newInstance);
                if (newHeight > 1)
                {
                    MagItemMovement
                       .MoveSnappedItemsVertically(context,
                                                   _snappedItems,
                                                   _snappedSourceItem.PosOnCanvas.Y + _snappedSourceItem.Size.Y - MagGraphItem.GridSize.Y / 2,
                                                   MagGraphItem.GridSize.Y * (newHeight - 1));
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
        else if (context.TryGetActiveOutputLine(out _))
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

            var primaryInput = newInstance.Inputs.FirstOrDefault();
            var primaryOutput = newInstance.Outputs.FirstOrDefault();
            if (primaryInput != null && primaryOutput != null)
            {
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
                            var connectionToAdd = new Symbol.Connection(tc.SourceItem.Id,
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
                        var connectionToAdd = new Symbol.Connection(newInstance.SymbolChildId,
                                                                    primaryOutput.Id,
                                                                    tc.TargetItem.Id,
                                                                    tc.TargetInput.Id);
                        context.MacroCommand
                               .AddAndExecCommand(new AddConnectionCommand(context.CompositionInstance.Symbol,
                                                                           connectionToAdd,
                                                                           tc.MultiInputIndex));
                    }
                }
            }
        }

        // TODO: add preset selection...

        ParameterPopUp.NodeIdRequestedForParameterWindowActivation = newSymbolChild.Id;
        context.Layout.FlagStructureAsChanged();

        Complete(context);
    }

    private static int ComputeNewItemUnitsHeight(GraphUiContext context, Symbol symbol, SymbolUi.Child newChildUi,
                                                 Instance newInstance)
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

        var newHeight = inputLines.Count + outputLines.Count - 1;
        return newHeight;
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
}