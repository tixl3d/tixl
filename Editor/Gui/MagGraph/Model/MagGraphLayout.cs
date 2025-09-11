#nullable enable

using System.Diagnostics;
using System.Runtime.CompilerServices;
using T3.Core.Operator;
using T3.Core.Operator.Slots;
using T3.Editor.Gui.Interaction;
using T3.Editor.Gui.MagGraph.Interaction;
using T3.Editor.Gui.MagGraph.States;
using T3.Editor.Gui.OutputUi;
using T3.Editor.Gui.UiHelpers;
using T3.Editor.UiModel;
using T3.Editor.UiModel.InputsAndTypes;

// ReSharper disable MergeIntoPattern

// ReSharper disable ForeachCanBePartlyConvertedToQueryUsingAnotherGetEnumerator
// ReSharper disable ForeachCanBeConvertedToQueryUsingAnotherGetEnumerator
// ReSharper disable SuggestBaseTypeForParameter

namespace T3.Editor.Gui.MagGraph.Model;

/// <summary>
/// Holds an intermediate view model that is updated if required. This view model
/// builds referenceable items, view elements, and connections. This makes it much easier to traverse the
/// graph without dictionary lookups. The layout also precomputes the visibility of input links, which simplifies
/// the layout and rendering of connection lines (one of the most complicated parts of the legacy layout).
/// Generate considerations:
/// - The layout model is a temporary data that is completely generated from the t3-ui data.
/// - Its computation is expensive and should only be done if required.
/// - This might be especially complicated if the state cannot be stored in the layout model, because it
///   relies on a temp. property like expanding a parameter type group.
///
/// It basically separates the following steps:
/// - generating the ui-model data (esp. elements, their inputs and connection slot indices)
/// - Updating the layouts
/// 
/// </summary>
internal sealed class MagGraphLayout
{
    public readonly Dictionary<Guid, MagGraphItem> Items = new(127);
    public readonly List<MagGraphConnection> MagConnections = new(127);
    public readonly Dictionary<Guid, MagGraphAnnotation> Annotations = new(63);

    public void ComputeLayout(GraphUiContext context, bool forceUpdate = false)
    {
        var compositionOp = context.CompositionInstance;

        if (!SymbolUiRegistry.TryGetSymbolUi(compositionOp.Symbol.Id, out var parentSymbolUi))
            return;

        if (forceUpdate || FrameStats.Last.UndoRedoTriggered || StructureFlaggedAsChanged ||
            HasCompositionDataChanged(compositionOp.Symbol, ref _compositionModelHash))
            RefreshDataStructure(context, parentSymbolUi);

        // TODO: This only needs to be done, on structural changes or when items have been moved
        UpdateConnectionLayout();
        ComputeVerticalStackBoundaries(context.View);
    }

    public void FlagStructureAsChanged()
    {
        StructureFlaggedAsChanged = true;
    }

    private int _structureUpdateCycle;

    private void RefreshDataStructure(GraphUiContext context, SymbolUi parentSymbolUi)
    {
        var composition = context.CompositionInstance;

        _structureUpdateCycle++;
        CollectItemReferences(composition, parentSymbolUi);
        CollectedAnnotations(parentSymbolUi);
        UpdateConnectionSources(composition);
        UpdateVisibleItemLines(context);
        CollectConnectionReferences(composition);
        StructureFlaggedAsChanged = false;
    }

    private void CollectedAnnotations(SymbolUi compositionSymbolUi)
    {
        Annotations.Clear();
        var addedCount = 0;
        var updatedCount = 0;

        foreach (var (annotationId, annotation) in compositionSymbolUi.Annotations)
        {
            if (Annotations.TryGetValue(annotationId, out var opItem))
            {
                updatedCount++;
                opItem.LastUpdateCycle = _structureUpdateCycle;
            }
            else
            {
                opItem = new MagGraphAnnotation
                             {
                                 Id = annotation.Id,
                                 Annotation = annotation,
                                 DampedPosOnCanvas = annotation.PosOnCanvas,
                                 DampedSize = annotation.Size,
                             };

                Annotations[annotationId] = opItem;
                addedCount++;
            }
        }

        var hasObsoleteAnnotations = Annotations.Count > updatedCount + addedCount;
        if (!hasObsoleteAnnotations) return;

        foreach (var a in Annotations.Values)
        {
            if (a.LastUpdateCycle >= _structureUpdateCycle)
                continue;

            Annotations.Remove(a.Id);
            a.IsRemoved = true;
        }
    }

