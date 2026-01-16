using ImGuiNET;
using T3.Core.DataTypes.Vector;
using T3.Core.Operator;
using T3.Editor.Gui.Interaction;
using T3.Editor.Gui.Legacy.Interaction.Connections;
using T3.Editor.Gui.UiHelpers;
using T3.Editor.UiModel;
using T3.Editor.UiModel.Commands;
using T3.Editor.UiModel.Commands.Graph;
using T3.Editor.UiModel.Modification;
using T3.Editor.UiModel.ProjectHandling;
using T3.Editor.UiModel.Selection;
using Vector2 = System.Numerics.Vector2;

namespace T3.Editor.Gui.Legacy.Interaction;

/// <summary>
/// Handles selection and dragging (with snapping) of node canvas elements
/// </summary>
internal sealed class SelectableNodeMovement(IGraphView graphView, Func<Instance> getCompositionOp, Func<IEnumerable<ISelectableCanvasObject>> getSelectableChildren, NodeSelection selection)
{
    /// <summary>
    /// Reset to avoid accidental dragging of previous elements 
    /// </summary>
    public void Reset()
    {
        _moveCommand = null;
        _draggedNodes.Clear();
    }

    /// <summary>
    /// For certain edge cases the release handling of nodes cannot be detected.
    /// This is a work around to clear the state on mouse release
    /// </summary>
    public static void CompleteFrame()
    {
        if (ProjectView.Focused?.GraphView is not GraphView canvas)
            return;
        
        canvas.SelectableNodeMovement.DoCompleteFrame();

        // Due to refactoring we only call this for the active graph.
        // We need to evaluate if this is sufficient.
        // foreach (var graphWindow in GraphWindow.GraphWindowInstances)
        // {
        //     graphWindow.GraphCanvas.SelectableNodeMovement.OnCompleteFrame();
        // }
    }

    private void DoCompleteFrame()
    {
        if (ImGui.IsMouseReleased(0) && _moveCommand != null)
        {
            Reset();
        }
    }

    /// <summary>
    /// NOTE: This has to be called for ALL movable elements (ops, inputs, outputs) and directly after ImGui.Item
    /// </summary>
    public void Handle(ISelectableCanvasObject node, Instance instance = null)
    {

        var composition = getCompositionOp();
        var justClicked = ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenBlockedByPopup) && ImGui.IsMouseClicked(ImGuiMouseButton.Left);
            
        var isActiveNode = node.Id == _draggedNodeId;
        if (justClicked)
        {
            _draggedNodeId = node.Id;
            var parentUi = composition.GetSymbolUi();
            if (selection.IsNodeSelected(node))
            {
                _draggedNodes = selection.GetSelectedNodes<ISelectableCanvasObject>().ToList();
            }
            else
            {
                if(UserSettings.Config.SmartGroupDragging)
                    _draggedNodes = FindSnappedNeighbours(node).ToList();
                    
                _draggedNodes.Add(node);
            }

            _moveCommand = new ModifyCanvasElementsCommand(parentUi, _draggedNodes, selection);
            _shakeDetector.ResetShaking();
        }
        else if (isActiveNode && ImGui.IsMouseDown(ImGuiMouseButton.Left) && _moveCommand != null)
        {
            if (!T3Ui.IsCurrentlySaving && _shakeDetector.TestDragForShake(ImGui.GetMousePos()))
            {
                _moveCommand.StoreCurrentValues();
                UndoRedoStack.Add(_moveCommand);
                NodeActions.DisconnectDraggedNodes(getCompositionOp(), _draggedNodes);
            }
            HandleNodeDragging(node);
        }
        else if (isActiveNode && ImGui.IsMouseReleased(0) && _moveCommand != null)
        {
            if (_draggedNodeId != node.Id)
                return;
                
            var singleDraggedNode = (_draggedNodes.Count == 1) ? _draggedNodes[0] : null;
            _draggedNodeId = Guid.Empty;
            _draggedNodes.Clear();

            var wasDragging = ImGui.GetMouseDragDelta(ImGuiMouseButton.Left).LengthSquared() > UserSettings.Config.ClickThreshold;
            if (wasDragging)
            {
                _moveCommand.StoreCurrentValues();

                if (singleDraggedNode != null && ConnectionSplitHelper.BestMatchLastFrame != null && singleDraggedNode is SymbolUi.Child childUi)
                {
                    var instanceForUiChild = composition.Children[childUi.Id];
                    ConnectionMaker.SplitConnectionWithDraggedNode(graphView, 
                                                                   childUi, 
                                                                   ConnectionSplitHelper.BestMatchLastFrame.Connection, 
                                                                   instanceForUiChild,
                                                                   _moveCommand, selection);
                    _moveCommand = null;
                }
                else
                {
                    UndoRedoStack.Add(_moveCommand);
                }

                // // Reorder inputs nodes if dragged
                // var selectedInputs = NodeSelection.GetSelectedNodes<IInputUi>().ToList();
                // if (selectedInputs.Count > 0)
                // {
                //     var composition = GraphCanvas.Current.CompositionOp;
                //     var compositionUi = SymbolUiRegistry.Entries[composition.Symbol.Id];
                //     composition.Symbol.InputDefinitions.Sort((a, b) =>
                //                                              {
                //                                                  var childA = compositionUi.InputUis[a.Id];
                //                                                  var childB = compositionUi.InputUis[b.Id];
                //                                                  return (int)(childA.PosOnCanvas.Y * 10000 + childA.PosOnCanvas.X) -
                //                                                         (int)(childB.PosOnCanvas.Y * 10000 + childB.PosOnCanvas.X);
                //                                              });
                //     composition.Symbol.SortInputSlotsByDefinitionOrder();
                //     InputsAndOutputs.AdjustInputOrderOfSymbol(composition.Symbol);
                // }
            }
            else
            {
                if (!selection.IsNodeSelected(node))
                {
                    var replaceSelection = !ImGui.GetIO().KeyShift;
                    if (replaceSelection)
                    {
                        if (node is SymbolUi.Child childUi3)
                        {
                            selection.SetSelection(childUi3, instance);
                        }
                        else
                        {
                            selection.SetSelection(node);
                        }
                    }
                    else
                    {
                        if (node is SymbolUi.Child childUi2)
                        {
                            selection.AddSelection(childUi2, instance);
                        }
                        else
                        {
                            selection.AddSelection(node);
                        }
                    }
                }
                else
                {
                    if (ImGui.GetIO().KeyShift)
                    {
                        selection.DeselectNode(node, instance);
                    }
                }
            }

            _moveCommand = null;
        }
        else if (ImGui.IsMouseReleased(0) && _moveCommand == null)
        {
            // This happens after shake
            _draggedNodes.Clear();
        }


