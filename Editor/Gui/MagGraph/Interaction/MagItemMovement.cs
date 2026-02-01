#nullable enable

using System.Diagnostics;
using ImGuiNET;
using T3.Core.DataTypes.Vector;
using T3.Core.Operator;
using T3.Editor.Gui.Interaction;
using T3.Editor.Gui.Interaction.Snapping;
using T3.Editor.Gui.MagGraph.Model;
using T3.Editor.Gui.MagGraph.States;
using T3.Editor.Gui.MagGraph.Ui;
using T3.Editor.Gui.Styling;
using T3.Editor.Gui.UiHelpers;
using T3.Editor.UiModel;
using T3.Editor.UiModel.Commands.Graph;
using T3.Editor.UiModel.InputsAndTypes;
using T3.Editor.UiModel.Modification;
using T3.Editor.UiModel.ProjectHandling;
using T3.Editor.UiModel.Selection;
using MagGraphView = T3.Editor.Gui.MagGraph.Ui.MagGraphView;
using Vector2 = System.Numerics.Vector2;

// ReSharper disable ForeachCanBePartlyConvertedToQueryUsingAnotherGetEnumerator
// ReSharper disable UseWithExpressionToCopyStruct

namespace T3.Editor.Gui.MagGraph.Interaction;

/// <summary>
/// Provides functions for moving, snapping, connecting, insertion etc. of operators. It controlled by the <see cref="StateMachine{T}"/>
/// </summary>
/// <remarks>
/// Things would be slightly more efficient if this would would use SnapGraphItems. However this would
/// prevent us from reusing fence selection. Enforcing this to be used for dragging inputs and outputs
/// makes this class unnecessarily complex.
/// </remarks>
internal sealed partial class MagItemMovement
{
    internal MagItemMovement(GraphUiContext graphUiContext, MagGraphView magGraphView, MagGraphLayout layout, NodeSelection nodeSelection)
    {
        _view = magGraphView;
        _layout = layout;
        _nodeSelection = nodeSelection;
        _context = graphUiContext;
    }

    private readonly GraphUiContext _context;

    internal void PrepareFrame(GraphUiContext context)
    {
        //PrepareDragInteraction();
        _snapHandlerX.DrawSnapIndicator(context.View, UiColors.StatusActivated.Fade(0.3f));
    }

    internal void PrepareDragInteraction()
    {
        UpdateConnectionsToDraggedItems(DraggedItems); // Sadly structure might change during drag...
        UpdateSnappedConnectionsToDraggedItems();
    }

    internal void CompleteDragOperation(GraphUiContext context)
    {
        Debug.Assert(context.MacroCommand != null);
        if (context.MacroCommand != null)
        {
            context.MoveElementsCommand?.StoreCurrentValues();
            context.CompleteMacroCommand();
        }

        if (!InputPicking.TryInitializeInputSelectionPickerForDraggedItem(context))
            Reset();
    }

    /// <summary>
    /// Reset to avoid accidental dragging of previous elements 
    /// </summary>
    internal void StopDragOperation()
    {
        _lastAppliedOffset = Vector2.Zero;
        SpliceSets.Clear();
        DraggedItems.Clear();
    }

    /// <summary>
    /// This will reset the state including all highlights and indicators 
    /// </summary>
    internal void Reset()
    {
        // TODO: should be done by states...
        _context.ActiveSourceItem = null;
        _context.DraggedPrimaryOutputType = null;

        DraggedItems.Clear();
        _shakeDetector.ResetShaking();
    }

    internal static void SelectActiveItem(GraphUiContext context)
    {
        var item = context.ActiveItem;
        if (item == null)
            return;

        var append = ImGui.GetIO().KeyShift;

        if (context.Selector.IsNodeSelected(item))
        {
            if (append)
            {
                context.Selector.DeselectNode(item, item.Instance);
            }
        }
        else
        {
            if (append)
            {
                item.AddToSelection(context.Selector);
            }
            else
            {
                item.Select(context.Selector);
            }
        }
    }

    internal void UpdateDragging(GraphUiContext context)
    {
        if (!T3Ui.IsCurrentlySaving && _shakeDetector.TestDragForShake(ImGui.GetMousePos()))
        {
            _shakeDetector.ResetShaking();
            if (HandleShakeDisconnect(context))
            {
                _layout.FlagStructureAsChanged();
                return;
            }
        }

        var snappingChanged = HandleSnappedDragging(context);
        if (!snappingChanged)
            return;

        _layout.FlagStructureAsChanged();

        HandleUnsnapAndCollapse(context);

        if (!_snapping.IsSnapped)
            return;

        if (_snapping.IsInsertion)
        {
            TrySplitInsert(context);
        }
        else
        {
            TryCreateNewConnectionFromSnap(context); 
        }
    }

    private bool HandleShakeDisconnect(GraphUiContext context)
    {
        //Log.Debug("Shake it!");
        Debug.Assert(context.MacroCommand != null);
        if (_connectionsToDraggedItems.Count == 0)
            return false;

        NodeActions.DisconnectDraggedNodes(context.CompositionInstance, DraggedItems.Select(i => i.Selectable).ToList());

        // foreach (var c in _borderConnections)
        // {
        //     context.MacroCommand.AddAndExecCommand(new DeleteConnectionCommand(context.CompositionOp.Symbol, c.AsSymbolConnection(), 0));
        // }

        _layout.FlagStructureAsChanged();
        return true;
    }

    internal static void UpdateLongPressIndicator(float longTapProgress)
    {
        var dl = ImGui.GetWindowDrawList();
        dl.AddCircle(ImGui.GetMousePos(), 100 * (1 - longTapProgress), Color.White.Fade(MathF.Pow(longTapProgress, 3)));
    }

