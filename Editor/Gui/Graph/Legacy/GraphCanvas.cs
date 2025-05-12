using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using ImGuiNET;
using T3.Core.Operator;
using T3.Core.Operator.Slots;
using T3.Core.Resource;
using T3.Core.SystemUi;
using T3.Core.UserData;
using T3.Editor.External;
using T3.Editor.Gui.Graph.Dialogs;
using T3.Editor.Gui.Graph.Interaction;
using T3.Editor.Gui.Graph.Legacy.Interaction;
using T3.Editor.Gui.Graph.Legacy.Interaction.Connections;
using T3.Editor.Gui.Interaction;
using T3.Editor.Gui.Interaction.Variations;
using T3.Editor.Gui.MagGraph.Interaction;
using T3.Editor.Gui.OutputUi;
using T3.Editor.Gui.Styling;
using T3.Editor.Gui.UiHelpers;
using T3.Editor.SystemUi;
using T3.Editor.UiModel;
using T3.Editor.UiModel.Commands;
using T3.Editor.UiModel.Exporting;
using T3.Editor.UiModel.InputsAndTypes;
using T3.Editor.UiModel.Modification;
using T3.Editor.UiModel.ProjectHandling;
using T3.Editor.UiModel.Selection;
using T3.SystemUi;

namespace T3.Editor.Gui.Graph.Legacy;

/// <summary>
/// A <see cref="ICanvas"/> that displays the graph of an Operator.
/// </summary>
internal sealed class GraphCanvas : ScalableCanvas, IGraphCanvas
{
    public SymbolBrowser SymbolBrowser { get; set; }

    private readonly NodeSelection _nodeSelection;
    private readonly NavigationHistory _navigationHistory;
    private readonly NodeNavigation _nodeNavigation;
    private readonly NodeGraphLayouting _nodeGraphLayouting;
    private Legacy.Graph _graph;

    private ProjectView _projectView;

    public ProjectView ProjectView
    {
        set
        {
            _projectView = value;
            _graph = new Legacy.Graph(_projectView, this, () => SymbolBrowser);
        }
    }

    public void Close()
    {
        ConnectionMaker.RemoveWindow(this);
        _nodeNavigation.FocusInstanceRequested -= OpenAndFocusInstance;
    }

    public void CreatePlaceHolderConnectedToInput(SymbolUi.Child symbolChildUi, Symbol.InputDefinition inputInputDefinition)
    {
        if (_projectView.InstView == null)
        {
            Log.Error("Failed to access composition op?");
            return;
        }
        
        ConnectionMaker.StartFromInputSlot(this, _projectView.InstView.Symbol, symbolChildUi, inputInputDefinition);
        var freePosition = NodeGraphLayouting.FindPositionForNodeConnectedToInput(_projectView.InstView.SymbolUi, symbolChildUi);
        ConnectionMaker.InitSymbolBrowserAtPosition(this, SymbolBrowser, freePosition);
    }
    
    void IGraphCanvas.ExtractAsConnectedOperator<T>(InputSlot<T> inputSlot, SymbolUi.Child symbolChildUi, Symbol.Child.Input input)
    {
        if (_projectView?.InstView == null)
            return;
        
        var freePosition = NodeGraphLayouting.FindPositionForNodeConnectedToInput(_projectView.InstView.SymbolUi, symbolChildUi);
        ParameterExtraction.ExtractAsConnectedOperator(inputSlot, symbolChildUi, input, freePosition);
    }


    public void StartDraggingFromInputSlot(SymbolUi.Child symbolChildUi, Symbol.InputDefinition inputInputDefinition)
    {
        if (_projectView.InstView == null)
        {
            Log.Error("Failed to access composition op?");
            return;
        }
        
        ConnectionMaker.StartFromInputSlot(this,  _projectView.InstView.Symbol, symbolChildUi, inputInputDefinition);
    }

    public static ProjectView CreateWithComponents(OpenedProject openedProject)
    {
        ProjectView.CreateIndependentComponents(openedProject, out var navigationHistory, out var nodeSelection, out var graphImageBackground);
        var projectView = new ProjectView(openedProject, navigationHistory, nodeSelection, graphImageBackground);
        var canvas = new GraphCanvas(nodeSelection,
                                     openedProject.Structure,
                                     navigationHistory,
                                     projectView.NodeNavigation,
                                     getComposition: () => projectView.CompositionInstance)
        {
            ProjectView = projectView
        };

        projectView.GraphCanvas = canvas;
        canvas.SymbolBrowser = new SymbolBrowser(projectView, canvas);
        ConnectionMaker.AddWindow(canvas);
        return projectView;
    }

