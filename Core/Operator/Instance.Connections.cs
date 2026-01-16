using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using T3.Core.DataTypes;
using T3.Core.Logging;
using T3.Core.Operator.Slots;
using T3.Core.Utils;

namespace T3.Core.Operator;

public abstract partial class Instance
{
    internal void ReconnectChildren()
    {
        if (!NeedsInternalReconnections)
            return;

        if (TryGetParentInstance(out var parent, false) && parent is { NeedsInternalReconnections: true })
        {
            // if the parent instance also needs reconnections, we return early
            // as the parent will trigger this method in its children as well
            parent.ReconnectChildren();
            return;
        }
        
        // prevent recursion by setting the connection status prematurely
        _status |= InstanceStatus.ConnectedInternally;
        _status |= InstanceStatus.IsReconnecting;

        // first removes all connections from direct children
        // we do this so we can blindly re-apply all of our connections
        foreach (var childInst in Children.PreExistingValues)
        {
            childInst.DisconnectInputs();
        }

        // ensure all of our children have their own internall connections connected (recursive)
        foreach (var child in SymbolChild.Symbol.Children.Values)
        {
            if (Children.TryGetChildInstance(child.Id, out var childInst) )
            {
                if(childInst.NeedsInternalReconnections)
                    childInst.ReconnectChildren();
            }
            else
            {
                Log.Error($"Failed to create/locate child instance {child.Id} in {SymbolChild}");
            }
        }
        
        // actually create the connections for this instance and its children
        CreateConnectionsForInstance(this);
        _status &= ~InstanceStatus.IsReconnecting; // clear reconnecting status
        return;
        
        static void CreateConnectionsForInstance(Instance instance)
        {
            // create connections between child instances populated with CreateAndAddNewChildInstance
            var child = instance.SymbolChild;
            var symbol = instance.Symbol;
            var connections = symbol.Connections;
        
            // if connections already exist for the symbol, remove any that shouldn't exist anymore
            if (connections.Count != 0)
            {
                var conHashToCount = new Dictionary<ulong, int>(connections.Count);
                for (var index = 0; index < connections.Count; index++) // warning: the order in which these are processed matters
                {
                    var connection = connections[index];
                    ulong highPart = 0xFFFFFFFF & (ulong)connection.TargetSlotId.GetHashCode();
                    ulong lowPart = 0xFFFFFFFF & (ulong)connection.TargetParentOrChildId.GetHashCode();
                    ulong hash = (highPart << 32) | lowPart;
                    conHashToCount.TryGetValue(hash, out int count);

                    if (!instance.TryAddConnection(connection, count, true))
                    {
                        Log.Warning($"Removing obsolete connecting in {symbol}...");
                        // todo: this removal should be moved into the Symbol class
                        connections.RemoveAt(index);
                        index--;
                        continue;
                    }

                    conHashToCount[hash] = count + 1;
                }
            }

            // connect animations if available
            // note: by accessing the Values property, all children are guaranteed to be created, even if they are not connected to anything
            symbol.Animator.CreateUpdateActionsForExistingCurves(instance.Children.Values);

            if (child.IsBypassed)
            {
                SetBypassFor(instance, true, invalidate: false);
            }
        }
    }

    // disconnects all inputs of this instance - all considered "external" connections
    private int DisconnectInputs()
    {
        // clear our connections - we may be reassigned to another parent
        int disconnectCount = 0;
        for (var index = 0; index < _inputs.Count; index++)
        {
            var input = _inputs[index];
            while (input.HasInputConnections)
            {
                input.RemoveConnection();
                ++disconnectCount;
            }
        }

        return disconnectCount;
    }

    internal bool TryGetTargetSlot(Symbol.Connection connection, [NotNullWhen(true)] out ISlot targetSlot, bool allowCreate)
    {
        // Get target Instance
        var targetParentOrChildId = connection.TargetParentOrChildId;
        IEnumerable<ISlot> targetSlotList;

        if (targetParentOrChildId == Guid.Empty)
        {
            targetSlotList = Outputs;
        }
        else
        {
            if (!Children.TryGetChildInstance(targetParentOrChildId, out var targetInstance, allowCreate))
            {
                targetSlot = null;
                return false;
            }

            targetSlotList = targetInstance.Inputs;
        }

        foreach (var slot in targetSlotList)
        {
            if (slot.Id != connection.TargetSlotId)
                continue;

            targetSlot = slot;
            return true;
        }

        targetSlot = null;
        return false;
    }

