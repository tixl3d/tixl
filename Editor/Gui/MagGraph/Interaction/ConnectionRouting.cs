#nullable enable
using ImGuiNET;
using T3.Editor.Gui.Interaction;
using T3.Editor.Gui.Interaction.Snapping;
using T3.Editor.Gui.MagGraph.Model;
using T3.Editor.Gui.MagGraph.States;
using T3.Editor.Gui.Styling;
using T3.Editor.Gui.UiHelpers;
using T3.Editor.UiModel;
using T3.Editor.UiModel.Commands;
using T3.Editor.UiModel.Commands.Graph;

namespace T3.Editor.Gui.MagGraph.Interaction;

/// <summary>
/// Adds, selects, drags, merges and removes the route points of connection lines. Hovering is collected while
/// the connections are drawn and evaluated after drawing.
/// </summary>
/// <remarks>
/// Route points are selectables of the composition, so fence selection, node drags, section drags and delete
/// handle them like operators. This class only covers the gestures that start on a point or a line.
/// </remarks>
internal sealed class ConnectionRouting
{
    internal enum ClickResults
    {
        Ignored,
        StartedDrag,
    }

    /** Heights a dragged route point snaps to, collected when the drag starts. */
    private sealed class SnapHeights : IValueSnapAttractor
    {
        internal readonly List<float> Values = [];

        void IValueSnapAttractor.CheckForSnap(ref SnapResult result)
        {
            foreach (var value in Values)
            {
                result.TryToImproveWithAnchorValue(value);
            }
        }
    }

    internal ConnectionRouting()
    {
        _snapAttractors = [_snapHeights];
    }

    internal static float GrabRadius => 7 * T3Ui.UiScaleFactor;

    /**
     * Alt, although Alt+drag also pans the canvas: a click on a cable starts the route drag before a pan could,
     * and the view doesn't pan while a point is dragged.
     */
    private static bool IsRoutingModifierDown => ImGui.GetIO().KeyAlt;

    /** Clicks on a route point, or Alt+clicks on a cable, belong to routing and not to the cable or the canvas. */
    internal bool CapturesClick => _hoveredPointId != Guid.Empty || (IsRoutingModifierDown && _hoveredCable != null);

    /** True while Alt+click on the hovered cable would add a route point. */
    internal bool WouldAddPoint => IsRoutingModifierDown && _hoveredCable != null && _hoveredPointId == Guid.Empty;

    internal bool IsDraggingPoint => _dragCommand != null;

    internal bool IsPointHighlighted(Guid pointId) => pointId == _hoveredPointId || pointId == _draggedPointId;

    internal Guid MergeTargetPointId => _mergeTargetPointId;

    /** Points drawn in the last frame. Routes of deleted connections are kept for undo but never drawn. */
    internal HashSet<Guid> VisiblePointIds => _visiblePointIds;

    /** Called before the connections are drawn, which register what is hovered. */
    internal void PrepareFrame()
    {
        _hoveredPointId = Guid.Empty;
        _closestHandleDistance = float.MaxValue;
        _hoveredCable = null;
        _hoveredCables.Clear();
        _visiblePointIds.Clear();
    }

    internal void RegisterHandle(Guid pointId, Vector2 positionOnScreen)
    {
        _visiblePointIds.Add(pointId);
        var distance = Vector2.Distance(positionOnScreen, ImGui.GetMousePos());
        if (distance > GrabRadius || distance >= _closestHandleDistance)
            return;

        _closestHandleDistance = distance;
        _hoveredPointId = pointId;
    }

    /// <param name="insertIndex">Where a point added at <paramref name="positionOnScreen"/> belongs in the route.</param>
    internal void RegisterHoveredCable(MagGraphConnection connection, Vector2 positionOnScreen, int insertIndex)
    {
        _hoveredCables.Add((connection, positionOnScreen, insertIndex));

        var mousePosition = ImGui.GetMousePos();
        if (_hoveredCable != null
            && Vector2.Distance(positionOnScreen, mousePosition) >= Vector2.Distance(_hoveredCablePositionOnScreen, mousePosition))
            return;

        _hoveredCable = connection;
        _hoveredCablePositionOnScreen = positionOnScreen;
    }