    private GraphCanvas(NodeSelection nodeSelection, Structure structure, NavigationHistory navigationHistory, NodeNavigation nodeNavigation,
                        Func<Instance> getComposition)
    {
        _nodeNavigation = nodeNavigation;
        _nodeSelection = nodeSelection;
        _navigationHistory = navigationHistory;
        nodeNavigation.FocusInstanceRequested += OpenAndFocusInstance;
        _nodeGraphLayouting = new NodeGraphLayouting(nodeSelection, structure);
        SelectableNodeMovement = new SelectableNodeMovement(this, getComposition, () => SelectableChildren, _nodeSelection);
    }

    // moved to Close()
    // ~GraphCanvas()
    // {
    //     _nodeNavigation.FocusInstanceRequested -= OpenAndFocusInstance;
    // }

    [Flags]
    public enum GraphDrawingFlags
    {
        None = 0,
        PreventInteractions = 1 << 1,
        HideGrid = 1 << 2,
    }

    #region drawing UI ====================================================================
    /// <summary>
    /// Scales the canvas and handles basic canvas interactions
    /// </summary>
    public void BeginDraw(bool backgroundActive, bool bgHasInteractionFocus)
    {
        var flags = GraphDrawingFlags.None;

        if (backgroundActive)
            flags |= GraphDrawingFlags.HideGrid;

        if (bgHasInteractionFocus)
            flags |= GraphDrawingFlags.PreventInteractions;

        var preventInteractions = flags.HasFlag(GraphDrawingFlags.PreventInteractions);

        _preventInteractions = preventInteractions;
        _drawingFlags = flags;

        var editingFlags = T3Ui.EditingFlags.None;

        if (SymbolBrowser.IsOpen)
            editingFlags |= T3Ui.EditingFlags.PreventZoomWithMouseWheel;

        if (preventInteractions)
            editingFlags |= T3Ui.EditingFlags.PreventMouseInteractions;

        UpdateCanvas(out _, editingFlags);
    }

