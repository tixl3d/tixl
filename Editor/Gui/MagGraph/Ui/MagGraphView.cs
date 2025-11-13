#nullable enable
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using ImGuiNET;
using T3.Core.Operator;
using T3.Core.Operator.Slots;
using T3.Editor.Gui.Graph.Interaction;
using T3.Editor.Gui.Interaction;
using T3.Editor.Gui.MagGraph.Interaction;
using T3.Editor.Gui.MagGraph.Model;
using T3.Editor.Gui.MagGraph.States;
using T3.Editor.Gui.UiHelpers;
using T3.Editor.Gui.Windows.AssetLib;
using T3.Editor.UiModel;
using T3.Editor.UiModel.Modification;
using T3.Editor.UiModel.ProjectHandling;
using T3.Editor.UiModel.Selection;

namespace T3.Editor.Gui.MagGraph.Ui;

/**
 * Draws and handles interaction with graph.
 */
internal sealed partial class MagGraphView : ScalableCanvas, IGraphView
{
    public ScalableCanvas Canvas => this;
    
    public static ProjectView CreateWithComponents(OpenedProject openedProject)
    {
        ProjectView.CreateIndependentComponents(openedProject,
                                                out var navigationHistory,
                                                out var nodeSelection,
                                                out var graphImageBackground);

        var projectView = new ProjectView(openedProject, navigationHistory, nodeSelection, graphImageBackground);
        
        if (projectView.CompositionInstance == null)
        {
            Log.Error("Can't create graph without defined composition op");
            return projectView; // TODO: handle this properly
        }

        var canvas = new MagGraphView(projectView);
        projectView.OnCompositionChanged += canvas.CompositionChangedHandler;
        projectView.OnCompositionContentChanged += canvas.CompositionContentChangedHandler;

        projectView.GraphView = canvas;
        return projectView;
    }

    private void CompositionChangedHandler(ProjectView arg1, Guid arg2)
    {
        _context.Layout.FlagStructureAsChanged();
    }

    private void CompositionContentChangedHandler(ProjectView view, ProjectView.ChangeTypes changes)
    {
        Debug.Assert(view == _projectView);
        if ((changes & (ProjectView.ChangeTypes.Connections | ProjectView.ChangeTypes.Children)) != 0)
        {
            _context.Layout.FlagStructureAsChanged();
        }
    }

    private readonly ProjectView _projectView;

    #region implement IGraph canvas
    bool IGraphView.Destroyed { get => _destroyed; set => _destroyed = value; }

    void IGraphView.FocusViewToSelection()
    {
        if (_projectView.CompositionInstance == null)
            return;

        var selectionBounds = NodeSelection.GetSelectionBounds(_projectView.NodeSelection, _projectView.CompositionInstance);
        FitAreaOnCanvas(selectionBounds);
    }

    void IGraphView.OpenAndFocusInstance(IReadOnlyList<Guid> path)
    {
        if (path.Count == 1)
        {
            _projectView.TrySetCompositionOp(path, ScalableCanvas.Transition.JumpOut, path[0]);
            return;
        }

        var compositionPath = path.Take(path.Count - 1).ToList();
        _projectView.TrySetCompositionOp(compositionPath, ScalableCanvas.Transition.JumpIn, path[^1]);
    }

    private Instance _previousInstance;

    void IGraphView.BeginDraw(bool backgroundActive, bool bgHasInteractionFocus)
    {
        //TODO: This should probably be handled by CompositionChangedHandler
        if (_projectView.CompositionInstance != null && _projectView.CompositionInstance != _previousInstance)
        {
            if (bgHasInteractionFocus && _context.StateMachine.CurrentState != GraphStates.BackgroundContentIsInteractive)
            {
                _context.StateMachine.SetState(GraphStates.BackgroundContentIsInteractive, _context);
            }
            
            _previousInstance = _projectView.CompositionInstance;
            _context = new GraphUiContext(_projectView, this);
        }
    }

    public bool HasActiveInteraction => _context.StateMachine.CurrentState != GraphStates.Default;


    void IGraphView.Close()
    {
        _destroyed = true;
        _projectView.OnCompositionChanged -= CompositionChangedHandler;
        _projectView.OnCompositionContentChanged -= CompositionContentChangedHandler;
    }

