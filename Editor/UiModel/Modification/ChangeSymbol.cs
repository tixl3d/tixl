using T3.Core.Operator;
using T3.Editor.UiModel.Commands;
using T3.Editor.UiModel.Commands.Graph;
using T3.Editor.UiModel.Selection;

namespace T3.Editor.UiModel.Modification;

/// <summary>
/// Replace a symbol with another symbol.
/// </summary>
/// <remarks>
/// Maybe this method has to be refactored or deprecated.
/// </remarks>
internal static class ChangeSymbol
{
    public static void ChangeOperatorSymbol(NodeSelection nodeSelection, Instance compositionOp, List<SymbolUi.Child> selectedChildUis, Symbol symbol)
    {
        var nextSelection = new List<Symbol.Child>();

        var executedCommands = new List<ICommand>();

        foreach (var sel in selectedChildUis)
        {
            var result = ChangeOperatorSymbol(sel, symbol, executedCommands, nodeSelection);
            if (result != null)
                nextSelection.Add(result);
        }

        if(nextSelection.Count > 0)
            UndoRedoStack.Add(new MacroCommand(nextSelection.Count == 1 ? "Change Symbol" : "Change Symbols ("+ nextSelection.Count + ")", executedCommands));

        nodeSelection.Clear();
        nextSelection.ForEach(symbolChild =>
                              {
                                  var childUi = symbolChild.GetChildUi();
                                  var instance = compositionOp.Children[symbolChild.Id];
                                  nodeSelection.AddSelection(childUi, instance);
                              });
    }