    internal static void SortInputSlotsByDefinitionOrder(Instance instance)
    {
        // order the inputs by the given input definitions. original order is coming from code, but input def order is the relevant one
        var inputs = instance._inputs;
        var inputDefinitions = instance.Symbol.InputDefinitions;
        int numInputs = inputs.Count;
        var lastIndex = numInputs - 1;

        for (int i = 0; i < lastIndex; i++)
        {
            Guid inputId = inputDefinitions[i].Id;
            if (inputs[i].Id != inputId)
            {
                int index = inputs.FindIndex(i + 1, input => input.Id == inputId);
                if (index == -1)
                    continue;
                //Debug.Assert(index >= 0);
                inputs.Swap(i, index);
                Debug.Assert(inputId == inputs[i].Id);
            }
        }

        #if DEBUG
        if (numInputs > 0)
        {
            #if SKIP_ASSERTS
                Debug.Assert(inputs.Count == inputDefinitions.Count);
            #endif
        }
        #endif
    }

    private bool TryGetSourceSlot(Symbol.Connection connection, [NotNullWhen(true)] out ISlot sourceSlot, bool allowCreate)
    {
        // Get source Instance
        IEnumerable<ISlot> sourceSlotList;

        var sourceParentOrChildId = connection.SourceParentOrChildId;
        if (sourceParentOrChildId == Guid.Empty)
        {
            sourceSlotList = Inputs;
        }
        else
        {
            if (!Children.TryGetChildInstance(sourceParentOrChildId, out var sourceInstance, allowCreate))
            {
                sourceSlot = null;
                return false;
            }

            sourceSlotList = sourceInstance.Outputs;
        }

        // Get source Slot
        sourceSlot = null;
        var gotSourceSlot = false;

        foreach (var slot in sourceSlotList)
        {
            if (slot.Id != connection.SourceSlotId)
                continue;

            sourceSlot = slot;
            gotSourceSlot = true;
            break;
        }

        return gotSourceSlot;
    }

    internal bool TryAddConnection(Symbol.Connection connection, int multiInputIndex, bool allowCreate)
    {
        if (!TryGetSourceSlot(connection, out var sourceSlot, allowCreate) ||
            !TryGetTargetSlot(connection, out var targetSlot, allowCreate))
            return false;

        targetSlot.AddConnection(sourceSlot, multiInputIndex);
        sourceSlot.DirtyFlag.Invalidate();
        return true;
    }

    private protected void SetupInputAndOutputsFromType()
    {
        var symbol = Symbol;
        var assemblyInfo = symbol.SymbolPackage.AssemblyInformation;
        if (!assemblyInfo.OperatorTypeInfo.TryGetValue(symbol.Id, out var operatorTypeInfo))
        {
            Log.Error($"Can't find operatorTypeInfo for id {symbol} {symbol.Id} in {assemblyInfo}");
            Debug.Assert(false);
        }

        //var operatorTypeInfo = assemblyInfo.OperatorTypeInfo[symbol.Id];
        foreach (var input in operatorTypeInfo.Inputs)
        {
            var attribute = input.Attribute;
            var inputSlot = input.GetSlotObject(this);
            inputSlot.Parent = this;
            inputSlot.Id = attribute.Id;
            inputSlot.MappedType = attribute.MappedType;
            _inputs.Add(inputSlot);
        }

        // outputs identified by attribute
        foreach (var output in operatorTypeInfo.Outputs)
        {
            var slot = output.GetSlotObject(this);
            slot.Parent = this;
            slot.Id = output.Attribute.Id;
            _outputs.Add(slot);
        }
    }