    void IGraphView.CreatePlaceHolderConnectedToInput(SymbolUi.Child symbolChildUi, Symbol.InputDefinition inputInputDefinition)
    {
        if (_context.StateMachine.CurrentState != GraphStates.Default)
        {
            Log.Debug("Can't insert placeholder while interaction is active");
            return;
        }

        if (_context.Layout.Items.TryGetValue(symbolChildUi.Id, out var item))
        {
            _context.Placeholder.OpenForItemInput(_context, item, inputInputDefinition.Id, MagGraphItem.Directions.Horizontal);

            // Calculate where the placeholder will be (to the left of the item)
            var placeholderPos = item.PosOnCanvas + new Vector2(-MagGraphItem.GridSize.X, 0);
            var currentVisibleArea = GetVisibleCanvasArea();

            // If placeholder position is outside the left edge of visible area, scroll to make it visible
            if (placeholderPos.X < currentVisibleArea.Min.X)
            {
                var scrollOffset = currentVisibleArea.Min.X - placeholderPos.X*1.01f;

                ScrollTarget = new Vector2(ScrollTarget.X - scrollOffset, ScrollTarget.Y);
            }
        }
    }

    void IGraphView.ExtractAsConnectedOperator<T>(InputSlot<T> inputSlot, SymbolUi.Child symbolChildUi, Symbol.Child.Input input)
    {
        if (!_context.Layout.Items.TryGetValue(symbolChildUi.Id, out var sourceItem))
        {
            return;
        }

        var insertionLineIndex = InputPicking.GetInsertionLineIndex(inputSlot.Parent.Inputs,
                                                                    sourceItem.InputLines,
                                                                    input.Id,
                                                                    out var shouldPushDown);

        var focusedItemPosOnCanvas = sourceItem.PosOnCanvas + new Vector2(-sourceItem.Size.X, MagGraphItem.GridSize.Y * insertionLineIndex);

        _context.StartMacroCommand("Extract parameters");
        if (shouldPushDown)
        {
            MagItemMovement
               .MoveSnappedItemsVertically(_context,
                                           MagItemMovement.CollectSnappedItems(sourceItem, includeRoot: false),
                                           sourceItem.PosOnCanvas.Y + (insertionLineIndex - 0.5f) * MagGraphItem.GridSize.Y,
                                           MagGraphItem.GridSize.Y);
        }

        // Todo: This should use undo/redo
        ParameterExtraction.ExtractAsConnectedOperator(inputSlot, symbolChildUi, input, focusedItemPosOnCanvas);
        _context.Layout.FlagStructureAsChanged();
        _context.CompleteMacroCommand();
    }

    void IGraphView.StartDraggingFromInputSlot(SymbolUi.Child symbolChildUi, Symbol.InputDefinition inputInputDefinition)
    {
        Log.Debug($"{nameof(IGraphView.StartDraggingFromInputSlot)}() not implemented yet");
    }
    #endregion

    private MagGraphView(ProjectView projectView)
    {
        _projectView = projectView;
        EnableParentZoom = false;
        _context = new GraphUiContext(projectView, this);
        _previousInstance = projectView.CompositionInstance!;
    }

    private ImRect _visibleCanvasArea;

    private bool IsRectVisible(ImRect rect)
    {
        return _visibleCanvasArea.Overlaps(rect);
    }

    public bool IsItemVisible(ISelectableCanvasObject item)
    {
        return IsRectVisible(ImRect.RectWithSize(item.PosOnCanvas, item.Size));
    }

    public bool IsFocused { get; private set; }
    public bool IsHovered { get; private set; }

