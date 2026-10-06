using ImGuiNET;
using T3.Core.Settings;
using T3.Core.Utils;
using T3.Editor.Gui.MagGraph.Interaction;
using T3.Editor.Gui.MagGraph.Model;
using T3.Editor.Gui.MagGraph.States;
using T3.Editor.Gui.Styling;
using T3.Editor.Gui.UiHelpers;
using T3.Editor.UiModel;
using T3.Editor.UiModel.InputsAndTypes;
using Color = T3.Core.DataTypes.Vector.Color;
using Vector2 = System.Numerics.Vector2;

namespace T3.Editor.Gui.MagGraph.Ui;

internal sealed partial class MagGraphView
{
    private void DrawConnection(MagGraphConnection connection, ImDrawListPtr drawList, GraphUiContext context)
    {
        if (connection.Style == MagGraphConnection.ConnectionStyles.Unknown)
            return;

        if (connection.SourceItem.IsCollapsedAway && connection.TargetItem.IsCollapsedAway)
            return;

        var type = connection.Type;

        // if (!TypeUiRegistry.TryGetPropertiesForType(type, out var typeUiProperties))
        //     return;

        var isSelected = context.Selector.IsSelected(connection.SourceItem) ||
                         context.Selector.IsSelected(connection.TargetItem);

        var typeUiProperties = TypeUiRegistry.GetPropertiesForType(type);

        var anchorSize = 4 * CanvasScale;
        var idleFadeProgress = MathUtils.RemapAndClamp(connection.SourceOutput.DirtyFlag.FramesSinceLastUpdate, 0, 100, 1, 0f);

        var color = typeUiProperties.Color.Fade(_context.GraphOpacity);

        var wasHoveredLastFrame = !_context.PreventInteraction && ConnectionHovering.IsHovered(connection);
        var selectedColor = isSelected || wasHoveredLastFrame
                                ? ColorVariations.OperatorLabel.Apply(color)
                                : ColorVariations.ConnectionLines.Apply(color);

        var typeColor = ColorVariations.ConnectionLines.Apply(selectedColor).Fade(MathUtils.Lerp(0.6f, 1, idleFadeProgress));
        var connectionTypeColor = ColorVariations.OperatorLabel.Apply(selectedColor).Fade(MathUtils.Lerp(0.6f, 1, idleFadeProgress));

        // Alt over a cable asks for a place to put a bend point; the marker is added on top of the
        // normal route rather than replacing it, so the cable never disappears if the marker doesn't draw.
        var isCandidateForThisCable = context.ConnectionHovering.IsShowingCandidateFor(connection);

        var thickness = MathUtils.Lerp(0.25f, 2f, idleFadeProgress) + (isSelected | wasHoveredLastFrame ? 2 : 0);

        // A manually rerouted connection is drawn by DrawReroutedConnection after the nodes, so its
        // detour stays visible instead of disappearing behind them.
        if (HasConnectionWaypoints(connection))
            return;

        Vector2 sourceOnCanvas;
        if (connection.SourceItem.IsCollapsedAway)
        {
            if (!context.Layout.Sections.TryGetValue(connection.SourceItem.ChildUi!.HiddenInCollapsedSectionId, out var section))
                return;

            sourceOnCanvas = section.PosOnCanvas + new Vector2(section.Size.X - 5, MagGraphItem.LineHeight/2);
        }
        else
        {
            sourceOnCanvas = connection.DampedSourcePos;
        }

        var sourcePosOnScreen = TransformPosition(sourceOnCanvas);

        Vector2 targetOnCanvas;
        if (connection.TargetItem.IsCollapsedAway)
        {
            if (!context.Layout.Sections.TryGetValue(connection.TargetItem.ChildUi!.HiddenInCollapsedSectionId, out var section))
                return;

            targetOnCanvas = section.PosOnCanvas + new Vector2(2, MagGraphItem.LineHeight/2);
        }
        else
        {
            targetOnCanvas = connection.DampedTargetPos;
        }

        var targetPosOnScreen = TransformPosition(targetOnCanvas);

        //var targetPosOnScreen = TransformPosition(connection.DampedTargetPos);

        var anchorWidth = 1.5f * 2;
        var anchorHeight = 2f * 2;

        if (connection.IsSnapped)
        {
            switch (connection.Style)
            {
                case MagGraphConnection.ConnectionStyles.MainOutToMainInSnappedHorizontal:
                case MagGraphConnection.ConnectionStyles.MainOutToInputSnappedHorizontal:
                {
                    var isPotentialSplitTarget = _context.ItemMovement.SpliceSets.Count > 0
                                                 && !_context.ItemMovement.DraggedItems.Contains(connection.SourceItem)
                                                 && _context.ItemMovement.SpliceSets
                                                            .Any(sp
                                                                     => sp.Direction == MagGraphItem.Directions.Horizontal
                                                                        && sp.Type == type);
                    if (isPotentialSplitTarget)
                    {
                        var extend = new Vector2(0, MagGraphItem.GridSize.Y * CanvasScale * 0.25f);

                        drawList.AddRectFilled(
                                               sourcePosOnScreen - extend + new Vector2(-2, 0),
                                               sourcePosOnScreen + extend + new Vector2(0, 0),
                                               typeColor.Fade(Blink)
                                              );
                    }

                    //drawList.AddCircleFilled(sourcePosOnScreen, anchorSize * 1.6f, typeColor, 3);
                    drawList.AddTriangleFilled(
                                               sourcePosOnScreen + new Vector2(-anchorHeight / 2, -anchorWidth) * CanvasScale * 2,
                                               sourcePosOnScreen + new Vector2(anchorHeight / 2, 0) * CanvasScale * 2,
                                               sourcePosOnScreen + new Vector2(-anchorHeight / 2, anchorWidth) * CanvasScale * 2,
                                               connectionTypeColor);
                    break;
                }

                case MagGraphConnection.ConnectionStyles.MainOutToMainInSnappedVertical:
                {
                    var isPotentialSplitTarget = _context.ItemMovement.SpliceSets.Count > 0
                                                 && !_context.ItemMovement.DraggedItems.Contains(connection.SourceItem)
                                                 && _context.ItemMovement.SpliceSets
                                                            .Any(x
                                                                     => x.Direction == MagGraphItem.Directions.Vertical
                                                                        && x.Type == type);
                    if (isPotentialSplitTarget)
                    {
                        var extend = new Vector2(MagGraphItem.GridSize.X * CanvasScale * 0.06f, 0);

                        drawList.AddRectFilled(
                                               sourcePosOnScreen - extend + new Vector2(0, -2),
                                               sourcePosOnScreen + extend + new Vector2(0, 0),
                                               typeColor.Fade(Blink)
                                              );
                    }

                    drawList.AddTriangleFilled(
                                               sourcePosOnScreen + new Vector2(-anchorWidth, -anchorHeight / 2) * CanvasScale * 2,
                                               sourcePosOnScreen + new Vector2(anchorWidth, -anchorHeight / 2) * CanvasScale * 2,
                                               sourcePosOnScreen + new Vector2(0, anchorHeight / 2) * CanvasScale * 2,
                                               connectionTypeColor);
                    break;
                }

                case MagGraphConnection.ConnectionStyles.AdditionalOutToMainInputSnappedVertical:
                    drawList.AddCircleFilled(sourcePosOnScreen, anchorSize * 1.6f, Color.Red, 3);
                    break;
            }
        }
        else
        {
            var d = Vector2.Distance(sourcePosOnScreen, targetPosOnScreen) / 2;

            if (InputSnapper.BestInputMatch.Item == connection.TargetItem
                && InputSnapper.BestInputMatch.SlotId == connection.TargetInput.Id
                && InputSnapper.BestInputMatch.MultiInputIndex == connection.MultiInputIndex
                && (InputSnapper.BestInputMatch.InputSnapType == InputSnapper.InputSnapTypes.Normal ||
                    InputSnapper.BestInputMatch.InputSnapType == InputSnapper.InputSnapTypes.ReplaceMultiInput))
            {
                typeColor = UiColors.StatusAttention.Fade(0.3f + 0.1f * Blink);
            }

            switch (connection.Style)
            {
                case MagGraphConnection.ConnectionStyles.BottomToTop:
                {
                    if (VerticalConnectionDrawer.DrawConnection(CanvasScale,
                                                                TransformRect(connection.SourceItem.Area),
                                                                sourcePosOnScreen,
                                                                TransformRect(connection.TargetItem.VerticalStackArea),
                                                                targetPosOnScreen,
                                                                typeColor,
                                                                thickness,
                                                                out var hoverPositionOnLine,
                                                                out var normalizedHoverPos))
                    {
                        if (context.StateMachine.CurrentState == GraphStates.Default)
                            ConnectionHovering.RegisterHoverPoint(connection, typeColor, hoverPositionOnLine, normalizedHoverPos, sourcePosOnScreen);
                    }

                    drawList.AddTriangleFilled(
                                               targetPosOnScreen + new Vector2(-1, -1 + 1) * CanvasScale * 3,
                                               targetPosOnScreen + new Vector2(1, -1 + 1) * CanvasScale * 3,
                                               targetPosOnScreen + new Vector2(0, 1 + 1) * CanvasScale * 3,
                                               typeColor);
                    break;
                }
                case MagGraphConnection.ConnectionStyles.BottomToLeft:
                    drawList.AddBezierCubic(sourcePosOnScreen,
                                            sourcePosOnScreen + new Vector2(0, d),
                                            targetPosOnScreen - new Vector2(d, 0),
                                            targetPosOnScreen,
                                            typeColor.Fade(0.6f),
                                            2);

                    break;
                case MagGraphConnection.ConnectionStyles.RightToTop:
                    drawList.AddBezierCubic(sourcePosOnScreen,
                                            sourcePosOnScreen + new Vector2(d, 0),
                                            targetPosOnScreen - new Vector2(0, d),
                                            targetPosOnScreen,
                                            typeColor.Fade(0.6f),
                                            2);

                    drawList.AddTriangleFilled(
                                               sourcePosOnScreen + new Vector2(-1, -1) * CanvasScale * 5,
                                               sourcePosOnScreen + new Vector2(1, -1) * CanvasScale * 5,
                                               sourcePosOnScreen + new Vector2(0, 1) * CanvasScale * 5,
                                               typeColor);
                    break;

                case MagGraphConnection.ConnectionStyles.RightToLeft:

                {
                    if (GraphConnectionDrawer.DrawConnection(CanvasScale,
                                                             TransformRect(connection.SourceItem.Area),
                                                             sourcePosOnScreen,
                                                             TransformRect(connection.TargetItem.VerticalStackArea),
                                                             targetPosOnScreen,
                                                             typeColor,
                                                             thickness,
                                                             out var hoverPositionOnLine,
                                                             out var normalizedHoverPos))
                    {
                        if (context.StateMachine.CurrentState == GraphStates.Default)
                            ConnectionHovering.RegisterHoverPoint(connection, typeColor, hoverPositionOnLine, normalizedHoverPos, sourcePosOnScreen);
                    }

                    // Draw triangle
                    drawList.AddTriangleFilled(
                                               targetPosOnScreen + new Vector2(0, -anchorWidth) * CanvasScale * 1,
                                               targetPosOnScreen + new Vector2(anchorHeight, 0) * CanvasScale * 1,
                                               targetPosOnScreen + new Vector2(0, anchorWidth) * CanvasScale * 1,
                                               typeColor);
                    break;
                }
                case MagGraphConnection.ConnectionStyles.Unknown:
                    break;
                case MagGraphConnection.ConnectionStyles.MainOutToMainInSnappedHorizontal:
                    break;
                case MagGraphConnection.ConnectionStyles.MainOutToMainInSnappedVertical:
                    break;
                case MagGraphConnection.ConnectionStyles.MainOutToInputSnappedHorizontal:
                    break;
                case MagGraphConnection.ConnectionStyles.AdditionalOutToMainInputSnappedVertical:
                    break;
            }

            if (isCandidateForThisCable)
            {
                DrawCandidateMarker(drawList, context, typeColor);
            }
        }
    }