    /// <summary>
    /// Update dragged items and use anchor definitions to identify and use potential snap targets
    /// </summary>
    private bool HandleSnappedDragging(GraphUiContext context)
    {
        var dl = ImGui.GetWindowDrawList();

        if (!_hasDragged)
        {
            _dragStartPosInOpOnCanvas = context.View.InverseTransformPositionFloat(ImGui.GetMousePos());
            _hasDragged = true;
        }

        var mousePosOnCanvas = context.View.InverseTransformPositionFloat(ImGui.GetMousePos());
        var requestedDeltaOnCanvas = mousePosOnCanvas - _dragStartPosInOpOnCanvas;

        var dragExtend = MagGraphItem.GetItemsBounds(DraggedItems);
        dragExtend.Expand(SnapThreshold * context.View.Scale.X);

        if (_view.ShowDebug)
        {
            dl.AddCircle(_view.TransformPosition(_dragStartPosInOpOnCanvas), 10, Color.Blue);

            dl.AddLine(_view.TransformPosition(_dragStartPosInOpOnCanvas),
                       _view.TransformPosition(_dragStartPosInOpOnCanvas + requestedDeltaOnCanvas), Color.Blue);

            dl.AddRect(_view.TransformPosition(dragExtend.Min),
                       _view.TransformPosition(dragExtend.Max),
                       Color.Green.Fade(0.1f));
        }

        var overlappingItems = new List<MagGraphItem>();
        
        foreach (var otherItem in _layout.Items.Values)
        {
            if (DraggedItems.Contains(otherItem) || !dragExtend.Overlaps(otherItem.Area))
                continue;

            overlappingItems.Add(otherItem);
        }

        // Move back to non-snapped position
        foreach (var n in DraggedItems)
        {
            n.PosOnCanvas -= _lastAppliedOffset; // Move to position
            n.PosOnCanvas += requestedDeltaOnCanvas; // Move to request position
        }

        if (DraggedItems.Count != _context.Selector.Selection.Count)
        {
            foreach (var a in _context.Selector.Selection)
            {
                if (a is not Annotation)
                    continue;
                
                a.PosOnCanvas -= _lastAppliedOffset; // Move to position
                a.PosOnCanvas += requestedDeltaOnCanvas; // Move to request position
            }
        }
        

        _lastAppliedOffset = requestedDeltaOnCanvas;
        _snapping.Reset();

        foreach (var ip in SpliceSets)
        {
            var insertionAnchorItem = DraggedItems.FirstOrDefault(i => i.Id == ip.InputItemId);
            if (insertionAnchorItem == null)
                continue;

            foreach (var otherItem in overlappingItems)
            {
                _snapping.TestItemsForInsertion(otherItem, insertionAnchorItem, ip, _view);
            }
        }

        foreach (var otherItem in overlappingItems)
        {
            foreach (var draggedItem in DraggedItems)
            {
                _snapping.TestItemsForSnap(otherItem, draggedItem, false, _view);
                _snapping.TestItemsForSnap(draggedItem, otherItem, true, _view);
            }
        }

        // Highlight best distance
        if (_view.ShowDebug && _snapping.BestDistance < 500)
        {
            var p1 = _view.TransformPosition(_snapping.OutAnchorPos);
            var p2 = _view.TransformPosition(_snapping.InputAnchorPos);
            dl.AddLine(p1, p2, UiColors.ForegroundFull.Fade(0.1f), 6);
        }

        // Snapped
        var snapPositionChanged = false;
        if (_snapping.IsSnapped)
        {
            var bestSnapDelta = _snapping.Reverse
                                    ? _snapping.InputAnchorPos - _snapping.OutAnchorPos
                                    : _snapping.OutAnchorPos - _snapping.InputAnchorPos;

            // var snapTargetPos = _snapping.Reverse
            //                         ? _snapping.InputAnchorPos 
            //                         : _snapping.OutAnchorPos;

            var snapPos = mousePosOnCanvas + bestSnapDelta;

            // dl.AddLine(_canvas.TransformPosition(mousePosOnCanvas),
            //            context.Canvas.TransformPosition(mousePosOnCanvas) + _canvas.TransformDirection(bestSnapDelta),
            //            Color.White);

            if (Vector2.Distance(snapPos, _lastSnapDragPositionOnCanvas) > 2) // ugh. Magic number
            {
                snapPositionChanged = true;
                LastSnapTime = ImGui.GetTime();
                _lastSnapDragPositionOnCanvas = snapPos;
                LastSnapTargetPositionOnCanvas = _snapping.Reverse
                                                     ? _snapping.InputAnchorPos
                                                     : _snapping.OutAnchorPos;
            }

            foreach (var n in DraggedItems)
            {
                n.PosOnCanvas += bestSnapDelta;
            }

            _lastAppliedOffset += bestSnapDelta;
        }
        // Unsnapped...
        else
        {
            _lastSnapDragPositionOnCanvas = Vector2.Zero;
            LastSnapTime = double.NegativeInfinity;

            HandleHorizontalAlignmentSnapping(context);
        }

        var snappingChanged = _snapping.IsSnapped != _wasSnapped || _snapping.IsSnapped && snapPositionChanged;
        _wasSnapped = _snapping.IsSnapped;
        return snappingChanged;
    }

    private void HandleHorizontalAlignmentSnapping(GraphUiContext context)
    {
        if (!UserSettings.Config.EnableHorizontalSnapping) 
            return;
        
        var snapFactor = 1f;
        var newDragPosInCanvas = _draggedSelectables[0].PosOnCanvas;

        var isFirst = true;
        ImRect bounds = default;
        foreach (var item in DraggedItems)
        {
            var itemBounds = item.Bounds;
            if (isFirst)
            {
                bounds = itemBounds;
                isFirst = false;
                continue;
            }

            bounds.Add(itemBounds);
        }

        bounds.Expand(200);


        _visibleItemsForSnapping.Clear();
        foreach (var snapTo in context.Layout.Items.Values)
        {
            if (bounds.Overlaps(snapTo.Bounds))
                _visibleItemsForSnapping.Add(snapTo);
        }

        if (!_snapHandlerX.TryCheckForSnapping(newDragPosInCanvas.X, out var snappedXValue,
                                               context.View.Scale.X * snapFactor,
                                               DraggedItems,
                                               _visibleItemsForSnapping
                                              )) 
            return;
        
        var snapDelta = snappedXValue - newDragPosInCanvas.X;
        var offset = new Vector2((float)snapDelta, 0);
        foreach (var n in DraggedItems)
        {
            n.PosOnCanvas += offset;
        }

        _lastAppliedOffset += offset;
    }

    private static readonly ValueSnapHandler _snapHandlerX = new(SnapResult.Orientations.Horizontal);
    private static readonly List<MagGraphItem> _visibleItemsForSnapping = [];

    public void StartDragOperation(GraphUiContext context)
    {
        _draggedSelectables = DraggedItems.Select(i => i as ISelectableCanvasObject).ToList();
        if (_draggedSelectables.Count == 0)
            return;
        
        var macroCommand = context.StartMacroCommand("Move nodes");

        context.MoveElementsCommand = new ModifyCanvasElementsCommand(context.CompositionInstance.Symbol.Id, _draggedSelectables, _nodeSelection);
        macroCommand.AddExecutedCommandForUndo(context.MoveElementsCommand);

        _lastAppliedOffset = Vector2.Zero;
        _hasDragged = false;

        UpdateConnectionsToDraggedItems(DraggedItems);
        UpdateSnappedConnectionsToDraggedItems();
        
        var mousePosInCanvas = context.View.InverseTransformPositionFloat(ImGui.GetMousePos());
        InitSpliceLinks(DraggedItems, mousePosInCanvas);
        InitPrimaryDraggedOutput();

        _unsnappedBorderConnectionsBeforeDrag.Clear();
        foreach (var c in _connectionsToDraggedItems)
        {
            if (!c.IsSnapped)
            {
                _unsnappedBorderConnectionsBeforeDrag.Add(c.ConnectionHash);
            }
        }

        _context.CompositionInstance.Symbol.GetSymbolUi().FlagAsModified();
    }

