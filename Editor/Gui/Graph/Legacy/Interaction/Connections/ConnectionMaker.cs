#nullable enable
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using T3.Core.Operator;
using T3.Core.Operator.Slots;
using T3.Editor.Gui.Interaction;
using T3.Editor.UiModel;
using T3.Editor.UiModel.Commands;
using T3.Editor.UiModel.Commands.Graph;
using T3.Editor.UiModel.ProjectHandling;
using T3.Editor.UiModel.Selection;
using Vector2 = System.Numerics.Vector2;

namespace T3.Editor.Gui.Legacy.Interaction.Connections;

/// <summary>
/// Handles the creation of new  <see cref="Symbol.Connection"/>s. 
/// It provides accessors for highlighting matching input slots and methods that need to be
/// called when connections are completed or aborted.
///
/// To support multiple parallel <see cref="ScalableCanvas"/> we keep the <see cref="ConnectionInProgress"/>
/// for each window in a dictionary.
/// </summary>
internal static class ConnectionMaker
{
    private static readonly Dictionary<IGraphView, ConnectionInProgress> _tempConnectionsForGraphViews = new();

    private sealed class ConnectionInProgress
    {
        public readonly List<TempConnection> TempConnections = [];
        public MacroCommand? Command;
        public bool IsDisconnectingFromInput;

        public void Reset()
        {
            TempConnections.Clear();
            Command = null;
            IsDisconnectingFromInput = false;
        }

        internal void SetTempConnection(TempConnection c)
        {
            TempConnections.Clear();
            TempConnections.Add(c);
        }
    }

    public static void GraphGraphView(IGraphView graph)
    {
        _tempConnectionsForGraphViews.Add(graph, new ConnectionInProgress());
    }

    public static void RemoveGraphView(IGraphView graph)
    {
        _tempConnectionsForGraphViews.Remove(graph);
    }

    public static bool IsMatchingInputType(IGraphView graph, Type valueType)
    {
        var connectionList = _tempConnectionsForGraphViews[graph].TempConnections;
        return connectionList.Count > 0
               && connectionList[0].TargetSlotId == NotConnectedId
               && connectionList[0].ConnectionType == valueType;
    }

    public static bool IsMatchingOutputType(IGraphView graph, Type valueType)
    {
        var connectionList = _tempConnectionsForGraphViews[graph].TempConnections;
        return connectionList.Count == 1
               && connectionList[0].SourceSlotId == NotConnectedId
               && connectionList[0].ConnectionType == valueType;
    }

    public static bool IsOutputSlotCurrentConnectionSource(IGraphView graph, SymbolUi.Child sourceUi, Symbol.OutputDefinition outputDef)
    {
        var connectionList = _tempConnectionsForGraphViews[graph].TempConnections;
        return connectionList.Count == 1
               && connectionList[0].SourceParentOrChildId == sourceUi.SymbolChild.Id
               && connectionList[0].SourceSlotId == outputDef.Id;
    }

    public static bool IsInputSlotCurrentConnectionTarget(IGraphView graph, SymbolUi.Child targetUi, Symbol.InputDefinition inputDef, int multiInputIndex = 0)
    {
        var connectionList = _tempConnectionsForGraphViews[graph].TempConnections;
        return connectionList.Count == 1
               && connectionList[0].TargetParentOrChildId == targetUi.SymbolChild.Id
               && connectionList[0].TargetSlotId == inputDef.Id;
    }

    public static bool IsInputNodeCurrentConnectionSource(IGraphView graph, Symbol.InputDefinition inputDef)
    {
        var connectionList = _tempConnectionsForGraphViews[graph].TempConnections;
        return connectionList.Count == 1
               && connectionList[0].SourceParentOrChildId == UseSymbolContainerId
               && connectionList[0].SourceSlotId == inputDef.Id;
    }

    public static bool IsOutputNodeCurrentConnectionTarget(IGraphView graph, Symbol.OutputDefinition outputDef)
    {
        var connectionList = _tempConnectionsForGraphViews[graph].TempConnections;
        return connectionList.Count == 1
               && connectionList[0].TargetParentOrChildId == UseSymbolContainerId
               && connectionList[0].TargetSlotId == outputDef.Id;
    }

    public static void StartFromOutputSlot(IGraphView graph, NodeSelection selection, SymbolUi.Child sourceUi, Symbol.OutputDefinition outputDef)
    {
        var inProgress = _tempConnectionsForGraphViews[graph];
        StartOperation(inProgress, $"Connect from {sourceUi.SymbolChild.ReadableName}.{outputDef.Name}");

        var selectedSymbolChildUis = selection.GetSelectedChildUis().OrderBy(c => c.PosOnCanvas.Y * 100 + c.PosOnCanvas.X).ToList();
        selectedSymbolChildUis.Reverse();

        if (selectedSymbolChildUis.Count > 1 && (selectedSymbolChildUis.Any(c => c.Id == sourceUi.Id)))
            //if (selectedSymbolChildUis.Count > 1)
        {
            selectedSymbolChildUis.Reverse();

            // add temp connections for all selected nodes that have the same primary output type
            foreach (var selectedChild in selectedSymbolChildUis)
            {
                var outputDefinitions = selectedChild.SymbolChild.Symbol.OutputDefinitions;
                if (outputDefinitions.Count == 0)
                    continue;

                var firstOutput = selectedChild == sourceUi
                                      ? outputDef
                                      : outputDefinitions[0];
                if (firstOutput.ValueType != outputDef.ValueType)
                    continue;

                inProgress.TempConnections.Add(new TempConnection(
                                                                  sourceParentOrChildId: selectedChild.SymbolChild.Id,
                                                                  sourceSlotId: firstOutput.Id,
                                                                  targetParentOrChildId: NotConnectedId,
                                                                  targetSlotId: NotConnectedId,
                                                                  outputDef.ValueType));
            }
        }

        else
        {
            inProgress.SetTempConnection(new TempConnection(
                                                            sourceParentOrChildId: sourceUi.SymbolChild.Id,
                                                            sourceSlotId: outputDef.Id,
                                                            targetParentOrChildId: NotConnectedId,
                                                            targetSlotId: NotConnectedId,
                                                            outputDef.ValueType));
        }
    }

