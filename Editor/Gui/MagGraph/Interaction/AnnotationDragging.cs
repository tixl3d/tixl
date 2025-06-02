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
using T3.Editor.UiModel.Selection;

namespace T3.Editor.Gui.MagGraph.Interaction;

/// <summary>
/// Handles the dragging of annotations
/// </summary>
internal static class AnnotationDragging
{
    internal static void Draw(GraphUiContext context)
    {
        _snapHandlerY.DrawSnapIndicator(context.Canvas, UiColors.StatusActivated.Fade(0.3f));
        _snapHandlerX.DrawSnapIndicator(context.Canvas, UiColors.StatusActivated.Fade(0.3f));

        // Setup...
        var instViewSymbolUi = context?.ProjectView?.InstView?.SymbolUi;
        if (instViewSymbolUi == null)
            return;

        var annotationId = context.ActiveAnnotationId;

        if (!context.Layout.Annotations.TryGetValue(annotationId, out var magAnnotation))
        {
            context.ActiveAnnotationId = Guid.Empty;
            context.StateMachine.SetState(GraphStates.Default, context);
            return;
        }

        var annotation = magAnnotation.Annotation;

        // Start dragging...
        {
            var started = context.ActiveAnnotationId != _draggedAnnotationId;
            if (started)
            {
                _draggedAnnotationId = context.ActiveAnnotationId;
                _dragStartDelta = ImGui.GetMousePos() - context.Canvas.TransformPosition(magAnnotation.PosOnCanvas);

                _draggedNodes.Clear();
                if (context.Selector.IsNodeSelected(annotation))
                {
                    _draggedNodes.AddRange(context.Selector.GetSelectedNodes<ISelectableCanvasObject>());
                }
                else
                {
                    if (!ImGui.GetIO().KeyCtrl)
                        _draggedNodes.AddRange(FindAnnotatedOps(context.ProjectView.InstView.SymbolUi, annotation));
                    
                    _draggedNodes.Add(annotation);
                }

                _moveCommand = new ModifyCanvasElementsCommand(instViewSymbolUi, _draggedNodes, context.Selector);
            }

            if (started)
                return;
        }

        // Update dragging...
        {
            var minSize = MathF.Min(MagGraphItem.GridSize.X, MagGraphItem.GridSize.Y);
            var gridSize = Vector2.One * minSize;

            _visibleAnnotationsForSnapping.Clear();
            foreach (var snapTo in context.Layout.Annotations.Values)
            {
                if (context.Canvas.IsItemVisible(snapTo))
                    _visibleAnnotationsForSnapping.Add(snapTo);
            }

            var newDragPos = ImGui.GetMousePos() - _dragStartDelta;
            var newDragPosInCanvas = context.Canvas.InverseTransformPositionFloat(newDragPos);
            var moveDeltaOnCanvas = newDragPosInCanvas - magAnnotation.PosOnCanvas;

            // Horizontal snapping
            var snapFactor = 0.5f;
            if (_snapHandlerX.TryCheckForSnapping(newDragPosInCanvas.X, out var snappedXValue,
                                                  context.Canvas.Scale.X * snapFactor,
                                                      [magAnnotation],
                                                  _visibleAnnotationsForSnapping
                                                 ))
            {
                var snapDelta = snappedXValue - newDragPosInCanvas.X;
                moveDeltaOnCanvas.X += (float)snapDelta;
            }
            else if (_snapHandlerX.TryCheckForSnapping(newDragPosInCanvas.X + magAnnotation.Size.X, out var snappedXValue2,
                                                       context.Canvas.Scale.X * snapFactor,
                                                           [magAnnotation],
                                                       _visibleAnnotationsForSnapping
                                                      ))
            {
                var snapDelta = snappedXValue2 - newDragPosInCanvas.X - magAnnotation.Size.X;
                moveDeltaOnCanvas.X += (float)snapDelta;
            }
            else if (_snapHandlerX.TryCheckForSnapping(newDragPosInCanvas.X, out var snappedXValue3,
                                                       context.Canvas.Scale.X ,
                                                           [],
                                                           [new RasterSnapAttractor
                                                                {
                                                                    Canvas = context.Canvas,
                                                                    GridSize = gridSize,
                                                                    Direction = RasterSnapAttractor.Directions.Horizontal
                                                                }]))
            {
                moveDeltaOnCanvas.X += (float)(snappedXValue3 - newDragPosInCanvas.X);
            }

            // Vertical snapping...
            if (_snapHandlerY.TryCheckForSnapping(newDragPosInCanvas.Y, out var snappedYValue,
                                                  context.Canvas.Scale.Y * snapFactor,
                                                      [magAnnotation],
                                                  _visibleAnnotationsForSnapping
                                                 ))
            {
                var snapDelta = snappedYValue - newDragPosInCanvas.Y;
                moveDeltaOnCanvas.Y += (float)snapDelta;
            }
            else if (_snapHandlerY.TryCheckForSnapping(newDragPosInCanvas.Y + magAnnotation.Size.Y, out var snappedYValue2,
                                                       context.Canvas.Scale.Y * snapFactor,
                                                           [magAnnotation],
                                                       _visibleAnnotationsForSnapping
                                                      ))
            {
                moveDeltaOnCanvas.Y += (float)(snappedYValue2 - newDragPosInCanvas.Y - magAnnotation.Size.Y);
            }
            else if (_snapHandlerY.TryCheckForSnapping(newDragPosInCanvas.Y, out var snappedYValue3,
                                                       context.Canvas.Scale.Y ,
                                                           [],
                                                           [new RasterSnapAttractor
                                                                {
                                                                    Canvas = context.Canvas,
                                                                    GridSize = gridSize,
                                                                    Direction = RasterSnapAttractor.Directions.Vertical
                                                                }]))
            {
                moveDeltaOnCanvas.Y += (float)(snappedYValue3 - newDragPosInCanvas.Y);
            }

            foreach (var e in _draggedNodes)
            {
                e.PosOnCanvas += moveDeltaOnCanvas;
            }
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
                if (context.Selector.IsNodeSelected(annotation))
                {
                    if (ImGui.GetIO().KeyShift)
                    {
                        context.Selector.DeselectNode(annotation, null);
                    }
                }
                else
                {
                    if (!ImGui.GetIO().KeyShift)
                        context.Selector.Clear();

                    context.Selector.AddSelection(annotation);
                }
            }

            context.StateMachine.SetState(GraphStates.Default, context);
            _draggedNodes.Clear();
            _draggedAnnotationId = Guid.Empty;
            _moveCommand = null;
        }
    }

    private static readonly List<MagGraphAnnotation> _visibleAnnotationsForSnapping = [];

    private static List<ISelectableCanvasObject> FindAnnotatedOps(SymbolUi parentUi, Annotation annotation)
    {
        var matches = new List<ISelectableCanvasObject>();
        var aRect = new ImRect(annotation.PosOnCanvas, annotation.PosOnCanvas + annotation.Size);

        // Find ops
        foreach (var n in parentUi.ChildUis.Values)
        {
            var nRect = new ImRect(n.PosOnCanvas, n.PosOnCanvas + n.Size);
            if (aRect.Contains(nRect))
                matches.Add(n);
        }

        foreach (var n in parentUi.InputUis.Values)
        {
            var nRect = new ImRect(n.PosOnCanvas, n.PosOnCanvas + n.Size);
            if (aRect.Contains(nRect))
                matches.Add(n);
        }

        foreach (var n in parentUi.OutputUis.Values)
        {
            var nRect = new ImRect(n.PosOnCanvas, n.PosOnCanvas + n.Size);
            if (aRect.Contains(nRect))
                matches.Add(n);
        }


        // Find nested annotations
        foreach (var a in parentUi.Annotations.Values)
        {
            if (a == annotation)
                continue;

            var nRect = new ImRect(a.PosOnCanvas, a.PosOnCanvas + a.Size);
            if (aRect.Contains(nRect))
                matches.Add(a);
        }

        return matches;
    }

    private static Guid _draggedAnnotationId = Guid.Empty;
    private static Vector2 _dragStartDelta;
    private static ModifyCanvasElementsCommand _moveCommand;
    private static readonly List<ISelectableCanvasObject> _draggedNodes = [];
    private static readonly ValueSnapHandler _snapHandlerX = new(SnapResult.Orientations.Horizontal);
    private static readonly ValueSnapHandler _snapHandlerY = new(SnapResult.Orientations.Vertical);
}