    private readonly HashSet<int> _unsnappedBorderConnectionsBeforeDrag = [];

    /// <summary>
    /// Handles op disconnection and collapsed
    /// </summary>
    private void HandleUnsnapAndCollapse(GraphUiContext context)
    {
        Debug.Assert(context.MacroCommand != null);

        var unsnappedConnections = new List<MagGraphConnection>();

        //var enableDisconnected = UserSettings.Config.DisconnectOnUnsnap ^ ImGui.GetIO().KeyShift;
        // if (!enableDisconnected)
        //     return;
        
        // Delete unsnapped connections
        foreach (var mc in _layout.MagConnections)
        {
            if (!IsBorderConnection(mc, DraggedItems))
                continue;
            
            if (_unsnappedBorderConnectionsBeforeDrag.Contains(mc.ConnectionHash))
                continue;

            var existedBeforeDragStart = _connectionToDragItemHashes.Contains(mc.ConnectionHash);
            if (existedBeforeDragStart)
            {
                var isVerticalStackDisconnect = FindVerticalCollapsableConnectionPairs(_connectionsToDraggedItems);
                var isHorizontalStackDisconnect = FindHorizontalCollapsableConnectionPairs(_connectionsToDraggedItems);
                if(isVerticalStackDisconnect.Count != 1 && isHorizontalStackDisconnect.Count != 1)
                    continue;
            }
            
            unsnappedConnections.Add(mc);

            var targetItemInputLine = mc.TargetItem.InputLines[mc.InputLineIndex];
            var connection = mc.AsSymbolConnection();

            context.MacroCommand.AddAndExecCommand(new DeleteConnectionCommand(context.CompositionInstance.Symbol,
                                                                               connection,
                                                                               targetItemInputLine.MultiInputIndex));
            mc.IsTemporary = true;
        }

        if (TryCollapseDragFromVerticalStack(context, unsnappedConnections))
            return;

        if (TryCollapseDragFromHorizontalStack(context, unsnappedConnections))
            return;

        TryCollapseDisconnectedInputs(context, unsnappedConnections);
    }

    private bool TryCollapseDragFromVerticalStack(GraphUiContext context, List<MagGraphConnection> unsnappedConnections)
    {
        Debug.Assert(context.MacroCommand != null);

        if (unsnappedConnections.Count == 0)
            return false;

        var list = FindVerticalCollapsableConnectionPairs(unsnappedConnections);

        if (list.Count > 1)
        {
            Log.Debug("Collapsing has too many possibilities");
            return false;
        }

        if (list.Count == 0)
            return false;

        var pair = list[0];
        if (pair.Ca.SourceItem.Instance != null)
        {
            if (Structure.CheckForCycle(pair.Ca.SourceItem.Instance, pair.Cb.TargetItem.Id))
            {
                Log.Debug("Sorry, this connection would create a cycle. (1)");
                return false;
            }
        }

        var potentialMovers = CollectSnappedItems(pair.Cb.TargetItem);

        // Clarify if the subset of items snapped to lower target op is sufficient to fill most gaps

        // First find movable items and then create command with movements
        var movableItems = MoveToCollapseVerticalGaps(pair.Ca, pair.Cb, potentialMovers, true);
        if (movableItems.Count == 0)
            return false;

        var affectedItemsAsNodes = movableItems.Select(i => i as ISelectableCanvasObject).ToList();
        var newMoveCommand = new ModifyCanvasElementsCommand(context.CompositionInstance.Symbol.Id, affectedItemsAsNodes, _nodeSelection);
        context.MacroCommand.AddExecutedCommandForUndo(newMoveCommand);

        MoveToCollapseVerticalGaps(pair.Ca, pair.Cb, movableItems, false);
        newMoveCommand.StoreCurrentValues();

        context.MacroCommand.AddAndExecCommand(new AddConnectionCommand(context.CompositionInstance.Symbol,
                                                                        new Symbol.Connection(pair.Ca.SourceItem.Id,
                                                                                              pair.Ca.SourceOutput.Id,
                                                                                              pair.Cb.TargetItem.Id,
                                                                                              pair.Cb.TargetInput.Id),
                                                                        0));
        return true;
    }

    private bool TryCollapseDragFromHorizontalStack(GraphUiContext context, List<MagGraphConnection> unsnappedConnections)
    {
        Debug.Assert(context.MacroCommand != null);

        if (unsnappedConnections.Count == 0)
            return false;

        var list = FindHorizontalCollapsableConnectionPairs(unsnappedConnections);

        if (list.Count > 1)
        {
            Log.Debug("Collapsing has too many possibilities");
            return false;
        }

        if (list.Count == 0)
            return false;

        var pair = list[0];

        if (pair.Ca.SourceItem.Instance != null)
        {
            if (Structure.CheckForCycle(pair.Ca.SourceItem.Instance, pair.Cb.TargetItem.Id))
            {
                Log.Debug("Sorry, this connection would create a cycle. (1)");
                return false;
            }
        }

        var potentialMovers = CollectSnappedItems(pair.Cb.TargetItem);

        // First find movable items and then create command with movements
        var movableItems = MoveToCollapseHorizontalGaps(pair.Ca, pair.Cb, potentialMovers, true);
        if (movableItems.Count == 0)
            return false;

        var affectedItemsAsNodes = movableItems.Select(i => i as ISelectableCanvasObject).ToList();
        var newMoveCommand = new ModifyCanvasElementsCommand(context.CompositionInstance.Symbol.Id, affectedItemsAsNodes, _nodeSelection);
        context.MacroCommand.AddExecutedCommandForUndo(newMoveCommand);

        MoveToCollapseHorizontalGaps(pair.Ca, pair.Cb, movableItems, false);
        newMoveCommand.StoreCurrentValues();

        context.MacroCommand.AddAndExecCommand(new AddConnectionCommand(context.CompositionInstance.Symbol,
                                                                        new Symbol.Connection(pair.Ca.SourceItem.Id,
                                                                                              pair.Ca.SourceOutput.Id,
                                                                                              pair.Cb.TargetItem.Id,
                                                                                              pair.Cb.TargetInput.Id),
                                                                        pair.Cb.MultiInputIndex));
        return true;
    }

