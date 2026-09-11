using ImGuiNET;
using T3.Core.Utils;
using T3.Editor.Gui.MagGraph.Interaction;
using T3.Editor.Gui.MagGraph.Model;
using T3.Editor.Gui.MagGraph.States;
using T3.Editor.Gui.Styling;
using T3.Editor.Gui.UiHelpers;
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

        var sourceInMerge = context.ItemMovement.TryGetRerouteMergePreview(connection.SourceItem, out var mergeSource, out var sourceRadius);
        var targetInMerge = context.ItemMovement.TryGetRerouteMergePreview(connection.TargetItem, out var mergeTarget, out var targetRadius);
        if (sourceInMerge && targetInMerge)
            return;

        if (connection.SourceItem.IsCollapsedAway && connection.TargetItem.IsCollapsedAway)
            return;

        var stroke = context.ConnectionStroke;
        var queryPath = stroke.IsActive && !connection.IsTemporary ? stroke.ObservePath : null;
        stroke.SetConnection(queryPath != null ? connection : null);

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

        if (sourceInMerge)
            sourceOnCanvas = mergeSource + new Vector2(sourceRadius, 0);

        var sourcePosOnScreen = TransformPosition(sourceOnCanvas);
        if (connection.SourceItem.IsReroute && !connection.SourceItem.IsCollapsedAway
                                           && connection.Style == MagGraphConnection.ConnectionStyles.RightToLeft)
        {
            var radius = TransformDirection(new Vector2(sourceInMerge ? sourceRadius : connection.SourceItem.RerouteRadius)).X;
            var overlap = MathF.Min(0.5f * T3Ui.UiScaleFactor, radius);
            var center = sourceInMerge ? mergeSource : connection.SourceItem.DampedPosOnCanvas + connection.SourceItem.Size / 2;
            // Cancel the path drawer's half-pixel shift and overlap the dot's outline.
            sourcePosOnScreen = TransformPosition(center) + new Vector2(radius - overlap - 0.5f, -0.5f);
        }

        Vector2 targetOnCanvas;
        if (connection.TargetItem.IsCollapsedAway)
        {
            if (!context.Layout.Sections.TryGetValue(connection.TargetItem.ChildUi!.HiddenInCollapsedSectionId, out var section))
                return;

            targetOnCanvas = section.PosOnCanvas + new Vector2(2, MagGraphItem.LineHeight/2);
        }
        else if (connection.TargetItem.IsReroute)
        {
            MagGraphItem.InputAnchorPoint anchor = default;
            connection.TargetItem.GetInputAnchorAtIndex(0, ref anchor);
            targetOnCanvas = anchor.PositionOnCanvas;
        }
        else
        {
            targetOnCanvas = connection.DampedTargetPos;
        }

        if (targetInMerge)
        {
            // Preserve the incoming arrowhead's offset from the edge of the dot.
            var socketOffset = connection.TargetItem.Size.X / 2 - connection.TargetItem.RerouteRadius;
            targetOnCanvas = mergeTarget - new Vector2(targetRadius + socketOffset, 0);
        }

        var targetPosOnScreen = TransformPosition(targetOnCanvas);

        //var targetPosOnScreen = TransformPosition(connection.DampedTargetPos);

        var anchorWidth = 1.5f * 2;
        var anchorHeight = 2f * 2;

        if (connection.IsSnapped)
        {
            if (queryPath != null)
            {
                stroke.TestMarker(drawList, sourcePosOnScreen, 7 * CanvasScale);
                if (stroke.Contains(connection))
                {
                    var highlight = stroke.IsCut ? UiColors.StatusAttention : UiColors.StatusAutomated;
                    drawList.AddCircle(sourcePosOnScreen, 9 * CanvasScale, highlight, 12, 2 * T3Ui.UiScaleFactor);
                }
            }

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
                                                                MathUtils.Lerp(0.25f, 2f, idleFadeProgress) + (isSelected | wasHoveredLastFrame ? 2 : 0),
                                                                out var hoverPositionOnLine,
                                                                out var normalizedHoverPos, queryPath))
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
                    drawList.PathClear();
                    drawList.PathLineTo(sourcePosOnScreen);
                    drawList.PathBezierCubicCurveTo(sourcePosOnScreen + new Vector2(0, d),
                                                    targetPosOnScreen - new Vector2(d, 0), targetPosOnScreen);
                    queryPath?.Invoke(drawList);
                    drawList.PathStroke(typeColor.Fade(0.6f), ImDrawFlags.None, 2);

                    break;
                case MagGraphConnection.ConnectionStyles.RightToTop:
                    drawList.PathClear();
                    drawList.PathLineTo(sourcePosOnScreen);
                    drawList.PathBezierCubicCurveTo(sourcePosOnScreen + new Vector2(d, 0),
                                                    targetPosOnScreen - new Vector2(0, d), targetPosOnScreen);
                    queryPath?.Invoke(drawList);
                    drawList.PathStroke(typeColor.Fade(0.6f), ImDrawFlags.None, 2);

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
                                                             MathUtils.Lerp(0.25f, 2f, idleFadeProgress) + (isSelected | wasHoveredLastFrame ? 2 : 0),
                                                             out var hoverPositionOnLine,
                                                             out var normalizedHoverPos, queryPath))
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
        }
    }
}