    internal ClickResults HandleClick(GraphUiContext context)
    {
        var symbolUi = context.CompositionInstance.Symbol.GetSymbolUi();
        var routes = symbolUi.ConnectionRoutes;
        var isModifierDown = IsRoutingModifierDown;
        var mouseOnCanvas = context.View.InverseTransformPositionFloat(ImGui.GetMousePos());

        if (_hoveredPointId != Guid.Empty && routes.Points.TryGetValue(_hoveredPointId, out var point))
        {
            if (!isModifierDown && !context.Selector.IsNodeSelected(point))
            {
                if (ImGui.GetIO().KeyShift)
                {
                    context.Selector.AddSelection(point);
                }
                else
                {
                    context.Selector.SetSelection(point);
                }
            }

            StartDrag(new ModifyConnectionRoutesCommand(symbolUi.Symbol.Id, routes, "Move route point"),
                      context, routes, point, point.PosOnCanvas - mouseOnCanvas);

            // Decided on release, so an Alt+drag that happens to start on a point moves it instead of deleting it.
            _removeIfReleasedWithoutMoving = isModifierDown;
            return ClickResults.StartedDrag;
        }

        if (!isModifierDown || _hoveredCable == null || _hoveredCable.IsSnapped)
            return ClickResults.Ignored;

        var command = new ModifyConnectionRoutesCommand(symbolUi.Symbol.Id, routes, "Add route point");
        var positionOnCanvas = context.View.InverseTransformPositionFloat(_hoveredCablePositionOnScreen);
        var pointId = routes.InsertSharedPoint(CollectOverlappingCables(), positionOnCanvas);
        var newPoint = routes.Points[pointId];
        context.Selector.SetSelection(newPoint);
        StartDrag(command, context, routes, newPoint, positionOnCanvas - mouseOnCanvas);
        _hasChanged = true;
        return ClickResults.StartedDrag;
    }

    internal void UpdateDrag(GraphUiContext context)
    {
        if (_dragCommand == null)
            return;

        var routes = context.CompositionInstance.Symbol.GetSymbolUi().ConnectionRoutes;
        if (!routes.Points.TryGetValue(_draggedPointId, out var draggedPoint))
        {
            CancelDrag(context);
            return;
        }

        if (_removeIfReleasedWithoutMoving)
        {
            if (!HasMovedSincePress)
                return;

            _removeIfReleasedWithoutMoving = false;
        }

        if (_shakeDetector.TestDragForShake(ImGui.GetMousePos()))
        {
            routes.RemovePoint(_draggedPointId);
            context.Selector.DeselectNode(draggedPoint);
            _dragCommand.Name = "Remove route point";
            _hasChanged = true;
            CommitDrag();
            return;
        }

        // Merging only makes sense for a single point: a group keeps its shape.
        _mergeTargetPointId = _pointsMovingWithDrag.Count == 1 ? FindMergeTarget(context, routes) : Guid.Empty;
        var newPosition = _mergeTargetPointId != Guid.Empty
                              ? routes.Points[_mergeTargetPointId].PosOnCanvas
                              : context.View.InverseTransformPositionFloat(ImGui.GetMousePos()) + _grabOffset;

        // Ctrl bypasses snapping.
        if (_mergeTargetPointId == Guid.Empty
            && !ImGui.GetIO().KeyCtrl
            && _snapHeights.Values.Count > 0
            && _snapHandlerY.TryCheckForSnapping(newPosition.Y, out var snappedY, context.View.Scale.Y, null, _snapAttractors))
        {
            newPosition.Y = (float)snappedY;
        }

        var offset = newPosition - _draggedPointStartPosition;
        foreach (var (point, startPosition) in _pointsMovingWithDrag)
        {
            point.PosOnCanvas = startPosition + offset;
        }

        _hasChanged |= offset != Vector2.Zero;
    }

