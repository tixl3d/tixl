using ImGuiNET;
using T3.Core.Utils;
using T3.Editor.Gui.Interaction;
using T3.Editor.Gui.Legacy.Interaction;
using T3.Editor.Gui.Styling;
using T3.Editor.Gui.UiHelpers;
using T3.Editor.UiModel;
using T3.Editor.UiModel.Commands;
using T3.Editor.UiModel.Commands.Graph;
using T3.Editor.UiModel.ProjectHandling;
using T3.Editor.UiModel.Selection;

namespace T3.Editor.Gui.Legacy;

/// <summary>
/// Draws an AnnotationElement and handles its interaction
/// </summary>
internal sealed class AnnotationElement
{
    private readonly Annotation _annotation;
    public AnnotationElement(ProjectView components, Annotation annotation)
    {
        _components = components;
        _annotation = annotation;
    }
        
    public void StartRenaming()
    {
        _requestedRenameId = _annotation.Id;
    }

    private Guid _requestedRenameId = Guid.Empty;

    internal void Draw(ImDrawListPtr drawList, ScalableCanvas canvas)
    {
        var annotation = _annotation;
        var screenArea = canvas.TransformRect(new ImRect(annotation.PosOnCanvas, annotation.PosOnCanvas + annotation.Size));
        var titleSize = annotation.Size;
        titleSize.Y = MathF.Min(titleSize.Y, 14 * T3Ui.UiScaleFactor);

        // Keep height of title area at a minimum height when zooming out
        var clickableArea = canvas.TransformRect(new ImRect(annotation.PosOnCanvas, annotation.PosOnCanvas + titleSize));
        var height = MathF.Min(16 * T3Ui.UiScaleFactor, screenArea.GetHeight());
        clickableArea.Max.Y = clickableArea.Min.Y + height;

        var isVisible = ImGui.IsRectVisible(screenArea.Min, screenArea.Max);

        if (!isVisible)
            return;

        ImGui.PushID(annotation.Id.GetHashCode());

        // Resize indicator
        {
            ImGui.SetMouseCursor(ImGuiMouseCursor.ResizeNWSE);
            ImGui.SetCursorScreenPos(screenArea.Max - new Vector2(10, 10) * T3Ui.UiScaleFactor);
            ImGui.Button("##resize", new Vector2(10, 10) * T3Ui.UiScaleFactor);
            if (ImGui.IsItemActive() && ImGui.IsMouseDragging(ImGuiMouseButton.Left))
            {
                var delta = canvas.InverseTransformDirection(ImGui.GetIO().MouseDelta);
                annotation.Size += delta;
            }

            ImGui.SetMouseCursor(ImGuiMouseCursor.Arrow);
        }

        // Background
        const float backgroundAlpha = 0.2f;
        const float headerHoverAlpha = 0.3f;

        drawList.AddRectFilled(screenArea.Min, screenArea.Max, UiColors.BackgroundFull.Fade(backgroundAlpha));

        // Interaction
        ImGui.SetCursorScreenPos(clickableArea.Min);
        ImGui.InvisibleButton("##annotationHeader", clickableArea.GetSize());

        DrawUtils.DebugItemRect();
        var isHeaderHovered = ImGui.IsItemHovered();
        if (isHeaderHovered)
        {
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
        }

        // Header
        drawList.AddRectFilled(clickableArea.Min, clickableArea.Max,
                               annotation.Color.Fade(isHeaderHovered ? headerHoverAlpha : 0));

        HandleDragging();
        var shouldRename = (ImGui.IsItemHovered() && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left)) || _requestedRenameId == annotation.Id;
        RenamingAnnotation.Draw(annotation, screenArea, shouldRename);
        if (shouldRename)
        {
            _requestedRenameId = Guid.Empty;
        }

        var borderColor = _components.NodeSelection.IsNodeSelected(annotation)
                              ? UiColors.Selection
                              : UiColors.BackgroundFull.Fade(isHeaderHovered ? headerHoverAlpha : backgroundAlpha);

        const float thickness = 1;
        drawList.AddRect(screenArea.Min - Vector2.One * thickness,
                         screenArea.Max + Vector2.One * thickness,
                         borderColor,
                         0f,
                         0,
                         thickness);

        // Label
        if(!string.IsNullOrEmpty(annotation.Title)) {
            var canvasScale = canvas.Scale.X;
            var font = annotation.Title.StartsWith("# ") ? Fonts.FontLarge: Fonts.FontNormal;
            var fade = MathUtils.SmootherStep(0.25f, 0.6f, canvasScale);
            drawList.PushClipRect(screenArea.Min, screenArea.Max, true);
            var labelPos = screenArea.Min + new Vector2(8, 6) * T3Ui.DisplayScaleFactor;

            var fontSize = canvasScale > 1 
                               ? font.FontSize
                               : canvasScale >  Fonts.FontSmall.Scale / Fonts.FontNormal.Scale
                                   ? font.FontSize
                                   : font.FontSize * canvasScale;
            drawList.AddText(font,
                             fontSize,
                             labelPos,
                             ColorVariations.OperatorLabel.Apply(annotation.Color.Fade(fade)),
                             annotation.Title);
            drawList.PopClipRect();
        }

