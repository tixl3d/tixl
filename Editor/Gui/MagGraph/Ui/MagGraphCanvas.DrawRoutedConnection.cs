using ImGuiNET;
using T3.Editor.Gui.MagGraph.Interaction;
using T3.Editor.Gui.MagGraph.Model;
using T3.Editor.Gui.MagGraph.States;
using T3.Editor.Gui.Styling;
using T3.Editor.Gui.UiHelpers;
using T3.Editor.UiModel;
using Color = T3.Core.DataTypes.Vector.Color;
using Vector2 = System.Numerics.Vector2;

namespace T3.Editor.Gui.MagGraph.Ui;

internal sealed partial class MagGraphView
{
    /// <summary>
    /// Draws a connection through its route points in the style of unrouted lines: horizontal runs joined by arcs.
    /// Lines pass every point horizontally, so cables sharing a point merge smoothly into one trunk.
    /// </summary>
    private void DrawRoutedConnection(MagGraphConnection connection, List<Guid> pointIds, ImDrawListPtr drawList, GraphUiContext context,
                                      Vector2 sourceOnScreen, Vector2 targetOnScreen, Color color, float thickness)
    {
        var style = connection.Style;
        var sourceDirection = style is MagGraphConnection.ConnectionStyles.BottomToTop or MagGraphConnection.ConnectionStyles.BottomToLeft
                                  ? Vector2.UnitY
                                  : Vector2.UnitX;
        var targetDirection = style is MagGraphConnection.ConnectionStyles.BottomToTop or MagGraphConnection.ConnectionStyles.RightToTop
                                  ? Vector2.UnitY
                                  : Vector2.UnitX;
        var pointDirection = style == MagGraphConnection.ConnectionStyles.BottomToTop ? Vector2.UnitY : Vector2.UnitX;

        _routeSegments.Clear();
        drawList.PathClear();
        drawList.PathLineTo(sourceOnScreen);

        var previous = sourceOnScreen;
        var previousDirection = sourceDirection;
        var previousArea = TransformRect(connection.SourceItem.Area);
        for (var index = 0; index < pointIds.Count; index++)
        {
            if (!_connectionRoutes.Points.TryGetValue(pointIds[index], out var routePoint))
                continue;

            var pointOnScreen = TransformPosition(routePoint.PosOnCanvas);
            AddRouteSegment(drawList, previous, previousDirection, previousArea, pointOnScreen, pointDirection, default);
            _routeSegments.Add((drawList._Path.Size, index));
            previous = pointOnScreen;
            previousDirection = pointDirection;

            // An empty area makes the arc drawer assume a node-sized box around the point.
            previousArea = default;
        }

        AddRouteSegment(drawList, previous, previousDirection, previousArea,
                        targetOnScreen, targetDirection, TransformRect(connection.TargetItem.VerticalStackArea));
        _routeSegments.Add((drawList._Path.Size, pointIds.Count));

        if (TryFindHoverOnPath(drawList, out var hoverPosition, out var normalizedHoverPos, out var insertIndex))
        {
            if (context.StateMachine.CurrentState == GraphStates.Default)
            {
                ConnectionHovering.RegisterHoverPoint(connection, color, hoverPosition, normalizedHoverPos, sourceOnScreen);
                context.ConnectionRouting.RegisterHoveredCable(connection, hoverPosition, insertIndex);
            }
            else if (context.StateMachine.CurrentState == GraphStates.DragItems)
            {
                context.ItemMovement.RegisterWireSplitCandidate(connection, color, hoverPosition);
            }
        }

        if (CanvasScale > 0.5f)
        {
            drawList.AddPolyline(ref drawList._Path[0], drawList._Path.Size,
                                 UiColors.WindowBackground.Fade(0.6f * color.A),
                                 ImDrawFlags.None,
                                 thickness + 5f);
        }

        drawList.PathStroke(color, ImDrawFlags.None, thickness);

        DrawRouteTargetArrow(drawList, targetOnScreen, targetDirection, color);
        DrawRoutePointHandles(pointIds, drawList, context, color);
    }

    /** Arcs like unrouted lines between horizontal ends; a curve where an end leaves or enters vertically. */
    private void AddRouteSegment(ImDrawListPtr drawList, Vector2 from, Vector2 fromDirection, ImRect fromArea,
                                 Vector2 to, Vector2 toDirection, ImRect toArea)
    {
        if (fromDirection == Vector2.UnitX && toDirection == Vector2.UnitX)
        {
            GraphConnectionDrawer.AppendArcPath(drawList, CanvasScale, fromArea, from, toArea, to);
            return;
        }

        var tangentLength = Vector2.Distance(from, to) * 0.4f;
        drawList.PathBezierCubicCurveTo(from + fromDirection * tangentLength,
                                        to - toDirection * tangentLength,
                                        to,
                                        0);
    }