    /// <summary>
    /// This is an intermediate helper method that should be replaced with a generalized implementation shared by
    /// all graph windows. It's especially unfortunate because it relies on GraphWindow.Focus to exist as open window :(
    ///
    /// It uses changes to context.CompositionOp to refresh the view to either the complete content or to the
    /// view saved in user settings...
    /// </summary>
    // private void InitializeCanvasScope(GraphUiContext context)
    // {
    //     if (ProjectView.Focused?.GraphCanvas is not ScalableCanvas canvas)
    //         return;
    //
    //
    //     // Meh: This relies on TargetScope already being set to new composition.
    //     var newViewArea = canvas.GetVisibleCanvasArea();
    //     if (UserSettings.Config.ViewedCanvasAreaForSymbolChildId.TryGetValue(context.CompositionInstance.SymbolChildId, out var savedCanvasView))
    //     {
    //         newViewArea = savedCanvasView;
    //     }
    //
    //     var scope = GetScopeForCanvasArea(newViewArea);
    //     context.Canvas.SetScopeWithTransition(scope, ICanvas.Transition.Instant);
    // }
    private void HandleSymbolDropping(GraphUiContext context)
    {
        if (!DragAndDropHandling.IsDragging)
            return;

        ImGui.SetCursorPos(Vector2.Zero);
        ImGui.InvisibleButton("## drop", ImGui.GetWindowSize());
        
        if (DragAndDropHandling.IsDraggingWith(DragAndDropHandling.SymbolDraggingId))
        {
            if (!DragAndDropHandling.TryGetDataDroppedLastItem(DragAndDropHandling.SymbolDraggingId, out var data))
                return;

            if (!Guid.TryParse(data, out var symbolId))
            {
                Log.Warning("Invalid data format for drop? " + data);
                return;
            }

            TryCreateSymbolInstanceOnGraph(context, symbolId, out _);
        }
        else if (DragAndDropHandling.IsDraggingWith(DragAndDropHandling.AssetDraggingId))
        {
            if (!DragAndDropHandling.TryGetDataDroppedLastItem(DragAndDropHandling.AssetDraggingId, out var aliasPath))
                return;

            if (!AssetLibrary.GetAssetFromAliasPath(aliasPath, out var asset))
            {
                Log.Warning($"Can't get asset for {aliasPath}");
                return;
            }

            if (asset.AssetType == null)
            {
                Log.Warning($"{aliasPath} has no asset type");
                return;
            }
            
            if (asset.AssetType.PrimaryOperators.Count == 0)
            {
                Log.Warning($"{aliasPath} of type {asset.AssetType} has no matching operator symbols");
                return;
            }

            if (TryCreateSymbolInstanceOnGraph(context, asset.AssetType.PrimaryOperators[0], out var newInstance))
            {
                if (!AssetLibrary.TryGetFileInputFromInstance(newInstance, out var stringInput, out _))
                {
                    Log.Warning("Failed to get file path parameter from op");
                    return;
                }
                
                Log.Debug($"Created {newInstance} with {aliasPath}", newInstance);
                
                stringInput.TypedInputValue.Assign(new InputValue<string>(aliasPath));
                stringInput.DirtyFlag.ForceInvalidate();
                stringInput.Parent.Parent?.Symbol.InvalidateInputInAllChildInstances(stringInput);
                stringInput.Input.IsDefault = false;
            }
        }
    }

    private bool TryCreateSymbolInstanceOnGraph(GraphUiContext context, Guid guid, [NotNullWhen(true)] out Instance? newInstance)
    {
        newInstance = null;
        if (SymbolUiRegistry.TryGetSymbolUi(guid, out var symbolUi))
        {
            var symbol = symbolUi.Symbol;
            var posOnCanvas = InverseTransformPositionFloat(ImGui.GetMousePos());
            if (!SymbolUiRegistry.TryGetSymbolUi(context.CompositionInstance.Symbol.Id, out var compositionOpSymbolUi))
            {
                Log.Warning("Failed to get symbol id for " + context.CompositionInstance.SymbolChildId);
                return false;
            }

            var childUi = GraphOperations.AddSymbolChild(symbol, compositionOpSymbolUi, posOnCanvas);
            newInstance = context.CompositionInstance.Children[childUi.Id];
            context.Selector.SetSelection(childUi, newInstance);
            context.Layout.FlagStructureAsChanged();
            return true;
        }

        Log.Warning($"Symbol {guid} not found in registry");
        return false;
    }