    public static void StartFromInputSlot(IGraphView graphView, Symbol parentSymbol, SymbolUi.Child targetUi, Symbol.InputDefinition inputDef,
                                          int multiInputIndex = 0)
    {
        var inProgress = _tempConnectionsForGraphViews[graphView];

        if (FindConnectionToInputSlot(parentSymbol, targetUi, inputDef, multiInputIndex, out var existingConnection))
        {
            var newCommand = StartOperation(inProgress, $"Disconnect {targetUi.SymbolChild.ReadableName}.{inputDef.Name}");
            newCommand.AddAndExecCommand(new DeleteConnectionCommand(parentSymbol, existingConnection, multiInputIndex));
            inProgress.SetTempConnection(new TempConnection(
                                                            sourceParentOrChildId: existingConnection.SourceParentOrChildId,
                                                            sourceSlotId: existingConnection.SourceSlotId,
                                                            targetParentOrChildId: NotConnectedId,
                                                            targetSlotId: NotConnectedId,
                                                            inputDef.DefaultValue.ValueType));
            inProgress.IsDisconnectingFromInput = true;
        }
        else
        {
            StartOperation(inProgress, $"Connecting from {targetUi.SymbolChild.ReadableName}.{inputDef.Name}");
            inProgress.SetTempConnection(new TempConnection(
                                                            sourceParentOrChildId: NotConnectedId,
                                                            sourceSlotId: NotConnectedId,
                                                            targetParentOrChildId: targetUi.SymbolChild.Id,
                                                            targetSlotId: inputDef.Id,
                                                            inputDef.DefaultValue.ValueType));
            inProgress.IsDisconnectingFromInput = false;
        }
    }

    public static void StartFromInputNode(IGraphView graph, Symbol.InputDefinition inputDef)
    {
        var inProgress = _tempConnectionsForGraphViews[graph];
        StartOperation(inProgress, $"Connecting from {inputDef.Name}");
        inProgress.SetTempConnection(new TempConnection(
                                                        sourceParentOrChildId: UseSymbolContainerId,
                                                        sourceSlotId: inputDef.Id,
                                                        targetParentOrChildId: NotConnectedId,
                                                        targetSlotId: NotConnectedId,
                                                        inputDef.DefaultValue.ValueType));
    }

    public static void StartFromOutputNode(IGraphView graph, Symbol parentSymbol, Symbol.OutputDefinition outputDef)
    {
        var inProgress = _tempConnectionsForGraphViews[graph];

        var newCommand = StartOperation(inProgress, $"Connecting to {outputDef.Name}");
        var existingConnection = parentSymbol.Connections.Find(c => c.TargetParentOrChildId == UseSymbolContainerId
                                                                    && c.TargetSlotId == outputDef.Id);

        if (existingConnection != null)
        {
            newCommand.AddAndExecCommand(new DeleteConnectionCommand(parentSymbol, existingConnection, 0));
            inProgress.SetTempConnection(new TempConnection(
                                                            sourceParentOrChildId: existingConnection.SourceParentOrChildId,
                                                            sourceSlotId: existingConnection.SourceSlotId,
                                                            targetParentOrChildId: NotConnectedId,
                                                            targetSlotId: NotConnectedId,
                                                            outputDef.ValueType));
            inProgress.IsDisconnectingFromInput = true;
        }
        else
        {
            inProgress.SetTempConnection(new TempConnection(
                                                            sourceParentOrChildId: NotConnectedId,
                                                            sourceSlotId: NotConnectedId,
                                                            targetParentOrChildId: UseSymbolContainerId,
                                                            targetSlotId: outputDef.Id,
                                                            outputDef.ValueType));
            inProgress.IsDisconnectingFromInput = false;
        }
    }

    public static void StartOperation(IGraphView graph, string commandName) => StartOperation(_tempConnectionsForGraphViews[graph], commandName);

    private static MacroCommand StartOperation(ConnectionInProgress inProgress, string commandName)
    {
        if (inProgress.TempConnections.Count != 0)
        {
            Log.Warning($"Inconsistent TempConnection count of {inProgress.TempConnections.Count}. Last operation incomplete?");
        }

        inProgress.Command = new MacroCommand(commandName);
        return inProgress.Command;
    }

    public static void CompleteOperation(IGraphView graph, List<ICommand> doneCommands, string newCommandTitle)
        => CompleteOperation(_tempConnectionsForGraphViews[graph], doneCommands, newCommandTitle);

    private static void CompleteOperation(ConnectionInProgress inProgress, List<ICommand>? doneCommands = null, string? newCommandTitle = null)
    {
        var inProgressCommand = inProgress.Command ?? StartOperation(inProgress, "Temp op");

        if (doneCommands != null)
        {
            foreach (var c in doneCommands)
            {
                inProgressCommand.AddExecutedCommandForUndo(c);
            }
        }

        if (!string.IsNullOrEmpty(newCommandTitle))
        {
            inProgressCommand.Name = newCommandTitle;
        }

        UndoRedoStack.Add(inProgressCommand);
        Reset(inProgress);
    }