        var wasDraggingRight = ImGui.GetMouseDragDelta(ImGuiMouseButton.Right).Length() > UserSettings.Config.ClickThreshold;
        if (ImGui.IsMouseReleased(ImGuiMouseButton.Right)
            && !wasDraggingRight
            && ImGui.IsItemHovered()
            && !selection.IsNodeSelected(node))
        {
            if (node is SymbolUi.Child childUi2)
            {
                selection.SetSelection(childUi2, instance);
            }
            else
            {
                selection.SetSelection(node);
            }
        }
    }

    private void HandleNodeDragging(ISelectableCanvasObject draggedNode)
    {
            
        if (!ImGui.IsMouseDragging(ImGuiMouseButton.Left))
        {
            _isDragging = false;
            return;
        }

        if (!_isDragging)
        {
            _dragStartPosInOpOnCanvas =  graphView.Canvas.InverseTransformPositionFloat(ImGui.GetMousePos()) - draggedNode.PosOnCanvas;
            _isDragging = true;
        }


        var mousePosOnCanvas = graphView.Canvas.InverseTransformPositionFloat(ImGui.GetMousePos());
        var newDragPosInCanvas = mousePosOnCanvas - _dragStartPosInOpOnCanvas;

        var bestDistanceInCanvas = float.PositiveInfinity;
        var targetSnapPositionInCanvas = Vector2.Zero;

        foreach (var offset in _snapOffsetsInCanvas)
        {
            var heightAffectFactor = 0;
            if (Math.Abs(offset.X) < 0.01f)
            {
                if (offset.Y > 0)
                {
                    heightAffectFactor = -1;
                }
                else
                {
                    heightAffectFactor = 1;
                }
            }

            foreach (var neighbor in getSelectableChildren())
            {
                if (neighbor == draggedNode || _draggedNodes.Contains(neighbor))
                    continue;

                var offset2 = new Vector2(offset.X, -neighbor.Size.Y * heightAffectFactor + offset.Y);
                var snapToNeighborPos = neighbor.PosOnCanvas + offset2;

                var d = Vector2.Distance(snapToNeighborPos, newDragPosInCanvas);
                if (!(d < bestDistanceInCanvas))
                    continue;

                targetSnapPositionInCanvas = snapToNeighborPos;
                bestDistanceInCanvas = d;
            }
        }

        var snapDistanceInCanvas = graphView.Canvas.InverseTransformDirection(new Vector2(20, 0)).X;
        var isSnapping = bestDistanceInCanvas < snapDistanceInCanvas;

        var moveDeltaOnCanvas = isSnapping
                                    ? targetSnapPositionInCanvas - draggedNode.PosOnCanvas
                                    : newDragPosInCanvas - draggedNode.PosOnCanvas;

        // Drag selection
        foreach (var e in _draggedNodes)
        {
            e.PosOnCanvas += moveDeltaOnCanvas;
        }
    }

    public void HighlightSnappedNeighbours(SymbolUi.Child childUi)
    {
        if (selection.IsNodeSelected(childUi))
            return;

        var drawList = ImGui.GetWindowDrawList();
        var parentUi = getCompositionOp().GetSymbolUi();
        var snappedNeighbours = FindSnappedNeighbours(childUi);

        var color = new Color(1f, 1f, 1f, 0.08f);
        snappedNeighbours.Add(childUi);
        var expandOnScreen = graphView.Canvas.TransformDirection(SelectableNodeMovement.SnapPadding).X / 2;

        foreach (var snappedNeighbour in snappedNeighbours)
        {
            var areaOnCanvas = new ImRect(snappedNeighbour.PosOnCanvas, snappedNeighbour.PosOnCanvas + snappedNeighbour.Size);
            var areaOnScreen = graphView.Canvas.TransformRect(areaOnCanvas);
            areaOnScreen.Expand(expandOnScreen);
            drawList.AddRect(areaOnScreen.Min, areaOnScreen.Max, color, rounding: 10, ImDrawFlags.RoundCornersAll, thickness: 4);
        }
    }

    public List<ISelectableCanvasObject> FindSnappedNeighbours(ISelectableCanvasObject childUi)
    {
        //var pool = new HashSet<ISelectableNode>(parentUi.ChildUis);
        var pool = new HashSet<ISelectableCanvasObject>(getSelectableChildren());
        pool.Remove(childUi);
        return RecursivelyFindSnappedInPool(childUi, pool).Distinct().ToList();
    }

    public static List<ISelectableCanvasObject> RecursivelyFindSnappedInPool(ISelectableCanvasObject childUi, HashSet<ISelectableCanvasObject> snapCandidates)
    {
        var snappedChildren = new List<ISelectableCanvasObject>();

        foreach (var candidate in snapCandidates.ToList())
        {
            if (AreChildUisSnapped(childUi, candidate) == Alignment.NoSnapped)
                continue;

            snappedChildren.Add(candidate);
            snapCandidates.Remove(candidate);
            snappedChildren.AddRange(RecursivelyFindSnappedInPool(candidate, snapCandidates));
        }

        return snappedChildren;
    }

    public const float Tolerance = 0.1f;

    private static Alignment AreChildUisSnapped(ISelectableCanvasObject childA, ISelectableCanvasObject childB)
    {
        var alignedHorizontally = Math.Abs(childA.PosOnCanvas.Y - childB.PosOnCanvas.Y) < Tolerance;
        var alignedVertically = Math.Abs(childA.PosOnCanvas.X - childB.PosOnCanvas.X) < Tolerance;

        if (alignedVertically)
        {
            if (Math.Abs(childA.PosOnCanvas.Y + childA.Size.Y + SelectableNodeMovement.SnapPadding.Y
                         - childB.PosOnCanvas.Y) < Tolerance)
            {
                return Alignment.SnappedBelow;
            }

            if (Math.Abs(childB.PosOnCanvas.Y + childB.Size.Y + SelectableNodeMovement.SnapPadding.Y
                         - childA.PosOnCanvas.Y) < Tolerance)
            {
                return Alignment.SnappedAbove;
            }
        }

        if (alignedHorizontally)
        {
            if (Math.Abs(childA.PosOnCanvas.X + childA.Size.X + SelectableNodeMovement.SnapPadding.X
                         - childB.PosOnCanvas.X) < Tolerance)
            {
                return Alignment.SnappedToRight;
            }

            if (Math.Abs(childB.PosOnCanvas.X + childB.Size.X + SelectableNodeMovement.SnapPadding.X
                         - childA.PosOnCanvas.X) < Tolerance)
            {
                return Alignment.SnappedToLeft;
            }
        }

        return Alignment.NoSnapped;
    }

    private enum Alignment
    {
        NoSnapped,
        SnappedBelow,
        SnappedAbove,
        SnappedToRight,
        SnappedToLeft,
    }

    public static readonly Vector2 SnapPadding = new(40, 20);
    public static readonly Vector2 PaddedDefaultOpSize = SymbolUi.Child.DefaultOpSize + SnapPadding;

    private static readonly Vector2[] _snapOffsetsInCanvas =
        {
            new(SymbolUi.Child.DefaultOpSize.X + SnapPadding.X, 0),
            new(-SymbolUi.Child.DefaultOpSize.X - +SnapPadding.X, 0),
            new(0, SnapPadding.Y),
            new(0, -SnapPadding.Y)
        };

    private bool _isDragging;
    private Vector2 _dragStartPosInOpOnCanvas;

    private ModifyCanvasElementsCommand _moveCommand;
    private readonly ShakeDetector _shakeDetector = new();

    private Guid _draggedNodeId = Guid.Empty;
    private List<ISelectableCanvasObject> _draggedNodes = new();
}