    public void DrawGraph(ImDrawListPtr drawList, float graphOpacity)
    {
        if (_projectView?.CompositionInstance == null)
            return;
        
        ConnectionSnapEndHelper.PrepareNewFrame();

        DrawDropHandler(_projectView.CompositionInstance, _projectView.CompositionInstance.GetSymbolUi());
        ImGui.SetCursorScreenPos(Vector2.One * 100);

        if (!_preventInteractions)
        {
            var compositionOp = _projectView.CompositionInstance;
            var compositionUi = compositionOp.GetSymbolUi();
            //compositionUi.FlagAsModified();

            if (KeyboardBinding.Triggered(UserActions.FocusSelection))
                FocusViewToSelection();

            if (!T3Ui.IsCurrentlySaving && KeyboardBinding.Triggered(UserActions.Duplicate))
            {
                NodeActions.CopySelectedNodesToClipboard(_nodeSelection, compositionOp);
                NodeActions.PasteClipboard(_nodeSelection, this, compositionOp);
            }

            if (!T3Ui.IsCurrentlySaving && KeyboardBinding.Triggered(UserActions.DeleteSelection))
            {
                NodeActions.DeleteSelectedElements(_nodeSelection, compositionUi);
            }

            if (KeyboardBinding.Triggered(UserActions.ToggleDisabled))
            {
                NodeActions.ToggleDisabledForSelectedElements(_nodeSelection);
            }

            if (KeyboardBinding.Triggered(UserActions.ToggleBypassed))
            {
                NodeActions.ToggleBypassedForSelectedElements(_nodeSelection);
            }

            if (KeyboardBinding.Triggered(UserActions.PinToOutputWindow))
            {
                if (UserSettings.Config.FocusMode)
                {
                    var selectedImage = _nodeSelection.GetFirstSelectedInstance();
                    if (selectedImage != null && ProjectView.Focused != null)
                    {
                        ProjectView.Focused.SetBackgroundOutput(selectedImage);
                    }
                }
                else
                {
                    NodeActions.PinSelectedToOutputWindow(_projectView, _nodeSelection, compositionOp);
                }
            }

            if (KeyboardBinding.Triggered(UserActions.DisplayImageAsBackground))
            {
                var selectedImage = _nodeSelection.GetFirstSelectedInstance();
                if (selectedImage != null && ProjectView.Focused != null)
                {
                    ProjectView.Focused.SetBackgroundOutput(selectedImage);
                    //GraphWindow.Focused.SetBackgroundInstanceForCurrentGraph(selectedImage);
                }
            }

            if (KeyboardBinding.Triggered(UserActions.CopyToClipboard))
            {
                NodeActions.CopySelectedNodesToClipboard(_nodeSelection, compositionOp);
            }

            if (!T3Ui.IsCurrentlySaving && KeyboardBinding.Triggered(UserActions.PasteFromClipboard))
            {
                NodeActions.PasteClipboard(_nodeSelection, this, compositionOp);
            }

            if (KeyboardBinding.Triggered(UserActions.LayoutSelection))
            {
                _nodeGraphLayouting.ArrangeOps(compositionOp);
            }

            if (!T3Ui.IsCurrentlySaving && KeyboardBinding.Triggered(UserActions.AddAnnotation))
            {
                var newAnnotation = NodeActions.AddAnnotation(_nodeSelection, this, compositionOp);
                _graph.RenameAnnotation(newAnnotation);
            }

            IReadOnlyList<Guid> navigationPath = null;

            // Navigation
            if (KeyboardBinding.Triggered(UserActions.NavigateBackwards))
            {
                navigationPath = _navigationHistory.NavigateBackwards();
            }

            if (KeyboardBinding.Triggered(UserActions.NavigateForward))
            {
                navigationPath = _navigationHistory.NavigateForward();
            }

            if (navigationPath != null)
                _projectView.TrySetCompositionOp(navigationPath);

            if (KeyboardBinding.Triggered(UserActions.SelectToAbove))
            {
                _nodeNavigation.SelectAbove();
            }

            if (KeyboardBinding.Triggered(UserActions.SelectToRight))
            {
                _nodeNavigation.SelectRight();
            }

            if (KeyboardBinding.Triggered(UserActions.SelectToBelow))
            {
                _nodeNavigation.SelectBelow();
            }

            if (KeyboardBinding.Triggered(UserActions.AddComment))
            {
                EditCommentDialog.ShowNextFrame();
            }

            if (KeyboardBinding.Triggered(UserActions.SelectToLeft))
            {
                _nodeNavigation.SelectLeft();
            }

            if (KeyboardBinding.Triggered(UserActions.DisplayImageAsBackground))
            {
                var selectedImage = _nodeSelection.GetFirstSelectedInstance();
                if (selectedImage != null)
                {
                    _projectView.GraphImageBackground.OutputInstance = selectedImage;
                }
            }
        }

        if (ImGui.IsWindowFocused() && !_preventInteractions)
        {
            var io = ImGui.GetIO();
            var editingSomething = ImGui.IsAnyItemActive();

            if (!io.KeyCtrl && !io.KeyShift && !io.KeyAlt && !editingSomething)
            {
                if (ImGui.IsKeyDown((ImGuiKey)Key.W))
                {
                    _dampedScrollVelocity.Y -= InverseTransformDirection(Vector2.One * UserSettings.Config.KeyboardScrollAcceleration).Y;
                }

                if (ImGui.IsKeyDown((ImGuiKey)Key.S))
                {
                    _dampedScrollVelocity.Y += InverseTransformDirection(Vector2.One * UserSettings.Config.KeyboardScrollAcceleration).Y;
                }

                if (ImGui.IsKeyDown((ImGuiKey)Key.A))
                {
                    _dampedScrollVelocity.X -= InverseTransformDirection(Vector2.One * UserSettings.Config.KeyboardScrollAcceleration).X;
                }

                if (ImGui.IsKeyDown((ImGuiKey)Key.D))
                {
                    _dampedScrollVelocity.X += InverseTransformDirection(Vector2.One * UserSettings.Config.KeyboardScrollAcceleration).X;
                }

                if (ImGui.IsKeyDown((ImGuiKey)Key.Q))
                {
                    var center = WindowPos + WindowSize / 2;
                    ApplyZoomDelta(center, 1.05f, out _);
                }

                if (ImGui.IsKeyDown((ImGuiKey)Key.E))
                {
                    var center = WindowPos + WindowSize / 2;
                    ApplyZoomDelta(center, 1 / 1.05f, out _);
                }
            }
        }

        ScrollTarget += _dampedScrollVelocity;
        _dampedScrollVelocity *= 0.90f;

        drawList.PushClipRect(WindowPos, WindowPos + WindowSize);

        if (!_drawingFlags.HasFlag(GraphDrawingFlags.HideGrid))
            DrawGrid(drawList);

        if (ImGui.IsWindowHovered(ImGuiHoveredFlags.AllowWhenBlockedByActiveItem))
        {
            ConnectionSplitHelper.PrepareNewFrame(_projectView);
        }

        SymbolBrowser.Draw();

        graphOpacity *= _preventInteractions ? 0.3f : 1;
        _graph.DrawGraph(drawList, _drawingFlags.HasFlag(GraphDrawingFlags.PreventInteractions), _projectView.CompositionInstance, graphOpacity);

        RenamingOperator.Draw(_projectView);
        var tempConnections = ConnectionMaker.GetTempConnectionsFor(this);

        var doubleClicked = ImGui.IsMouseDoubleClicked(0);

        var isSomething = (ImGui.IsWindowHovered(ImGuiHoveredFlags.AllowWhenBlockedByPopup) || ImGui.IsWindowFocused())
                          && !_preventInteractions
                          && tempConnections.Count == 0;

        var isOnBackground = ImGui.IsWindowFocused() && !ImGui.IsAnyItemActive();
        var shouldHandleFenceSelection = isSomething
                                         || isOnBackground && (ImGui.IsMouseDoubleClicked(0) || KeyboardBinding.Triggered(UserActions.CloseOperator));

        if (shouldHandleFenceSelection)
        {
            HandleFenceSelection(_projectView.CompositionInstance, _selectionFence);
        }

        if (isOnBackground && doubleClicked)
        {
            _projectView.TrySetCompositionOpToParent();
        }

        if (tempConnections.Count > 0 && ImGui.IsMouseReleased(0))
        {
            var isAnyItemHovered = ImGui.IsAnyItemHovered();
            var droppedOnBackground =
                ImGui.IsWindowHovered(ImGuiHoveredFlags.AllowWhenBlockedByActiveItem | ImGuiHoveredFlags.AllowWhenBlockedByPopup) && !isAnyItemHovered;
            if (droppedOnBackground)
            {
                ConnectionMaker.InitSymbolBrowserAtPosition(this, SymbolBrowser, InverseTransformPositionFloat(ImGui.GetIO().MousePos));
            }
            else
            {
                var connectionDroppedOnBackground =
                    tempConnections[0].GetStatus() != ConnectionMaker.TempConnection.Status.TargetIsDraftNode;
                if (connectionDroppedOnBackground)
                {
                    //Log.Warning("Skipping complete operation on background drop?");
                    //  ConnectionMaker.CompleteOperation();
                }
            }
        }

        drawList.PopClipRect();

        var compositionInstance = _projectView.CompositionInstance;

        if (FrameStats.Current.OpenedPopUpName == string.Empty)
            CustomComponents.DrawContextMenuForScrollCanvas(() => DrawContextMenuContent(compositionInstance), ref _contextMenuIsOpen);

        _duplicateSymbolDialog.Draw(compositionInstance, _nodeSelection.GetSelectedChildUis().ToList(), ref _nameSpaceForDialogEdits,
                                    ref _symbolNameForDialogEdits,
                                    ref _symbolDescriptionForDialog);
        _combineToSymbolDialog.Draw(compositionInstance, _projectView,
                                    ref _nameSpaceForDialogEdits,
                                    ref _symbolNameForDialogEdits,
                                    ref _symbolDescriptionForDialog);

        _renameSymbolDialog.Draw(_nodeSelection.GetSelectedChildUis().ToList(), ref _symbolNameForDialogEdits);

        EditCommentDialog.Draw(_nodeSelection);

        if (compositionInstance != _projectView.OpenedProject.RootInstance && !compositionInstance.Symbol.SymbolPackage.IsReadOnly)
        {
            var symbol = compositionInstance.Symbol;
            _addInputDialog.Draw(symbol);
            _addOutputDialog.Draw(symbol);
        }

        LibWarningDialog.Draw(_projectView);
        EditNodeOutputDialog.Draw();
        SelectableNodeMovement.CompleteFrame();
    }

    
    
    
    public bool HasActiveInteraction
    {
        get
        {
            var t = ConnectionMaker.GetTempConnectionsFor(this);
            return t.Count > 0;
        }
    }

