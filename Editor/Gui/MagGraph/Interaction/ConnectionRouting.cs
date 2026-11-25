#nullable enable
using ImGuiNET;
using T3.Editor.Gui.MagGraph.Model;
using T3.Editor.Gui.MagGraph.States;
using T3.Editor.Gui.UiHelpers;
using T3.Editor.UiModel;
using T3.Editor.UiModel.Commands;
using T3.Editor.UiModel.Commands.Graph;

namespace T3.Editor.Gui.MagGraph.Interaction;

/// <summary>
/// Adds, drags, merges and removes the route points of connection lines, and moves points along with dragged
/// operators. Hovering is collected while the connections are drawn and evaluated after drawing.
/// </summary>
internal sealed class ConnectionRouting
{
    internal enum ClickResults
    {
        Ignored,
        StartedDrag,
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

    /** Called before the connections are drawn, which register what is hovered. */
    internal void PrepareFrame()
    {
        _hoveredPointId = Guid.Empty;
        _closestHandleDistance = float.MaxValue;
        _hoveredCable = null;
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
        _hoveredCable = connection;
        _hoveredCablePositionOnScreen = positionOnScreen;
        _hoveredCableInsertIndex = insertIndex;
    }

    internal ClickResults HandleClick(GraphUiContext context)
    {
        var symbolUi = context.CompositionInstance.Symbol.GetSymbolUi();
        var routes = symbolUi.ConnectionRoutes;
        var isModifierDown = IsRoutingModifierDown;
        var mouseOnCanvas = context.View.InverseTransformPositionFloat(ImGui.GetMousePos());

        if (_hoveredPointId != Guid.Empty && routes.Points.TryGetValue(_hoveredPointId, out var pointPosition))
        {
            StartDrag(symbolUi, _hoveredPointId, pointPosition - mouseOnCanvas, "Move route point");

            // Decided on release, so an Alt+drag that happens to start on a point moves it instead of deleting it.
            _removeIfReleasedWithoutMoving = isModifierDown;
            return ClickResults.StartedDrag;
        }

        if (!isModifierDown || _hoveredCable == null || _hoveredCable.IsSnapped)
            return ClickResults.Ignored;

        var command = new ModifyConnectionRoutesCommand(symbolUi.Symbol.Id, routes, "Add route point");
        var positionOnCanvas = context.View.InverseTransformPositionFloat(_hoveredCablePositionOnScreen);
        var pointId = routes.InsertPoint(_hoveredCable.RouteKey, _hoveredCableInsertIndex, positionOnCanvas);
        StartDrag(command, pointId, positionOnCanvas - mouseOnCanvas);
        _hasChanged = true;
        return ClickResults.StartedDrag;
    }

    internal void UpdateDrag(GraphUiContext context)
    {
        if (_dragCommand == null)
            return;

        var routes = context.CompositionInstance.Symbol.GetSymbolUi().ConnectionRoutes;
        if (!routes.Points.ContainsKey(_draggedPointId))
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

        _mergeTargetPointId = FindMergeTarget(context, routes);
        var newPosition = _mergeTargetPointId != Guid.Empty
                              ? routes.Points[_mergeTargetPointId]
                              : context.View.InverseTransformPositionFloat(ImGui.GetMousePos()) + _grabOffset;

        if (routes.Points[_draggedPointId] == newPosition)
            return;

        routes.Points[_draggedPointId] = newPosition;
        _hasChanged = true;
    }

    internal void CompleteDrag(GraphUiContext context)
    {
        if (_dragCommand == null)
            return;

        var routes = context.CompositionInstance.Symbol.GetSymbolUi().ConnectionRoutes;
        if (_removeIfReleasedWithoutMoving && !HasMovedSincePress)
        {
            routes.RemovePoint(_draggedPointId);
            _dragCommand.Name = "Remove route point";
            _hasChanged = true;
        }
        else if (_mergeTargetPointId != Guid.Empty && routes.CanMerge(_draggedPointId, _mergeTargetPointId))
        {
            routes.MergePoint(_draggedPointId, _mergeTargetPointId);
            _hasChanged = true;
        }

        if (_hasChanged)
        {
            _dragCommand.StoreCurrentValues();
            UndoRedoStack.Add(_dragCommand);
        }

        ResetDrag();
    }

    internal void CancelDrag(GraphUiContext context)
    {
        if (_dragCommand == null)
            return;

        if (_hasChanged)
            _dragCommand.Undo();

        ResetDrag();
    }

