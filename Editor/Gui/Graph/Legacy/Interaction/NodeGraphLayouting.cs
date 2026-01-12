using SharpDX.Direct3D11;
using T3.Core.Operator;
using T3.Editor.Gui.UiHelpers;
using T3.Editor.UiModel;
using T3.Editor.UiModel.Commands;
using T3.Editor.UiModel.Commands.Graph;
using T3.Editor.UiModel.ProjectHandling;
using T3.Editor.UiModel.Selection;

namespace T3.Editor.Gui.Legacy.Interaction;

internal sealed class NodeGraphLayouting
{
    private readonly NodeSelection _nodeSelection;
    private readonly Structure _structure;
    public NodeGraphLayouting(NodeSelection nodeSelection, Structure structure)
    {
        _nodeSelection = nodeSelection;
        _structure = structure;
    }
    
    public void ArrangeOps(Instance composition)
    {
        var commands = new List<ICommand>();
        var compositionSymbolUi = composition.GetSymbolUi();

        foreach (var n in _nodeSelection.GetSelectedChildUis().ToList())
        {
            //var xxx = NodeOperations.CollectSlotDependencies(n)
            var connectedChildren = Structure.CollectConnectedChildren(n.SymbolChild, composition.Symbol);

            // First pass is rough layout
            var nodesForSecondPass = new Dictionary<ISelectableCanvasObject, int>();
            RecursivelyAlignChildren(n, connectedChildren, ref commands, ref nodesForSecondPass, composition);

            var minX = float.MaxValue;
            foreach (var (secondPassOp, depthForOp) in nodesForSecondPass)
            {
                if (secondPassOp is not SymbolUi.Child childUi)
                    continue;

                childUi.PosOnCanvas += new Vector2(0, +40);
                var morePasses = new Dictionary<ISelectableCanvasObject, int>();

                var pos = Vector2.Zero;
                var compositionOpSymbol = composition.Symbol;
                var connectedTargetsIds = compositionOpSymbol.Connections
                                                             .Where(ccc => ccc.SourceParentOrChildId == secondPassOp.Id
                                                                           && ccc.TargetParentOrChildId != childUi.Id)
                                                             .Select(ccc => ccc.TargetParentOrChildId).ToList();
                foreach (var id in connectedTargetsIds)
                {
                    if (compositionSymbolUi.ChildUis.TryGetValue(id, out var connectedOp))
                    {
                        minX = MathF.Min(minX, connectedOp.PosOnCanvas.X);
                    }

                    var p = childUi.PosOnCanvas;
                    p.X = minX - SelectableNodeMovement.PaddedDefaultOpSize.X;
                    childUi.PosOnCanvas = p;
                }

                RecursivelyAlignChildren(childUi, connectedChildren, ref commands, ref morePasses, composition);
            }
        }

        var command = new MacroCommand("arrange", commands);
        UndoRedoStack.Add(command);
    }