    public static void AbortOperation(IGraphView graph) => AbortOperation(_tempConnectionsForGraphViews[graph]);

    private static void AbortOperation(ConnectionInProgress inProgress)
    {
        inProgress.Command?.Undo();
        Reset(inProgress);
    }

    private static void Reset(ConnectionInProgress inProgress)
    {
        inProgress.Reset();
        ConnectionSnapEndHelper.ResetSnapping();
    }

    private static void AdjustGraphLayoutForNewNode(MacroCommand inProgressCommand, Symbol parent, Symbol.Connection connection, NodeSelection selection)
    {
        if (connection.IsConnectedToSymbolOutput || connection.IsConnectedToSymbolInput)
        {
            Log.Debug("re-layout is not not supported for input and output nodes yet");
            return;
        }

        var parentSymbolUi = parent.GetSymbolUi();
        var sourceNodeUi = parentSymbolUi.ChildUis[connection.SourceParentOrChildId];
        var targetNodeUi = parentSymbolUi.ChildUis[connection.TargetParentOrChildId];
        var center = (sourceNodeUi.PosOnCanvas + targetNodeUi.PosOnCanvas) / 2;
        //var commands = new List<ICommand>();

        var changedChildren = new List<ISelectableCanvasObject>();
        var changedChildUis = new List<SymbolUi.Child>();

        var requiredGap = SymbolUi.Child.DefaultOpSize.X + SelectableNodeMovement.SnapPadding.X;
        var xSource = sourceNodeUi.PosOnCanvas.X + sourceNodeUi.Size.X;
        var xTarget = targetNodeUi.PosOnCanvas.X;

        var currentGap = xTarget - xSource - SelectableNodeMovement.SnapPadding.X;
        if (currentGap > requiredGap)
            return;

        var offset = Math.Min(requiredGap - currentGap, requiredGap);

        // Collect all connected ops further down the tree
        var connectedOps = new HashSet<Symbol.Child>();
        RecursivelyAddChildren(ref connectedOps, parent, targetNodeUi.SymbolChild);

        foreach (var child in connectedOps)
        {
            if (!parentSymbolUi.ChildUis.TryGetValue(child.Id, out var childUi) || childUi.PosOnCanvas.X > center.X)
                continue;

            changedChildren.Add(childUi);
            changedChildUis.Add(childUi);
        }

        var command = new ModifyCanvasElementsCommand(parentSymbolUi.Symbol.Id, changedChildren, selection);
        foreach (var childUi in changedChildUis)
        {
            var pos = childUi.PosOnCanvas;

            pos.X -= offset;
            childUi.PosOnCanvas = pos;
        }

        command.StoreCurrentValues();

        inProgressCommand.AddExecutedCommandForUndo(command);
    }

    private static void RecursivelyAddChildren(ref HashSet<Symbol.Child> set, Symbol parent, Symbol.Child targetNode)
    {
        foreach (var inputDef in targetNode.Symbol.InputDefinitions)
        {
            var connections = parent.Connections.FindAll(c => c.TargetSlotId == inputDef.Id
                                                              && c.TargetParentOrChildId == targetNode.Id);

            foreach (var c in connections)
            {
                if (c.SourceParentOrChildId == UseSymbolContainerId) // TODO move symbol inputs?
                    continue;

                if (!parent.Children.TryGetValue(c.SourceParentOrChildId, out var sourceOp))
                {
                    Log.Error($"Can't find child for connection source {c.SourceParentOrChildId}");
                    continue;
                }

                if (!set.Add(sourceOp))
                    continue;

                RecursivelyAddChildren(ref set, parent, sourceOp);
            }
        }
    }

    public static void CompleteAtInputSlot(IGraphView graph, Instance childInstance, SymbolUi.Child targetUi, Symbol.InputDefinition input,
                                           int multiInputIndex = 0,
                                           bool insertMultiInput = false)
    {
        var symbolInstance = childInstance.Parent;
        Debug.Assert(symbolInstance != null);
        
        
        var inProgress = _tempConnectionsForGraphViews[graph];
        var inProgressCommand = inProgress.Command;
        
        // This can happen if a connection would lead to a cycle
        if (inProgressCommand == null)
            return;

        var connectionList = inProgress.TempConnections;
        
        var sourceId = connectionList[0].SourceParentOrChildId;
        if (sourceId != Guid.Empty) // empty if coming from the parent input slots
        {
            var sourceInstance = symbolInstance.Children[sourceId];

            if (Structure.CheckForCycle(sourceInstance.Outputs[0], targetUi.Id))
            {
                Log.Debug("This action is not allowed. This connection would result in a cycle.");
                AbortOperation(inProgress);
            }
        }

        var symbol = symbolInstance.Symbol;

        var newConnections = new List<Symbol.Connection>();

        for (var index = connectionList.Count - 1; index >= 0; index--)
        {
            var tempConnection = connectionList[index];
            newConnections.Add(new Symbol.Connection(sourceParentOrChildId: tempConnection.SourceParentOrChildId,
                                                     sourceSlotId: tempConnection.SourceSlotId,
                                                     targetParentOrChildId: targetUi.SymbolChild.Id,
                                                     targetSlotId: input.Id));
        }

        // get the previous connection
        var allConnectionsToSlot = symbol.Connections.FindAll(c => c.TargetParentOrChildId == targetUi.SymbolChild.Id &&
                                                                   c.TargetSlotId == input.Id);
        bool replacesConnection;
        if (insertMultiInput)
        {
            replacesConnection = multiInputIndex % 2 != 0;
            multiInputIndex /= 2; // divide by 2 to get correct insertion index in existing connections
        }
        else
        {
            replacesConnection = allConnectionsToSlot.Count != 0;
        }

        var addCommands = new List<ICommand>();
        foreach (var newConnection in newConnections)
        {
            addCommands.Add(new AddConnectionCommand(symbol, newConnection, multiInputIndex));
        }


        
        //Debug.Assert(inProgressCommand != null);
        if (replacesConnection)
        {
            var connectionToRemove = allConnectionsToSlot[multiInputIndex];
            var deleteCommand = new DeleteConnectionCommand(symbol, connectionToRemove, multiInputIndex);

            inProgressCommand.AddAndExecCommand(deleteCommand);
            foreach (var addCommand in addCommands)
            {
                inProgressCommand.AddAndExecCommand(addCommand);
            }

            inProgressCommand.Name = $"Reconnect to {targetUi.SymbolChild.ReadableName}.{input.Name}";
        }
        else
        {
            foreach (var addCommand in addCommands)
            {
                inProgressCommand.AddAndExecCommand(addCommand);
            }
        }

        CompleteOperation(inProgress);
    }