    private void HandleFenceSelection(Instance compositionOp, SelectionFence selectionFence)
    {
        switch (selectionFence.UpdateAndDraw(out var selectMode))
        {
            case SelectionFence.States.PressedButNotMoved:
                if (selectMode == SelectionFence.SelectModes.Replace)
                    _nodeSelection.Clear();
                break;

            case SelectionFence.States.Updated:
                var bounds = selectionFence.BoundsUnclamped;
                HandleSelectionFenceUpdate(bounds, compositionOp, selectMode);
                break;

            case SelectionFence.States.CompletedAsClick:
                // A hack to prevent clearing selection when opening parameter popup
                if (ImGui.IsPopupOpen("", ImGuiPopupFlags.AnyPopup))
                    break;

                _nodeSelection.Clear();
                _nodeSelection.SetSelectionToComposition(compositionOp);
                break;
        }
    }

    private void HandleSelectionFenceUpdate(ImRect bounds, Instance compositionOp, SelectionFence.SelectModes selectMode)
    {
        var boundsInCanvas = InverseTransformRect(bounds);
        var nodesToSelect = NodeSelection.GetSelectableChildren(compositionOp)
                                         .Where(child => child is Annotation
                                                             ? boundsInCanvas.Contains(child.Rect)
                                                             : child !=null && child.Rect.Overlaps(boundsInCanvas));

        if (selectMode == SelectionFence.SelectModes.Replace)
        {
            _nodeSelection.Clear();
        }

        var isRemoval = selectMode == SelectionFence.SelectModes.Remove;

        foreach (var node in nodesToSelect)
        {
            if (node is SymbolUi.Child symbolChildUi)
            {
                if (!compositionOp.TryGetChildInstance(symbolChildUi.Id, false, out var instance, out _))
                {
                    Log.Error("Can't find instance");
                }

                if (isRemoval)
                {
                    _nodeSelection.DeselectNode(symbolChildUi, instance);
                }
                else
                {
                    _nodeSelection.AddSelection(symbolChildUi, instance);
                }
            }
            else
            {
                if (isRemoval)
                {
                    _nodeSelection.DeselectNode(node);
                }
                else
                {
                    _nodeSelection.AddSelection(node);
                }
            }
        }
    }