    private void HandleFenceSelection(GraphUiContext context, SelectionFence selectionFence)
    {
        var shouldBeActive =
                ImGui.IsWindowHovered(ImGuiHoveredFlags.AllowWhenBlockedByPopup)
                && (_context.StateMachine.CurrentState == GraphStates.Default
                    || _context.StateMachine.CurrentState == GraphStates.HoldBackground)
                && _context.StateMachine.StateTime > 0.01f // Prevent glitches when coming from other states.
            ;

        if (!shouldBeActive)
        {
            selectionFence.Reset();
            return;
        }

        switch (selectionFence.UpdateAndDraw(out var selectMode))
        {
            case SelectionFence.States.PressedButNotMoved:
                if (selectMode == SelectionFence.SelectModes.Replace)
                    _context.Selector.Clear();
                break;

            case SelectionFence.States.Updated:
                HandleSelectionFenceUpdate(selectionFence.BoundsUnclamped, selectMode);
                break;

            case SelectionFence.States.CompletedAsClick:
                // A hack to prevent clearing selection when opening parameter popup
                if (ImGui.IsPopupOpen("", ImGuiPopupFlags.AnyPopup))
                    break;

                _context.Selector.Clear();
                _context.Selector.SetSelectionToComposition(context.CompositionInstance);
                break;
            case SelectionFence.States.Inactive:
                break;
            case SelectionFence.States.CompletedAsArea:
                break;
            default:
                throw new ArgumentOutOfRangeException();
        }
    }

    // TODO: Support non graph items like annotations.
    private void HandleSelectionFenceUpdate(ImRect bounds, SelectionFence.SelectModes selectMode)
    {
        var boundsInCanvas = InverseTransformRect(bounds);

        if (selectMode == SelectionFence.SelectModes.Replace)
        {
            _context.Selector.Clear();
        }

        // Add items
        foreach (var item in _context.Layout.Items.Values)
        {
            var rect = new ImRect(item.PosOnCanvas, item.PosOnCanvas + item.Size);
            if (!rect.Overlaps(boundsInCanvas))
                continue;

            if (selectMode == SelectionFence.SelectModes.Remove)
            {
                _context.Selector.DeselectNode(item, item.Instance);
            }
            else
            {
                if (item.Variant == MagGraphItem.Variants.Operator)
                {
                    _context.Selector.AddSelection(item.Selectable, item.Instance);
                }
                else
                {
                    _context.Selector.AddSelection(item.Selectable);
                }
            }
        }

        foreach (var magAnnotation in _context.Layout.Annotations.Values)
        {
            var annotationArea = new ImRect(magAnnotation.PosOnCanvas, magAnnotation.PosOnCanvas + magAnnotation.Size);
            if (!boundsInCanvas.Contains(annotationArea))
                continue;

            if (selectMode == SelectionFence.SelectModes.Remove)
            {
                _context.Selector.DeselectNode(magAnnotation.Annotation);
            }
            else
            {
                _context.Selector.AddSelection(magAnnotation.Annotation);
            }
        }
    }

    // private void CenterView()
    // {
    //     var visibleArea = new ImRect();
    //     var isFirst = true;
    //
    //     foreach (var item in _context.Layout.Items.Values)
    //     {
    //         if (isFirst)
    //         {
    //             visibleArea = item.Area;
    //             isFirst = false;
    //             continue;
    //         }
    //
    //         visibleArea.Add(item.PosOnCanvas);
    //     }
    //
    //     FitAreaOnCanvas(visibleArea);
    // }

    private float GetHoverTimeForId(Guid id)
    {
        if (id != _lastHoverId)
            return 0;

        return HoverTime;
    }

    private readonly SelectionFence _selectionFence = new();
    private Vector2 GridSizeOnScreen => TransformDirection(MagGraphItem.GridSize);
    private float CanvasScale => Scale.X;

    // ReSharper disable once FieldCanBeMadeReadOnly.Global
    public bool ShowDebug = false; //ImGui.GetIO().KeyAlt;

    private Guid _lastHoverId;
    private double _hoverStartTime;
    private float HoverTime => (float)(ImGui.GetTime() - _hoverStartTime);
    private GraphUiContext _context;
    private bool _destroyed;

    protected override ScalableCanvas? Parent => null;

    public void FocusViewToSelection(GraphUiContext context)
    {
        var areaOnCanvas = NodeSelection.GetSelectionBounds(context.Selector, context.CompositionInstance);
        areaOnCanvas.Expand(200);
        FitAreaOnCanvas(areaOnCanvas);
    }
}