    public static void CompleteAtOutputSlot(IGraphView grpah, Instance sourceInstance, SymbolUi.Child sourceUi, Symbol.OutputDefinition output)
    {
        var connectionProgress = _tempConnectionsForGraphViews[grpah];
        if (sourceInstance.Parent == null)
            return;
        
        Debug.Assert(connectionProgress.Command != null);
        
        var tempConnections = connectionProgress.TempConnections;

        if (tempConnections.Count != 1)
        {
            Log.Debug("Can only connect one line");
            tempConnections.Clear();
            return;
        }

        var tempConnection = tempConnections[0];

        // Check for cycles
        var targetId = tempConnection.TargetParentOrChildId;
        if (Structure.CheckForCycle(sourceInstance, targetId))
        {
            Log.Debug("This action is not allowed. This connection would result in a cycle.");
            tempConnections.Clear();
            ConnectionSnapEndHelper.ResetSnapping();
            return;
        }

        var newConnection = new Symbol.Connection(sourceParentOrChildId: sourceUi.SymbolChild.Id,
                                                  sourceSlotId: output.Id,
                                                  targetParentOrChildId: tempConnection.TargetParentOrChildId,
                                                  targetSlotId: tempConnection.TargetSlotId);

        connectionProgress.Command.AddAndExecCommand(new AddConnectionCommand(sourceInstance.Parent.Symbol,
                                                                              newConnection, 0));
        CompleteOperation(connectionProgress);
    }

    #region related to SymbolBrowser
    /// <remarks>
    /// Assumes that a temp connection has be created earlier and is now dropped on the background
    /// </remarks>
    public static void InitSymbolBrowserAtPosition(IGraphView graph, SymbolBrowser symbolBrowser, Vector2 canvasPosition)
    {
        var inProgress = _tempConnectionsForGraphViews[graph];
        var connectionList = inProgress.TempConnections;
        if (connectionList.Count == 0)
            return;

        if (inProgress.IsDisconnectingFromInput)
        {
            CompleteOperation(inProgress);
            return;
        }

        var firstConnectionType = connectionList[0].ConnectionType;
        if (connectionList.Count == 1)
        {
            if (connectionList[0].TargetParentOrChildId == NotConnectedId)
            {
                inProgress.SetTempConnection(new TempConnection(
                                                                sourceParentOrChildId: connectionList[0].SourceParentOrChildId,
                                                                sourceSlotId: connectionList[0].SourceSlotId,
                                                                targetParentOrChildId: UseDraftChildId,
                                                                targetSlotId: NotConnectedId,
                                                                firstConnectionType));
                symbolBrowser.OpenAt(canvasPosition, firstConnectionType, null, false);
            }
            else if (connectionList[0].SourceParentOrChildId == NotConnectedId)
            {
                inProgress.SetTempConnection(new TempConnection(
                                                                sourceParentOrChildId: UseDraftChildId,
                                                                sourceSlotId: NotConnectedId,
                                                                targetParentOrChildId: connectionList[0].TargetParentOrChildId,
                                                                targetSlotId: connectionList[0].TargetSlotId,
                                                                firstConnectionType));
                symbolBrowser.OpenAt(canvasPosition, null, firstConnectionType, false);
            }
        }
        // Multiple TempConnections only work when they are connected to outputs 
        else if (connectionList.Count > 1)
        {
            var validForMultiInput = connectionList.All(c =>
                                                            c.GetStatus() == TempConnection.Status.TargetIsUndefined
                                                            && c.ConnectionType == firstConnectionType);
            if (validForMultiInput)
            {
                var oldConnections = connectionList.ToArray().Reverse();
                connectionList.Clear();
                foreach (var c in oldConnections)
                {
                    connectionList.Add(new TempConnection(
                                                          sourceParentOrChildId: c.SourceParentOrChildId,
                                                          sourceSlotId: c.SourceSlotId,
                                                          targetParentOrChildId: UseDraftChildId,
                                                          targetSlotId: NotConnectedId,
                                                          firstConnectionType));
                }

                symbolBrowser.OpenAt(canvasPosition, firstConnectionType, null, onlyMultiInputs: true);
            }
        }
        else
        {
            AbortOperation(inProgress);
        }
    }