    internal static bool SetBypassFor(Instance instance, bool shouldBypass, bool invalidate = true)
    {
        var mainInputSlot = instance.Inputs[0];
        var mainOutputSlot = instance.Outputs[0];

        var wasByPassed = false;

        // note - can this be made more flexible by not having a "main" input/output requirement and instead
        // matching any one-to-one input/output type pairs?
        
        switch (mainOutputSlot)
        {
            case Slot<Command> commandOutput when mainInputSlot is Slot<Command> commandInput:
                if (shouldBypass)
                {
                    wasByPassed = commandOutput.TrySetBypassToInput(commandInput);
                }
                else
                {
                    commandOutput.RestoreUpdateAction();
                }

                if (invalidate)
                    InvalidateConnected(commandInput);
                break;

            case Slot<BufferWithViews> bufferOutput when mainInputSlot is Slot<BufferWithViews> bufferInput:
                if (shouldBypass)
                {
                    wasByPassed = bufferOutput.TrySetBypassToInput(bufferInput);
                }
                else
                {
                    bufferOutput.RestoreUpdateAction();
                }

                if (invalidate)
                    InvalidateConnected(bufferInput);

                break;
            case Slot<MeshBuffers> bufferOutput when mainInputSlot is Slot<MeshBuffers> bufferInput:
                if (shouldBypass)
                {
                    wasByPassed = bufferOutput.TrySetBypassToInput(bufferInput);
                }
                else
                {
                    bufferOutput.RestoreUpdateAction();
                }

                if (invalidate)
                    InvalidateConnected(bufferInput);

                break;
            case Slot<Texture2D> texture2dOutput when mainInputSlot is Slot<Texture2D> texture2dInput:
                if (shouldBypass)
                {
                    wasByPassed = texture2dOutput.TrySetBypassToInput(texture2dInput);
                }
                else
                {
                    texture2dOutput.RestoreUpdateAction();
                }

                if (invalidate)
                    InvalidateConnected(texture2dInput);

                break;
            case Slot<float> floatOutput when mainInputSlot is Slot<float> floatInput:
                if (shouldBypass)
                {
                    wasByPassed = floatOutput.TrySetBypassToInput(floatInput);
                }
                else
                {
                    floatOutput.RestoreUpdateAction();
                }

                if (invalidate)
                    InvalidateConnected(floatInput);

                break;

            case Slot<Vector2> vec2Output when mainInputSlot is Slot<Vector2> vec2Input:
                if (shouldBypass)
                {
                    wasByPassed = vec2Output.TrySetBypassToInput(vec2Input);
                }
                else
                {
                    vec2Output.RestoreUpdateAction();
                }

                if (invalidate)
                    InvalidateConnected(vec2Input);

                break;
            case Slot<Vector3> vec3Output when mainInputSlot is Slot<Vector3> vec3Input:
                if (shouldBypass)
                {
                    wasByPassed = vec3Output.TrySetBypassToInput(vec3Input);
                }
                else
                {
                    vec3Output.RestoreUpdateAction();
                }

                if (invalidate)
                    InvalidateConnected(vec3Input);

                break;
            case Slot<string> stringOutput when mainInputSlot is Slot<string> stringInput:
                if (shouldBypass)
                {
                    wasByPassed = stringOutput.TrySetBypassToInput(stringInput);
                }
                else
                {
                    stringOutput.RestoreUpdateAction();
                }

                if (invalidate)
                    InvalidateConnected(stringInput);
                break;
        }
        
        if(wasByPassed)
            instance._status |= InstanceStatus.Bypassed;
        else
            instance._status &= ~InstanceStatus.Bypassed;

        return wasByPassed;
        
        static void InvalidateConnected<T>(Slot<T> bufferInput)
        {
            if (bufferInput.TryGetAsMultiInputTyped(out var multiInput))
            {
                foreach (var connection in multiInput.CollectedInputs)
                {
                    InvalidateParentInputs(connection);
                }
            }
            else
            {
                var connection = bufferInput.FirstConnection;
                InvalidateParentInputs(connection);
            }

            return;

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            void InvalidateParentInputs(ISlot connection)
            {
                if (connection.ValueType == typeof(string)) // TODO: clarify and comment why whe need this check
                    return;

                connection.DirtyFlag.Invalidate();
            }
        }
    }

    private void MarkNeedsConnections()
    {
        _status &= ~InstanceStatus.ConnectedInternally;

        if (TryGetParentInstance(out var parent, false))
        {
            parent.MarkNeedsConnections();
        }
    }
}