    internal void CompleteDrag(GraphUiContext context)
    {
        if (_dragCommand == null)
            return;

        var routes = context.CompositionInstance.Symbol.GetSymbolUi().ConnectionRoutes;
        if (_removeIfReleasedWithoutMoving && !HasMovedSincePress)
        {
            if (routes.Points.TryGetValue(_draggedPointId, out var point))
                context.Selector.DeselectNode(point);

            routes.RemovePoint(_draggedPointId);
            _dragCommand.Name = "Remove route point";
            _hasChanged = true;
        }
        else if (_mergeTargetPointId != Guid.Empty && routes.CanMerge(_draggedPointId, _mergeTargetPointId))
        {
            if (routes.Points.TryGetValue(_draggedPointId, out var point))
                context.Selector.DeselectNode(point);

            routes.MergePoint(_draggedPointId, _mergeTargetPointId);
            _dragCommand.Name = "Merge route points";
            _hasChanged = true;
        }

        CommitDrag();
    }

    internal void CancelDrag(GraphUiContext context)
    {
        if (_dragCommand == null)
            return;

        if (_hasChanged)
            _dragCommand.Undo();

        ResetDrag();
    }

    internal void DrawSnapIndicator(GraphUiContext context)
    {
        _snapHandlerY.DrawSnapIndicator(context.View, UiColors.StatusActivated.Fade(0.3f));
    }

    /// <summary>A selected point drags all selected points along; an unselected one moves alone.</summary>
    private void StartDrag(ModifyConnectionRoutesCommand command, GraphUiContext context, ConnectionRoutes routes,
                           ConnectionRoutes.RoutePoint point, Vector2 grabOffset)
    {
        _dragCommand = command;
        _draggedPointId = point.Id;
        _draggedPointStartPosition = point.PosOnCanvas;
        _grabOffset = grabOffset;
        _mergeTargetPointId = Guid.Empty;
        _hasChanged = false;
        _removeIfReleasedWithoutMoving = false;
        _shakeDetector.ResetShaking();

        _pointsMovingWithDrag.Clear();
        _pointsMovingWithDrag.Add((point, point.PosOnCanvas));
        if (context.Selector.IsNodeSelected(point))
        {
            foreach (var selectable in context.Selector.Selection)
            {
                if (selectable is ConnectionRoutes.RoutePoint selectedPoint
                    && selectedPoint != point
                    && routes.Points.ContainsKey(selectedPoint.Id))
                {
                    _pointsMovingWithDrag.Add((selectedPoint, selectedPoint.PosOnCanvas));
                }
            }
        }

        CollectSnapHeights(context, routes, point.Id);
    }

    /// <summary>
    /// The output and input of every line through the dragged point, and its other points that stay in place, so
    /// straight horizontal runs are easy to make.
    /// </summary>
    private void CollectSnapHeights(GraphUiContext context, ConnectionRoutes routes, Guid draggedPointId)
    {
        _snapHeights.Values.Clear();
        foreach (var connection in context.Layout.MagConnections)
        {
            if (connection.IsSnapped
                || connection.IsTemporary
                || !routes.TryGetRoute(connection.RouteKey, out var pointIds)
                || !pointIds.Contains(draggedPointId))
                continue;

            _snapHeights.Values.Add(connection.SourcePos.Y);
            _snapHeights.Values.Add(connection.TargetPos.Y);

            foreach (var pointId in pointIds)
            {
                if (routes.Points.TryGetValue(pointId, out var otherPoint) && !IsMovingWithDrag(otherPoint))
                    _snapHeights.Values.Add(otherPoint.PosOnCanvas.Y);
            }
        }
    }