    internal static List<SnapCollapseConnectionPair> FindLinkedVerticalCollapsableConnectionPairs(List<MagGraphConnection> unsnappedConnections)
    {
        // Find collapses
        var pairs = new List<SnapCollapseConnectionPair>();

        var ordered = unsnappedConnections.OrderBy(x => x.SourcePos.Y);

        var capturedConnections = new HashSet<MagGraphConnection>();
        // Flood fill vertical snap pairs
        foreach (var topC in ordered)
        {
            if (topC.Style != MagGraphConnection.ConnectionStyles.MainOutToMainInSnappedVertical)
                continue;

            if (capturedConnections.Contains(topC))
                continue;
            
            var linkedOutConnection = topC;

            // Find matching linked connections
            while (true)
            {
                if (!linkedOutConnection.IsSnapped)
                    break;

                var targetItem = linkedOutConnection.TargetItem;
                if (targetItem == null)
                    break;

                if (!targetItem.TryGetPrimaryOutConnections(out var outConnections))
                    break;

                var cOut = outConnections[0];

                if (!unsnappedConnections.Contains(cOut))
                    break;

                linkedOutConnection = cOut;

                capturedConnections.Add(linkedOutConnection);
            }

            if (linkedOutConnection != topC && topC.Type == linkedOutConnection.Type)
            {
                pairs.Add(new SnapCollapseConnectionPair(topC, linkedOutConnection));
            }
        }

        return pairs;
    }

    private static List<SnapCollapseConnectionPair> FindVerticalCollapsableConnectionPairs(List<MagGraphConnection> unsnappedConnections)
    {
        var list = new List<SnapCollapseConnectionPair>();

        // Collapse ops dragged from vertical stack...
        var potentialLinks = new List<MagGraphConnection>();

        foreach (var mc in unsnappedConnections)
        {
            if (mc.Style == MagGraphConnection.ConnectionStyles.MainOutToMainInSnappedVertical)
            {
                potentialLinks.Clear();
                foreach (var cb in unsnappedConnections)
                {
                    if (mc != cb
                        && cb.Style == MagGraphConnection.ConnectionStyles.MainOutToMainInSnappedVertical
                        && cb.SourcePos.Y > mc.SourcePos.Y
                        && Math.Abs(cb.SourcePos.X - mc.SourcePos.X) < SnapTolerance
                        && cb.Type == mc.Type)
                    {
                        potentialLinks.Add(cb);
                    }
                }

                if (potentialLinks.Count == 1)
                {
                    list.Add(new SnapCollapseConnectionPair(mc, potentialLinks[0]));
                }
                else if (potentialLinks.Count > 1)
                {
                    Log.Debug("Collapsing with gaps not supported yet");
                }
            }
        }

        return list;
    }

    private static List<SnapCollapseConnectionPair> FindHorizontalCollapsableConnectionPairs(List<MagGraphConnection> unsnappedConnections)
    {
        var list = new List<SnapCollapseConnectionPair>();

        // Collapse ops dragged from vertical stack...
        var potentialLinks = new List<MagGraphConnection>();

        foreach (var mc in unsnappedConnections)
        {
            if (mc.Style == MagGraphConnection.ConnectionStyles.MainOutToMainInSnappedHorizontal)
            {
                potentialLinks.Clear();
                foreach (var cb in unsnappedConnections)
                {
                    if (mc != cb
                        && (cb.Style == MagGraphConnection.ConnectionStyles.MainOutToMainInSnappedHorizontal || 
                            cb.Style ==MagGraphConnection.ConnectionStyles.MainOutToInputSnappedHorizontal)
                        && cb.SourcePos.X > mc.SourcePos.X
                        && Math.Abs(cb.SourcePos.Y - mc.SourcePos.Y) < SnapTolerance
                        && cb.Type == mc.Type)
                    {
                        potentialLinks.Add(cb);
                    }
                }

                if (potentialLinks.Count == 1)
                {
                    list.Add(new SnapCollapseConnectionPair(mc, potentialLinks[0]));
                }
                else if (potentialLinks.Count > 1)
                {
                    Log.Debug("Collapsing with gaps not supported yet");
                }
            }
        }

        return list;
    }

    private void TryCollapseDisconnectedInputs(GraphUiContext context, List<MagGraphConnection> unsnappedConnections)
    {
        if (unsnappedConnections.Count == 0)
            return;

        var snappedItems = new HashSet<MagGraphItem>();
        foreach (var mc in unsnappedConnections)
        {
            CollectSnappedItems(mc.TargetItem, snappedItems);
        }

        // Collapse lines of no longer visible inputs stack...
        var collapseLines = new HashSet<float>();
        foreach (var mc in unsnappedConnections)
        {
            var disconnectedInputWouldCollapseLine = DisconnectedInputWouldCollapseLine(mc);
            if (disconnectedInputWouldCollapseLine)
            {
                collapseLines.Add(mc.SourcePos.Y);
            }
        }

        if (collapseLines.Count == 0)
            return;

        foreach (var y in collapseLines.OrderDescending())
        {
            MoveSnappedItemsVertically(context,
                                       snappedItems,
                                       y,
                                       -MagGraphItem.GridSize.Y
                                      );
        }
    }

    public static bool DisconnectedInputWouldCollapseLine(MagGraphConnection connection)
    {
        var inputWasNotPrimary = connection.InputLineIndex > 0;
        if (connection.TargetItem.Variant != MagGraphItem.Variants.Operator)
            return false;
        
        var inputWasOptional = connection.TargetItem.InputLines[connection.InputLineIndex].InputUi.Relevancy == Relevancy.Optional;

        var multiInputConnectionCount = 0;

        foreach (var line in connection.TargetItem.InputLines)
        {
            if (line.InputUi.Id == connection.TargetItem.InputLines[connection.InputLineIndex].Id)
                multiInputConnectionCount++;
        }

        var multiInputHadOtherConnectedMultiInput = multiInputConnectionCount > 1;
        var connectedToLeftInput =
            connection.Style == MagGraphConnection.ConnectionStyles.BottomToLeft
            || connection.Style == MagGraphConnection.ConnectionStyles.RightToLeft
            || connection.Style == MagGraphConnection.ConnectionStyles.MainOutToMainInSnappedHorizontal
            || connection.Style == MagGraphConnection.ConnectionStyles.MainOutToInputSnappedHorizontal;

        if (connectedToLeftInput
            && (inputWasNotPrimary && inputWasOptional)
            || multiInputHadOtherConnectedMultiInput)
            return true;
        
        return false;
    }