/// <summary>
/// A simple help to snap to a grid raster of a canvas
/// </summary>
internal struct RasterSnapAttractor : IValueSnapAttractor
{
    public ScalableCanvas Canvas;
    public required Vector2 GridSize;
    public required Directions Direction;

    public enum Directions
    {
        Horizontal,
        Vertical,
    }

    public void CheckForSnap(ref SnapResult result)
    {
        if (Canvas == null)
            return;

        if (Canvas.Scale.X < 1f)
            return;

        var window = new ImRect(Canvas.WindowPos, Canvas.WindowPos + Canvas.WindowSize);
        var windowInCanvas = Canvas.InverseTransformRect(window);

        var alignedTopLeftCanvas = new Vector2((int)(windowInCanvas.Min.X / GridSize.X) * GridSize.X,
                                               (int)(windowInCanvas.Min.Y / GridSize.Y) * GridSize.Y);

        var count = windowInCanvas.GetSize() / GridSize;

        if (Direction == Directions.Horizontal)
        {
            for (int ix = 0; ix < 200 && ix <= count.X + 1; ix++)
            {
                var x = (int)(alignedTopLeftCanvas.X + ix * GridSize.X);
                result.TryToImproveWithAnchorValue(x);
            }
        }
        else
        {
            for (int iy = 0; iy < 200 && iy <= count.Y + 1; iy++)
            {
                var y = (int)(alignedTopLeftCanvas.Y + iy * GridSize.Y);
                result.TryToImproveWithAnchorValue(y);
            }
        }
    }
}