    private bool IsMovingWithDrag(ConnectionRoutes.RoutePoint point)
    {
        foreach (var (movingPoint, _) in _pointsMovingWithDrag)
        {
            if (movingPoint == point)
                return true;
        }

        return false;
    }

    private void CommitDrag()
    {
        if (_hasChanged && _dragCommand != null)
        {
            _dragCommand.StoreCurrentValues();
            UndoRedoStack.Add(_dragCommand);
        }

        ResetDrag();
    }

    private static bool HasMovedSincePress => ImGui.GetIO().MouseDragMaxDistanceAbs[0].Length() >= UserSettings.Config.ClickThreshold;

    private void ResetDrag()
    {
        _dragCommand = null;
        _draggedPointId = Guid.Empty;
        _mergeTargetPointId = Guid.Empty;
        _hasChanged = false;
        _removeIfReleasedWithoutMoving = false;
        _pointsMovingWithDrag.Clear();
    }

    /// <summary>
    /// Lines from the same output that run on top of the clicked one at the click position. They all get the new
    /// point, so clicking a trunk keeps it a trunk.
    /// </summary>
    private List<(ConnectionRoutes.Key Key, int Index)> CollectOverlappingCables()
    {
        var clickedKey = _hoveredCable!.RouteKey;
        var maxDistance = OverlapDistance * T3Ui.UiScaleFactor;
        var insertions = new List<(ConnectionRoutes.Key Key, int Index)>();
        foreach (var (connection, positionOnScreen, insertIndex) in _hoveredCables)
        {
            var key = connection.RouteKey;
            if (connection.IsSnapped
                || !key.HasSameSource(clickedKey)
                || Vector2.Distance(positionOnScreen, _hoveredCablePositionOnScreen) > maxDistance)
                continue;

            var isDuplicate = false;
            foreach (var (existingKey, _) in insertions)
            {
                isDuplicate |= existingKey == key;
            }

            if (!isDuplicate)
                insertions.Add((key, insertIndex));
        }

        return insertions;
    }

    /** How close the hover positions of two lines must be, in pixels, to count as overlapping. */
    private const float OverlapDistance = 3;

    private Guid FindMergeTarget(GraphUiContext context, ConnectionRoutes routes)
    {
        var mousePosition = ImGui.GetMousePos();
        var closestDistance = GrabRadius;
        var closestPointId = Guid.Empty;

        foreach (var pointId in _visiblePointIds)
        {
            if (pointId == _draggedPointId || !routes.Points.TryGetValue(pointId, out var point))
                continue;

            var distance = Vector2.Distance(context.View.TransformPosition(point.PosOnCanvas), mousePosition);
            if (distance >= closestDistance || !routes.CanMerge(_draggedPointId, pointId))
                continue;

            closestDistance = distance;
            closestPointId = pointId;
        }

        return closestPointId;
    }

    private readonly HashSet<Guid> _visiblePointIds = [];
    private Guid _hoveredPointId;
    private float _closestHandleDistance;
    private MagGraphConnection? _hoveredCable;
    private Vector2 _hoveredCablePositionOnScreen;
    private readonly List<(MagGraphConnection Connection, Vector2 PositionOnScreen, int InsertIndex)> _hoveredCables = [];

    private ModifyConnectionRoutesCommand? _dragCommand;
    private Guid _draggedPointId;
    private Vector2 _draggedPointStartPosition;
    private Guid _mergeTargetPointId;
    private Vector2 _grabOffset;
    private bool _hasChanged;
    private bool _removeIfReleasedWithoutMoving;
    private readonly List<(ConnectionRoutes.RoutePoint Point, Vector2 StartPosition)> _pointsMovingWithDrag = [];
    private readonly ShakeDetector _shakeDetector = new();

    private readonly SnapHeights _snapHeights = new();
    private readonly IValueSnapAttractor[] _snapAttractors;
    private readonly ValueSnapHandler _snapHandlerY = new(SnapResult.Orientations.Vertical);
}