    public static void OpenSymbolBrowserAtOutput(ProjectView components, SymbolUi.Child childUi, Instance instance,
                                                 Guid outputId)
    {
        var primaryOutput = instance.Outputs.SingleOrDefault(o => o.Id == outputId);
        if (primaryOutput == null)
            return;

        if (components.GraphView is not GraphView canvas)
            return;
        
        var connectionList = _tempConnectionsForGraphViews[canvas];
        StartOperation(connectionList, "Insert Operator");
        InsertSymbolBrowser(components, childUi, instance, primaryOutput, canvas.SymbolBrowser);
    }

    public static void InsertSymbolInstance(ProjectView components, Symbol symbol)
    {
        var graph = components.GraphView;

        if (_tempConnectionsForGraphViews.TryGetValue(graph, out var inProgress) || inProgress == null)
        {
            Log.Warning("Can't insert connection without active canvas.");
            return;
        }
        
        var selection = components.NodeSelection;
        var instance = selection.GetSelectedInstanceWithoutComposition();
        
        if (instance?.Parent == null)
            return;

        var parentUi = instance.Parent.GetSymbolUi();

        if (!parentUi.ChildUis.TryGetValue(instance.SymbolChildId, out var symbolChildUi))
        {
            return;
        }

        if (instance.Outputs.Count < 1)
            return;

        var posOnCanvas = symbolChildUi.PosOnCanvas;

        var primaryOutput = instance.Outputs[0];
        
        var newCommand= StartOperation(inProgress, "Insert Operator");
        
        var connections = instance.Parent.Symbol.Connections.FindAll(connection => connection.SourceParentOrChildId == instance.SymbolChildId
                                                                                   && connection.SourceSlotId == primaryOutput.Id);

        var connectionList = inProgress.TempConnections;
        connectionList.Add(new TempConnection(
                                              sourceParentOrChildId: instance.SymbolChildId,
                                              sourceSlotId: primaryOutput.Id,
                                              targetParentOrChildId: UseDraftChildId,
                                              targetSlotId: NotConnectedId,
                                              primaryOutput.ValueType));

        if (connections.Count > 0)
        {
            AdjustGraphLayoutForNewNode(newCommand, instance.Parent.Symbol, connections[0], components.NodeSelection);
            foreach (var oldConnection in connections)
            {
                var multiInputIndex = instance.Parent.Symbol.GetMultiInputIndexFor(oldConnection);
                newCommand.AddAndExecCommand(new DeleteConnectionCommand(instance.Parent.Symbol, oldConnection, multiInputIndex));
                connectionList.Add(new TempConnection(
                                                      sourceParentOrChildId: UseDraftChildId,
                                                      sourceSlotId: NotConnectedId,
                                                      targetParentOrChildId: oldConnection.TargetParentOrChildId,
                                                      targetSlotId: oldConnection.TargetSlotId,
                                                      primaryOutput.ValueType,
                                                      multiInputIndex));
            }
        }
        else
        {
            posOnCanvas.X += SymbolUi.Child.DefaultOpSize.X + SelectableNodeMovement.SnapPadding.X;
        }

        var commandsForUndo = new List<ICommand>();
        var parent = instance.Parent;
        var parentSymbol = parent.Symbol;

        var addSymbolChildCommand = new AddSymbolChildCommand(instance.Parent.Symbol, symbol.Id) { PosOnCanvas = posOnCanvas };
        commandsForUndo.Add(addSymbolChildCommand);
        addSymbolChildCommand.Do();
        var newSymbolChild = parentSymbol.Children[addSymbolChildCommand.AddedChildId];

        // Select new node
        var parentSymbolUi = parentSymbol.GetSymbolUi();
        if (!parentUi.ChildUis.TryGetValue(newSymbolChild.Id, out var newChildUi))
        {
            Log.Warning("Unable to create new operator");
            return;
        }

        var newInstance = instance.Parent.Children[newChildUi.Id];

        selection.SetSelection(newChildUi, newInstance);

        foreach (var c in connectionList)
        {
            switch (c.GetStatus())
            {
                case ConnectionMaker.TempConnection.Status.SourceIsDraftNode:
                    var outputDef = newSymbolChild.Symbol.GetOutputMatchingType(c.ConnectionType);
                    if (outputDef == null)
                    {
                        Log.Error("Failed to find matching output connection type " + c.ConnectionType);
                        continue;
                    }

                    var newConnectionToSource = new Symbol.Connection(sourceParentOrChildId: newSymbolChild.Id,
                                                                      sourceSlotId: outputDef.Id,
                                                                      targetParentOrChildId: c.TargetParentOrChildId,
                                                                      targetSlotId: c.TargetSlotId);
                    var addConnectionCommand = new AddConnectionCommand(parentSymbolUi.Symbol, newConnectionToSource, c.MultiInputIndex);
                    addConnectionCommand.Do();
                    commandsForUndo.Add(addConnectionCommand);
                    break;

                case ConnectionMaker.TempConnection.Status.TargetIsDraftNode:
                    var inputDef = newSymbolChild.Symbol.GetInputMatchingType(c.ConnectionType);
                    if (inputDef == null)
                    {
                        Log.Warning("Failed to complete node creation");
                        continue;
                    }

                    var newConnectionToInput = new Symbol.Connection(sourceParentOrChildId: c.SourceParentOrChildId,
                                                                     sourceSlotId: c.SourceSlotId,
                                                                     targetParentOrChildId: newSymbolChild.Id,
                                                                     targetSlotId: inputDef.Id);
                    var connectionCommand = new AddConnectionCommand(parentSymbolUi.Symbol, newConnectionToInput, c.MultiInputIndex);
                    connectionCommand.Do();
                    commandsForUndo.Add(connectionCommand);
                    break;
            }
        }

        CompleteOperation(inProgress, commandsForUndo, "Insert Op " + newChildUi.SymbolChild.ReadableName);
        ParameterPopUp.NodeIdRequestedForParameterWindowActivation = newSymbolChild.Id;
    }

