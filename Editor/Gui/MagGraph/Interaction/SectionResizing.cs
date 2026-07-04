using ImGuiNET;
using T3.Editor.Gui.Interaction.Snapping;
using T3.Editor.Gui.MagGraph.Model;
using T3.Editor.Gui.MagGraph.States;
using T3.Editor.Gui.Styling;
using T3.Editor.Gui.UiHelpers;
using T3.Editor.UiModel.Commands;
using T3.Editor.UiModel.Commands.Graph;

namespace T3.Editor.Gui.MagGraph.Interaction;

internal static class SectionResizing
{
    internal static void Draw(GraphUiContext context)
    {
        _snapHandlerY.DrawSnapIndicator(context.View, UiColors.ForegroundFull.Fade(0.1f));
        _snapHandlerX.DrawSnapIndicator(context.View, UiColors.ForegroundFull.Fade(0.1f));

        // Setup...
        var instViewSymbolUi = context?.ProjectView?.InstView?.SymbolUi;
        if (instViewSymbolUi == null)
            return;

        var sectionId = context.ActiveSectionId;

        if (!context.Layout.Sections.TryGetValue(sectionId, out var magSection))
        {
            context.ActiveSectionId = Guid.Empty;
            context.StateMachine.SetState(GraphStates.Default, context);
            return;
        }

        var section = magSection.Section;

        // Start dragging...
        {
            var started = context.ActiveSectionId != _draggedSectionId;
            if (started)
            {
                _draggedSectionId = context.ActiveSectionId;
                _dragStartDelta = ImGui.GetMousePos() - context.View.TransformPosition(magSection.PosOnCanvas + magSection.Size);
                _moveCommand = new ModifyCanvasElementsCommand(instViewSymbolUi, [section], context.Selector);
            }

            if (started)
                return;
        }

        // Update dragging...
        {
            var minSize = MathF.Min(MagGraphItem.GridSize.X, MagGraphItem.GridSize.Y);
            var gridSize = Vector2.One * minSize;
            
            var newDragPos = ImGui.GetMousePos() - _dragStartDelta;
            var newDragPosInCanvas = context.View.InverseTransformPositionFloat(newDragPos);

            if (_snapHandlerX.TryCheckForSnapping(newDragPosInCanvas.X, out var snappedPosX,
                                                  context.View.Scale.X * 0.25f,
                                                      [magSection],
                                                  context.Layout.Sections.Values
                                                 ))
            {
                newDragPosInCanvas.X = (float)snappedPosX;
            }
            else if (_snapHandlerX.TryCheckForSnapping(newDragPosInCanvas.X, out var snappedXValue3,
                                                       context.View.Scale.X * 0.25f,
                                                           [],
                                                           [new RasterSnapAttractor
                                                                {
                                                                    Canvas = context.View,
                                                                    GridSize = gridSize,
                                                                    Direction = RasterSnapAttractor.Directions.Horizontal
                                                                }]))
            {
                newDragPosInCanvas.X = (float)snappedXValue3;
            }

            if (_snapHandlerY.TryCheckForSnapping(newDragPosInCanvas.Y, out var snappedPosY,
                                                  context.View.Scale.Y * 0.25f,
                                                      [magSection],
                                                  context.Layout.Sections.Values
                                                 ))
            {
                newDragPosInCanvas.Y = (float)snappedPosY;
            }
            else if (_snapHandlerY.TryCheckForSnapping(newDragPosInCanvas.Y, out var snappedYValue3,
                                                       context.View.Scale.Y * 0.25f,
                                                           [],
                                                           [new RasterSnapAttractor
                                                                {
                                                                    Canvas = context.View,
                                                                    GridSize = gridSize,
                                                                    Direction = RasterSnapAttractor.Directions.Vertical
                                                                }]))
            {
                newDragPosInCanvas.Y = (float)snappedYValue3;
            }            

            section.Size = newDragPosInCanvas - section.PosOnCanvas;
        }

        // Complete dragging...
        var completed = ImGui.IsMouseReleased(ImGuiMouseButton.Left);
        if (completed)
        {
            var wasDragging = ImGui.GetMouseDragDelta(ImGuiMouseButton.Left).LengthSquared() > UserSettings.Config.ClickThreshold;
            if (wasDragging)
            {
                _moveCommand.StoreCurrentValues();
                UndoRedoStack.Add(_moveCommand);
            }
            else
            {
                _moveCommand.Undo();
                if (context.Selector.IsNodeSelected(section))
                {
                    if (ImGui.GetIO().KeyShift)
                    {
                        context.Selector.DeselectNode(section, null);
                    }
                }
                else
                {
                    if (!ImGui.GetIO().KeyShift)
                        context.Selector.Clear();

                    context.Selector.AddSelection(section);
                }
            }

            context.StateMachine.SetState(GraphStates.Default, context);
            _draggedSectionId = Guid.Empty;
            _moveCommand = null;
        }
    }

    private static Guid _draggedSectionId = Guid.Empty;
    private static Vector2 _dragStartDelta;
    private static ModifyCanvasElementsCommand _moveCommand;

    private static readonly ValueSnapHandler _snapHandlerX = new(SnapResult.Orientations.Horizontal);
    private static readonly ValueSnapHandler _snapHandlerY = new(SnapResult.Orientations.Vertical);
}