    /// <remarks>
    /// This method is extremely slow for large compositions...
    /// </remarks>
    private void CollectItemReferences(Instance compositionOp, SymbolUi compositionSymbolUi)
    {
        //Items.Clear();

        var addedItemCount = 0;
        var updatedItemCount = 0;

        foreach (var (childId, childInstance) in compositionOp.Children)
        {
            if (!compositionSymbolUi.ChildUis.TryGetValue(childId, out var childUi))
                continue;

            if (!SymbolUiRegistry.TryGetSymbolUi(childInstance.Symbol.Id, out var symbolUi))
                continue;

            if (Items.TryGetValue(childId, out var opItem))
            {
                opItem.ResetConnections(_structureUpdateCycle);
                updatedItemCount++;
            }
            else
            {
                opItem = new MagGraphItem
                             {
                                 // Variant = MagGraphItem.Variants.Operator,
                                 Id = childId,
                                 // Instance = childInstance,
                                 Selectable = childUi,
                                 SymbolChild = childInstance.SymbolChild,
                                 ChildUi =  childUi,
                                 DampedPosOnCanvas = childUi.PosOnCanvas,
                                 InstancePath = childInstance.InstancePath,
                                 // SymbolUi = symbolUi,
                                 // SymbolChild = childUi.SymbolChild,
                                 // ChildUi = childUi,
                                 // Size = MagGraphItem.GridSize,
                                 // LastUpdateCycle = _structureUpdateCycle,
                                 // DampedPosOnCanvas = childUi.PosOnCanvas,
                             };
                Items[childId] = opItem;
                addedItemCount++;
            }

            opItem.Variant = MagGraphItem.Variants.Operator;
            //opItem.Id = childId;
            opItem.InstancePath = childInstance.InstancePath;
            opItem.Selectable = childUi;
            opItem.SymbolUi = symbolUi;
            opItem.SymbolChild = childUi.SymbolChild;
            opItem.ChildUi = childUi;
            opItem.Size = MagGraphItem.GridSize;
            opItem.LastUpdateCycle = _structureUpdateCycle;
        }

        foreach (var input in compositionOp.Inputs)
        {
            if (Items.TryGetValue(input.Id, out var inputOp))
            {
                inputOp.ResetConnections(_structureUpdateCycle);
                updatedItemCount++;
            }
            else
            {
                var inputUi = compositionSymbolUi.InputUis[input.Id];
                Items[input.Id] = new MagGraphItem
                                      {
                                          Variant = MagGraphItem.Variants.Input,
                                          Id = input.Id,
                                          SymbolChild = compositionOp.SymbolChild,
                                          InstancePath = compositionOp.InstancePath,
                                          Selectable = inputUi,
                                          Size = MagGraphItem.GridSize,
                                          DampedPosOnCanvas = inputUi.PosOnCanvas,
                                          LastUpdateCycle = _structureUpdateCycle,
                                      };
                addedItemCount++;
            }
        }

        foreach (var output in compositionOp.Outputs)
        {
            var outputUi = compositionSymbolUi.OutputUis[output.Id];
            if (Items.TryGetValue(output.Id, out var outputOp))
            {
                outputOp.ResetConnections(_structureUpdateCycle);
                updatedItemCount++;
            }
            else
            {
                Items[output.Id] = new MagGraphItem
                                       {
                                           Variant = MagGraphItem.Variants.Output,
                                           Id = output.Id,
                                           SymbolChild = compositionOp.SymbolChild,
                                           InstancePath = compositionOp.InstancePath,
                                           Selectable = outputUi,
                                           Size = MagGraphItem.GridSize,
                                           DampedPosOnCanvas = outputUi.PosOnCanvas,
                                       };
                addedItemCount++;
            }
        }

        if (Items.TryGetValue(PlaceholderCreation.PlaceHolderId, out var placeholderOp))
        {
            placeholderOp.ResetConnections(_structureUpdateCycle);
            updatedItemCount++;
        }

        var hasObsoleteItems = Items.Count > updatedItemCount + addedItemCount;
        if (hasObsoleteItems)
        {
            foreach (var item in Items.Values)
            {
                if (item.LastUpdateCycle >= _structureUpdateCycle)
                    continue;

                Items.Remove(item.Id);
                item.Variant = MagGraphItem.Variants.Obsolete;
            }
        }
    }