    public static void OpenBrowserWithSingleSelection(ProjectView components, SymbolUi.Child childUi, Instance instance, SymbolBrowser symbolBrowser)
    {
        if (instance.Outputs.Count < 1)
            return;

        //StartOperation("Insert Operator");
        var primaryOutput = instance.Outputs[0];

        InsertSymbolBrowser(components, childUi, instance, primaryOutput, symbolBrowser);
    }

    private static void InsertSymbolBrowser(ProjectView components, SymbolUi.Child childUi, Instance instance, ISlot primaryOutput, SymbolBrowser symbolBrowser)
    {
        if (instance.Parent == null)
            return;

        var canvas = components.GraphView;
        var inProgress = _tempConnectionsForGraphViews[canvas];
        
        var newCommand =StartOperation(inProgress, "Insert Operator");
        
        var connections = instance.Parent.Symbol.Connections.FindAll(connection => connection.SourceParentOrChildId == instance.SymbolChildId
                                                                                   && connection.SourceSlotId == primaryOutput.Id);

        var connectionList = inProgress.TempConnections;
        connectionList.Add(new TempConnection(
                                              sourceParentOrChildId: instance.SymbolChildId,
                                              sourceSlotId: primaryOutput.Id,
                                              targetParentOrChildId: UseDraftChildId,
                                              targetSlotId: NotConnectedId,
                                              primaryOutput.ValueType));

        Type? filterOutputType = null;
        if (connections.Count > 0)
        {
            var mainConnection = connections[0];
            if (mainConnection.IsConnectedToSymbolOutput)
            {
                var compUi = instance.Parent.GetSymbolUi();

                if (compUi.OutputUis.TryGetValue(mainConnection.TargetSlotId, out var outputUi))
                {
                    var moveCommand =
                        new ModifyCanvasElementsCommand(compUi, new List<ISelectableCanvasObject>() { outputUi }, components.NodeSelection);
                    outputUi.PosOnCanvas += new Vector2(SymbolUi.Child.DefaultOpSize.X, 0);
                    moveCommand.StoreCurrentValues();
                    newCommand.AddAndExecCommand(moveCommand);
                }
            }
            else
            {
                AdjustGraphLayoutForNewNode(newCommand, instance.Parent.Symbol, mainConnection, components.NodeSelection);
                foreach (var oldConnection in connections)
                {
                    var multiInputIndex = instance.Parent.Symbol.GetMultiInputIndexFor(oldConnection);
                    newCommand.AddAndExecCommand(new DeleteConnectionCommand(instance.Parent.Symbol, oldConnection, multiInputIndex));
                    connectionList.Add(new TempConnection(
                                                          sourceParentOrChildId: UseDraftChildId,
                                                          sourceSlotId: NotConnectedId,
                                                          targetParentOrChildId: oldConnection.TargetParentOrChildId,
                                                          targetSlotId: oldConnection.TargetSlotId,
                                                          primaryOutput.ValueType,
                                                          multiInputIndex));
                    filterOutputType = primaryOutput.ValueType;
                }
            }
        }

        symbolBrowser.OpenAt(childUi.PosOnCanvas + new Vector2(childUi.Size.X, 0)
                                                        + new Vector2(SelectableNodeMovement.SnapPadding.X, 0),
                                    primaryOutput.ValueType, filterOutputType, false);
    }

    public static void SplitConnectionWithSymbolBrowser(ProjectView components, Symbol parentSymbol, Symbol.Connection connection,
                                                        Vector2 positionInCanvas, SymbolBrowser symbolBrowser)
    {
        if (connection.IsConnectedToSymbolOutput)
        {
            Log.Debug("Splitting connections to output is not implemented yet");
            return;
        }

        if (connection.IsConnectedToSymbolInput)
        {
            Log.Debug("Splitting connections from inputs is not implemented yet");
            return;
        }

        var canvas = components.GraphView;
        var inProgress = _tempConnectionsForGraphViews[canvas];

        StartOperation(inProgress, "Split connection with new Operator");
        Debug.Assert(inProgress.Command != null);

        // Todo: Fix me for output nodes
        var child = parentSymbol.Children[connection.TargetParentOrChildId];
        var inputDef = child.Symbol.InputDefinitions.Single(i => i.Id == connection.TargetSlotId);

        //var commands = new List<ICommand>();
        var multiInputIndex = parentSymbol.GetMultiInputIndexFor(connection);
        //_tempDeletionCommands.Add(  new DeleteConnectionCommand(parentSymbol, connection, multiInputIndex));;

        AdjustGraphLayoutForNewNode(inProgress.Command, parentSymbol, connection, components.NodeSelection);
        inProgress.Command.AddAndExecCommand(new DeleteConnectionCommand(parentSymbol, connection, multiInputIndex));

        var connectionList = inProgress.TempConnections;
        connectionList.Clear();
        var connectionType = inputDef.DefaultValue.ValueType;
        connectionList.Add(new TempConnection(
                                              sourceParentOrChildId: connection.SourceParentOrChildId,
                                              sourceSlotId: connection.SourceSlotId,
                                              targetParentOrChildId: UseDraftChildId,
                                              targetSlotId: NotConnectedId,
                                              connectionType));

        connectionList.Add(new TempConnection(
                                              sourceParentOrChildId: UseDraftChildId,
                                              sourceSlotId: NotConnectedId,
                                              targetParentOrChildId: connection.TargetParentOrChildId,
                                              targetSlotId: connection.TargetSlotId,
                                              connectionType,
                                              multiInputIndex));

        symbolBrowser.OpenAt(positionInCanvas, connectionType, connectionType, false);
    }
    #endregion