    ///<summary>
    /// Iterate through gap lines and move items below upwards
    /// </summary>
    public static HashSet<MagGraphItem> MoveToCollapseVerticalGaps(MagGraphConnection ca, MagGraphConnection cb, HashSet<MagGraphItem> movableItems,
                                                                   bool dryRun)
    {
        var minY = ca.SourcePos.Y;
        var maxY = cb.TargetPos.Y;
        var lineWidth = MagGraphItem.GridSize.Y;

        var snapCount = (maxY - minY) / lineWidth;
        var roundedSnapCount = (int)(snapCount + 0.5f);
        var affectedItems = new HashSet<MagGraphItem>();
        for (var lineIndex = roundedSnapCount - 1; lineIndex >= 0; lineIndex--)
        {
            var isLineBlocked = false;
            var middleSnapLineY = minY + (0.5f + lineIndex) * lineWidth;

            foreach (var item in movableItems)
            {
                if (item.Area.Min.Y >= middleSnapLineY || item.Area.Max.Y <= middleSnapLineY)
                    continue;

                isLineBlocked = true;
                break;
            }

            if (isLineBlocked)
                continue;

            // move lines below up one step
            foreach (var item in movableItems)
            {
                if (item.PosOnCanvas.Y < middleSnapLineY)
                    continue;

                affectedItems.Add(item);

                if (!dryRun)
                    item.PosOnCanvas -= new Vector2(0, lineWidth);
            }
        }

        return affectedItems;
    }


    ///<summary>
    /// Try to close gap be looking for disjoint snapped set on the left and right
    /// </summary>
    private  HashSet<MagGraphItem> MoveToCollapseHorizontalGaps(MagGraphConnection ca, MagGraphConnection cb, HashSet<MagGraphItem> movableItems,
                                                                      bool dryRun)
    {
        var counterOffset = Vector2.Zero;
        foreach (var ss in SpliceSets)
        {
            if (ss.InputItemId == ca.TargetItem.Id && ss.Direction == MagGraphItem.Directions.Horizontal)
            {
                counterOffset = ss.DragPositionWithinBlock;
            }
        }

        var leftBatch = CollectSnappedItems(ca.SourceItem, null, true, ca.ConnectionHash);
        var rightBatch = CollectSnappedItems(cb.TargetItem, null, true, ca.ConnectionHash);
            
        var onlyLeft = new HashSet<MagGraphItem>(leftBatch);
        onlyLeft.ExceptWith(rightBatch);

        var onlyRight = new HashSet<MagGraphItem>(rightBatch);
        onlyRight.ExceptWith(leftBatch);

        var intersection = new HashSet<MagGraphItem>(rightBatch);
        intersection.IntersectWith(leftBatch);

        if (intersection.Count != 0)
        {
            return [];
        }
        
        var affectedItems = new HashSet<MagGraphItem>(onlyLeft);
        affectedItems.UnionWith(onlyRight);
        if (dryRun)
            return affectedItems;

        foreach (var item in onlyRight)
        {
            item.PosOnCanvas += new Vector2( counterOffset.X - MagGraphItem.GridSize.X, 0);
        }

        foreach (var item in onlyLeft)
        {
            item.PosOnCanvas += new Vector2(counterOffset.X, 0);
        }

        return affectedItems;
    }

    /// <summary>
    /// A pair of snapped connections that can be fused after unsnapping some items from a stack
    /// </summary>
    internal sealed record SnapCollapseConnectionPair(MagGraphConnection Ca, MagGraphConnection Cb);

    ///<summary>
    /// Search for potential new connections through snapping
    /// </summary>
    private void TryCreateNewConnectionFromSnap(GraphUiContext context)
    {
        if (!_snapping.IsSnapped)
            return;

        Debug.Assert(context.MacroCommand != null);

        var newConnections = new List<PotentialConnection>();
        foreach (var draggedItem in DraggedItems)
        {
            foreach (var otherItem in _layout.Items.Values)
            {
                if (DraggedItems.Contains(otherItem))
                    continue;

                GetPotentialConnectionsAfterSnap(ref newConnections, draggedItem, otherItem);
                GetPotentialConnectionsAfterSnap(ref newConnections, otherItem, draggedItem);
            }
        }

        foreach (var potentialConnection in newConnections)
        {
            // Avoid accidental vertical double connections to item above when snapping a horizontal connection
            if (potentialConnection.TargetItem.OutputLines.Length > 0
                && potentialConnection.TargetItem.OutputLines[0].ConnectionsOut.Count > 0)
            {
                var wouldConnectionOutSnapAfterMove = false;
                foreach (var targetOutConnection in potentialConnection.TargetItem.OutputLines[0].ConnectionsOut)
                {
                    if (targetOutConnection.IsSnapped)
                        continue;

                    var newOutputPos = targetOutConnection.SourceItem.PosOnCanvas
                                       + new Vector2(MagGraphItem.GridSize.X, MagGraphItem.GridSize.Y * (0.5f + targetOutConnection.VisibleOutputIndex));
                    if (Vector2.Distance(newOutputPos, targetOutConnection.TargetPos) < 0.01f)
                    {
                        wouldConnectionOutSnapAfterMove = true;
                    }
                }

                if (wouldConnectionOutSnapAfterMove)
                {
                    Log.Debug("Avoiding double vertical connection...");
                    continue;
                }
            }

            // Avoid accidental vertical double connections to item below when snapping a horizontal connection
            if (potentialConnection.SourceItem.OutputLines.Length > 0
                && potentialConnection.SourceItem.OutputLines[0].ConnectionsOut.Count > 0)
            {
                var wouldConnectionOutSnapAfterMove = false;
                foreach (var sourceOutConnection in potentialConnection.SourceItem.OutputLines[0].ConnectionsOut)
                {
                    // if (sourceOutConnection.IsSnapped && !sourceOutConnection.WasDisconnected.)
                    //     continue;

                    var newOutputPos = sourceOutConnection.SourceItem.PosOnCanvas
                                       + new Vector2(MagGraphItem.GridSize.X, MagGraphItem.GridSize.Y * (0.5f + sourceOutConnection.VisibleOutputIndex));
                    if (Vector2.Distance(newOutputPos, sourceOutConnection.TargetPos) < 4f)
                    {
                        wouldConnectionOutSnapAfterMove = true;
                    }
                }

                if (wouldConnectionOutSnapAfterMove)
                {
                    Log.Debug("Avoiding double connection...");
                    continue;
                }
            }

            var sourceParentOfSymbolChildId =
                potentialConnection.SourceItem.Variant == MagGraphItem.Variants.Input ? Guid.Empty : potentialConnection.SourceItem.Id;

            var targetParentOfSymbolChildId =
                potentialConnection.TargetItem.Variant == MagGraphItem.Variants.Output ? Guid.Empty : potentialConnection.TargetItem.Id;

            var newConnection = new Symbol.Connection(sourceParentOfSymbolChildId,
                                                      potentialConnection.OutputLine.Id,
                                                      targetParentOfSymbolChildId,
                                                      potentialConnection.InputLine.Id);

            if (Structure.CheckForCycle(context.CompositionInstance.Symbol, newConnection))
            {
                Log.Debug("Sorry, this connection would create a cycle. (4)");
                continue;
            }

            context.MacroCommand.AddAndExecCommand(new AddConnectionCommand(context.CompositionInstance.Symbol,
                                                                            newConnection,
                                                                            potentialConnection.InputLine.MultiInputIndex));
        }
    }

