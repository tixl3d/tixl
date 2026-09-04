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
/// Handles the dragging of sections
/// </summary>
internal static class SectionDragging
{
    internal static void Draw(GraphUiContext context)
    {
        _snapHandlerY.DrawSnapIndicator(context.View, UiColors.StatusActivated.Fade(0.3f));
        _snapHandlerX.DrawSnapIndicator(context.View, UiColors.StatusActivated.Fade(0.3f));

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
                _dragStartDelta = ImGui.GetMousePos() - context.View.TransformPosition(magSection.PosOnCanvas);

                _draggedNodes.Clear();
                if (context.Selector.IsNodeSelected(section))
                {
                    _draggedNodes.AddRange(context.Selector.GetSelectedNodes<ISelectableCanvasObject>());
                }
                else
                {
                    if (!ImGui.GetIO().KeyCtrl)
                        _draggedNodes.AddRange(FindOpsInSection(context.ProjectView.InstView.SymbolUi, section));
                    
                    _draggedNodes.Add(section);
                }

                _draggedSectionIds.Clear();
                foreach (var node in _draggedNodes)
                {
                    if (node is Section draggedSection)
                        _draggedSectionIds.Add(draggedSection.Id);
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

            // Frames moving with the drag can't be snap targets - the group would
            // snap against itself and jitter
            _visibleSectionsForSnapping.Clear();
            foreach (var snapTo in context.Layout.Sections.Values)
            {
                if (_draggedSectionIds.Contains(snapTo.Id))
                    continue;

                if (context.View.IsItemVisible(snapTo) && !snapTo.Section.IsHiddenInCollapsedSection)
                    _visibleSectionsForSnapping.Add(snapTo);
            }

            var newDragPos = ImGui.GetMousePos() - _dragStartDelta;
            var newDragPosInCanvas = context.View.InverseTransformPositionFloat(newDragPos);
            var moveDeltaOnCanvas = newDragPosInCanvas - magSection.PosOnCanvas;

            // Horizontal snapping
            var snapFactor = 0.5f;
            if (_snapHandlerX.TryCheckForSnapping(newDragPosInCanvas.X, out var snappedXValue,
                                                  context.View.Scale.X * snapFactor,
                                                      [magSection],
                                                  _visibleSectionsForSnapping
                                                 ))
            {
                var snapDelta = snappedXValue - newDragPosInCanvas.X;
                moveDeltaOnCanvas.X += (float)snapDelta;
            }
            else if (_snapHandlerX.TryCheckForSnapping(newDragPosInCanvas.X + magSection.Size.X, out var snappedXValue2,
                                                       context.View.Scale.X * snapFactor,
                                                           [magSection],
                                                       _visibleSectionsForSnapping
                                                      ))
            {
                var snapDelta = snappedXValue2 - newDragPosInCanvas.X - magSection.Size.X;
                moveDeltaOnCanvas.X += (float)snapDelta;
            }
            else if (_snapHandlerX.TryCheckForSnapping(newDragPosInCanvas.X, out var snappedXValue3,
                                                       context.View.Scale.X ,
                                                           [],
                                                           [new RasterSnapAttractor
                                                                {
                                                                    Canvas = context.View,
                                                                    GridSize = gridSize,
                                                                    Direction = RasterSnapAttractor.Directions.Horizontal
                                                                }]))
            {
                moveDeltaOnCanvas.X += (float)(snappedXValue3 - newDragPosInCanvas.X);
            }

            // Vertical snapping...
            if (_snapHandlerY.TryCheckForSnapping(newDragPosInCanvas.Y, out var snappedYValue,
                                                  context.View.Scale.Y * snapFactor,
                                                      [magSection],
                                                  _visibleSectionsForSnapping
                                                 ))
            {
                var snapDelta = snappedYValue - newDragPosInCanvas.Y;
                moveDeltaOnCanvas.Y += (float)snapDelta;
            }
            else if (_snapHandlerY.TryCheckForSnapping(newDragPosInCanvas.Y + magSection.Size.Y, out var snappedYValue2,
                                                       context.View.Scale.Y * snapFactor,
                                                           [magSection],
                                                       _visibleSectionsForSnapping
                                                      ))
            {
                moveDeltaOnCanvas.Y += (float)(snappedYValue2 - newDragPosInCanvas.Y - magSection.Size.Y);
            }
            else if (_snapHandlerY.TryCheckForSnapping(newDragPosInCanvas.Y, out var snappedYValue3,
                                                       context.View.Scale.Y ,
                                                           [],
                                                           [new RasterSnapAttractor
                                                                {
                                                                    Canvas = context.View,
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

                // Section ownership is re-derived from geometry on the layout refresh
                context.Layout.FlagStructureAsChanged();
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
            _draggedNodes.Clear();
            _draggedSectionId = Guid.Empty;
            _moveCommand = null;
        }
    }

    private static readonly List<MagGraphSection> _visibleSectionsForSnapping = [];
    private static readonly HashSet<Guid> _draggedSectionIds = [];

    private static List<ISelectableCanvasObject> FindOpsInSection(SymbolUi parentUi, Section section)
    {
        var matches = new List<ISelectableCanvasObject>();
        SectionTree.CollectSectionContents(parentUi, section, matches);
        return matches;
    }

    private static Guid _draggedSectionId = Guid.Empty;
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