    internal static bool IsTargetInvalid(IGraphView graph, Type type)
    {
        var connectionList = _tempConnectionsForGraphViews[graph].TempConnections;
        return T3Ui.IsAnyPopupOpen
               || connectionList.Count == 0
               || connectionList.All(c => c.ConnectionType != type);
    }

    public static void SplitConnectionWithDraggedNode(IGraphView graph, SymbolUi.Child childUi, Symbol.Connection oldConnection, Instance instance,
                                                      ModifyCanvasElementsCommand moveCommand, NodeSelection selection)
    {
        var parent = instance.Parent;
        if (parent == null)
            return;
        
        var inProgress = _tempConnectionsForGraphViews[graph];
        StartOperation(inProgress, $"Split connection with {childUi.SymbolChild.ReadableName}");

        var inProgressCommand = inProgress.Command;
        Debug.Assert(inProgressCommand != null);
        inProgressCommand.AddExecutedCommandForUndo(moveCommand); // FIXME: this will break consistency check 
        
        var siblingDict = parent.Children;
        if (!siblingDict.TryGetChildInstance(oldConnection.SourceParentOrChildId, out var sourceInstance)
            || !siblingDict.TryGetChildInstance(oldConnection.TargetParentOrChildId, out var targetInstance))
        {
            Log.Warning("Can't split this connection");
            return;
        }

        var outputDef = sourceInstance.Symbol.OutputDefinitions.Single(outDef => outDef.Id == oldConnection.SourceSlotId);
        var connectionType = outputDef.ValueType;

        // Check if nodes primary input is not connected
        var
            firstMatchingInput = instance.Inputs.FirstOrDefault(input => input.ValueType == connectionType);
        if (instance.Outputs.Count < 1)
        {
            Log.Warning("Can't use node without outputs for splitting");
            return;
        }

        var primaryOutput = instance.Outputs[0];

        if (firstMatchingInput == null
            || firstMatchingInput.HasInputConnections)
        {
            Log.Warning("Op doesn't have valid connections");
            return;
        }

        if (primaryOutput.ValueType != firstMatchingInput.ValueType)
        {
            Log.Warning("Op doesn't match connection type");
            return;
        }

        //var connectionCommands = new List<ICommand>();
        var multiInputIndex = parent.Symbol.GetMultiInputIndexFor(oldConnection);
        AdjustGraphLayoutForNewNode(inProgressCommand, parent.Symbol, oldConnection, selection);

        var parentUi = parent.Symbol.GetSymbolUi();
        var sourceUi = parentUi.ChildUis[sourceInstance.SymbolChildId];
        var targetUi = parentUi.ChildUis[targetInstance.SymbolChildId];
        var isSnappedHorizontally = (Math.Abs(sourceUi.PosOnCanvas.Y - targetUi.PosOnCanvas.Y) < 0.01f)
                                    && Math.Abs(sourceUi.PosOnCanvas.X + sourceUi.Size.X + SelectableNodeMovement.SnapPadding.X) - targetUi.PosOnCanvas.X <
                                    0.1f;

        var parentSymbolUi = parent.GetSymbolUi();
        if (isSnappedHorizontally)
        {
            childUi.PosOnCanvas = sourceUi.PosOnCanvas + new Vector2(sourceUi.Size.X + SelectableNodeMovement.SnapPadding.X, 0);
            inProgressCommand.AddAndExecCommand(new ModifyCanvasElementsCommand(parentSymbolUi, [childUi], selection));
        }

        inProgressCommand.AddAndExecCommand(new DeleteConnectionCommand(parent.Symbol, oldConnection, multiInputIndex));
        inProgressCommand.AddAndExecCommand(new AddConnectionCommand(parentSymbolUi.Symbol, new Symbol.Connection(oldConnection.SourceParentOrChildId,
                                                                              oldConnection.SourceSlotId,
                                                                              childUi.SymbolChild.Id,
                                                                              firstMatchingInput.Id
                                                                         ), 0));

        inProgressCommand.AddAndExecCommand(new AddConnectionCommand(parentSymbolUi.Symbol, new Symbol.Connection(childUi.SymbolChild.Id,
                                                                              primaryOutput.Id,
                                                                              oldConnection.TargetParentOrChildId,
                                                                              oldConnection.TargetSlotId
                                                                         ), multiInputIndex));
        //var marcoCommand = new MacroCommand("Insert node to connection", connectionCommands);
        //UndoRedoStack.AddAndExecute(marcoCommand);
        CompleteOperation(inProgress);
    }

    public static void CompleteAtSymbolInputNode(IGraphView graph, SymbolUi parentSymbolUi, Symbol.InputDefinition inputDef)
    {
        var inProgress = _tempConnectionsForGraphViews[graph];
        Debug.Assert(inProgress.Command != null);

        foreach (var c in inProgress.TempConnections)
        {
            var newConnection = new Symbol.Connection(sourceParentOrChildId: UseSymbolContainerId,
                                                      sourceSlotId: inputDef.Id,
                                                      targetParentOrChildId: c.TargetParentOrChildId,
                                                      targetSlotId: c.TargetSlotId);
            inProgress.Command.AddAndExecCommand(new AddConnectionCommand(parentSymbolUi.Symbol, newConnection, 0));
        }

        CompleteOperation(inProgress);
    }

