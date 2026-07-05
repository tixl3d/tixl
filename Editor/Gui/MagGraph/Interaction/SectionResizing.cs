using ImGuiNET;
using T3.Core.Utils;
using T3.Editor.Gui.Interaction.Snapping;
using T3.Editor.Gui.MagGraph.Model;
using T3.Editor.Gui.MagGraph.States;
using T3.Editor.Gui.Styling;
using T3.Editor.Gui.UiHelpers;
using T3.Editor.UiModel;
using T3.Editor.UiModel.Commands;
using T3.Editor.UiModel.Selection;
using T3.Editor.UiModel.Commands.Graph;

namespace T3.Editor.Gui.MagGraph.Interaction;

internal static class SectionResizing
{
    [Flags]
    internal enum Handles
    {
        None = 0,
        Left = 1 << 0,
        Right = 1 << 1,
        Top = 1 << 2,
        Bottom = 1 << 3,

        TopLeft = Top | Left,
        TopRight = Top | Right,
        BottomLeft = Bottom | Left,
        BottomRight = Bottom | Right,
    }

    /// <summary>
    /// The edges dragged by the current interaction. Set by the graph view's resize
    /// handles before entering the ResizeSection state.
    /// </summary>
    internal static Handles ActiveHandles = Handles.BottomRight;

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
                _startBoundsMin = section.PosOnCanvas;
                _startBoundsMax = section.PosOnCanvas + section.Size;
                _startMousePosInCanvas = context.View.InverseTransformPositionFloat(ImGui.GetMousePos());
                _moveCommand = new ModifyCanvasElementsCommand(instViewSymbolUi, [section], context.Selector);
            }

            if (started)
                return;
        }

        ImGui.SetMouseCursor(GetCursorForHandles(ActiveHandles));

        // Update dragging...
        {
            var mousePosInCanvas = context.View.InverseTransformPositionFloat(ImGui.GetMousePos());
            var dragDelta = mousePosInCanvas - _startMousePosInCanvas;

            var min = _startBoundsMin;
            var max = _startBoundsMax;

            if ((ActiveHandles & Handles.Left) != 0)
            {
                var newEdge = SnapEdge(_snapHandlerX, _startBoundsMin.X + dragDelta.X, context, magSection, context.View.Scale.X,
                                       RasterSnapAttractor.Directions.Horizontal);
                min.X = MathF.Min(newEdge, max.X - MagGraphItem.GridSize.X);
            }
            else if ((ActiveHandles & Handles.Right) != 0)
            {
                var newEdge = SnapEdge(_snapHandlerX, _startBoundsMax.X + dragDelta.X, context, magSection, context.View.Scale.X,
                                       RasterSnapAttractor.Directions.Horizontal);
                max.X = MathF.Max(newEdge, min.X + MagGraphItem.GridSize.X);
            }

            if ((ActiveHandles & Handles.Top) != 0)
            {
                var newEdge = SnapEdge(_snapHandlerY, _startBoundsMin.Y + dragDelta.Y, context, magSection, context.View.Scale.Y,
                                       RasterSnapAttractor.Directions.Vertical);
                min.Y = MathF.Min(newEdge, max.Y - MagGraphItem.GridSize.Y);
            }
            else if ((ActiveHandles & Handles.Bottom) != 0)
            {
                var newEdge = SnapEdge(_snapHandlerY, _startBoundsMax.Y + dragDelta.Y, context, magSection, context.View.Scale.Y,
                                       RasterSnapAttractor.Directions.Vertical);
                max.Y = MathF.Max(newEdge, min.Y + MagGraphItem.GridSize.Y);
            }

            section.PosOnCanvas = min;
            section.Size = max - min;
        }

        // Complete dragging...
        var completed = ImGui.IsMouseReleased(ImGuiMouseButton.Left);
        if (completed)
        {
            var wasDragging = ImGui.GetMouseDragDelta(ImGuiMouseButton.Left).LengthSquared() > UserSettings.Config.ClickThreshold;
            if (wasDragging)
            {
                // Resizing changes only this frame's rect - it never moves other ops or
                // frames. Ownership adjusts via derivation on the layout refresh.
                _moveCommand.StoreCurrentValues();
                UndoRedoStack.Add(_moveCommand);
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
            _draggedSectionId = Guid.Empty;
            _moveCommand = null;
            ActiveHandles = Handles.None;
        }
    }

    internal static ImGuiMouseCursor GetCursorForHandles(Handles handles)
    {
        return handles switch
                   {
                       Handles.TopLeft or Handles.BottomRight => ImGuiMouseCursor.ResizeNWSE,
                       Handles.TopRight or Handles.BottomLeft => ImGuiMouseCursor.ResizeNESW,
                       Handles.Left or Handles.Right          => ImGuiMouseCursor.ResizeEW,
                       Handles.Top or Handles.Bottom          => ImGuiMouseCursor.ResizeNS,
                       _                                      => ImGuiMouseCursor.Arrow,
                   };
    }

    private static float SnapEdge(ValueSnapHandler snapHandler, float edgeValue, GraphUiContext context, MagGraphSection magSection,
                                  float canvasScale, RasterSnapAttractor.Directions direction)
    {
        if (snapHandler.TryCheckForSnapping(edgeValue, out var snappedToSection, canvasScale * 0.25f,
                                                [magSection],
                                            context.Layout.Sections.Values))
        {
            return (float)snappedToSection;
        }

        var minGrid = MathF.Min(MagGraphItem.GridSize.X, MagGraphItem.GridSize.Y);
        if (snapHandler.TryCheckForSnapping(edgeValue, out var snappedToGrid, canvasScale * 0.25f,
                                                [],
                                                [new RasterSnapAttractor
                                                     {
                                                         Canvas = context.View,
                                                         GridSize = Vector2.One * minGrid,
                                                         Direction = direction
                                                     }]))
        {
            return (float)snappedToGrid;
        }

        return edgeValue;
    }

    private static Guid _draggedSectionId = Guid.Empty;
    private static Vector2 _startBoundsMin;
    private static Vector2 _startBoundsMax;
    private static Vector2 _startMousePosInCanvas;
    private static ModifyCanvasElementsCommand _moveCommand;

    private static readonly ValueSnapHandler _snapHandlerX = new(SnapResult.Orientations.Horizontal);
    private static readonly ValueSnapHandler _snapHandlerY = new(SnapResult.Orientations.Vertical);
}