    /// <summary>
    /// Draws the dot marking where a new bend point would land. The position comes from the hover point
    /// registered along the cable, which is where the click would actually insert.
    /// </summary>
    private static void DrawCandidateMarker(ImDrawListPtr drawList, GraphUiContext context, Color color)
    {
        var position = context.ConnectionHovering.ClosestCandidatePositionOnScreen;
        if (position == Vector2.Zero)
            return;

        var radius = 4 * T3Ui.UiScaleFactor;
        drawList.AddCircleFilled(position, radius, color, 12);
        drawList.AddCircle(position, radius + 2 * T3Ui.UiScaleFactor, UiColors.StatusAttention, 16, 1.5f * T3Ui.UiScaleFactor);
    }

    /// <summary>
    /// Draws connections that carry manual bend points, after the nodes, so their detour stays on top
    /// of the nodes it routes around. Lines without bend points keep their usual style behind the nodes.
    /// </summary>
    private void DrawReroutedConnection(MagGraphConnection connection, ImDrawListPtr drawList, GraphUiContext context)
    {
        if (!HasConnectionWaypoints(connection))
            return;

        var canvasPoints = CanvasPointsFor(connection, context, useDampedPositions: false);

        // A bend point saved onto a collapsed (snapped) route can land on its own endpoint, leaving a
        // zero-length path. Drawing nothing at all would make the connection look deleted, so fall back
        // to the automatic route - which also restores the snapped anchor.
        if (canvasPoints.Count < 2 || Vector2.DistanceSquared(canvasPoints[0], canvasPoints[^1]) < 0.0001f)
        {
            DrawConnection(connection, drawList, context);
            return;
        }

        // The bend points define where the cable has to turn, not a set of free diagonals between them:
        // the route is squared off into horizontal and vertical runs that are joined by arcs sized and
        // tessellated with the same connection settings the automatic routes use, so a rerouted cable
        // curves like the cables around it.
        ConnectionRouteBuilder.Build(canvasPoints,
                                     UserSettings.Config.MaxCurveRadius,
                                     CanvasScale,
                                     UserSettings.Config.MaxSegmentCount,
                                     _canvasRoutePoints);

        // Drawing happens in screen space, so the route has to be transformed first.
        _pathPointsOnScreen.Clear();
        foreach (var canvasPoint in _canvasRoutePoints)
        {
            _pathPointsOnScreen.Add(TransformPosition(canvasPoint));
        }

        if (_pathPointsOnScreen.Count < 2)
        {
            DrawConnection(connection, drawList, context);
            return;
        }

        var isSelected = context.Selector.IsSelected(connection.SourceItem) ||
                         context.Selector.IsSelected(connection.TargetItem);

        var idleFadeProgress = MathUtils.RemapAndClamp(connection.SourceOutput.DirtyFlag.FramesSinceLastUpdate, 0, 100, 1, 0f);
        var color = TypeUiRegistry.GetPropertiesForType(connection.Type).Color.Fade(_context.GraphOpacity);

        var wasHoveredLastFrame = !_context.PreventInteraction && ConnectionHovering.IsHovered(connection);
        var selectedColor = isSelected || wasHoveredLastFrame
                                ? ColorVariations.OperatorLabel.Apply(color)
                                : ColorVariations.ConnectionLines.Apply(color);

        var typeColor = ColorVariations.ConnectionLines.Apply(selectedColor).Fade(MathUtils.Lerp(0.6f, 1, idleFadeProgress));

        // The idle cable fades in alpha, but must not thin to a hairline: a rerouted route is a manual
        // edit the user still needs to see and grab, so it keeps a legible weight when idle.
        var thickness = MathUtils.Lerp(1.5f, 2f, idleFadeProgress) + (isSelected | wasHoveredLastFrame ? 2 : 0);

        var sourcePosOnScreen = _pathPointsOnScreen[0];
        var targetPosOnScreen = _pathPointsOnScreen[^1];

        var hoveredHandleIndex = ConnectionBendPoints.FindBendPointAt(this, connection, ImGui.GetMousePos());

        if (GraphConnectionDrawer.DrawConnection(CanvasScale,
                                                 _pathPointsOnScreen,
                                                 sourcePosOnScreen,
                                                 targetPosOnScreen,
                                                 typeColor,
                                                 thickness,
                                                 out var hoverPositionOnLine,
                                                 out var normalizedHoverPos))
        {
            if (context.StateMachine.CurrentState == GraphStates.Default)
                ConnectionHovering.RegisterHoverPoint(connection, typeColor, hoverPositionOnLine, normalizedHoverPos, sourcePosOnScreen);
        }

        // Drawn after the cable so the handles stay grabbable where they sit on top of it.
        ConnectionBendPoints.DrawHandles(this,
                                         drawList,
                                         connection,
                                         typeColor,
                                         hoveredHandleIndex,
                                         context.ConnectionHovering.DraggedBendPointIndex(connection));

        DrawCandidateBendPoint(drawList, connection, context, typeColor);

        // Arrow head, matching the other orthogonal connection styles.
        drawList.AddTriangleFilled(
                                   targetPosOnScreen + new Vector2(0, -1.5f) * CanvasScale * 1,
                                   targetPosOnScreen + new Vector2(2f, 0) * CanvasScale * 1,
                                   targetPosOnScreen + new Vector2(0, 1.5f) * CanvasScale * 1,
                                   typeColor);
    }