    private void DrawRouteTargetArrow(ImDrawListPtr drawList, Vector2 targetOnScreen, Vector2 targetDirection, Color color)
    {
        const float arrowWidth = 3;
        const float arrowLength = 4;
        var side = new Vector2(targetDirection.Y, targetDirection.X);
        drawList.AddTriangleFilled(targetOnScreen - side * arrowWidth * CanvasScale,
                                   targetOnScreen + targetDirection * arrowLength * CanvasScale,
                                   targetOnScreen + side * arrowWidth * CanvasScale,
                                   color);
    }

    private void DrawRoutePointHandles(List<Guid> pointIds, ImDrawListPtr drawList, GraphUiContext context, Color color)
    {
        if (CanvasScale < MinCanvasScaleForRouteHandles)
            return;

        var routing = context.ConnectionRouting;
        var radius = 3.5f * T3Ui.UiScaleFactor;
        var lineWidth = 1.5f * T3Ui.UiScaleFactor;

        foreach (var pointId in pointIds)
        {
            if (!_connectionRoutes.Points.TryGetValue(pointId, out var routePoint))
                continue;

            var pointOnScreen = TransformPosition(routePoint.PosOnCanvas);
            routing.RegisterHandle(pointId, pointOnScreen);

            if (routing.IsPointHighlighted(pointId))
            {
                drawList.AddCircleFilled(pointOnScreen, radius * 1.3f, color, 16);
            }
            else
            {
                drawList.AddCircleFilled(pointOnScreen, radius, UiColors.WindowBackground, 16);
                drawList.AddCircle(pointOnScreen, radius, color, 16, lineWidth);
            }

            if (context.Selector.IsSelected(routePoint))
                drawList.AddCircle(pointOnScreen, radius * 1.9f, UiColors.ForegroundFull.Fade(_context.GraphOpacity), 16, lineWidth);

            if (pointId == routing.MergeTargetPointId)
                drawList.AddCircle(pointOnScreen, radius * 2.5f, color, 24, lineWidth);
        }
    }

    /// <summary>
    /// The generic hover test assumes a cable stays between its end points, which a routed detour doesn't.
    /// </summary>
    private bool TryFindHoverOnPath(ImDrawListPtr drawList, out Vector2 positionOnPath, out float normalizedPosition, out int insertIndex)
    {
        positionOnPath = Vector2.Zero;
        normalizedPosition = -1;
        insertIndex = 0;

        var pathSize = drawList._Path.Size;
        if (pathSize < 2)
            return false;

        var mousePosition = ImGui.GetMousePos();
        var maxDistance = 6 * T3Ui.UiScaleFactor;
        var closestDistance = maxDistance;
        var closestSegmentEnd = -1;
        var lengthAtClosest = 0f;
        var totalLength = 0f;

        for (var index = 1; index < pathSize; index++)
        {
            var start = drawList._Path[index - 1];
            var end = drawList._Path[index];
            var segment = end - start;
            var segmentLength = segment.Length();

            var t = segmentLength > 0.0001f
                        ? Math.Clamp(Vector2.Dot(mousePosition - start, segment) / (segmentLength * segmentLength), 0, 1)
                        : 0;
            var closestPoint = start + segment * t;
            var distance = Vector2.Distance(closestPoint, mousePosition);
            if (distance < closestDistance)
            {
                closestDistance = distance;
                closestSegmentEnd = index;
                positionOnPath = closestPoint;
                lengthAtClosest = totalLength + segmentLength * t;
            }

            totalLength += segmentLength;
        }

        if (closestSegmentEnd < 0 || totalLength <= 0)
            return false;

        normalizedPosition = lengthAtClosest / totalLength;
        foreach (var (pathEndIndex, routeInsertIndex) in _routeSegments)
        {
            insertIndex = routeInsertIndex;
            if (closestSegmentEnd < pathEndIndex)
                break;
        }

        return true;
    }

    private const float MinCanvasScaleForRouteHandles = 0.3f;

    /** Set once per frame before connections are drawn. */
    private ConnectionRoutes _connectionRoutes = new();

    /** Path index after each curve of the routed cable being drawn, and where a point added on it belongs in the route. */
    private readonly List<(int PathEndIndex, int InsertIndex)> _routeSegments = new(8);
}