    private static void GetPotentialConnectionsAfterSnap(ref List<PotentialConnection> result, MagGraphItem a, MagGraphItem b)
    {
        MagGraphConnection? inConnection;

        for (var bInputLineIndex = 0; bInputLineIndex < b.InputLines.Length; bInputLineIndex++)
        {
            ref var bInputLine = ref b.InputLines[bInputLineIndex];
            inConnection = bInputLine.ConnectionIn;

            int aOutLineIndex;
            for (aOutLineIndex = 0; aOutLineIndex < a.OutputLines.Length; aOutLineIndex++)
            {
                ref var outputLine = ref a.OutputLines[aOutLineIndex]; // Avoid copying data from array
                if (bInputLine.Type != outputLine.Output.ValueType)
                    continue;

                // vertical
                if (aOutLineIndex == 0 && bInputLineIndex == 0)
                {
                    AddPossibleNewConnections(ref result,
                                              ref outputLine,
                                              ref bInputLine,
                                              new Vector2(a.Area.Min.X + MagGraphItem.WidthHalf, a.Area.Max.Y),
                                              new Vector2(b.Area.Min.X + MagGraphItem.WidthHalf, b.Area.Min.Y));
                }

                // horizontal
                if (outputLine.Output.ValueType == bInputLine.Type)
                {
                    AddPossibleNewConnections(ref result,
                                              ref outputLine,
                                              ref bInputLine,
                                              new Vector2(a.Area.Max.X, a.Area.Min.Y + (0.5f + outputLine.VisibleIndex) * MagGraphItem.LineHeight),
                                              new Vector2(b.Area.Min.X, b.Area.Min.Y + (0.5f + bInputLine.VisibleIndex) * MagGraphItem.LineHeight));
                }
            }
        }

        return;

        void AddPossibleNewConnections(ref List<PotentialConnection> newConnections,
                                       ref MagGraphItem.OutputLine outputLine,
                                       ref MagGraphItem.InputLine inputLine,
                                       Vector2 outPos,
                                       Vector2 inPos)
        {
            if (Vector2.Distance(outPos, inPos) > SnapTolerance)
                return;

            if (inConnection != null)
                return;

            // Clarify if outConnection should also be empty...
            if (outputLine.ConnectionsOut.Count > 0)
            {
                if (outputLine.ConnectionsOut[0].IsSnapped
                    && (outputLine.ConnectionsOut[0].SourcePos - inPos).Length() < SnapTolerance)
                    return;
            }

            newConnections.Add(new PotentialConnection(a, outputLine,
                                                       b, inputLine));
        }
    }

    private sealed record PotentialConnection(
        MagGraphItem SourceItem,
        MagGraphItem.OutputLine OutputLine,
        MagGraphItem TargetItem,
        MagGraphItem.InputLine InputLine);

    private void UpdateConnectionsToDraggedItems(HashSet<MagGraphItem> draggedItems)
    {
        _connectionsToDraggedItems.Clear();
        _connectionToDragItemHashes.Clear();
        
        // This could be optimized by only looking for dragged item connections
        foreach (var c in _layout.MagConnections)
        {
            if (!IsBorderConnection(c, draggedItems))
                continue;
            
            _connectionsToDraggedItems.Add(c);
            _connectionToDragItemHashes.Add(c.ConnectionHash);
        }
    }

    private static bool IsBorderConnection(MagGraphConnection c, HashSet<MagGraphItem> draggedItems)
    {
        var isTargetDragged = draggedItems.Contains(c.TargetItem);
        var isSourceDragged = draggedItems.Contains(c.SourceItem);

        return isTargetDragged != isSourceDragged;
    }    
    
    
    /// <summary>
    /// This is updated every frame...
    /// </summary>
    private void UpdateSnappedConnectionsToDraggedItems()
    {
        _snappedConnectionToDragItemHashes.Clear();

        foreach (var c in _connectionsToDraggedItems)
        {
            if (c.IsSnapped)
                _snappedConnectionToDragItemHashes.Add(c.ConnectionHash);
        }
    }

    /// <summary>
    /// When starting a new drag operation, we try to identify border input anchors of the dragged items,
    /// that can be used to insert them between other snapped items.
    /// </summary>
    private void InitSpliceLinks(HashSet<MagGraphItem> draggedItems, Vector2 mousePosInCanvas)
    {
        SpliceSets.Clear();

        foreach (var inputItemA in draggedItems)
        {
            var itemAInputCount = inputItemA.GetInputAnchorCount();
            MagGraphItem.InputAnchorPoint inputAnchor = default;

            for (var inputAnchorIndex = 0; inputAnchorIndex < itemAInputCount; inputAnchorIndex++)
            {
                inputItemA.GetInputAnchorAtIndex(inputAnchorIndex, ref inputAnchor);
                // make sure it's a snapped border connection
                if (inputAnchor.SnappedConnectionHash != MagGraphItem.FreeAnchor
                    && !_snappedConnectionToDragItemHashes.Contains(inputAnchor.SnappedConnectionHash))
                {
                    continue;
                }

                var spliceSets = new List<SpliceLink>();
                foreach (var outputItemB in draggedItems)
                {
                    var xy = inputAnchor.Direction == MagGraphItem.Directions.Horizontal ? 0 : 1;

                    if (Math.Abs(inputItemA.PosOnCanvas[1 - xy] - outputItemB.PosOnCanvas[1 - xy]) > SnapTolerance)
                        continue;

                    var outputAnchorCount = outputItemB.GetOutputAnchorCount();
                    MagGraphItem.OutputAnchorPoint outputAnchor = default;

                    for (var outputIndex = 0; outputIndex < outputAnchorCount; outputIndex++)
                    {
                        outputItemB.GetOutputAnchorAtIndex(outputIndex, ref outputAnchor);
                        if (outputAnchor.SnappedConnectionHash != MagGraphItem.FreeAnchor
                            && !_snappedConnectionToDragItemHashes.Contains(outputAnchor.SnappedConnectionHash))
                        {
                            continue;
                        }

                        if (
                            outputAnchor.Direction != inputAnchor.Direction
                            || inputAnchor.ConnectionType != outputAnchor.ConnectionType)
                        {
                            continue;
                        }

                        var inputAnchorPositionOnCanvas = inputAnchor.PositionOnCanvas - inputItemA.PosOnCanvas;
                        
                        spliceSets.Add(new SpliceLink(inputItemA.Id,
                                                                inputAnchor.SlotId,
                                                                outputItemB.Id,
                                                                outputAnchor.SlotId,
                                                                inputAnchor.Direction,
                                                                inputAnchor.ConnectionType,
                                                                outputAnchor.PositionOnCanvas[xy] - inputAnchor.PositionOnCanvas[xy],
                                                                mousePosInCanvas - inputItemA.PosOnCanvas,
                                                                inputAnchorPositionOnCanvas
                                                               ));
                    }
                }

                // Skip insertion lines with gaps
                if (spliceSets.Count == 1)
                {
                    SpliceSets.Add(spliceSets[0]);
                }
            }
        }
    }