    private bool HasConnectionWaypoints(MagGraphConnection connection)
    {
        return connection.GetWaypoints() is { Count: > 0 };
    }

    /// <summary>
    /// Marks where a new bend point would land while <c>Alt</c> is held over a cable, so the gesture has
    /// a visible target instead of the point simply appearing on click.
    /// </summary>
    /// <remarks>
    /// The marker position comes from the hover point registered along the cable, so it sits exactly
    /// where a click would insert. A cable that still follows its automatic route is hidden while the
    /// candidate is active, so the marker reads as the preview of where the bend point will go.
    /// </remarks>
    private void DrawCandidateBendPoint(ImDrawListPtr drawList,
                                        MagGraphConnection connection,
                                        GraphUiContext context,
                                        Color color)
    {
        if (!context.ConnectionHovering.IsShowingCandidateFor(connection))
            return;

        if (HasConnectionWaypoints(connection))
        {
            DrawCandidateMarker(drawList, context, color);
            return;
        }

        if (UserSettings.Config.HoverMode == UserSettings.GraphHoverModes.Disabled)
            return;

        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(5, 5));
        ImGui.BeginTooltip();
        ImGui.PushFont(Fonts.FontSmall);
        ImGui.TextUnformatted("Click to add a bend point and reroute this cable");
        ImGui.PopFont();
        ImGui.EndTooltip();
        ImGui.PopStyleVar();
    }

    /// <summary>
    /// Resolves the polyline a connection follows: source, then its saved bend points, then target.
    /// Returns an empty list when an endpoint can't be located, so callers skip the connection.
    /// </summary>
    /// <param name="useDampedPositions">
    /// True for the normal line, which eases into place as nodes move. False for rerouted lines, which
    /// must pass exactly through the bend points rather than lag behind them.
    /// </param>
    private List<Vector2> CanvasPointsFor(MagGraphConnection connection, GraphUiContext context, bool useDampedPositions)
    {
        _canvasPathPoints.Clear();

        if (connection.SourceItem.IsCollapsedAway)
        {
            if (!context.Layout.Sections.TryGetValue(connection.SourceItem.ChildUi!.HiddenInCollapsedSectionId, out var sourceSection))
                return _canvasPathPoints;

            _canvasPathPoints.Add(sourceSection.PosOnCanvas + new Vector2(sourceSection.Size.X - 5, MagGraphItem.LineHeight / 2));
        }
        else
        {
            _canvasPathPoints.Add(useDampedPositions ? connection.DampedSourcePos : connection.SourcePos);
        }

        if (connection.TargetItem.IsCollapsedAway)
        {
            if (!context.Layout.Sections.TryGetValue(connection.TargetItem.ChildUi!.HiddenInCollapsedSectionId, out var targetSection))
            {
                _canvasPathPoints.Clear();
                return _canvasPathPoints;
            }

            _canvasPathPoints.Add(targetSection.PosOnCanvas + new Vector2(2, MagGraphItem.LineHeight / 2));
            return _canvasPathPoints;
        }

        // Bend points that landed on top of their neighbour would only add cusps, so they are dropped.
        connection.AppendWaypoints(_canvasPathPoints, out _);

        // The target is always drawn, even when a bend point sits right on it; only bend points are optional.
        _canvasPathPoints.Add(useDampedPositions ? connection.DampedTargetPos : connection.TargetPos);

        return _canvasPathPoints;
    }

    private readonly List<Vector2> _canvasPathPoints = new(16);

    /// <summary>The squared-off route of the connection currently being drawn, in canvas space.</summary>
    private readonly List<Vector2> _canvasRoutePoints = new(64);

    private readonly List<Vector2> _pathPointsOnScreen = new(64);
}