    // private Symbol GetSelectedSymbol()
    // {
    //     var selectedChildUi = GetSelectedChildUis().FirstOrDefault();
    //     return selectedChildUi != null ? selectedChildUi.SymbolChild.Symbol : compositionOp.Symbol;
    // }

    private void DrawDropHandler(Instance compositionOp, SymbolUi compositionOpSymbolUi)
    {
        if (!T3Ui.DraggingIsInProgress)
            return;

        ImGui.SetCursorPos(Vector2.Zero);
        ImGui.InvisibleButton("## drop", ImGui.GetWindowSize());

        if (ImGui.BeginDragDropTarget())
        {
            var payload = ImGui.AcceptDragDropPayload("Symbol");
            if (ImGui.IsMouseReleased(0))
            {
                var myString = Marshal.PtrToStringAuto(payload.Data);
                if (myString != null)
                {
                    var guidString = myString.Split('|')[0];
                    var guid = Guid.Parse(guidString);
                    Log.Debug("dropped symbol here" + payload + " " + myString + "  " + guid);

                    if (SymbolUiRegistry.TryGetSymbolUi(guid, out var symbolUi))
                    {
                        var symbol = symbolUi.Symbol;
                        var posOnCanvas = InverseTransformPositionFloat(ImGui.GetMousePos());
                        var childUi = GraphOperations.AddSymbolChild(symbol, compositionOpSymbolUi, posOnCanvas);

                        var instance = compositionOp.Children[childUi.Id];
                        _nodeSelection.SetSelection(childUi, instance);
                    }
                    else
                    {
                        Log.Warning($"Symbol {guid} not found in registry");
                    }

                    T3Ui.DraggingIsInProgress = false;
                }
            }

            ImGui.EndDragDropTarget();
        }
    }
    
    public void FocusViewToSelection()
    {
        if (_projectView?.CompositionInstance == null)
            return;
        
        FitAreaOnCanvas(NodeSelection.GetSelectionBounds(_nodeSelection, _projectView.CompositionInstance));
    }