    public static void CompleteAtSymbolOutputNode(IGraphView graph, Symbol parentSymbol, Symbol.OutputDefinition outputDef)
    {
        var connectionList = _tempConnectionsForGraphViews[graph].TempConnections;
        bool added = false;
        foreach (var c in connectionList)
        {
            var newConnection = new Symbol.Connection(sourceParentOrChildId: c.SourceParentOrChildId,
                                                      sourceSlotId: c.SourceSlotId,
                                                      targetParentOrChildId: UseSymbolContainerId,
                                                      targetSlotId: outputDef.Id);
            parentSymbol.AddConnection(newConnection);
            added = true;
        }

        if (added)
            parentSymbol.GetSymbolUi().FlagAsModified();

        connectionList.Clear();
        ConnectionSnapEndHelper.ResetSnapping();
    }

    private static bool FindConnectionToInputSlot(Symbol parentSymbol, SymbolUi.Child targetUi, Symbol.InputDefinition input,
                                                  int multiInputIndex,
                                                  [NotNullWhen(true)] out Symbol.Connection? connection)
    {
        connection = null;

        var inputId = input.Id;
        var connections = parentSymbol.Connections.FindAll(c => c.TargetSlotId == inputId
                                                                && c.TargetParentOrChildId == targetUi.SymbolChild.Id);

        if (connections.Count <= multiInputIndex)
            return false;

        connection = connections[multiInputIndex];
        return true;
    }

    /// <summary>
    /// A special Id the flags a connection as incomplete because either the source or the target is not yet connected.
    /// </summary>
    public static Guid NotConnectedId = Guid.Parse("eeeeeeee-E0DF-47C7-A17F-E297672EE1F3");

    /// <summary>
    /// A special Id that indicates that the source of target of a connection is not a child but an input or output node
    /// </summary>
    public static readonly Guid UseSymbolContainerId = Guid.Empty;

    /// <summary>
    /// A special id indicating that the connection is ending in the <see cref="SymbolBrowser"/>
    /// </summary>
    public static Guid UseDraftChildId = Guid.Parse("ffffffff-E0DF-47C7-A17F-E297672EE1F3");

    public sealed class TempConnection : Symbol.Connection
    {
        public TempConnection(Guid sourceParentOrChildId, Guid sourceSlotId, Guid targetParentOrChildId, Guid targetSlotId, Type type,
                              int multiInputIndex = 0) :
            base(sourceParentOrChildId, sourceSlotId, targetParentOrChildId, targetSlotId)
        {
            ConnectionType = type;
            MultiInputIndex = multiInputIndex;
        }

        public readonly Type ConnectionType;
        public readonly int MultiInputIndex;

        public Status GetStatus()
        {
            if (TargetParentOrChildId == NotConnectedId
                && TargetSlotId == NotConnectedId)
            {
                return Status.TargetIsUndefined;
            }

            if (TargetParentOrChildId == UseDraftChildId
                && TargetSlotId == NotConnectedId)
            {
                return Status.TargetIsDraftNode;
            }

            if (SourceParentOrChildId == NotConnectedId
                && SourceSlotId == NotConnectedId)
            {
                return Status.SourceIsUndefined;
            }

            if (SourceParentOrChildId == UseDraftChildId
                && SourceSlotId == NotConnectedId)
            {
                return Status.SourceIsDraftNode;
            }

            if (SourceParentOrChildId == NotConnectedId
                && SourceSlotId == NotConnectedId
                && TargetParentOrChildId == NotConnectedId
                && TargetSlotId == NotConnectedId
               )
            {
                return Status.NotTemporary;
            }

            Log.Warning("Found undefined connection type:" + this);
            return Status.Undefined;
        }


        
        public enum Status
        {
            NotTemporary,
            SourceIsUndefined,
            SourceIsDraftNode,
            TargetIsUndefined,
            TargetIsDraftNode,
            Undefined,
        }
    }

    /// <summary>
    /// Build hashes for symbol specific input slots. These are then used
    /// the compute relevancy.
    /// </summary>
    /// <todo>
    /// Should be fixed and updated
    /// </todo>
    private static void UpdateConnectSlotHashes(IGraphView graph, out int sourceInputHash, out int targetInputHash)
    {
        sourceInputHash = 0;
        targetInputHash = 0;

        var tempConnections = ConnectionMaker.GetTempConnectionsFor(graph);

        foreach (var c in tempConnections)
        {
            switch (c.GetStatus())
            {
                case ConnectionMaker.TempConnection.Status.SourceIsDraftNode:
                    targetInputHash = c.TargetSlotId.GetHashCode();
                    break;

                case ConnectionMaker.TempConnection.Status.TargetIsDraftNode:
                    sourceInputHash = c.SourceSlotId.GetHashCode();
                    break;
            }
        }
    }
    
    public static bool HasTempConnectionsFor(IGraphView graph)
    {
        return _tempConnectionsForGraphViews[graph].TempConnections.Count > 0;
    }

    public static IReadOnlyList<TempConnection> GetTempConnectionsFor(IGraphView graph)
    {
        return _tempConnectionsForGraphViews.TryGetValue(graph, out var inProgress)
                   ? inProgress.TempConnections
                   : _emptyList;
    }

    private static readonly List<TempConnection> _emptyList = [];
}