    private float RecursivelyAlignChildren(SymbolUi.Child childUi,
                                                  HashSet<Guid> connectedChildIds,
                                                  ref List<ICommand> moveCommands,
                                                  ref Dictionary<ISelectableCanvasObject, int> nodesForSecondPass,
                                                  Instance composition,
                                                  int depth = 0,
                                                  List<ISelectableCanvasObject> sortedIn = null)
    {
        sortedIn ??= new List<ISelectableCanvasObject>();
        nodesForSecondPass ??= new Dictionary<ISelectableCanvasObject, int>();

        var parentUi = composition.GetSymbolUi();
        var parentSymbol = composition.Symbol;
        var connectedChildUis = (from con in parentSymbol.Connections
                                 where !con.IsConnectedToSymbolInput && !con.IsConnectedToSymbolOutput
                                 from sourceChildUi in parentUi.ChildUis.Values
                                 where con.SourceParentOrChildId == sourceChildUi.Id
                                       && con.TargetParentOrChildId == childUi.Id
                                 select sourceChildUi).Distinct().ToArray();

        // Order connections by input definition order
        var connections = (from con in parentSymbol.Connections
                           where !con.IsConnectedToSymbolInput && !con.IsConnectedToSymbolOutput
                           from sourceChildUi in parentUi.ChildUis.Values
                           where con.SourceParentOrChildId == sourceChildUi.Id
                                 && con.TargetParentOrChildId == childUi.Id
                           select con).Distinct().ToArray();

        // Sort the incoming operators into the correct input order and
        // ignore operators that can't be auto-layouted because their outputs
        // have connection to multiple operators
        var sortedInputOps = new List<SymbolUi.Child>();
        foreach (var inputDef in childUi.SymbolChild.Symbol.InputDefinitions)
        {
            var matchingConnections = connections.Where(c => c.TargetSlotId == inputDef.Id).ToArray();
            var connectedOpsForInput = matchingConnections.SelectMany(c => connectedChildUis.Where(ccc => ccc.Id == c.SourceParentOrChildId));

            foreach (var op in connectedOpsForInput)
            {
                // var outputsWithForkingConnections = compositionSymbol.Connections
                //                                              .Where(c5 => c5.SourceParentOrChildId == op.Id
                //                                                                       && c5.TargetParentOrChildId != childUi.Id);

                var count = parentSymbol.Connections
                                             .Where(ccc => ccc.SourceParentOrChildId == op.Id
                                                           && ccc.TargetParentOrChildId != childUi.Id)
                                             .Count(ccc => connectedChildIds.Contains(ccc.TargetParentOrChildId));

                sortedInputOps.Add(op);
                if (count > 0)
                {
                    if (nodesForSecondPass.TryGetValue(op, out var lastDepth))
                    {
                        nodesForSecondPass[op] = Math.Max(lastDepth, depth);
                    }
                    else
                    {
                        nodesForSecondPass[op] = depth;
                    }
                }
            }
        }

        float verticalOffset = 0;
        var snappedCount = 0;
        foreach (var connectedChildUi in sortedInputOps)
        {
            if (sortedIn.Contains(connectedChildUi))
                continue;

            var thumbnailPadding = HasThumbnail(connectedChildUi) ? connectedChildUi.Size.X : 0;
            if (snappedCount > 0)
                verticalOffset += thumbnailPadding;

            var moveCommand = new ModifyCanvasElementsCommand(parentUi, [connectedChildUi], _nodeSelection);
            connectedChildUi.PosOnCanvas = childUi.PosOnCanvas + new Vector2(-(childUi.Size.X + SelectableNodeMovement.SnapPadding.X), verticalOffset);
            moveCommand.StoreCurrentValues();
            moveCommands.Add(moveCommand);

            sortedIn.Add(connectedChildUi);
            verticalOffset += RecursivelyAlignChildren(connectedChildUi, connectedChildIds, ref moveCommands, ref nodesForSecondPass, composition, depth + 1,
                                                       sortedIn: sortedIn);

            sortedIn.Add(connectedChildUi);
            //NodeSelection.AddSelection(connectedChildUi);

            if (composition.Children.TryGetChildInstance(connectedChildUi.Id, out var instance))
            {
                _nodeSelection.AddSelection(connectedChildUi, instance);
            }

            snappedCount++;
        }

        var requiredHeight = Math.Max(verticalOffset, childUi.Size.Y);
        return requiredHeight + SelectableNodeMovement.SnapPadding.Y;
    }

    private static bool HasThumbnail(SymbolUi.Child childUi)
    {
        return childUi.SymbolChild.Symbol.OutputDefinitions.Count > 0 && childUi.SymbolChild.Symbol.OutputDefinitions[0].ValueType == typeof(Texture2D);
    }

    public static Vector2 FindPositionForNodeConnectedToInput(SymbolUi symbolUi, SymbolUi.Child connectionTargetUi)
    {
        var idealPos = connectionTargetUi.PosOnCanvas 
                       + new Vector2(-SelectableNodeMovement.PaddedDefaultOpSize.X, 0);

        //var symbolUi = compositionSymbolUi.GetSymbolUi();
        var interferingOps = symbolUi.ChildUis.Values
                                     .Where(op =>
                                                op.PosOnCanvas.Y >= idealPos.Y
                                                && op.PosOnCanvas.X + op.Size.X >= idealPos.X
                                                && op.PosOnCanvas.X <= idealPos.X + SelectableNodeMovement.PaddedDefaultOpSize.X)
                                     .OrderBy(op => op.PosOnCanvas.Y).ToList();

        var needsMoreChecks = true;
        while (needsMoreChecks)
        {
            needsMoreChecks = false;
            foreach (var op in interferingOps)
            {
                var idealArea = ImRect.RectWithSize(idealPos, SelectableNodeMovement.PaddedDefaultOpSize);
                var opArea = ImRect.RectWithSize(op.PosOnCanvas, op.Size);
                if (idealArea.Overlaps(opArea))
                {
                    idealPos.Y = opArea.Max.Y + SelectableNodeMovement.SnapPadding.Y;
                    needsMoreChecks = true;
                }
            }
        }
        
        return idealPos;
    }
}