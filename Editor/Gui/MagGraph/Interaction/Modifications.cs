using T3.Core.Operator;
using T3.Editor.Gui.MagGraph.Model;
using T3.Editor.Gui.MagGraph.States;
using T3.Editor.Gui.OutputUi;
using T3.Editor.UiModel;
using T3.Editor.UiModel.Commands;
using T3.Editor.UiModel.Commands.Annotations;
using T3.Editor.UiModel.Commands.Graph;
using T3.Editor.UiModel.InputsAndTypes;
using T3.Editor.UiModel.Modification;
using T3.Editor.UiModel.Selection;

namespace T3.Editor.Gui.MagGraph.Interaction;

internal static class Modifications
{

    internal static ChangeSymbol.SymbolModificationResults AlignSelectionToLeft(GraphUiContext context)
    {
        var results = ChangeSymbol.SymbolModificationResults.Nothing;
        
        if (context.Selector.Selection.Count == 0)
            return results;

        if(!SymbolUiRegistry.TryGetSymbolUi(context.CompositionInstance.Symbol.Id, out var compositionUi))
        {
            Log.Warning("Can't find composition ui?");
            return results;
        }

        var minX = float.PositiveInfinity;
        foreach (var i in context.Selector.Selection)
        {
            minX = MathF.Min(minX, i.PosOnCanvas.X);
        }
        
        var moveCommand = new ModifyCanvasElementsCommand(compositionUi.Symbol.Id, context.Selector.Selection, context.Selector );
        
        foreach (var i in context.Selector.Selection)
        {
            var pos = i.PosOnCanvas;
            pos.X = minX;
            i.PosOnCanvas= pos;
        }
        
        moveCommand.StoreCurrentValues();
        UndoRedoStack.Add(moveCommand);
        return ChangeSymbol.SymbolModificationResults.Nothing;
    }
    
    
    /// <summary>
    /// Deletes the selected items and tries to collapse the gaps and patches the connection gaps is possible
    /// </summary>
    /// <param name="context"></param>
    internal static ChangeSymbol.SymbolModificationResults DeleteSelection(GraphUiContext context)
    {
        var results = ChangeSymbol.SymbolModificationResults.Nothing;
        
        if (context.Selector.Selection.Count == 0)
            return results;

        if(!SymbolUiRegistry.TryGetSymbolUi(context.CompositionInstance.Symbol.Id, out var compositionUi))
        {
            Log.Warning("Can't find composition ui?");
            return results;
        }

        var deletedItems = new List<MagGraphItem>();
        var deletedChildUis = new List<SymbolUi.Child>();
        var deletedInputUis = new List<IInputUi>();
        var deletedOutputUis = new List<IOutputUi>();
        var deletedAnnotations = new List<MagGraphAnnotation>();
        
        foreach (var s in context.Selector.Selection)
        {
            if (context.Layout.Items.TryGetValue(s.Id, out var item))
            {
                if (item.Variant != MagGraphItem.Variants.Operator)
                {
                    if (compositionUi.Symbol.SymbolPackage.IsReadOnly)
                    {
                        Log.Warning("Can't delete inputs or outputs from a read only symbol.");
                        continue;
                    }

                    if (item.Variant == MagGraphItem.Variants.Input)
                    {
                        deletedInputUis.Add(item.Selectable as IInputUi);
                    }
                    else if (item.Variant == MagGraphItem.Variants.Output)
                    {
                        deletedOutputUis.Add(item.Selectable as IOutputUi);
                    }

                    continue;
                }

                if (!compositionUi.ChildUis.TryGetValue(item.Id, out var childUi))
                {
                    Log.Warning("Can't find symbol child for " + item);
                    continue;
                }

                //uiItems.Add(new ItemWithChildUi(item, childUi));
                deletedItems.Add(item);
                deletedChildUis.Add(childUi);
            }
            else if (s is Annotation annotation)
            {
                if (!context.Layout.Annotations.TryGetValue(s.Id, out var mga))
                {
                    Log.Warning("Can't find annotation: " + s.Id);
                    continue;
                }

                deletedAnnotations.Add(mga);
            }
            else 
            {
                Log.Warning("Can't find selectable item " + s);
                continue;
            }
        }

        // find obsolete connections...
        var obsoleteConnections = new HashSet<MagGraphConnection>();
        foreach (var i in deletedItems)
        {
            foreach (var inLine in i.InputLines)
            {
                if (inLine.ConnectionIn != null)
                    obsoleteConnections.Add(inLine.ConnectionIn);
            }

            foreach (var outLine in i.OutputLines)
            {
                obsoleteConnections.UnionWith(outLine.ConnectionsOut);
            }
        }
        
        if (deletedChildUis.Count == 0 && deletedInputUis.Count == 0 && deletedOutputUis.Count == 0
            && deletedAnnotations.Count==0)
            return results;

        var macroCommand = new MacroCommand("Delete items");
        if (deletedChildUis.Count > 0)
        {
            var collapsableConnectionPairs= MagItemMovement.FindLinkedVerticalCollapsableConnectionPairs(obsoleteConnections.ToList());

            // Delete items...
            macroCommand.AddAndExecCommand(new DeleteSymbolChildrenCommand(compositionUi, deletedChildUis));

            // Collapse vertical gaps
            var relevantItems = MagItemMovement.CollectSnappedItems(deletedItems);
            var movableItems = new HashSet<MagGraphItem>(relevantItems.Except(deletedItems));

            foreach (var pair in collapsableConnectionPairs)
            {
                var mci = pair.Ca;
                var mco = pair.Cb;
                var affectedItems = MagItemMovement.MoveToCollapseVerticalGaps(mci, mco, movableItems, true);
                if (affectedItems.Count == 0)
                    continue;
                
                var affectedItemsAsNodes = movableItems.Select(i => i as ISelectableCanvasObject).ToList();
                var newMoveCommand = new ModifyCanvasElementsCommand(context.CompositionInstance.Symbol.Id, affectedItemsAsNodes, context.Selector);
                macroCommand.AddExecutedCommandForUndo(newMoveCommand);
            
                MagItemMovement.MoveToCollapseVerticalGaps(mci, mco, movableItems, dryRun:false);
                
                newMoveCommand.StoreCurrentValues();
            
                macroCommand.AddAndExecCommand(new AddConnectionCommand(context.CompositionInstance.Symbol,
                                                                        new Symbol.Connection(mci.SourceItem.Id,
                                                                                                  mci.SourceOutput.Id,
                                                                                                  mco.TargetItem.Id,
                                                                                                  mco.TargetInput.Id),
                                                                        0));                            
            }
        }


        if (deletedInputUis.Count > 0 || deletedOutputUis.Count > 0)
        {
            InputsAndOutputs.RemoveInputsAndOutputsFromSymbol(inputIdsToRemove: deletedInputUis.Select(entry => entry.Id).ToArray(),
                                                              outputIdsToRemove: deletedOutputUis.Select(entry => entry.Id).ToArray(),
                                                              symbol: compositionUi.Symbol);
        }

        if (deletedAnnotations.Count > 0)
        {
            foreach (var a in deletedAnnotations)
            {
                macroCommand.AddAndExecCommand(new DeleteAnnotationCommand(compositionUi, a.Annotation));
            }
        }

        UndoRedoStack.Add(macroCommand);
        
        context.Layout.FlagStructureAsChanged();
        context.Selector.Clear();
        context.Layout.FlagStructureAsChanged();
        return results;
    }

    //private record ItemWithChildUi(MagGraphItem Item, SymbolUi.Child UiChild);
}