    ///<summary>
    /// Search for potential new connections through snapping
    /// </summary>
    // ReSharper disable once UnusedMethodReturnValue.Local
    private static bool TrySplitInsert(GraphUiContext context)
    {
        if (!_snapping.IsSnapped || _snapping.TargetItem == null || _snapping.SourceItem == null)
            return false;

        Debug.Assert(context.MacroCommand != null);

        var spliceLink = _snapping.InsertionPoint;

        // Split connection
        var connection = _snapping.TargetItem.InputLines[_snapping.InputLineIndex].ConnectionIn;
        if (connection == null)
        {
            Log.Warning("Missing connection?");
            return true;
        }

        if (spliceLink == null)
        {
            Log.Warning("Insertion point is undefined?");
            return false;
        }

        if (connection.SourceItem.Instance != null)
        {
            if (Structure.CheckForCycle(connection.SourceItem.Instance, spliceLink.InputItemId))
            {
                Log.Debug("Sorry, this connection would create a cycle. (2)");
                return false;
            }
        }

        if (connection.TargetItem.Instance != null)
        {
            if (Structure.CheckForCycle(connection.TargetItem.Instance, spliceLink.OutputItemId))
            {
                Log.Debug("Sorry, this connection would create a cycle. (3)");
                return false;
            }
        }

        context.MacroCommand.AddAndExecCommand(new DeleteConnectionCommand(context.CompositionInstance.Symbol,
                                                                           connection.AsSymbolConnection(),
                                                                           0));
        context.MacroCommand.AddAndExecCommand(new AddConnectionCommand(context.CompositionInstance.Symbol,
                                                                        new Symbol.Connection(connection.SourceItem.Id,
                                                                                              connection.SourceOutput.Id,
                                                                                              spliceLink.InputItemId,
                                                                                              spliceLink.InputId
                                                                                             ), 0));

        context.MacroCommand.AddAndExecCommand(new AddConnectionCommand(context.CompositionInstance.Symbol,
                                                                        new Symbol.Connection(spliceLink.OutputItemId,
                                                                                              spliceLink.OutputId,
                                                                                              connection.TargetItem.Id,
                                                                                              connection.TargetInput.Id
                                                                                             ), 0));

        // Move items
        if (spliceLink.Direction == MagGraphItem.Directions.Vertical)
        {
            MoveSnappedItemsVertically(context,
                                       CollectSnappedItems(_snapping.TargetItem),
                                       _snapping.OutAnchorPos.Y - MagGraphItem.GridSize.Y / 2,
                                       spliceLink.Distance);
        }
        else
        {
            var leftBatch = CollectSnappedItems(_snapping.SourceItem, null, true, connection.ConnectionHash);
            var rightBatch = CollectSnappedItems(_snapping.TargetItem, null, true, connection.ConnectionHash);
            
            var onlyLeft = new HashSet<MagGraphItem>(leftBatch);
            onlyLeft.ExceptWith(rightBatch);

            var onlyRight = new HashSet<MagGraphItem>(rightBatch);
            onlyRight.ExceptWith(leftBatch);

            var intersection = new HashSet<MagGraphItem>(rightBatch);
            intersection.IntersectWith(leftBatch);
            
            MoveItems(context, onlyLeft, -new Vector2(spliceLink.DragPositionWithinBlock.X ,0));            
            MoveItems(context, onlyRight, new Vector2(spliceLink.Distance - spliceLink.DragPositionWithinBlock.X,0));            
            MoveSnappedItemsHorizontally(context,
                                         intersection,
                                         _snapping.OutAnchorPos.X - MagGraphItem.GridSize.X / 2,
                                         spliceLink.Distance);
        }

        return true;
    }

    /// <summary>
    /// Creates and applies a command to move items vertically
    /// </summary>
    /// <returns>
    /// True if some items where moved
    /// </returns>
    public static void MoveSnappedItemsVertically(GraphUiContext context, HashSet<MagGraphItem> snappedItems, float yThreshold, float yDistance)
    {
        Debug.Assert(context.MacroCommand != null);
        var movableItems = new List<MagGraphItem>();
        foreach (var otherItem in snappedItems)
        {
            if (otherItem.PosOnCanvas.Y > yThreshold)
            {
                movableItems.Add(otherItem);
            }
        }

        if (movableItems.Count == 0)
            return;

        // Move items down...
        var affectedItemsAsNodes = movableItems.Select(i => i as ISelectableCanvasObject).ToList();
        var newMoveComment = new ModifyCanvasElementsCommand(context.CompositionInstance.Symbol.Id, affectedItemsAsNodes, context.Selector);
        context.MacroCommand.AddExecutedCommandForUndo(newMoveComment);

        foreach (var item in affectedItemsAsNodes)
        {
            item.PosOnCanvas += new Vector2(0, yDistance);
        }

        newMoveComment.StoreCurrentValues();
    }

    /// <summary>
    /// Creates and applies a command to move items vertically
    /// </summary>
    /// <returns>
    /// True if some items where moved
    /// </returns>
    public static void MoveSnappedItemsHorizontally(GraphUiContext context, HashSet<MagGraphItem> snappedItems, float xThreshold, float xDistance)
    {
        Debug.Assert(context.MacroCommand != null);
        
        var movableItems = new List<MagGraphItem>();
        foreach (var otherItem in snappedItems)
        {
            if (otherItem.PosOnCanvas.X > xThreshold)
            {
                movableItems.Add(otherItem);
            }
        }

        if (movableItems.Count == 0)
            return;

        MoveItems(context, movableItems, new Vector2(xDistance,0));
    }