    private readonly HashSet<int> _connectedOutputs = new(100);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int GetConnectionSourceHash(Symbol.Connection c)
    {
        var hash = c.SourceSlotId.GetHashCode();
        hash = hash * 31 + c.SourceParentOrChildId.GetHashCode();
        return hash;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int GetHashCodeForOutputSlot(ISlot output)
    {
        var hash = output.Id.GetHashCode();
        hash = hash * 31 + output.Parent.SymbolChildId.GetHashCode();
        return hash;
    }

    /// <summary>
    /// Sadly there is no obvious easy method to store if an output has a connection.
    /// So we collect connected outputs in a hashset. 
    /// </summary>
    private void UpdateConnectionSources(Instance composition)
    {
        _connectedOutputs.Clear();

        foreach (var c in composition.Symbol.Connections)
        {
            if (c.IsConnectedToSymbolInput)
                continue;

            _connectedOutputs.Add(GetConnectionSourceHash(c));
        }
    }

    private void UpdateVisibleItemLines(GraphUiContext context)
    {
        var inputLines = new List<MagGraphItem.InputLine>(8);
        var outputLines = new List<MagGraphItem.OutputLine>(4);

        foreach (var item in Items.Values)
        {
            inputLines.Clear();
            outputLines.Clear();

            var visibleIndex = 0;

            switch (item.Variant)
            {
                // Collect inputs
                case MagGraphItem.Variants.Operator:
                {
                    visibleIndex = CollectVisibleLines(context, item, inputLines, outputLines, _connectedOutputs);
                    break;
                }
                case MagGraphItem.Variants.Input:
                {
                    Debug.Assert(item.Selectable is IInputUi);
                    var parentInstance = item.Instance;

                    Debug.Assert(parentInstance != null);
                    var parentInput = parentInstance.Inputs.FirstOrDefault(i => i.Id == item.Id);

                    outputLines.Add(new MagGraphItem.OutputLine
                                        {
                                            Output = parentInput!, // This looks confusing but is correct
                                            Id = parentInput!.Id,
                                            OutputUi = null,
                                            OutputIndex = 0,
                                            VisibleIndex = 0,
                                            ConnectionsOut = [],
                                        });

                    item.PrimaryType = parentInput.ValueType;
                    break;
                }
                case MagGraphItem.Variants.Output:
                {
                    Debug.Assert(item.Selectable is IOutputUi);

                    var parentInstance = item.Instance;
                    Debug.Assert(parentInstance != null);

                    var parentOutput = parentInstance.Outputs.FirstOrDefault(o => o.Id == item.Id);
                    Debug.Assert(parentOutput != null);

                    inputLines.Add(new MagGraphItem.InputLine
                                       {
                                           Type = parentOutput.ValueType,
                                           Id = parentOutput.Id,
                                           Input = parentOutput,
                                           MultiInputIndex = 0,
                                           VisibleIndex = 0,
                                       });

                    item.PrimaryType = parentOutput.ValueType;
                    break;
                }
                case MagGraphItem.Variants.Placeholder:
                    break;

                default:
                    throw new ArgumentOutOfRangeException();
            }

            item.InputLines = inputLines.ToArray();
            item.OutputLines = outputLines.ToArray();

            //var count = Math.Max(1, item.InputLines.Count + item.OutputLines.Count -2);
            item.Size = new Vector2(MagGraphItem.Width, MagGraphItem.LineHeight * (Math.Max(1, visibleIndex)));
        }
    }

    /// <summary>
    /// Aggregate visible inputs and outputs into a consistent stack
    /// </summary>
    /// <remarks>
    /// This is accessible because for some use-cases we need to compute the height of inserted items.
    /// </remarks>
    internal static int CollectVisibleLines(GraphUiContext context, MagGraphItem item, List<MagGraphItem.InputLine> inputLines,
                                            List<MagGraphItem.OutputLine> outputLines,
                                            HashSet<int>? connectedOutputs = null)
    {
        var instance = item.Instance;
        Debug.Assert(instance != null && item.SymbolUi != null);
        int visibleIndex = 0;
        item.HasHiddenOutputs = false;

        for (var inputLineIndex = 0; inputLineIndex < instance.Inputs.Count; inputLineIndex++)
        {
            var input = instance.Inputs[inputLineIndex];
            if (!item.SymbolUi.InputUis.TryGetValue(input.Id, out var inputUi))
            {
                Log.Warning("Can't find input? " + input.Id);
                continue;
            }

            var isRelevant = inputUi.Relevancy is Relevancy.Relevant or Relevancy.Required;
            //var isMatchingType = false; //input.ValueType == typeof(float);//ConnectionTargetType;

            var isPrimaryInput = inputLineIndex == 0;

            if (input.IsMultiInput && input is IMultiInputSlot)
            {
                var shouldInputBeVisible = isRelevant || isPrimaryInput || input.HasInputConnections;

                var connectionsToInput = context
                                        .CompositionInstance
                                        .Symbol
                                        .Connections
                                        .FindAll(c => c.TargetParentOrChildId == item.Id
                                                      && c.TargetSlotId == input.Id);

                var multiConIndex = 0;
                var virtualConnectionCount = 0; // including disconnected
                var tempConnectionCount = 0; // count additional to connectionsToInput.count
                for (var virtualSubIndex = 0; virtualSubIndex < connectionsToInput.Count + tempConnectionCount + 1; virtualSubIndex++)
                {
                    if (IsDisconnectedVisibleMultiInputLine(context, item.Id, input.Id, visibleIndex))
                    {
                        inputLines.Add(new MagGraphItem.InputLine
                                           {
                                               Id = input.Id,
                                               Type = input.ValueType,
                                               Input = input,
                                               InputUi = inputUi,
                                               VisibleIndex = visibleIndex,
                                               MultiInputIndex = multiConIndex,
                                               ConnectionState = MagGraphItem.InputLineStates.TempConnection,
                                           });
                        visibleIndex++;
                        virtualConnectionCount++;
                        tempConnectionCount++;
                        continue;
                    }

                    if (shouldInputBeVisible && multiConIndex < connectionsToInput.Count)
                    {
                        inputLines.Add(new MagGraphItem.InputLine
                                           {
                                               Id = input.Id,
                                               Type = input.ValueType,
                                               Input = input,
                                               InputUi = inputUi,
                                               VisibleIndex = visibleIndex,
                                               MultiInputIndex = multiConIndex,
                                               ConnectionState = MagGraphItem.InputLineStates.Connected,
                                           });
                        virtualConnectionCount++;
                        visibleIndex++;
                        multiConIndex++;
                    }
                }

                // Show input even it not connected
                if (shouldInputBeVisible && virtualConnectionCount == 0)
                {
                    inputLines.Add(new MagGraphItem.InputLine
                                       {
                                           Id = input.Id,
                                           Type = input.ValueType,
                                           Input = input,
                                           InputUi = inputUi,
                                           VisibleIndex = visibleIndex,
                                           MultiInputIndex = multiConIndex,
                                           ConnectionState = MagGraphItem.InputLineStates.NotConnected,
                                       });
                    visibleIndex++;
                }
            }
            else
            {
                var hasInputConnections = input.HasInputConnections;
                if (isRelevant || isPrimaryInput || hasInputConnections)
                {
                    inputLines.Add(new MagGraphItem.InputLine
                                       {
                                           Id = input.Id,
                                           Type = input.ValueType,
                                           Input = input,
                                           InputUi = inputUi,
                                           VisibleIndex = visibleIndex,
                                           ConnectionState = hasInputConnections
                                                                 ? MagGraphItem.InputLineStates.Connected
                                                                 : MagGraphItem.InputLineStates.NotConnected,
                                       });
                    visibleIndex++;
                    continue;
                }

                var hasTempConnection = context.DisconnectedInputHashes.Count > 0
                                        && context.DisconnectedInputHashes.Contains(MagGraphConnection.GetItemInputHash(item.Id, input.Id, 0));
                if (hasTempConnection)
                {
                    inputLines.Add(new MagGraphItem.InputLine
                                       {
                                           Id = input.Id,
                                           Type = input.ValueType,
                                           Input = input,
                                           InputUi = inputUi,
                                           VisibleIndex = visibleIndex,
                                           ConnectionState = MagGraphItem.InputLineStates.TempConnection,
                                       });
                    visibleIndex++;
                }
            }
        }

        var hasNoInputs = visibleIndex == 0;

        // Collect outputs
        for (var outputIndex = 0; outputIndex < instance.Outputs.Count; outputIndex++)
        {
            var output = instance.Outputs[outputIndex];
            if (!item.SymbolUi.OutputUis.TryGetValue(output.Id, out var outputUi))
            {
                Log.Warning("Can't find outputUi:" + output.Id);
                continue;
            }

            // Should non connected secondary outputs be visible?
            int outputHash2 = GetHashCodeForOutputSlot(output);
            var isConnected = connectedOutputs != null && connectedOutputs.Contains(outputHash2);
            if (outputIndex > 0 && !isConnected)
            {
                item.HasHiddenOutputs = true;
                continue;
            }

            outputLines.Add(new MagGraphItem.OutputLine
                                {
                                    Id = outputUi.Id,
                                    Output = output,
                                    OutputUi = outputUi,
                                    OutputIndex = outputIndex,
                                    VisibleIndex = outputIndex == 0 ? 0 : visibleIndex,
                                    ConnectionsOut = [],
                                });
            if (outputIndex == 0)
            {
                item.PrimaryType = output.ValueType;
                if (hasNoInputs)
                    visibleIndex++;
            }
            else
            {
                visibleIndex++;
            }
        }

        // Fix height

        return visibleIndex;
    }

    private static bool IsDisconnectedVisibleMultiInputLine(GraphUiContext context, Guid itemId, Guid inputId,
                                                            int visibleInputIndex)
    {
        var itemInputHash = MagGraphConnection.GetItemInputHash(itemId, inputId, visibleInputIndex);
        var isDisconnectedVisibleMultiInputLine =
            context.DisconnectedInputHashes.Count > 0
            && context.DisconnectedInputHashes.Contains(itemInputHash);
        return isDisconnectedVisibleMultiInputLine;
    }

    private void CollectConnectionReferences(Instance composition)
    {
        MagConnections.Clear();
        MagConnections.Capacity = composition.Symbol.Connections.Count;

        Symbol.Connection c; // Avoid repetitive closure capture
        for (var cIndex = 0; cIndex < composition.Symbol.Connections.Count; cIndex++)
        {
            c = composition.Symbol.Connections[cIndex];

            if (c.IsConnectedToSymbolInput)
            {
                if (!Items.TryGetValue(c.TargetParentOrChildId, out var targetFromInputItem)
                    || !Items.TryGetValue(c.SourceSlotId, out var symbolInputItem))
                    continue;

                Debug.Assert(targetFromInputItem.Instance != null);

                var symbolInput = composition.Inputs.FirstOrDefault(i => i.Id == c.SourceSlotId);
                var targetInput = targetFromInputItem.Instance.Inputs.FirstOrDefault(i => i.Input.InputDefinition.Id == c.TargetSlotId);
                Debug.Assert(targetInput != null);

                FindVisibleIndex(targetFromInputItem, targetInput, out var targetInputIndex, out var targetMultiInputIndex);

                var connectionFromSymbolInput = new MagGraphConnection
                                                    {
                                                        Style = MagGraphConnection.ConnectionStyles.Unknown,
                                                        SourceItem = symbolInputItem,
                                                        SourceOutput = symbolInput,
                                                        TargetItem = targetFromInputItem,
                                                        //TargetInput = targetInput,
                                                        InputLineIndex = targetInputIndex,
                                                        OutputLineIndex = 0,
                                                        ConnectionHash = c.GetHashCode(),
                                                        MultiInputIndex = targetMultiInputIndex,
                                                        VisibleOutputIndex = 0,
                                                    };

                symbolInputItem.OutputLines[0].ConnectionsOut.Add(connectionFromSymbolInput);
                targetFromInputItem.InputLines[targetInputIndex].ConnectionIn = connectionFromSymbolInput;
                MagConnections.Add(connectionFromSymbolInput);
                continue;
            }

            if (c.IsConnectedToSymbolOutput)
            {
                if (!Items.TryGetValue(c.SourceParentOrChildId, out var sourceItem2)
                    || !Items.TryGetValue(c.TargetSlotId, out var symbolOutputItem))
                {
                    Log.Warning("Inconsistent output connection " + c);
                    continue;
                }

                Debug.Assert(sourceItem2.Instance != null);

                //var symbolOutput = composition.Outputs.FirstOrDefault((o => o.Id == c.TargetSlotId));
                var sourceOutput = sourceItem2.Instance.Outputs.FirstOrDefault(o => o.Id == c.SourceSlotId);

                Debug.Assert(sourceOutput != null);
                var outputIndex2 = 0;
                foreach (var outLine in sourceItem2.OutputLines)
                {
                    if (outLine.Output != sourceOutput)
                        continue;

                    outputIndex2 = outLine.OutputIndex;
                    break;
                }

                if (outputIndex2 >= sourceItem2.OutputLines.Length)
                {
                    //Log.Warning($"OutputIndex {outputIndex} exceeds number of output lines {sourceItem.OutputLines.Length} in {sourceItem}");
                    outputIndex2 = sourceItem2.OutputLines.Length - 1;
                }

                var connectionFromSymbolInput = new MagGraphConnection
                                                    {
                                                        Style = MagGraphConnection.ConnectionStyles.Unknown,
                                                        SourceItem = sourceItem2,
                                                        SourceOutput = sourceOutput,
                                                        TargetItem = symbolOutputItem,
                                                        //TargetInput = targetInput,
                                                        InputLineIndex = 0,
                                                        OutputLineIndex = 0,
                                                        ConnectionHash = c.GetHashCode(),
                                                        MultiInputIndex = 0,
                                                        VisibleOutputIndex = 0,
                                                    };

                sourceItem2.OutputLines[outputIndex2].ConnectionsOut.Add(connectionFromSymbolInput);
                symbolOutputItem.InputLines[0].ConnectionIn = connectionFromSymbolInput;
                MagConnections.Add(connectionFromSymbolInput);
                continue;
            }

            // Skip connection to symbol inputs and outputs for now
            if (c.IsConnectedToSymbolOutput
                || !Items.TryGetValue(c.SourceParentOrChildId, out var sourceItem)
                || !Items.TryGetValue(c.TargetParentOrChildId, out var targetItem)
               )
            {
                //Log.Warning("Can't find items for connection line");
                continue;
            }

            // Connections between nodes
            if (sourceItem.Instance == null || targetItem.Instance == null)
                continue;

            var output = sourceItem.Variant == MagGraphItem.Variants.Input
                             ? sourceItem.Instance.Inputs.FirstOrDefault(o => o.Id == c.SourceSlotId)
                             : sourceItem.Instance.Outputs.FirstOrDefault(o => o.Id == c.SourceSlotId);

            var input = targetItem.Instance.Inputs.FirstOrDefault(i => i.Input.InputDefinition.Id == c.TargetSlotId);

            if (output == null || input == null)
            {
                Log.Warning("Unable to find connected items?");
                continue;
            }

            FindVisibleIndex(targetItem, input, out var inputLineIndex, out var multiInputIndex2);

            int outputLineIndex;
            for (outputLineIndex = 0; outputLineIndex < sourceItem.OutputLines.Length; outputLineIndex++)
            {
                if (sourceItem.OutputLines[outputLineIndex].Output == output)
                    break;
            }

            if (outputLineIndex >= sourceItem.OutputLines.Length)
            {
                Log.Warning($"OutputIndex {outputLineIndex} exceeds number of output lines {sourceItem.OutputLines.Length} in {sourceItem}");
                outputLineIndex = sourceItem.OutputLines.Length - 1;
            }

            var snapGraphConnection = new MagGraphConnection
                                          {
                                              Style = MagGraphConnection.ConnectionStyles.Unknown,
                                              SourceItem = sourceItem,
                                              SourceOutput = output,
                                              TargetItem = targetItem,
                                              InputLineIndex = inputLineIndex,
                                              OutputLineIndex = outputLineIndex,
                                              ConnectionHash = c.GetHashCode(),
                                              MultiInputIndex = multiInputIndex2,
                                              VisibleOutputIndex = sourceItem.OutputLines[outputLineIndex].VisibleIndex,
                                          };

            targetItem.InputLines[inputLineIndex].ConnectionIn = snapGraphConnection;
            sourceItem.OutputLines[outputLineIndex].ConnectionsOut.Add(snapGraphConnection);
            MagConnections.Add(snapGraphConnection);
        }
    }

    /// <summary>
    /// This method needs to be rethought and cleaned up.
    /// MultiInputIndex will always return max count?
    /// </summary>
    private static void FindVisibleIndex(MagGraphItem targetItem, IInputSlot input, out int visibleInputIndex, out int multiInputIndex)
    {
        // Find connected index
        multiInputIndex = 0;
        for (visibleInputIndex = 0; visibleInputIndex < targetItem.InputLines.Length; visibleInputIndex++)
        {
            // Count other inputs
            var targetItemInputLine = targetItem.InputLines[visibleInputIndex];
            if (targetItemInputLine.Id != input.Id)
                continue;

            // Skip temp connections
            if (targetItemInputLine.ConnectionState == MagGraphItem.InputLineStates.TempConnection)
                continue;

            // Skip already connected multi-inputs slots...
            // (This assumes ConnectionIn to be nullified before using this)
            if (targetItemInputLine.ConnectionIn != null && visibleInputIndex < targetItem.InputLines.Length)
            {
                multiInputIndex++;
                continue;
            }

            // Is a match
            return;
        }
        Log.Warning("Input line not found?");
    }

    // Reuse list to avoid allocations
    private static readonly List<MagGraphItem> _listStackedItems = new(32);

    /// <summary>
    /// This improves the layout of arc connections inputs into multiple stacked ops so they
    /// avoid overlap.
    /// </summary>
    private void ComputeVerticalStackBoundaries(ScalableCanvas canvas)
    {
        MagGraphItem? previousItem = null;

        _listStackedItems.Clear();
        foreach (var item in Items.Values.OrderBy(i => MathF.Round(i.PosOnCanvas.X)).ThenBy(i => i.PosOnCanvas.Y))
        {
            item.VerticalStackArea = item.Area;

            if (previousItem == null)
            {
                _listStackedItems.Clear();
                _listStackedItems.Add(item);
                previousItem = item;
                continue;
            }

            // is stacked?
            if (!(Math.Abs(item.PosOnCanvas.X - previousItem.PosOnCanvas.X) < 10f)
                || !(Math.Abs(item.PosOnCanvas.Y - previousItem.Area.Max.Y) < 180f))
            {
                ApplyStackToItems();
            }

            _listStackedItems.Add(item);
            previousItem = item;
        }

        ApplyStackToItems();

        return;

        void ApplyStackToItems()
        {
            if (_listStackedItems.Count > 1)
            {
                var stackArea = new ImRect(_listStackedItems[0].PosOnCanvas,
                                           _listStackedItems[^1].Area.Max);
                foreach (var x in _listStackedItems)
                {
                    x.VerticalStackArea = stackArea;
                }

                // Draw Debug
                // var aOnScreen = canvas.TransformRect(stackArea);
                // ImGui.GetWindowDrawList().AddRect(aOnScreen.Min, aOnScreen.Max, Color.Green);
            }

            _listStackedItems.Clear();
        }
    }

    /// <summary>
    /// Update the layout of the connections (e.g. if they are snapped, of flowing vertically or horizontally)
    /// </summary>
    private void UpdateConnectionLayout()
    {
        foreach (var sc in MagConnections)
        {
            var sourceMin = sc.SourceItem.PosOnCanvas;
            var sourceMax = sourceMin + sc.SourceItem.Size;

            var targetMin = sc.TargetItem.PosOnCanvas;

            // Snapped horizontally
            if (
                MathF.Abs(sourceMax.X - targetMin.X) < 1
                && MathF.Abs((sourceMin.Y + sc.VisibleOutputIndex * MagGraphItem.GridSize.Y)
                             - (targetMin.Y + sc.InputLineIndex * MagGraphItem.GridSize.Y)) < 1)
            {
                sc.Style = sc.InputLineIndex == 0
                               ? MagGraphConnection.ConnectionStyles.MainOutToMainInSnappedHorizontal
                               : MagGraphConnection.ConnectionStyles.MainOutToInputSnappedHorizontal;

                var p = new Vector2(sourceMax.X, sourceMin.Y + (+sc.VisibleOutputIndex + 0.5f) * MagGraphItem.GridSize.Y);
                sc.SourcePos = p;
                sc.TargetPos = p;
                continue;
            }

            if (sc.InputLineIndex == 0
                && sc.OutputLineIndex == 0
                && MathF.Abs(sourceMin.X - targetMin.X) < 1
                && MathF.Abs(sourceMax.Y - targetMin.Y) < 1)
            {
                sc.Style = MagGraphConnection.ConnectionStyles.MainOutToMainInSnappedVertical;
                var p = new Vector2(sourceMin.X + MagGraphItem.GridSize.X / 2, targetMin.Y);
                sc.SourcePos = p;
                sc.TargetPos = p;
                continue;
            }

            if (sc.OutputLineIndex == 0
                && sc.InputLineIndex > 0
                && MathF.Abs(sourceMax.X - targetMin.X) < 1
                && MathF.Abs(sourceMin.Y - (targetMin.Y + sc.VisibleOutputIndex * MagGraphItem.GridSize.Y)) < 1
               )
            {
                //sc.Style = MagGraphConnection.ConnectionStyles.MainOutToInputSnappedHorizontal;
                sc.Style = MagGraphConnection.ConnectionStyles.BottomToLeft;
                var p = new Vector2(sourceMax.X, targetMin.Y + (0.5f + sc.InputLineIndex) * MagGraphItem.GridSize.Y);
                sc.SourcePos = new Vector2((sourceMin.X + sourceMax.X) / 2, sourceMax.Y);
                sc.TargetPos = p;
                continue;
            }

            if (sc.OutputLineIndex > 0
                && sc.InputLineIndex == 0
                && MathF.Abs(sourceMax.X - targetMin.Y) < 1
                && MathF.Abs(sourceMax.Y + (1 + sc.SourceItem.OutputLines.Length + sc.VisibleOutputIndex) * MagGraphItem.GridSize.Y) < 1)
            {
                sc.Style = MagGraphConnection.ConnectionStyles.AdditionalOutToMainInputSnappedVertical;
                var p = new Vector2(sourceMax.X, targetMin.Y + 0.5f * MagGraphItem.GridSize.Y);
                sc.SourcePos = p;
                sc.TargetPos = p;
                continue;
            }

            if (sc.OutputLineIndex == 0
                && sc.InputLineIndex == 0
                && sourceMax.Y < targetMin.Y
                //&& MathF.Abs(sourceMin.X - targetMin.X) < MagGraphItem.GridSize.X / 2
                && sourceMin.X > targetMin.X - MagGraphItem.GridSize.X / 2
               )
            {
                sc.SourcePos = new Vector2(sourceMin.X + MagGraphItem.GridSize.X / 2, sourceMax.Y);
                sc.TargetPos = new Vector2(targetMin.X + MagGraphItem.GridSize.X / 2, targetMin.Y);
                sc.Style = MagGraphConnection.ConnectionStyles.BottomToTop;
                continue;
            }

            sc.SourcePos = new Vector2(sourceMax.X, sourceMin.Y + (sc.VisibleOutputIndex + 0.5f) * MagGraphItem.GridSize.Y);
            sc.TargetPos = new Vector2(targetMin.X, targetMin.Y + (sc.InputLineIndex + 0.5f) * MagGraphItem.GridSize.Y);

            sc.Style = MagGraphConnection.ConnectionStyles.RightToLeft;

            //TODO: Snapped from output
            //TODO: Snapped to input
            //TODO: Snapped vertically
        }
    }

    /// <summary>
    /// We rely on manually flagging structure changes, because 
    /// computing a hash of a composition is not easy because the item order of children can change...
    /// </summary>
    private static bool HasCompositionDataChanged(Symbol composition, ref int originalHash)
    {
        var newHash = composition.Id.GetHashCode();

        if (newHash == originalHash)
            return false;

        originalHash = newHash;
        return true;
    }

    private int _compositionModelHash;
    private bool StructureFlaggedAsChanged { get; set; }
}