    private void DrawContextMenuContent(Instance compositionOp)
    {
        var clickPosition = ImGui.GetMousePosOnOpeningCurrentPopup();
        var compositionSymbolUi = compositionOp.GetSymbolUi();

        var selectedChildUis = _nodeSelection.GetSelectedChildUis().ToList();
        var nextUndoTitle = UndoRedoStack.CanUndo ? $" ({UndoRedoStack.GetNextUndoTitle()})" : string.Empty;
        if (ImGui.MenuItem("Undo" + nextUndoTitle,
                           shortcut: KeyboardBinding.ListKeyboardShortcuts(UserActions.Undo, false),
                           selected: false,
                           enabled: UndoRedoStack.CanUndo))
        {
            UndoRedoStack.Undo();
        }

        ImGui.Separator();

        // ------ for selection -----------------------
        var oneOpSelected = selectedChildUis.Count == 1;
        var someOpsSelected = selectedChildUis.Count > 0;

        var label = oneOpSelected
                        ? $"{selectedChildUis[0].SymbolChild.ReadableName}..."
                        : $"{selectedChildUis.Count} selected items...";

        ImGui.PushFont(Fonts.FontSmall);
        ImGui.PushStyleColor(ImGuiCol.Text, UiColors.Gray.Rgba);
        ImGui.TextUnformatted(label);
        ImGui.PopStyleColor();
        ImGui.PopFont();

        var allSelectedDisabled = selectedChildUis.TrueForAll(selectedChildUi => selectedChildUi.SymbolChild.IsDisabled);
        if (ImGui.MenuItem("Disable",
                           KeyboardBinding.ListKeyboardShortcuts(UserActions.ToggleDisabled, false),
                           selected: allSelectedDisabled,
                           enabled: selectedChildUis.Count > 0))
        {
            NodeActions.ToggleDisabledForSelectedElements(_nodeSelection);
        }

        var allSelectedBypassed = selectedChildUis.TrueForAll(selectedChildUi => selectedChildUi.SymbolChild.IsBypassed);
        if (ImGui.MenuItem("Bypassed",
                           KeyboardBinding.ListKeyboardShortcuts(UserActions.ToggleBypassed, false),
                           selected: allSelectedBypassed,
                           enabled: selectedChildUis.Count > 0))
        {
            NodeActions.ToggleBypassedForSelectedElements(_nodeSelection);
        }

        if (ImGui.MenuItem("Rename", oneOpSelected))
        {
            RenamingOperator.OpenForChildUi(selectedChildUis[0]);
        }

        if (ImGui.MenuItem("Add Comment",
                           KeyboardBinding.ListKeyboardShortcuts(UserActions.AddComment, false),
                           selected: false,
                           enabled: oneOpSelected))
        {
            EditCommentDialog.ShowNextFrame();
        }

        if (ImGui.MenuItem("Arrange sub graph",
                           KeyboardBinding.ListKeyboardShortcuts(UserActions.LayoutSelection, false),
                           selected: false,
                           enabled: someOpsSelected))
        {
            _nodeGraphLayouting.ArrangeOps(compositionOp);
        }

        var canModify = !compositionSymbolUi.Symbol.SymbolPackage.IsReadOnly;
        if (canModify)
        {
            // Disable if already enabled for all
            var disableBecauseAllEnabled
                = selectedChildUis
                   .TrueForAll(c2 => c2.EnabledForSnapshots);

            foreach (var c in selectedChildUis)
            {
                c.EnabledForSnapshots = !disableBecauseAllEnabled;
            }

            // Add to add snapshots
            var allSnapshots = VariationHandling.ActivePoolForSnapshots?.AllVariations;
            if (allSnapshots != null && allSnapshots.Count > 0)
            {
                if (disableBecauseAllEnabled)
                {
                    VariationHandling.RemoveInstancesFromVariations(selectedChildUis.Select(ui => ui.Id), allSnapshots);
                }
                // Remove from snapshots
                else
                {
                    var selectedInstances = selectedChildUis
                                           .Select(ui => compositionOp.Children[ui.Id])
                                           .ToList();
                    foreach (var snapshot in allSnapshots)
                    {
                        VariationHandling.ActivePoolForSnapshots.UpdateVariationPropertiesForInstances(snapshot, selectedInstances);
                    }
                }
            }

            compositionSymbolUi.FlagAsModified();
        }

        if (ImGui.BeginMenu("Display as..."))
        {
            if (ImGui.MenuItem("Small", "",
                               selected: selectedChildUis.Any(child => child.Style == SymbolUi.Child.Styles.Default),
                               enabled: someOpsSelected))
            {
                foreach (var childUi in selectedChildUis)
                {
                    childUi.Style = SymbolUi.Child.Styles.Default;
                }
            }

            if (ImGui.MenuItem("Resizable", "",
                               selected: selectedChildUis.Any(child => child.Style == SymbolUi.Child.Styles.Resizable),
                               enabled: someOpsSelected))
            {
                foreach (var childUi in selectedChildUis)
                {
                    childUi.Style = SymbolUi.Child.Styles.Resizable;
                }
            }

            if (ImGui.MenuItem("Expanded", "",
                               selected: selectedChildUis.Any(child => child.Style == SymbolUi.Child.Styles.Resizable),
                               enabled: someOpsSelected))
            {
                foreach (var childUi in selectedChildUis)
                {
                    childUi.Style = SymbolUi.Child.Styles.Expanded;
                }
            }

            ImGui.Separator();

            var isImage = oneOpSelected
                          && selectedChildUis[0].SymbolChild.Symbol.OutputDefinitions.Count > 0
                          && selectedChildUis[0].SymbolChild.Symbol.OutputDefinitions[0].ValueType == typeof(T3.Core.DataTypes.Texture2D);
            if (ImGui.MenuItem("Set image as graph background",
                               KeyboardBinding.ListKeyboardShortcuts(UserActions.DisplayImageAsBackground, false),
                               selected: false,
                               enabled: isImage))
            {
                var instance = compositionOp.Children[selectedChildUis[0].Id];
                _projectView.GraphImageBackground.OutputInstance = instance;
            }

            if (ImGui.MenuItem("Pin to output", oneOpSelected))
            {
                NodeActions.PinSelectedToOutputWindow(_projectView, _nodeSelection, compositionOp);
            }

            ImGui.EndMenu();
        }

        ImGui.Separator();

        if (ImGui.MenuItem("Copy",
                           KeyboardBinding.ListKeyboardShortcuts(UserActions.CopyToClipboard, false),
                           selected: false,
                           enabled: someOpsSelected))
        {
            NodeActions.CopySelectedNodesToClipboard(_nodeSelection, compositionOp);
        }

        if (ImGui.MenuItem("Paste", KeyboardBinding.ListKeyboardShortcuts(UserActions.PasteFromClipboard, false)))
        {
            NodeActions.PasteClipboard(_nodeSelection, this, compositionOp);
        }

        var selectedInputUis = _nodeSelection.GetSelectedNodes<IInputUi>().ToList();
        var selectedOutputUis = _nodeSelection.GetSelectedNodes<IOutputUi>().ToList();

        var isSaving = T3Ui.IsCurrentlySaving;

        if (ImGui.MenuItem("Delete",
                           shortcut: "Del", // dynamic assigned shortcut is too long
                           selected: false,
                           enabled: (someOpsSelected || selectedInputUis.Count > 0 || selectedOutputUis.Count > 0) && !isSaving))
        {
            NodeActions.DeleteSelectedElements(_nodeSelection, compositionSymbolUi, selectedChildUis, selectedInputUis, selectedOutputUis);
        }

        if (ImGui.MenuItem("Duplicate",
                           KeyboardBinding.ListKeyboardShortcuts(UserActions.Duplicate, false),
                           selected: false,
                           enabled: selectedChildUis.Count > 0 && !isSaving))
        {
            NodeActions.CopySelectedNodesToClipboard(_nodeSelection, compositionOp);
            NodeActions.PasteClipboard(_nodeSelection, this, compositionOp);
        }

        ImGui.Separator();

        if (ImGui.MenuItem("Replace with...", someOpsSelected && !isSaving))
        {
            var startingSearchString = selectedChildUis[0].SymbolChild.Symbol.Name;
            var position = selectedChildUis.Count == 1 ? selectedChildUis[0].PosOnCanvas : InverseTransformPositionFloat(ImGui.GetMousePos());
            SymbolBrowser.OpenAt(position, null, null, false, startingSearchString,
                                 symbol => { ChangeSymbol.ChangeOperatorSymbol(_nodeSelection, compositionOp, selectedChildUis, symbol); });
        }

        if (ImGui.BeginMenu("Symbol definition...", !isSaving))
        {
            if (ImGui.MenuItem("Rename Symbol", oneOpSelected))
            {
                _renameSymbolDialog.ShowNextFrame();
                _symbolNameForDialogEdits = selectedChildUis[0].SymbolChild.Symbol.Name;
                //NodeOperations.RenameSymbol(selectedChildUis[0].SymbolChild.Symbol, "NewName");
            }

            if (ImGui.MenuItem("Duplicate as new type...", oneOpSelected))
            {
                _symbolNameForDialogEdits = selectedChildUis[0].SymbolChild.Symbol.Name ?? string.Empty;
                _nameSpaceForDialogEdits = selectedChildUis[0].SymbolChild.Symbol.Namespace ?? string.Empty;
                _symbolDescriptionForDialog = "";
                _duplicateSymbolDialog.ShowNextFrame();
            }

            if (ImGui.MenuItem("Combine into new type...", someOpsSelected))
            {
                _nameSpaceForDialogEdits = compositionOp.Symbol.Namespace ?? string.Empty;
                _symbolDescriptionForDialog = "";
                _combineToSymbolDialog.ShowNextFrame();
            }

            ImGui.EndMenu();
        }
        //}

        var symbolPackage = compositionSymbolUi.Symbol.SymbolPackage;
        if (!symbolPackage.IsReadOnly)
        {
            if (ImGui.BeginMenu("Open folder..."))
            {
                if (ImGui.MenuItem("Project"))
                {
                    CoreUi.Instance.OpenWithDefaultApplication(symbolPackage.Folder);
                }

                if (ImGui.MenuItem("Resources"))
                {
                    CoreUi.Instance.OpenWithDefaultApplication(symbolPackage.ResourcesFolder);
                }

                ImGui.EndMenu();
            }
        }

        if (ImGui.BeginMenu("Add..."))
        {
            if (ImGui.MenuItem("Add Node...", "TAB", false, true))
            {
                SymbolBrowser.OpenAt(InverseTransformPositionFloat(clickPosition), null, null, false);
            }

            if (canModify)
            {
                if (ImGui.MenuItem("Add input parameter..."))
                {
                    _addInputDialog.ShowNextFrame();
                }

                if (ImGui.MenuItem("Add output..."))
                {
                    _addOutputDialog.ShowNextFrame();
                }
            }

            if (ImGui.MenuItem("Add Annotation",
                               shortcut: KeyboardBinding.ListKeyboardShortcuts(UserActions.AddAnnotation, false),
                               selected: false,
                               enabled: true))
            {
                var newAnnotation = NodeActions.AddAnnotation(_nodeSelection, this, compositionOp);
                _graph.RenameAnnotation(newAnnotation);
            }

            ImGui.EndMenu();
        }

        ImGui.Separator();

        if (ImGui.MenuItem("Export as Executable", oneOpSelected))
        {
            switch (PlayerExporter.TryExportInstance(compositionOp, selectedChildUis.Single(), out var reason, out var exportDir))
            {
                case false:
                    Log.Error(reason);
                    BlockingWindow.Instance.ShowMessageBox(reason, $"Failed to export {label}");
                    break;
                default:
                    Log.Info(reason);
                    BlockingWindow.Instance.ShowMessageBox(reason, $"Exported {label} successfully!");
                    // open export directory in native file explorer
                    CoreUi.Instance.OpenWithDefaultApplication(exportDir);
                    break;
            }
        }

        if (oneOpSelected)
        {
            var symbol = selectedChildUis.Single().SymbolChild.Symbol;
            CustomComponents.DrawSymbolCodeContextMenuItem(symbol);
            var childUi = selectedChildUis.Single();

            // get instance that is currently selected
            var instance = compositionOp.Children[childUi.Id];

            if (NodeActions.TryGetShaderPath(instance, out var filePath, out var owner))
            {
                var shaderIsReadOnly = owner.IsReadOnly;

                if (ImGui.MenuItem("Open in Shader Editor", true))
                {
                    if (shaderIsReadOnly)
                    {
                        CopyToTempShaderPath(filePath, out filePath);
                        BlockingWindow.Instance.ShowMessageBox("Warning - viewing a read-only shader. Modifications will not be saved.\n" +
                                                               "Following #include directives outside of the temp folder may lead you to read-only files, " +
                                                               "and editing those can break operators.\n\nWith great power...", "Warning");
                    }

                    EditorUi.Instance.OpenWithDefaultApplication(filePath);
                }
            }
        }
    }