        ImGui.PopID();
    }

    private void HandleDragging()
    {
        var nodeSelection = _components.NodeSelection;
        if (ImGui.IsItemActive())
        {
            if (ImGui.IsItemClicked(ImGuiMouseButton.Left))
            {
                var parentUi = _components.CompositionInstance.GetSymbolUi();
                _draggedNodeId = _annotation.Id;
                if (nodeSelection.IsNodeSelected(_annotation))
                {
                    _draggedNodes = nodeSelection.GetSelectedNodes<ISelectableCanvasObject>().ToList();
                }
                else
                {

                    if (!ImGui.GetIO().KeyCtrl)
                        _draggedNodes = FindAnnotatedOps(parentUi, _annotation).ToList();
                    _draggedNodes.Add(_annotation);
                }

                _moveCommand = new ModifyCanvasElementsCommand(parentUi, _draggedNodes, nodeSelection);
            }

            HandleNodeDragging(_annotation);
        }
        else if (ImGui.IsMouseReleased(0) && _moveCommand != null)
        {
            if (_draggedNodeId != _annotation.Id)
                return;

            // var singleDraggedNode = (_draggedNodes.Count == 1) ? _draggedNodes[0] : null;
            _draggedNodeId = Guid.Empty;
            _draggedNodes.Clear();

            var wasDragging = ImGui.GetMouseDragDelta(ImGuiMouseButton.Left).LengthSquared() > UserSettings.Config.ClickThreshold;
            if (wasDragging)
            {
                _moveCommand.StoreCurrentValues();
                UndoRedoStack.Add(_moveCommand);
            }
            else
            {
                if (!nodeSelection.IsNodeSelected(_annotation))
                {
                    if (!ImGui.GetIO().KeyShift)
                    {
                        nodeSelection.Clear();
                    }

                    nodeSelection.AddSelection(_annotation);
                }
                else
                {
                    if (ImGui.GetIO().KeyShift)
                    {
                        nodeSelection.DeselectNode(_annotation, null);
                    }
                }
            }

            _moveCommand = null;
        }

        var wasDraggingRight = ImGui.GetMouseDragDelta(ImGuiMouseButton.Right).Length() > UserSettings.Config.ClickThreshold;
        if (ImGui.IsMouseReleased(ImGuiMouseButton.Right)
            && !wasDraggingRight
            && ImGui.IsItemHovered()
            && !nodeSelection.IsNodeSelected(_annotation))
        {
            nodeSelection.SetSelection(_annotation);
        }
    }

    private static List<ISelectableCanvasObject> FindAnnotatedOps(SymbolUi parentUi, Annotation annotation)
    {
        var matches = new List<ISelectableCanvasObject>();
        var aRect = new ImRect(annotation.PosOnCanvas, annotation.PosOnCanvas + annotation.Size);

        foreach (var n in parentUi.ChildUis.Values)
        {
            var nRect = new ImRect(n.PosOnCanvas, n.PosOnCanvas + n.Size);
            if (aRect.Contains(nRect))
                matches.Add(n);
        }

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

    private void HandleNodeDragging(ISelectableCanvasObject draggedNode)
    {
        if (!ImGui.IsMouseDragging(ImGuiMouseButton.Left))
        {
            _isDragging = false;
            return;
        }
            
        var canvas = _components.GraphView.Canvas;

        if (!_isDragging)
        {
            _dragStartDelta = ImGui.GetMousePos() - canvas.TransformPosition(draggedNode.PosOnCanvas);
            _isDragging = true;
        }

        var newDragPos = ImGui.GetMousePos() - _dragStartDelta;
        var newDragPosInCanvas = canvas.InverseTransformPositionFloat(newDragPos);
        var moveDeltaOnCanvas = newDragPosInCanvas - draggedNode.PosOnCanvas;

        // Drag selection
        foreach (var e in _draggedNodes)
        {
            e.PosOnCanvas += moveDeltaOnCanvas;
        }
    }

    private bool _isDragging;
    private Vector2 _dragStartDelta;
    private ModifyCanvasElementsCommand _moveCommand;
    private readonly ProjectView _components;

    private Guid _draggedNodeId = Guid.Empty;
    private List<ISelectableCanvasObject> _draggedNodes = new();

}