    /// <summary>
    /// Points move with a node drag only if every route using them has both ends among the dragged items.
    /// Otherwise the cable of a single moved node stretches to its fixed points.
    /// </summary>
    internal void StartNodeDrag(GraphUiContext context, HashSet<MagGraphItem> draggedItems, MacroCommand macroCommand)
    {
        ResetNodeDrag();

        var symbolUi = context.CompositionInstance.Symbol.GetSymbolUi();
        var routes = symbolUi.ConnectionRoutes;
        if (routes.IsEmpty || draggedItems.Count == 0)
            return;

        foreach (var item in draggedItems)
        {
            _referenceItem ??= item;
            _draggedItemIds.Add(item.Id);
        }

        foreach (var (key, pointIds) in routes.Routes)
        {
            var bothEndsDragged = _draggedItemIds.Contains(key.SourceItemId) && _draggedItemIds.Contains(key.TargetItemId);
            foreach (var pointId in pointIds)
            {
                if (bothEndsDragged)
                {
                    _pointIdsMovingWithNodes.Add(pointId);
                }
                else
                {
                    _pointIdsStayingInPlace.Add(pointId);
                }
            }
        }

        foreach (var pointId in _pointIdsMovingWithNodes)
        {
            if (!_pointIdsStayingInPlace.Contains(pointId) && routes.Points.TryGetValue(pointId, out var position))
                _pointsFollowingNodes.Add((pointId, position));
        }

        if (_pointsFollowingNodes.Count == 0 || _referenceItem == null)
        {
            ResetNodeDrag();
            return;
        }

        _referenceItemStartPosition = _referenceItem.PosOnCanvas;
        _nodeDragCommand = new ModifyConnectionRoutesCommand(symbolUi.Symbol.Id, routes, "Move route points");
        macroCommand.AddExecutedCommandForUndo(_nodeDragCommand);
    }

    internal void UpdateNodeDrag(GraphUiContext context)
    {
        if (_nodeDragCommand == null || _referenceItem == null)
            return;

        var routes = context.CompositionInstance.Symbol.GetSymbolUi().ConnectionRoutes;
        var offset = _referenceItem.PosOnCanvas - _referenceItemStartPosition;
        foreach (var (pointId, startPosition) in _pointsFollowingNodes)
        {
            if (routes.Points.ContainsKey(pointId))
                routes.Points[pointId] = startPosition + offset;
        }
    }

    internal void CompleteNodeDrag()
    {
        _nodeDragCommand?.StoreCurrentValues();
        ResetNodeDrag();
    }

    internal void ResetNodeDrag()
    {
        _nodeDragCommand = null;
        _referenceItem = null;
        _draggedItemIds.Clear();
        _pointIdsMovingWithNodes.Clear();
        _pointIdsStayingInPlace.Clear();
        _pointsFollowingNodes.Clear();
    }

    private void StartDrag(SymbolUi symbolUi, Guid pointId, Vector2 grabOffset, string commandName)
    {
        StartDrag(new ModifyConnectionRoutesCommand(symbolUi.Symbol.Id, symbolUi.ConnectionRoutes, commandName), pointId, grabOffset);
    }

    private void StartDrag(ModifyConnectionRoutesCommand command, Guid pointId, Vector2 grabOffset)
    {
        _dragCommand = command;
        _draggedPointId = pointId;
        _grabOffset = grabOffset;
        _mergeTargetPointId = Guid.Empty;
        _hasChanged = false;
        _removeIfReleasedWithoutMoving = false;
    }

    private static bool HasMovedSincePress => ImGui.GetIO().MouseDragMaxDistanceAbs[0].Length() >= UserSettings.Config.ClickThreshold;

    private void ResetDrag()
    {
        _dragCommand = null;
        _draggedPointId = Guid.Empty;
        _mergeTargetPointId = Guid.Empty;
        _hasChanged = false;
    }

    /** Only drawn points qualify: routes of deleted connections are kept for undo but stay invisible. */
    private Guid FindMergeTarget(GraphUiContext context, ConnectionRoutes routes)
    {
        var mousePosition = ImGui.GetMousePos();
        var closestDistance = GrabRadius;
        var closestPointId = Guid.Empty;

        foreach (var pointId in _visiblePointIds)
        {
            if (pointId == _draggedPointId || !routes.Points.TryGetValue(pointId, out var position))
                continue;

            var distance = Vector2.Distance(context.View.TransformPosition(position), mousePosition);
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
    private int _hoveredCableInsertIndex;

    private ModifyConnectionRoutesCommand? _dragCommand;
    private Guid _draggedPointId;
    private Guid _mergeTargetPointId;
    private Vector2 _grabOffset;
    private bool _hasChanged;
    private bool _removeIfReleasedWithoutMoving;

    private ModifyConnectionRoutesCommand? _nodeDragCommand;
    private MagGraphItem? _referenceItem;
    private Vector2 _referenceItemStartPosition;
    private readonly HashSet<Guid> _draggedItemIds = [];
    private readonly HashSet<Guid> _pointIdsMovingWithNodes = [];
    private readonly HashSet<Guid> _pointIdsStayingInPlace = [];
    private readonly List<(Guid PointId, Vector2 StartPosition)> _pointsFollowingNodes = [];
}