    private static void CopyToTempShaderPath(string filePath, out string newFilePath)
    {
        var directory = Path.GetDirectoryName(filePath)!;
        var destinationDirectory = Path.Combine(FileLocations.TempFolder, "ReadOnlyShaders");

        if (Directory.Exists(destinationDirectory))
        {
            try
            {
                Directory.Delete(destinationDirectory, true);
            }
            catch (Exception e)
            {
                Log.Warning($"Failed to delete temp directory: {e}");
            }
        }

        var directoryInfo = Directory.CreateDirectory(destinationDirectory);

        // copy all files in directory to temp directory for intellisense to work
        var allFilesInDirectory = Directory.EnumerateFiles(directory);
        foreach (var file in allFilesInDirectory)
        {
            var destinationPath = Path.Combine(destinationDirectory, Path.GetFileName(file));
            File.Copy(file, destinationPath);
        }

        ShaderLinter.AddPackage(new ShaderCompiler.ShaderResourcePackage(directoryInfo), ResourceManager.SharedShaderPackages,
                                replaceExisting: true);
        newFilePath = Path.Combine(destinationDirectory, Path.GetFileName(filePath));
    }

    private void DrawGrid(ImDrawListPtr drawList)
    {
        var gridSize = Math.Abs(64.0f * Scale.X);
        for (var x = (-Scroll.X * Scale.X) % gridSize; x < WindowSize.X; x += gridSize)
        {
            drawList.AddLine(new Vector2(x, 0.0f) + WindowPos,
                             new Vector2(x, WindowSize.Y) + WindowPos, UiColors.CanvasGrid);
        }

        for (var y = (-Scroll.Y * Scale.Y) % gridSize; y < WindowSize.Y; y += gridSize)
        {
            drawList.AddLine(
                             new Vector2(0.0f, y) + WindowPos,
                             new Vector2(WindowSize.X, y) + WindowPos, UiColors.CanvasGrid);
        }
    }

