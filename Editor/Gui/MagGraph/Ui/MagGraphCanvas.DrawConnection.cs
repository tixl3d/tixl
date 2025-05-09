﻿using ImGuiNET;
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

internal sealed partial class MagGraphCanvas
{
    private void DrawConnection(MagGraphConnection connection, ImDrawListPtr drawList, GraphUiContext context)
    {
        if (connection.Style == MagGraphConnection.ConnectionStyles.Unknown)
            return;

        var type = connection.Type;

        // if (!TypeUiRegistry.TryGetPropertiesForType(type, out var typeUiProperties))
        //     return;

        var isSelected = context.Selector.IsSelected(connection.SourceItem) ||
                         context.Selector.IsSelected(connection.TargetItem);
        
        var typeUiProperties = TypeUiRegistry.GetPropertiesForType(type);

        var anchorSize = 4 * CanvasScale;
        var idleFadeProgress = MathUtils.RemapAndClamp(connection.SourceOutput.DirtyFlag.FramesSinceLastUpdate, 0, 100, 1, 0f);
        
        var color =  typeUiProperties.Color;
        var wasHoveredLastFrame = ConnectionHovering.IsHovered(connection); 
        var selectedColor = isSelected || wasHoveredLastFrame ?  ColorVariations.OperatorLabel.Apply(color)
                                : ColorVariations.ConnectionLines.Apply(color);
        
        var typeColor = ColorVariations.ConnectionLines.Apply(selectedColor).Fade(MathUtils.Lerp(0.6f, 1, idleFadeProgress));
        
        var sourcePosOnScreen = TransformPosition(connection.SourcePos);
        var targetPosOnScreen = TransformPosition(connection.TargetPos);

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
                                                 && !_context.ItemMovement.DraggedItems.Contains( connection.SourceItem)
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
                                               sourcePosOnScreen + new Vector2(-anchorHeight/2, -anchorWidth ) * CanvasScale * 2,
                                               sourcePosOnScreen + new Vector2(anchorHeight/2,0 ) * CanvasScale * 2,
                                               sourcePosOnScreen + new Vector2(-anchorHeight/2, anchorWidth) * CanvasScale * 2,
                                               typeColor);
                    break;
                }
                
                    
                case MagGraphConnection.ConnectionStyles.MainOutToMainInSnappedVertical:
                {
                    var isPotentialSplitTarget = _context.ItemMovement.SpliceSets.Count > 0
                                                 && !_context.ItemMovement.DraggedItems.Contains( connection.SourceItem)
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
                                               sourcePosOnScreen + new Vector2(-anchorWidth, -anchorHeight/2) * CanvasScale * 2,
                                               sourcePosOnScreen + new Vector2(anchorWidth, -anchorHeight/2) * CanvasScale * 2,
                                               sourcePosOnScreen + new Vector2(0, anchorHeight/2) * CanvasScale * 2,
                                               typeColor);
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

            switch (connection.Style)
            {
                case MagGraphConnection.ConnectionStyles.BottomToTop:
                    drawList.AddBezierCubic(sourcePosOnScreen,
                                            sourcePosOnScreen + new Vector2(0, d),
                                            targetPosOnScreen - new Vector2(0, d),
                                            targetPosOnScreen,
                                            typeColor.Fade(0.6f),
                                            2);
                    
                    drawList.AddTriangleFilled(
                                               targetPosOnScreen + new Vector2(-1, -1 + 1) * CanvasScale * 3,
                                               targetPosOnScreen + new Vector2(1, -1 + 1) * CanvasScale * 3,
                                               targetPosOnScreen + new Vector2(0, 1 + 1) * CanvasScale * 3,
                                               typeColor);                    
                    break;
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
                    
                    
                    // TODO: Implement this also for vertical connections.
                    if (GraphConnectionDrawer.DrawConnection(CanvasScale,
                                                             TransformRect(connection.SourceItem.Area),
                                                             sourcePosOnScreen,
                                                             TransformRect(connection.TargetItem.VerticalStackArea),
                                                             targetPosOnScreen,
                                                             typeColor,
                                                             MathUtils.Lerp(0.25f, 1f, idleFadeProgress) + (isSelected|wasHoveredLastFrame ? 2 : 0),
                                                             out var hoverPositionOnLine,
                                                             out var normalizedHoverPos))
                    {
                        if(context.StateMachine.CurrentState == GraphStates.Default)
                            ConnectionHovering.RegisterHoverPoint(connection, typeColor, hoverPositionOnLine, normalizedHoverPos, sourcePosOnScreen);                        
                    }
                    
                    // Draw triangle
                    //drawList.AddCircleFilled(targetPosOnScreen + new Vector2(3 * CanvasScale,0) , anchorSize * 1.2f, typeColor, 3);
                    drawList.AddTriangleFilled(
                                               targetPosOnScreen + new Vector2(0, -anchorWidth ) * CanvasScale * 1,
                                               targetPosOnScreen + new Vector2(anchorHeight,0 ) * CanvasScale * 1,
                                               targetPosOnScreen + new Vector2(0, anchorWidth) * CanvasScale * 1,
                                               typeColor);                    
                    break;
                
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