    private static Symbol.Child ChangeOperatorSymbol(SymbolUi.Child symbolChildUi, Symbol newSymbol, List<ICommand> executedCommands, NodeSelection selection)
    {
        var symbolChild = symbolChildUi.SymbolChild;
        if(symbolChild.Symbol == newSymbol)
            return null;

        var orgPos = symbolChildUi.PosOnCanvas;
        var parentSymbol = symbolChild.Parent;
        var parentSymbolPackage = (EditorSymbolPackage) parentSymbol.SymbolPackage;
        
        if(!parentSymbolPackage.TryGetSymbolUi(parentSymbol.Id, out var parentSymbolUi))
            throw new Exception($"Can't find symbol ui for symbol {parentSymbol.Id}");
        
        var conversionWasLossy = false;

        // move old SymbolChild to new position
        var moveCmd = new ModifyCanvasElementsCommand(parentSymbolUi, new List<ISelectableCanvasObject>() { symbolChildUi }, selection);
        symbolChildUi.PosOnCanvas = orgPos + new Vector2(0, 100);
        moveCmd.StoreCurrentValues();
        moveCmd.Do();
        executedCommands.Add(moveCmd);

        // create new SymbolChild at original position
        var addSymbolChildCommand = new AddSymbolChildCommand(parentSymbolUi.Symbol, newSymbol.Id) { PosOnCanvas = orgPos, ChildName = symbolChild.Name };
        addSymbolChildCommand.Do();
        executedCommands.Add(addSymbolChildCommand);

        var newSymbolChild = symbolChild.Parent.Children[addSymbolChildCommand.AddedChildId];

        // loop though inputs
        foreach (var input in symbolChild.Inputs)
        {
            var connections = symbolChild.Parent.Connections.FindAll(c => c.TargetSlotId == input.Key
                                                                    && c.TargetParentOrChildId == symbolChild.Id);

            var inputName = input.Value.Name;
            var destInput = newSymbolChild.Inputs.FirstOrDefault(x => x.Value.Name == inputName);

            if (connections.Count > 1) // can this happen?
            {
                Log.Warning("\"Change Symbol...\" : connections.Count > 1 : not yet implemented, skipping!");
                System.Diagnostics.Debug.Assert(false);
            }
            else 
            {
                var inputHasData = connections.Count > 0 || !input.Value.IsDefault;

                if (destInput.Value != null)
                {
                    if(input.Value.Value.ValueType == destInput.Value.Value.ValueType)
                    {
                        // treating default value as a special assignment,
                        // this is also how it is indicated in the UI.
                        // Just leave the initial value of newSymbol input
                        // (assuming this is same as default)

                        if (!input.Value.IsDefault) 
                        {
                            var changeValCommand = new ChangeInputValueCommand(symbolChild.Parent, newSymbolChild.Id, destInput.Value, input.Value.Value);
                            changeValCommand.Do();
                            executedCommands.Add(changeValCommand);
                        }

                        if (connections.Count == 1)
                        {
                            // remove old connection to symbolChild

                            var delCommand = new DeleteConnectionCommand(symbolChild.Parent, connections[0], 0);
                            delCommand.Do();
                            executedCommands.Add(delCommand);


                            // add new connection to newSymbolChild

                            var newConnectionToInput = new Symbol.Connection(
                                                    sourceParentOrChildId: connections[0].SourceParentOrChildId,
                                                    sourceSlotId: connections[0].SourceSlotId,
                                                    targetParentOrChildId: newSymbolChild.Id,
                                                    targetSlotId: destInput.Key);
                            var addCommand = new AddConnectionCommand(parentSymbolUi.Symbol, newConnectionToInput, 0);
                            addCommand.Do();
                            executedCommands.Add(addCommand);
                        }
                    }
                    else if(inputHasData)
                    {
                        conversionWasLossy = true;
                        Log.Info("\"Change Symbol...\" : type mismatching, input:" + inputName + ", " + input.Value.Value.ValueType + "(old) vs " + destInput.Value.Value.ValueType + "(new)");
                    }

                }
                else if(inputHasData)
                {
                    conversionWasLossy = true;
                    Log.Info("\"Change Symbol...\" : no matching input, name: " + inputName);
                }

            }

        }


        // loop though outputs
        foreach (var output in symbolChild.Outputs)
        {
            var connections = symbolChild.Parent.Connections.FindAll(c => c.SourceSlotId == output.Key
                                                                    && c.SourceParentOrChildId == symbolChild.Id);

            var outputName = output.Value.OutputDefinition.Name;
            var destOutput = newSymbolChild.Outputs.FirstOrDefault(x => x.Value.OutputDefinition.Name == outputName);

            foreach (var connection in connections)
            {
                if (destOutput.Value != null)
                {
                    if (output.Value.OutputDefinition.ValueType == destOutput.Value.OutputDefinition.ValueType)
                    {
                        // remove old connection from symbolChild

                        var multiInputIndex = symbolChild.Parent.GetMultiInputIndexFor(connection);
                        var delCommand = new DeleteConnectionCommand(symbolChild.Parent, connection, multiInputIndex);
                        delCommand.Do();
                        executedCommands.Add(delCommand);



                        // add new connection from newSymbolChild

                        var newConnectionToInput = new Symbol.Connection(
                                             sourceParentOrChildId: newSymbolChild.Id,
                                             sourceSlotId: destOutput.Key,
                                             targetParentOrChildId: connection.TargetParentOrChildId,
                                             targetSlotId: connection.TargetSlotId);
                        var addCommand = new AddConnectionCommand(parentSymbolUi.Symbol, newConnectionToInput, multiInputIndex);
                        addCommand.Do();
                        executedCommands.Add(addCommand);
                    }
                    else
                    {
                        conversionWasLossy = true;
                        Log.Info("\"Change Symbol...\" : type mismatching, input:" + outputName + ", " + output.Value.OutputDefinition.ValueType + "(old) vs " + destOutput.Value.OutputDefinition.ValueType + "(new)");
                    }
                }
                else
                {
                    conversionWasLossy = true;
                    Log.Info("\"Change Symbol...\" : no matching output, name: " + outputName + " (connection left as is)");
                }
            }
        }

        if(!conversionWasLossy)
        {
            var delCommand = new DeleteSymbolChildrenCommand(parentSymbolUi, new List<SymbolUi.Child>() { symbolChildUi });
            delCommand.Do();
            executedCommands.Add(delCommand);
        }
        else
        {
            Log.Warning("\"Change Symbol...\" : old operator not deleted due to lossy conversion");
        }

        return newSymbolChild;
    }


    [Flags]
    internal enum SymbolModificationResults
    {
        Nothing,
        
        /// <summary>
        /// A change that should trigger cache invalidation of UI structures for connections, nodes, etc.
        /// </summary>
        StructureChanged,
    }
}