    private static void MoveItems(GraphUiContext context, IEnumerable<MagGraphItem> movableItems, Vector2 offset)
    {
        Debug.Assert(context.MacroCommand != null);
        
        // Move items left...
        var affectedItemsAsNodes = movableItems.Select(i => i as ISelectableCanvasObject).ToList();
        var newMoveComment = new ModifyCanvasElementsCommand(context.CompositionInstance.Symbol.Id, affectedItemsAsNodes, context.Selector);
        context.MacroCommand.AddExecutedCommandForUndo(newMoveComment);

        foreach (var item in affectedItemsAsNodes)
        {
            item.PosOnCanvas += offset;
        }

        newMoveComment.StoreCurrentValues();
    }

    private void InitPrimaryDraggedOutput()
    {
        _context.DraggedPrimaryOutputType = null;
        _context.ItemForInputSelection = null;

        _context.ActiveSourceItem = FindPrimaryOutputItem();
        if (_context.ActiveSourceItem == null)
            return;

        _context.DraggedPrimaryOutputType = _context.ActiveSourceItem.PrimaryType;
    }

    /// <summary>
    /// Snapping an item onto a hidden parameter is a tricky ui problem.
    /// To allow this interaction we add "peek" anchor indicator to the dragged item set.
    /// If this is dropped without snapping onto an operator with hidden parameters of the matching type,
    /// we present cables.gl inspired picker interface.
    ///
    /// Set primary types to indicate targets for dragging
    /// This part is a little fishy. To have "some" solution we try to identify dragged
    /// constellations that has a single free horizontal output anchor on the left column.
    /// </summary>
    private MagGraphItem? FindPrimaryOutputItem()
    {
        if (DraggedItems.Count == 0)
            return null;

        if (DraggedItems.Count == 1)
            return DraggedItems.First();

        var itemsOrderedByX = DraggedItems.OrderByDescending(c => c.PosOnCanvas.X);
        var rightItem = itemsOrderedByX.First();

        // Check if there are multiple items on right edge column...
        var rightColumnItemCount = 0;
        foreach (var i in DraggedItems)
        {
            if (Math.Abs(i.PosOnCanvas.X - rightItem.PosOnCanvas.X) < SnapTolerance)
                rightColumnItemCount++;
        }

        if (rightColumnItemCount > 1)
            return null;

        if (DraggedItems.Count != CollectSnappedItems(rightItem).Count)
            return null;

        return rightItem;
    }

    /// <summary>
    /// Add snapped items to the given set or create new set
    /// </summary>
    public static HashSet<MagGraphItem> CollectSnappedItems(MagGraphItem rootItem,  HashSet<MagGraphItem>? set = null, bool includeRoot= true, int ignoreConnectionHash= 0)
    {
        set ??= [];

        Collect(rootItem);
        if (!includeRoot)
            set.Remove(rootItem);
        
        return set;

        void Collect(MagGraphItem item)
        {
            if (!set.Add(item))
                return;

            for (var index = 0; index < item.InputLines.Length; index++)
            {
                var c = item.InputLines[index].ConnectionIn;
                if (c == null || c.ConnectionHash == ignoreConnectionHash)
                    continue;

                if (c.IsSnapped && !c.IsTemporary)
                    Collect(c.SourceItem);
            }

            for (var index = 0; index < item.OutputLines.Length; index++)
            {
                var connections = item.OutputLines[index].ConnectionsOut;
                foreach (var c in connections)
                {
                    if (c.ConnectionHash != ignoreConnectionHash && c.IsSnapped)
                        Collect(c.TargetItem);
                }
            }
        }
    }

    public static HashSet<MagGraphItem> CollectSnappedItems(IEnumerable<MagGraphItem> rootItems)
    {
        var set = new HashSet<MagGraphItem>();

        foreach (var i in rootItems)
        {
            CollectSnappedItems(i, set);
        }

        return set;
    }

    internal void SetDraggedItemIds(List<Guid> selectedIds)
    {
        DraggedItems.Clear();
        foreach (var id in selectedIds)
        {
            if (_layout.Items.TryGetValue(id, out var i))
            {
                DraggedItems.Add(i);
            }
        }
    }

    internal void SetDraggedItems(List<ISelectableCanvasObject> selection)
    {
        DraggedItems.Clear();
        foreach (var s in selection)
        {
            if (_layout.Items.TryGetValue(s.Id, out var i))
            {
                DraggedItems.Add(i);
            }
        }
    }

    internal void SetDraggedItemIdsToSnappedForItem(MagGraphItem item)
    {
        DraggedItems.Clear();
        CollectSnappedItems(item, DraggedItems);
    }

    internal bool IsItemDragged(MagGraphItem item) => DraggedItems.Contains(item);

    internal double LastSnapTime = double.NegativeInfinity;
    private Vector2 _lastSnapDragPositionOnCanvas;

    /** for visual indication only */
    internal Vector2 LastSnapTargetPositionOnCanvas;

    internal readonly List<SpliceLink> SpliceSets = [];

    private Vector2 _lastAppliedOffset;
    private const float SnapThreshold = 30;
    
    /// <summary>
    /// Connections between dragged and non-dragged items
    /// </summary>
    private readonly List<MagGraphConnection> _connectionsToDraggedItems = [];
    private readonly HashSet<int> _connectionToDragItemHashes = [];
    private readonly HashSet<int> _snappedConnectionToDragItemHashes = [];
    
    private bool _wasSnapped;
    private static readonly MagItemMovement.Snapping _snapping = new();

    private static bool _hasDragged;
    private Vector2 _dragStartPosInOpOnCanvas;

    private readonly ShakeDetector _shakeDetector = new();

    internal readonly HashSet<MagGraphItem> DraggedItems = [];
    private List<ISelectableCanvasObject> _draggedSelectables = [];

    /// <summary>
    /// Defines a method how the currently dragged nodes could split some other snapped graph nodes
    /// either horizontally or vertically. It provides information on how to snap, what type of
    /// connection would be split and which inputs and outputs to splice in.
    /// </summary>
    /// <param name="DragPositionWithinBlock">The distance from top left position of InputItem (A) to where the mouse drag started.</param>
    internal sealed record SpliceLink(
        Guid InputItemId,
        Guid InputId,
        Guid OutputItemId,
        Guid OutputId,
        MagGraphItem.Directions Direction,
        Type Type,
        float Distance,
        Vector2 DragPositionWithinBlock,
        Vector2 AnchorOffset);

    

    private readonly MagGraphView _view;
    private readonly MagGraphLayout _layout;
    private readonly NodeSelection _nodeSelection;
    private const float SnapTolerance = 0.01f;
}