    private IEnumerable<ISelectableCanvasObject> SelectableChildren => _projectView?.CompositionInstance != null 
                                                                           ? NodeSelection.GetSelectableChildren(_projectView.CompositionInstance)
                                                                           : []; 

    //private readonly List<ISelectableCanvasObject> _selectableItems = new();
    #endregion

    #region public API
    bool IGraphCanvas.Destroyed { get; set; }

    public void OpenAndFocusInstance(IReadOnlyList<Guid> path)
    {
        if (path.Count == 1)
        {
            _projectView.TrySetCompositionOp(path, ICanvas.Transition.JumpOut, path[0]);
            return;
        }

        var compositionPath = path.Take(path.Count - 1).ToList();
        _projectView.TrySetCompositionOp(compositionPath, ICanvas.Transition.JumpIn, path[^1]);
    }
    #endregion

    private bool _contextMenuIsOpen;
    private bool _preventInteractions;
    private GraphDrawingFlags _drawingFlags;

    private readonly SelectionFence _selectionFence = new();
    private readonly AddInputDialog _addInputDialog = new();
    private readonly AddOutputDialog _addOutputDialog = new();
    private readonly CombineToSymbolDialog _combineToSymbolDialog = new();
    private readonly DuplicateSymbolDialog _duplicateSymbolDialog = new();
    private readonly RenameSymbolDialog _renameSymbolDialog = new();
    public readonly EditNodeOutputDialog EditNodeOutputDialog = new();
    public readonly EditCommentDialog EditCommentDialog = new();
    public readonly LibWarningDialog LibWarningDialog = new();

    private string _symbolNameForDialogEdits = "";
    private string _symbolDescriptionForDialog = "";
    private string _nameSpaceForDialogEdits = "";
    private Vector2 _dampedScrollVelocity = Vector2.Zero;

    protected override ScalableCanvas Parent => null;
    public SelectableNodeMovement SelectableNodeMovement { get; }
}