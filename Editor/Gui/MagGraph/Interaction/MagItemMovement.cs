#nullable enable

using System.Diagnostics;
using ImGuiNET;
using T3.Core.DataTypes.Vector;
using T3.Core.Utils;
using T3.Core.Operator;
using T3.Editor.Gui.Interaction;
using T3.Editor.Gui.Interaction.Snapping;
using T3.Editor.Gui.MagGraph.Model;
using T3.Editor.Gui.MagGraph.States;
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
    /// <summary>Creates movement handling for one graph view and its selection.</summary>
    /// <param name="graphUiContext">Graph context providing the current composition, layout, selection, and interaction state.</param>
    /// <param name="magGraphView">Canvas that transforms mouse positions and displays the dragged nodes.</param>
    /// <param name="layout">Layout whose node and connection records participate in snapping.</param>
    /// <param name="nodeSelection">Selection used to determine and update the dragged items.</param>
    internal MagItemMovement(GraphUiContext graphUiContext, MagGraphView magGraphView, MagGraphLayout layout, NodeSelection nodeSelection)
    {
        _view = magGraphView;
        _layout = layout;
        _nodeSelection = nodeSelection;
        _context = graphUiContext;
    }

    private readonly GraphUiContext _context;

    /// <summary>Updates movement targets and frame state for the current selection.</summary>
    /// <param name="context">Graph context providing the current composition, layout, selection, and interaction state.</param>
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

    /// <summary>
    /// Finishes movement and pending cleanup in the move macro, then resolves input picking.
    /// </summary>
    /// <param name="context">Graph context providing the current composition, layout, selection, and interaction state.</param>
    internal void CompleteDragOperation(GraphUiContext context)
    {
        Debug.Assert(context.MacroCommand != null);
        if (context.MacroCommand != null)
        {
            context.MoveElementsCommand?.StoreCurrentValues();
            CompleteSlowGrow(context);
            GrowSectionsToFitDisplacedMembers(context);
            context.CompleteMacroCommand();

            // Section ownership is re-derived from geometry on the layout refresh
            _layout.FlagStructureAsChanged();
        }

        if (!InputPicking.TryInitializeInputSelectionPickerForDraggedItem(context))
            Reset();
    }

    /// <summary>
    /// Dragging ops slowly against their section's bottom/right border makes the border
    /// yield. The speed test only decides the initial engagement — once the border
    /// yielded, it follows the dragged ops continuously, so the growth is smooth instead
    /// of stuttering with the speed hovering around the threshold. Pulling the ops fully
    /// back inside (or holding Shift) releases the latch, and the next border contact
    /// decides afresh. Disabled while the frame is selected, or when
    /// <see cref="UserSettings.ConfigData.SectionSlowResizeSpeed"/> is 0.
    /// </summary>
    /// <param name="context">Graph context providing the current composition, layout, selection, and interaction state.</param>
    private void TrySlowGrowSection(GraphUiContext context)
    {
        var unlockSpeed = UserSettings.Config.SectionSlowResizeSpeed;
        if (unlockSpeed <= 0 || _slowGrowSectionId == Guid.Empty || DraggedItems.Count == 0)
            return;

        var io = ImGui.GetIO();
        var speed = io.MouseDelta.Length() / MathF.Max(io.DeltaTime, 0.0001f) / T3Ui.UiScaleFactor;
        _dampedDragSpeed = MathUtils.Lerp(_dampedDragSpeed, speed, 0.3f);

        var symbolUi = context.CompositionInstance.GetSymbolUi();
        if (!symbolUi.Sections.TryGetValue(_slowGrowSectionId, out var section)
            || section.Collapsed || section.IsHiddenInCollapsedSection
            || context.Selector.IsNodeSelected(section))
            return;

        var draggedBounds = MagGraphItem.GetItemsBounds(DraggedItems);
        var boundsMin = section.PosOnCanvas;
        var boundsMax = section.PosOnCanvas + section.Size;

        // Border contact requires overlapping the frame on the other axis too - an op
        // dragged past the frame's corner is outside, not pressing against the border
        var overlapsVertically = draggedBounds.Min.Y < boundsMax.Y && draggedBounds.Max.Y > boundsMin.Y;
        var overlapsHorizontally = draggedBounds.Min.X < boundsMax.X && draggedBounds.Max.X > boundsMin.X;

        var touchesRight = overlapsVertically && draggedBounds.Max.X > boundsMax.X && draggedBounds.Min.X < boundsMax.X;
        var touchesBottom = overlapsHorizontally && draggedBounds.Max.Y > boundsMax.Y && draggedBounds.Min.Y < boundsMax.Y;

        if (io.KeyShift)
        {
            _slowGrowEngaged = false;
            return;
        }

        if (!_slowGrowEngaged)
        {
            // Fully back inside? The next contact decides afresh.
            if (!touchesRight && !touchesBottom)
                return;

            if (_dampedDragSpeed > unlockSpeed)
                return;

            _slowGrowEngaged = true;
        }

        // Follow the dragged ops continuously while engaged
        var newMax = boundsMax;
        if (touchesRight)
            newMax.X = draggedBounds.Max.X + SectionTree.ContentPadding.X;

        if (touchesBottom)
            newMax.Y = draggedBounds.Max.Y + SectionTree.ContentPadding.Y;

        if (draggedBounds.Max.X <= boundsMax.X && draggedBounds.Max.Y <= boundsMax.Y)
        {
            // Ops returned fully inside - release the latch (the frame keeps its size)
            _slowGrowEngaged = false;
            return;
        }

        if (newMax == boundsMax)
            return;

        if (_slowGrowCommand == null)
        {
            _slowGrowCommand = new ModifyCanvasElementsCommand(symbolUi, [section], context.Selector);
            _growPushPreview.Engage(symbolUi, section, context.Selector);
        }

        _slowGrewX |= newMax.X > boundsMax.X;
        _slowGrewY |= newMax.Y > boundsMax.Y;
        section.Size = newMax - section.PosOnCanvas;

        // Push neighbors ahead of the growing border right away - leaving overlaps
        // unresolved until release would let ownership derivation flip memberships mid-drag
        _growPushPreview.Update(section, pushDown: _slowGrewY, pushRight: _slowGrewX);
        _layout.FlagStructureAsChanged();
    }

    /// <summary>
    /// Folds the accumulated slow-grow resize into the drag's MacroCommand and pushes
    /// neighbors out of the grown bounds.
    /// </summary>
    /// <param name="context">Graph context providing the current composition, layout, selection, and interaction state.</param>
    private void CompleteSlowGrow(GraphUiContext context)
    {
        if (_slowGrowCommand == null)
        {
            _slowGrowSectionId = Guid.Empty;
            return;
        }

        _slowGrowCommand.StoreCurrentValues();
        context.MacroCommand!.AddExecutedCommandForUndo(_slowGrowCommand);
        _growPushPreview.Complete(context.MacroCommand);

        // Sibling overlaps are already resolved by the live preview; this settles
        // ancestor growth for nested frames
        var symbolUi = context.CompositionInstance.GetSymbolUi();
        if (symbolUi.Sections.TryGetValue(_slowGrowSectionId, out var grownSection))
        {
            // Only resolve along axes that actually grew - resolving the other axis would
            // "fix" remaining overlaps by flinging neighbors sideways
            var bounds = ImRect.RectWithSize(grownSection.PosOnCanvas, grownSection.Size);
            if (_slowGrewY)
                SectionTree.ResolveBoundsExpansion(symbolUi, grownSection, bounds, Vector2.UnitY, context.MacroCommand, context.Selector);

            if (_slowGrewX)
                SectionTree.ResolveBoundsExpansion(symbolUi, grownSection, bounds, Vector2.UnitX, context.MacroCommand, context.Selector);
        }

        _slowGrowCommand = null;
        _slowGrowSectionId = Guid.Empty;
    }

    private Guid _slowGrowSectionId;
    private ModifyCanvasElementsCommand? _slowGrowCommand;
    private readonly SectionPushPreview _growPushPreview = new();
    private bool _slowGrowEngaged;
    private bool _slowGrewX;
    private bool _slowGrewY;
    private float _dampedDragSpeed;

    /// <summary>
    /// Splice-inserting into a stack pushes the ops below/right of the insertion — when such
    /// displaced members cross their section's bottom or right border, the section grows to
    /// keep them and neighboring frames get pushed out of the claimed area, all folded into
    /// the drag's MacroCommand. Dragged items themselves are ignored, so deliberately
    /// dragging an op out of a frame never grows it.
    /// </summary>
    /// <param name="context">Graph context providing the current composition, layout, selection, and interaction state.</param>
    private void GrowSectionsToFitDisplacedMembers(GraphUiContext context)
    {
        Debug.Assert(context.MacroCommand != null);
        var symbolUi = context.CompositionInstance.GetSymbolUi();
        if (symbolUi.Sections.Count == 0)
            return;

        _draggedChildIds.Clear();
        foreach (var item in DraggedItems)
        {
            if (item.ChildUi != null)
                _draggedChildIds.Add(item.ChildUi.Id);
        }

        foreach (var section in symbolUi.Sections.Values)
        {
            if (section.Collapsed || section.IsHiddenInCollapsedSection)
                continue;

            GrowSectionToFitContent(context, symbolUi, section, _draggedChildIds);
        }
    }

    /// <summary>
    /// Grows a frame (bottom/right) so its member ops stay inside, pushing siblings and
    /// growing ancestors via the bounds-expansion cascade. Folds into the interaction's
    /// MacroCommand. Ops in <paramref name="excludedChildIds"/> are ignored, so a
    /// deliberate drag out of the frame never grows it.
    /// </summary>
    /// <param name="context">Graph context providing the current composition, layout, selection, and interaction state.</param>
    /// <param name="symbolUi">Composition UI containing the section and its children.</param>
    /// <param name="section">Section whose bounds must enclose its content.</param>
    /// <param name="excludedChildIds">Child IDs omitted from the bounds calculation, or null to include all children.</param>
    private static void GrowSectionToFitContent(GraphUiContext context, SymbolUi symbolUi, Section section, HashSet<Guid>? excludedChildIds)
    {
        Debug.Assert(context.MacroCommand != null);

        var contentMax = new Vector2(float.NegativeInfinity, float.NegativeInfinity);
        var hasContent = false;
        foreach (var childUi in symbolUi.ChildUis.Values)
        {
            if (childUi.SectionId != section.Id)
                continue;

            if (excludedChildIds != null && excludedChildIds.Contains(childUi.Id))
                continue;

            contentMax = Vector2.Max(contentMax, childUi.PosOnCanvas + childUi.Size);
            hasContent = true;
        }

        if (!hasContent)
            return;

        var boundsMax = section.PosOnCanvas + section.Size;
        var requiredMax = Vector2.Max(boundsMax, contentMax + SectionTree.ContentPadding);
        var grewX = requiredMax.X > boundsMax.X + 0.5f;
        var grewY = requiredMax.Y > boundsMax.Y + 0.5f;
        if (!grewX && !grewY)
            return;

        var resizeCommand = new ModifyCanvasElementsCommand(symbolUi, [section], context.Selector);
        section.Size = requiredMax - section.PosOnCanvas;
        resizeCommand.StoreCurrentValues();
        context.MacroCommand!.AddExecutedCommandForUndo(resizeCommand);

        var newBounds = ImRect.RectWithSize(section.PosOnCanvas, section.Size);
        if (grewY)
            SectionTree.ResolveBoundsExpansion(symbolUi, section, newBounds, Vector2.UnitY, context.MacroCommand, context.Selector);
        if (grewX)
            SectionTree.ResolveBoundsExpansion(symbolUi, section, newBounds, Vector2.UnitX, context.MacroCommand, context.Selector);
    }

    private static readonly HashSet<Guid> _draggedChildIds = [];

    /// <summary>
    /// Reset to avoid accidental dragging of previous elements 
    /// </summary>
    internal void StopDragOperation()
    {
        _lastAppliedOffset = Vector2.Zero;
        SpliceSets.Clear();
        DraggedItems.Clear();

        // If the drag ended without CompleteDragOperation, take the uncommitted
        // slow-grow preview back
        if (_slowGrowCommand != null)
        {
            _slowGrowCommand.Undo();
            _slowGrowCommand = null;
        }

        _growPushPreview.CancelAndReset();
        _slowGrowSectionId = Guid.Empty;
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

    /// <summary>Selects the active item while respecting the current multi-selection modifiers.</summary>
    /// <param name="context">Graph context providing the current composition, layout, selection, and interaction state.</param>
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

    /// <summary>Moves dragged items and tests automatic insertion and snapping only for eligible blocks.</summary>
    /// <param name="context">Graph context providing the current composition, layout, selection, and interaction state.</param>
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

        TrySlowGrowSection(context);

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

    /// <summary>Completes the move, disconnects the selection, and starts selective anchor cleanup with the existing undo grouping.</summary>
    /// <param name="context">Graph context providing the current composition, layout, selection, and interaction state.</param>
    /// <returns>True when a detected shake disconnects the dragged selection.</returns>
    private bool HandleShakeDisconnect(GraphUiContext context)
    {
        //Log.Debug("Shake it!");
        Debug.Assert(context.MacroCommand != null);
        if (_connectionsToDraggedItems.Count == 0)
            return false;

        var draggedSelectables = DraggedItems.Select(i => i.Selectable).ToList();
        var reroutesToRemove = new HashSet<MagGraphItem>();
        foreach (var connection in _connectionsToDraggedItems)
        {
            if (connection.SourceItem.IsReroute && DraggedItems.Contains(connection.SourceItem))
                reroutesToRemove.Add(connection.SourceItem);
            if (connection.TargetItem.IsReroute && DraggedItems.Contains(connection.TargetItem))
                reroutesToRemove.Add(connection.TargetItem);
        }

        foreach (var connection in _layout.MagConnections)
        {
            if (!DraggedItems.Contains(connection.SourceItem) || !DraggedItems.Contains(connection.TargetItem))
                continue;

            reroutesToRemove.Remove(connection.SourceItem);
            reroutesToRemove.Remove(connection.TargetItem);
        }

        if (reroutesToRemove.Count > 0)
        {
            // Cleanup removes dragged anchors; consume the held mouse button before they leave the layout.
            CompleteDragOperation(context);
            context.StateMachine.SetState(GraphStates.WaitForMouseRelease, context);
        }

        NodeActions.DisconnectNodes(context.CompositionInstance, draggedSelectables);

        // foreach (var c in _borderConnections)
        // {
        //     context.MacroCommand.AddAndExecCommand(new DeleteConnectionCommand(context.CompositionOp.Symbol, c.AsSymbolConnection(), 0));
        // }

        _layout.FlagStructureAsChanged();
        return true;
    }

    /// <summary>Draws the mouse-centered progress indicator for a pending long press.</summary>
    /// <param name="longTapProgress">Normalized progress toward completing the long-press gesture.</param>
    internal static void UpdateLongPressIndicator(float longTapProgress)
    {
        var dl = ImGui.GetWindowDrawList();
        dl.AddCircle(ImGui.GetMousePos(), 100 * (1 - longTapProgress), Color.White.Fade(MathF.Pow(longTapProgress, 3)));
    }

    /// <summary>
    /// Update dragged items and use anchor definitions to identify and use potential snap targets
    /// </summary>
    /// <param name="context">Graph context providing the current composition, layout, selection, and interaction state.</param>
    /// <returns>True when the drag changes its snapped placement.</returns>
    private bool HandleSnappedDragging(GraphUiContext context)
    {
        var dl = ImGui.GetWindowDrawList();

        if (!_hasDragged)
        {
            _dragStartPosInOpOnCanvas = context.View.InverseTransformPositionFloat(ImGui.GetMousePos());
            _hasDragged = true;

            // Remember the frame the drag started in for the slow-grow interaction
            _slowGrowSectionId = Guid.Empty;
            _slowGrowCommand = null;
            _growPushPreview.Reset();
            _slowGrowEngaged = false;
            _slowGrewX = false;
            _slowGrewY = false;
            _dampedDragSpeed = UserSettings.Config.SectionSlowResizeSpeed * 3; // start above unlock so a fresh grab doesn't engage instantly

            // Rearranging a selection that includes frames must never auto-grow or push -
            // that's deliberate layout work, not ops pressing against a border
            var selectionIncludesSection = false;
            foreach (var node in _nodeSelection.Selection)
            {
                if (node is not Section)
                    continue;

                selectionIncludesSection = true;
                break;
            }

            if (!selectionIncludesSection)
            {
                foreach (var item in DraggedItems)
                {
                    if (item.ChildUi == null || item.ChildUi.SectionId == Guid.Empty)
                        continue;

                    _slowGrowSectionId = item.ChildUi.SectionId;
                    break;
                }
            }
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

        // Only drag selected sections along when the drag started from the selection itself,
        // not when dragging an unselected snapped group.
        if (_draggedItemsFromSelection && DraggedItems.Count != _context.Selector.Selection.Count)
        {
            foreach (var a in _context.Selector.Selection)
            {
                if (a is not Section)
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
                // Only eligible items can join operator snap stacks.
                if (!otherItem.SupportsBlockLayout)
                    continue;

                _snapping.TestItemsForInsertion(otherItem, insertionAnchorItem, ip, _view);
            }
        }

        foreach (var otherItem in overlappingItems)
        {
            foreach (var draggedItem in DraggedItems)
            {
                if (!otherItem.SupportsBlockLayout || !draggedItem.SupportsBlockLayout)
                    continue;

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

    /// <summary>Adjusts dragged node positions to nearby horizontal alignment guides.</summary>
    /// <param name="context">Graph context providing the current composition, layout, selection, and interaction state.</param>
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

    /// <summary>Captures movement state and splice candidates at the start of a drag.</summary>
    /// <param name="context">Graph context providing the current composition, layout, selection, and interaction state.</param>
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
    /// <param name="context">Graph context providing the current composition, layout, selection, and interaction state.</param>
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

    /// <summary>Closes an eligible vertical stack gap left by the dragged block.</summary>
    /// <param name="context">Graph context providing the current composition, layout, selection, and interaction state.</param>
    /// <param name="unsnappedConnections">Connections crossing between the dragged items and the stationary graph.</param>
    /// <returns>True when a collapsible vertical connection pair is found and processed.</returns>
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
        var bridgeConnection = new Symbol.Connection(pair.Ca.SourceParentOrChildId,
                                                     pair.Ca.SourceOutput.Id,
                                                     pair.Cb.TargetParentOrChildId,
                                                     pair.Cb.TargetInput.Id);
        if (Structure.CheckForCycle(context.CompositionInstance.Symbol, bridgeConnection))
        {
            Log.Debug("Sorry, this connection would create a cycle. (1)");
            return false;
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
                                                                        bridgeConnection,
                                                                        pair.Cb.MultiInputIndex));
        return true;
    }

    /// <summary>Closes an eligible horizontal stack gap left by the dragged block.</summary>
    /// <param name="context">Graph context providing the current composition, layout, selection, and interaction state.</param>
    /// <param name="unsnappedConnections">Connections crossing between the dragged items and the stationary graph.</param>
    /// <returns>True when a collapsible horizontal connection pair is found and processed.</returns>
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

        var bridgeConnection = new Symbol.Connection(pair.Ca.SourceParentOrChildId,
                                                     pair.Ca.SourceOutput.Id,
                                                     pair.Cb.TargetParentOrChildId,
                                                     pair.Cb.TargetInput.Id);
        if (Structure.CheckForCycle(context.CompositionInstance.Symbol, bridgeConnection))
        {
            Log.Debug("Sorry, this connection would create a cycle. (1)");
            return false;
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
                                                                        bridgeConnection,
                                                                        pair.Cb.MultiInputIndex));
        return true;
    }

    /// <summary>Finds vertical connection pairs linked through the dragged block.</summary>
    /// <param name="unsnappedConnections">Connections exposed by unsnapping the dragged block.</param>
    /// <returns>Connection pairs whose stationary sides can be joined after removing the block.</returns>
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

    /// <summary>Collects vertical connection pairs that can close a stack gap.</summary>
    /// <param name="unsnappedConnections">Connections exposed by unsnapping the dragged block.</param>
    /// <returns>Eligible incoming and outgoing connection pairs.</returns>
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

    /// <summary>Collects horizontal connection pairs that can close a stack gap.</summary>
    /// <param name="unsnappedConnections">Connections exposed by unsnapping the dragged block.</param>
    /// <returns>Eligible incoming and outgoing connection pairs.</returns>
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

    /// <summary>Moves downstream stacks to close rows released by disconnected inputs.</summary>
    /// <param name="context">Graph context providing the current composition, layout, selection, and interaction state.</param>
    /// <param name="unsnappedConnections">Connections exposed by unsnapping the dragged block.</param>
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

    /// <summary>Checks whether disconnecting a wire would remove an optional or repeated input row.</summary>
    /// <param name="connection">Connection whose removal may eliminate its target input row.</param>
    /// <returns>True when the disconnected occurrence would collapse a visible input row.</returns>
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
    /// <param name="ca">Connection entering the removed block from the stationary source.</param>
    /// <param name="cb">Connection leaving the removed block for the stationary target.</param>
    /// <param name="movableItems">Items allowed to move while closing the vertical gap.</param>
    /// <param name="dryRun">True to collect affected items without changing their positions.</param>
    /// <returns>Items affected by the proposed or applied vertical collapse.</returns>
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
    /// <param name="ca">Connection entering the removed block from the stationary source.</param>
    /// <param name="cb">Connection leaving the removed block for the stationary target.</param>
    /// <param name="movableItems">Items allowed to move while closing the horizontal gap.</param>
    /// <param name="dryRun">True to collect affected items without changing their positions.</param>
    /// <returns>Items affected by the proposed or applied horizontal collapse.</returns>
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
    /// <param name="context">Graph context providing the current composition, layout, selection, and interaction state.</param>
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

    /// <summary>Collects compatible automatic connections between two block-layout participants.</summary>
    /// <param name="result">Destination list to which compatible connection candidates are appended.</param>
    /// <param name="a">Source item at the newly snapped boundary.</param>
    /// <param name="b">Target item at the newly snapped boundary.</param>
    private static void GetPotentialConnectionsAfterSnap(ref List<PotentialConnection> result, MagGraphItem a, MagGraphItem b)
    {
        // Automatic connections require both items to participate in block layout.
        if (!a.SupportsBlockLayout || !b.SupportsBlockLayout)
            return;

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

    /// <summary>Collects connections crossing the boundary of the dragged block.</summary>
    /// <param name="draggedItems">Items in the current dragged block.</param>
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

    /// <summary>Checks whether exactly one connection endpoint belongs to the dragged block.</summary>
    /// <param name="c">Connection to test for crossing the drag boundary.</param>
    /// <param name="draggedItems">Items in the current dragged block.</param>
    /// <returns>True when one endpoint is dragged and the other is stationary.</returns>
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
    /// <param name="draggedItems">Dragged items whose exposed slots may splice an existing connection.</param>
    /// <param name="mousePosInCanvas">Mouse position at drag start in canvas coordinates.</param>
    private void InitSpliceLinks(HashSet<MagGraphItem> draggedItems, Vector2 mousePosInCanvas)
    {
        SpliceSets.Clear();

        // Splicing requires the entire selection to participate in block layout.
        foreach (var item in draggedItems)
        {
            if (!item.SupportsBlockLayout)
                return;
        }

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
    /// <param name="context">Graph context providing the current composition, layout, selection, and interaction state.</param>
    /// <returns>True when the current splice candidate is accepted and its graph edits are applied.</returns>
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

        var connectionIntoSplice = new Symbol.Connection(connection.SourceParentOrChildId,
                                                         connection.SourceOutput.Id,
                                                         spliceLink.InputItemId,
                                                         spliceLink.InputId);

        var connectionOutOfSplice = new Symbol.Connection(spliceLink.OutputItemId,
                                                          spliceLink.OutputId,
                                                          connection.TargetParentOrChildId,
                                                          connection.TargetInput.Id);

        if (Structure.CheckForCycle(context.CompositionInstance.Symbol, connectionIntoSplice))
        {
            Log.Debug("Sorry, this connection would create a cycle. (2)");
            return false;
        }

        if (Structure.CheckForCycle(context.CompositionInstance.Symbol, connectionOutOfSplice))
        {
            Log.Debug("Sorry, this connection would create a cycle. (3)");
            return false;
        }

        context.MacroCommand.AddAndExecCommand(new DeleteConnectionCommand(context.CompositionInstance.Symbol,
                                                                           connection.AsSymbolConnection(),
                                                                           connection.MultiInputIndex));
        context.MacroCommand.AddAndExecCommand(new AddConnectionCommand(context.CompositionInstance.Symbol,
                                                                        connectionIntoSplice, 0));

        context.MacroCommand.AddAndExecCommand(new AddConnectionCommand(context.CompositionInstance.Symbol,
                                                                        connectionOutOfSplice, connection.MultiInputIndex));

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
    /// <param name="context">Graph context providing the current composition, layout, selection, and interaction state.</param>
    /// <param name="snappedItems">Snapped block containing the nodes eligible for movement.</param>
    /// <param name="yThreshold">Canvas-space Y threshold used to select the movable side of the block.</param>
    /// <param name="yDistance">Signed vertical displacement in canvas units.</param>
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

        MoveItems(context, movableItems, new Vector2(0, yDistance));
    }

    /// <summary>
    /// Creates and applies a command to move items vertically
    /// </summary>
    /// <returns>
    /// True if some items where moved
    /// </returns>
    /// <param name="context">Graph context providing the current composition, layout, selection, and interaction state.</param>
    /// <param name="snappedItems">Snapped block containing the nodes eligible for movement.</param>
    /// <param name="xThreshold">Canvas-space X threshold used to select the movable side of the block.</param>
    /// <param name="xDistance">Signed horizontal displacement in canvas units.</param>
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

    /// <summary>Moves the supplied items through the active undoable graph edit.</summary>
    /// <param name="context">Graph context providing the current composition, layout, selection, and interaction state.</param>
    /// <param name="movableItems">Items whose positions are updated by the movement command.</param>
    /// <param name="offset">Signed displacement in canvas coordinates.</param>
    private static void MoveItems(GraphUiContext context, IEnumerable<MagGraphItem> movableItems, Vector2 offset)
    {
        Debug.Assert(context.MacroCommand != null);
        
        // Move items left...
        var affectedItemsAsNodes = movableItems.Select(i => i as ISelectableCanvasObject).ToList();
        var newMoveComment = new ModifyCanvasElementsCommand(context.CompositionInstance.Symbol.Id, affectedItemsAsNodes, context.Selector);
        context.MacroCommand.AddExecutedCommandForUndo(newMoveComment);

        Log.Debug($"MoveItems: {affectedItemsAsNodes.Count} items by {offset}");
        foreach (var item in affectedItemsAsNodes)
        {
            item.PosOnCanvas += offset;
        }

        newMoveComment.StoreCurrentValues();

        // Programmatic pushes (splice inserts, added input rows, placeholder insertion) can
        // shove ops across their section's bottom/right border - grow those frames to keep them
        _growSectionIds.Clear();
        var symbolUi = context.CompositionInstance.GetSymbolUi();
        foreach (var item in movableItems)
        {
            if (item.ChildUi == null || item.ChildUi.SectionId == Guid.Empty)
                continue;

            if (!_growSectionIds.Add(item.ChildUi.SectionId))
                continue;

            if (!symbolUi.Sections.TryGetValue(item.ChildUi.SectionId, out var section)
                || section.Collapsed || section.IsHiddenInCollapsedSection)
                continue;

            GrowSectionToFitContent(context, symbolUi, section, excludedChildIds: null);
        }
    }

    private static readonly HashSet<Guid> _growSectionIds = [];

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
    /// <returns>Primary output item of a supported dragged block, or null when no unambiguous eligible item exists.</returns>
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
    /// <param name="rootItem">Item at which traversal of snapped connections begins.</param>
    /// <param name="set">Optional destination set to extend; a new set is created when null.</param>
    /// <param name="includeRoot">Whether the starting item is included in the result.</param>
    /// <param name="ignoreConnectionHash">Connection hash to exclude from traversal, or zero for no exclusion.</param>
    /// <returns>The supplied or newly created set of items reachable through the allowed snapped connections.</returns>
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

    /// <summary>Collects the union of the snapped blocks containing the supplied roots.</summary>
    /// <param name="rootItems">Starting items whose snapped neighborhoods are combined.</param>
    /// <returns>Distinct items in the reachable snapped blocks.</returns>
    public static HashSet<MagGraphItem> CollectSnappedItems(IEnumerable<MagGraphItem> rootItems)
    {
        var set = new HashSet<MagGraphItem>();

        foreach (var i in rootItems)
        {
            CollectSnappedItems(i, set);
        }

        return set;
    }

    /// <summary>Sets the dragged items by resolving their IDs in the current layout.</summary>
    /// <param name="selectedIds">Child IDs to resolve into the current layout's dragged-item set.</param>
    internal void SetDraggedItemIds(List<Guid> selectedIds)
    {
        DraggedItems.Clear();
        _draggedItemsFromSelection = false;
        foreach (var id in selectedIds)
        {
            if (_layout.Items.TryGetValue(id, out var i))
            {
                DraggedItems.Add(i);
            }
        }
    }

    /// <summary>Sets the dragged items from the supplied canvas selection.</summary>
    /// <param name="selection">Selected canvas objects to resolve into graph items.</param>
    internal void SetDraggedItems(List<ISelectableCanvasObject> selection)
    {
        DraggedItems.Clear();
        _draggedItemsFromSelection = true;
        foreach (var s in selection)
        {
            if (_layout.Items.TryGetValue(s.Id, out var i))
            {
                DraggedItems.Add(i);
            }
        }
    }

    /// <summary>Sets the dragged items to the snapped block containing the given item.</summary>
    /// <param name="item">Root item whose complete snapped block is to be dragged.</param>
    internal void SetDraggedItemIdsToSnappedForItem(MagGraphItem item)
    {
        DraggedItems.Clear();
        _draggedItemsFromSelection = false;
        CollectSnappedItems(item, DraggedItems);
    }

    /// <summary>Checks whether an item belongs to the active drag.</summary>
    /// <param name="item">Graph item to look up in the active drag set.</param>
    /// <returns>True when the item is in the dragged-item set.</returns>
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
    private bool _draggedItemsFromSelection;
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
