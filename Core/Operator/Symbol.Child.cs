#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using T3.Core.Compilation;
using T3.Core.DataTypes;
using T3.Core.DataTypes.ShaderGraph;
using T3.Core.Logging;
using T3.Core.Operator.Slots;
using T3.Core.Utils;
using Texture2D = T3.Core.DataTypes.Texture2D;

namespace T3.Core.Operator;

public partial class Symbol
{
    /// <summary>
    /// Represents an instance of a <see cref="Symbol"/> within a Symbol.
    /// </summary>
    public sealed class Child
    {
        /// <summary>A reference to the <see cref="Symbol"/> this is an instance from.</summary>
        public Symbol Symbol { get; init; }

        public Guid Id { get; }

        public Symbol? Parent { get; }

        public string Name { get; set; }

        public string ReadableName => string.IsNullOrEmpty(Name) ? Symbol.Name : Name;
        public bool HasCustomName => !string.IsNullOrEmpty(Name);

        public bool IsBypassed { get => _isBypassed; set => SetBypassed(value); }
        public bool IsDisabled
        {
            get
            {
                // Avoid LINQ because of allocations in inner loop
                foreach (var x in Outputs.Values)
                {
                    if (x.IsDisabled)
                        return true;
                }

                return false;
                //return Outputs.FirstOrDefault().Value?.IsDisabled ?? false;
            }
            set => SetDisabled(value);
        }

        public Dictionary<Guid, Input> Inputs { get; private init; } = new();
        public Dictionary<Guid, Output> Outputs { get; private init; } = new();
        internal IReadOnlyList<Instance> Instances => _instancesOfSelf;
        private readonly List<Instance> _instancesOfSelf = new();

        private readonly bool _isGeneric;

        internal Child(Symbol symbol, Guid childId, Symbol? parent, string? name, bool isBypassed)
        {
            Symbol = symbol;
            Id = childId;
            Parent = parent;
            Name = name ?? string.Empty;
            _isBypassed = isBypassed;
            _isGeneric = symbol.IsGeneric;
            symbol._childrenCreatedFromMe.Add(this);

            foreach (var inputDefinition in symbol.InputDefinitions)
            {
                if (!Inputs.TryAdd(inputDefinition.Id, new Input(inputDefinition)))
                {
                    throw new ApplicationException($"The ID for symbol input {symbol.Name}.{inputDefinition.Name} must be unique.");
                }
            }

            foreach (var outputDefinition in symbol.OutputDefinitions)
            {
                Symbol.OutputDefinition.TryGetNewOutputDataType(outputDefinition, out var outputData);
                var output = new Output(outputDefinition, outputData) { DirtyFlagTrigger = outputDefinition.DirtyFlagTrigger };
                if (!Outputs.TryAdd(outputDefinition.Id, output))
                {
                    throw new ApplicationException($"The ID for symbol output {symbol.Name}.{outputDefinition.Name} must be unique.");
                }
            }
        }

        private void SetDisabled(bool shouldBeDisabled)
        {
            if (Parent == null)
                return;

            var outputDefinitions = Symbol.OutputDefinitions;

            // Set disabled status on this child's outputs
            foreach (var outputDef in outputDefinitions)
            {
                if (outputDef == null)
                {
                    Log.Warning($"{Symbol.GetType()} {Symbol.Name} contains a null {typeof(Symbol.OutputDefinition)}", Id);
                    continue;
                }

                if (Outputs.TryGetValue(outputDef.Id, out var childOutput))
                {
                    childOutput.IsDisabled = shouldBeDisabled;
                }
                else
                {
                    Log.Warning($"{typeof(Symbol.Child)} {ReadableName} does not have the following child output as defined: " +
                                $"{childOutput?.OutputDefinition.Name}({nameof(Guid)}{childOutput?.OutputDefinition.Id})");
                }
            }

            // Set disabled status on outputs of each instanced copy of this child within all parents that contain it
            foreach (var parentInstance in Parent.InstancesOfSelf)
            {
                // This parent doesn't have an instance of our SymbolChild. Ignoring and continuing.
                if (!parentInstance.Children.TryGetValue(Id, out var matchingChildInstance))
                    continue;

                // Set disabled status on all outputs of each instance
                foreach (var slot in matchingChildInstance.Outputs)
                {
                    slot.IsDisabled = shouldBeDisabled;
                }
            }
        }

        #region sub classes =============================================================
        public sealed class Output
        {
            public Symbol.OutputDefinition OutputDefinition { get; }
            public IOutputData OutputData { get; }

            public bool IsDisabled { get; set; }

            public DirtyFlagTrigger DirtyFlagTrigger
            {
                get => _dirtyFlagTrigger ?? OutputDefinition.DirtyFlagTrigger;
                set => _dirtyFlagTrigger = (value != OutputDefinition.DirtyFlagTrigger) ? (DirtyFlagTrigger?)value : null;
            }

            private DirtyFlagTrigger? _dirtyFlagTrigger = null;

            public Output(Symbol.OutputDefinition outputDefinition, IOutputData outputData)
            {
                OutputDefinition = outputDefinition;
                OutputData = outputData;
            }

            public Output DeepCopy()
            {
                return new Output(OutputDefinition, OutputData);
            }
        }

        public sealed class Input
        {
            public InputDefinition InputDefinition { get; }
            public Guid Id => InputDefinition.Id;
            public bool IsMultiInput => InputDefinition.IsMultiInput;
            public InputValue DefaultValue => InputDefinition.DefaultValue;

            public string Name => InputDefinition.Name.SpaceCamelCap();

            /// <summary>The input value used for this symbol child</summary>
            public InputValue Value { get; }

            public bool IsDefault { get; set; }

            public Input(Symbol.InputDefinition inputDefinition)
            {
                InputDefinition = inputDefinition;
                Value = DefaultValue.Clone();
                IsDefault = true;
            }

            public void SetCurrentValueAsDefault()
            {
                if (DefaultValue.IsEditableInputReferenceType)
                {
                    DefaultValue.AssignClone(Value);
                }
                else
                {
                    DefaultValue.Assign(Value);
                }

                IsDefault = true;
            }

            public void ResetToDefault()
            {
                if (DefaultValue.IsEditableInputReferenceType)
                {
                    Value.AssignClone(DefaultValue);
                }
                else
                {
                    Value.Assign(DefaultValue);
                }

                IsDefault = true;
            }
        }
        #endregion

        private bool _isBypassed;

        public bool IsBypassable()
        {
            if (Symbol.OutputDefinitions.Count == 0)
                return false;

            if (Symbol.InputDefinitions.Count == 0)
                return false;

            var mainInput = Symbol.InputDefinitions[0];
            var mainOutput = Symbol.OutputDefinitions[0];

            if (mainInput.DefaultValue.ValueType != mainOutput.ValueType)
                return false;

            if (mainInput.DefaultValue.ValueType == typeof(Command))
                return true;

            if (mainInput.DefaultValue.ValueType == typeof(Texture2D))
                return true;

            if (mainInput.DefaultValue.ValueType == typeof(BufferWithViews))
                return true;

            if (mainInput.DefaultValue.ValueType == typeof(MeshBuffers))
                return true;

            if (mainInput.DefaultValue.ValueType == typeof(float))
                return true;

            if (mainInput.DefaultValue.ValueType == typeof(Vector2))
                return true;

            if (mainInput.DefaultValue.ValueType == typeof(Vector3))
                return true;

            if (mainInput.DefaultValue.ValueType == typeof(string))
                return true;

            if (mainInput.DefaultValue.ValueType == typeof(ShaderGraphNode))
                return true;

            return false;
        }

        private void SetBypassed(bool shouldBypass)
        {
            if (shouldBypass == _isBypassed)
                return;

            if (!IsBypassable())
                return;

            if (Parent == null)
            {
                // Clarify: shouldn't this be shouldBypass?
                _isBypassed = shouldBypass; // during loading parents are not yet assigned. This flag will later be used when creating instances
                return;
            }

            if (_instancesOfSelf.Count == 0)
            {
                _isBypassed = shouldBypass; // while duplicating / cloning as new symbol there are no instances yet.
                return;
            }

            // check if there is a connection
            var isOutputConnected = false;
            var mainOutputDef = Symbol.OutputDefinitions[0];
            foreach (var connection in Parent.Connections)
            {
                if (connection.SourceSlotId != mainOutputDef.Id || connection.SourceParentOrChildId != Id)
                    continue;

                isOutputConnected = true;
                break;
            }

            if (!isOutputConnected)
                return;

            var id = Id;
            foreach (var parentInstance in Parent.InstancesOfSelf)
            {
                var instance = parentInstance.Children[id];
                SetBypassForInstance(instance, shouldBypass);
            }

            _isBypassed = shouldBypass;
        }

        private static bool SetBypassForInstance(Instance instance, bool shouldBypass, bool invalidate = true)
        {
            var mainInputSlot = instance.Inputs[0];
            var mainOutputSlot = instance.Outputs[0];

            var wasByPassed = false;

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

                case Slot<System.Numerics.Vector2> vec2Output when mainInputSlot is Slot<System.Numerics.Vector2> vec2Input:
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
                case Slot<System.Numerics.Vector3> vec3Output when mainInputSlot is Slot<System.Numerics.Vector3> vec3Input:
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

            return wasByPassed;
        }

        private static void InvalidateConnected<T>(Slot<T> bufferInput)
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

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            void InvalidateParentInputs(ISlot connection)
            {
                if (connection.ValueType == typeof(string))
                    return;

                connection.DirtyFlag.Invalidate();
            }
        }

        public override string ToString()
        {
            return Parent?.Name + ">" + ReadableName;
        }

        internal static Guid CreateIdDeterministically(Symbol symbol, Symbol? parent)
        {
            //deterministically create a new guid from the symbol id
            using var hashComputer = IncrementalHash.CreateHash(HashAlgorithmName.SHA1);
            hashComputer.AppendData(symbol.Id.ToByteArray(), 0, 16);

            if (parent != null)
            {
                hashComputer.AppendData(parent.Id.ToByteArray(), 0, 16);
            }

            // SHA1 is 20 bytes long, but we only need 16 bytes for a guid
            var newGuidBytes = new ReadOnlySpan<byte>(hashComputer.GetHashAndReset(), 0, 16);
            return new Guid(newGuidBytes);
        }

        internal void DestroyChildInstances(Child child)
        {
            var idToDestroy = child.Id;
            foreach (var instance in _instancesOfSelf)
            {
                if (instance.ChildInstances.Remove(idToDestroy, out var childInstance))
                {
                    childInstance.Dispose(child);
                }
            }
        }

        private void DestroyAndClearAllInstances()
        {
            for (int i = _instancesOfSelf.Count - 1; i >= 0; i--)
            {
                DestroyAndRemoveInstanceAtIndex(i);
            }
        }

        private void DestroyAndRemoveInstanceAtIndex(int index)
        {
            _instancesOfSelf[index].Dispose(this, index);
        }

        internal void Dispose()
        {
            DestroyAndClearAllInstances();
            var removed = Symbol._childrenCreatedFromMe.Remove(this);
            Debug.Assert(removed);
        }

        internal void AddChildInstances(Child newChild, ICollection<Instance> listToAddNewInstancesTo)
        {
            foreach (var instance in _instancesOfSelf)
            {
                if (newChild.TryCreateNewInstance(instance, out var newInstance))
                {
                    listToAddNewInstancesTo.Add(newInstance);
                }
            }
        }

        internal void UpdateIOAndConnections(SlotChangeInfo slotChanges)
        {
            var hasParent = Parent != null;

            UpdateSymbolChildIO(this, slotChanges);

            if (!hasParent)
            {
                DestroyAndClearAllInstances();
                // just destroy all instances - we have no connections to worry about since we dont have a parent
                return;
            }

            // we dont need to update our instances/connections - our parents do that for us if they need it
            if (Parent != null && Parent.NeedsTypeUpdate)
            {
                // destroy all instances if necessary? probably not...
                //DestroyAndClearAllInstances();
                return;
            }

            // deal with removed connections
            var parentConnections = Parent!.Connections;
            // get all connections that belong to this instance
            var connectionsToReplace = parentConnections.FindAll(c => c.SourceParentOrChildId == Id ||
                                                                      c.TargetParentOrChildId == Id);
            // first remove those connections where the inputs/outputs doesn't exist anymore
            var connectionsToRemove =
                connectionsToReplace.FindAll(c =>
                                             {
                                                 return slotChanges.RemovedOutputDefinitions.Any(output =>
                                                                                                 {
                                                                                                     var outputId = output.Id;
                                                                                                     return outputId == c.SourceSlotId ||
                                                                                                         outputId == c.TargetSlotId;
                                                                                                 })
                                                        || slotChanges.RemovedInputDefinitions.Any(input =>
                                                                                                   {
                                                                                                       var inputId = input.Id;
                                                                                                       return inputId == c.SourceSlotId ||
                                                                                                           inputId == c.TargetSlotId;
                                                                                                   });
                                             });

            foreach (var connection in connectionsToRemove)
            {
                Parent.RemoveConnection(connection); // TODO: clarify if we need to iterate over all multi input indices
                connectionsToReplace.Remove(connection);
            }

            // now create the entries for those that will be reconnected after the instance has been replaced. Take care of the multi input order
            connectionsToReplace.Reverse();

            var connectionEntriesToReplace = new List<ConnectionEntry>(connectionsToReplace.Count);
            foreach (var con in connectionsToReplace)
            {
                if (Parent.TryGetMultiInputIndexOf(con, out var foundAtConnectionIndex, out var multiInputIndex))
                {
                    connectionEntriesToReplace.Add(new ConnectionEntry
                                                       {
                                                           Connection = con,
                                                           MultiInputIndex = multiInputIndex,
                                                           ConnectionIndex = foundAtConnectionIndex
                                                       });
                }
            }

            Parent.RemoveConnections(connectionEntriesToReplace);

            // Recreate all instances fresh
            for (var index = _instancesOfSelf.Count - 1; index >= 0; index--)
            {
                var parent = _instancesOfSelf[index].Parent;
                DestroyAndRemoveInstanceAtIndex(index);

                if (!TryCreateNewInstance(parent, newInstance: out _))
                {
                    Log.Error($"Could not recreate instance of symbol: {Symbol.Name} with parent: {Parent.Name}");
                }
            }

            // ... and add the connections again
            foreach (var entry in connectionEntriesToReplace.OrderBy(x => x.ConnectionIndex))
            {
                var connection = entry.Connection;
                Parent.AddConnection(connection, entry.MultiInputIndex);
            }
        }

        internal bool TryCreateNewInstance(Instance? parentInstance,
                                           [NotNullWhen(true)] out Instance? newInstance)
        {
            if (!TryCreateInstance(parentInstance, out newInstance, out var reason))
            {
                Log.Error(reason);
                return false;
            }

            _instancesOfSelf.Add(newInstance);

            if (parentInstance != null)
            {
                Instance.AddChildTo(parentInstance, newInstance);
            }

            // cache property accesses for performance
            var newInstanceInputDefinitions = Symbol.InputDefinitions;
            var newInstanceInputDefinitionCount = newInstanceInputDefinitions.Count;

            var newInstanceInputs = newInstance.Inputs;
            var newInstanceInputCount = newInstanceInputs.Count;

            var symbolChildInputs = Inputs;

            // set up the inputs for the child instance
            for (int i = 0; i < newInstanceInputDefinitionCount; i++)
            {
                if (i >= newInstanceInputCount)
                {
                    Log.Warning($"Skipping undefined input index");
                    continue;
                }

                var inputDefinitionId = newInstanceInputDefinitions[i].Id;
                var inputSlot = newInstanceInputs[i];
                if (!symbolChildInputs.TryGetValue(inputDefinitionId, out var input))
                {
                    Log.Warning($"Skipping undefined input: {inputDefinitionId}");
                    continue;
                }

                inputSlot.Input = input;
                inputSlot.Id = inputDefinitionId;
            }

            // cache property accesses for performance
            var childOutputDefinitions = Symbol.OutputDefinitions;
            var childOutputDefinitionCount = childOutputDefinitions.Count;

            var childOutputs = newInstance.Outputs;

            var symbolChildOutputs = Outputs;

            // set up the outputs for the child instance
            for (int i = 0; i < childOutputDefinitionCount; i++)
            {
                Debug.Assert(i < childOutputs.Count);
                var outputDefinition = childOutputDefinitions[i];
                var id = outputDefinition.Id;
                if (i >= childOutputs.Count)
                {
                    Log.Warning($"Skipping undefined output: {id}");
                    continue;
                }

                var outputSlot = childOutputs[i];
                outputSlot.Id = id;
                var symbolChildOutput = symbolChildOutputs[id];
                if (outputDefinition.OutputDataType != null)
                {
                    // output is using data, so link it
                    if (outputSlot is IOutputDataUser outputDataConsumer)
                    {
                        outputDataConsumer.SetOutputData(symbolChildOutput.OutputData);
                    }
                }

                outputSlot.DirtyFlag.Trigger = symbolChildOutput.DirtyFlagTrigger;
                outputSlot.IsDisabled = symbolChildOutput.IsDisabled;
            }

            return true;

            bool TryCreateInstance(Instance? parent,
                                   [NotNullWhen(true)] out Instance? newInstance,
                                   [NotNullWhen(false)] out string? reason2)
            {
                if (parent != null && parent.Children.ContainsKey(Id))
                {
                    reason2 = $"Instance {Name} with id ({Id}) already exists in {parent.Symbol}";
                    newInstance = null;
                    return false;
                }

                // make sure we're not instantiating a child that needs to be updated again later
                if (Symbol.NeedsTypeUpdate)
                {
                    Symbol.UpdateInstanceType();
                }

                if (!TryInstantiate(out newInstance, out reason2))
                {
                    Log.Error(reason2);
                    return false;
                }

                newInstance.SetChildId(Id);

                newInstance.Parent = parent;

                Instance.SortInputSlotsByDefinitionOrder(newInstance);

                // populates child instances of the new instance
                foreach (var child in Symbol.Children.Values)
                {
                    var success = child.TryCreateNewInstance(newInstance, out _);
                }

                // create connections between child instances populated with CreateAndAddNewChildInstance
                var connections = Symbol.Connections;

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
                        if (!conHashToCount.TryGetValue(hash, out int count))
                            conHashToCount.Add(hash, 0);

                        if (!newInstance.TryAddConnection(connection, count))
                        {
                            Log.Warning($"Removing obsolete connecting in {Symbol}...");
                            connections.RemoveAt(index);
                            index--;
                            continue;
                        }

                        conHashToCount[hash] = count + 1;
                    }
                }

                // connect animations if available
                Symbol.Animator.CreateUpdateActionsForExistingCurves(newInstance.Children.Values);

                if (_isBypassed)
                {
                    SetBypassForInstance(newInstance, true, invalidate: false);
                }

                return true;

                bool TryInstantiate([NotNullWhen(true)] out Instance? instance,
                                    [NotNullWhen(false)] out string? reason3)
                {
                    var symbolPackage = Symbol.SymbolPackage;
                    if (symbolPackage.AssemblyInformation.OperatorTypeInfo.TryGetValue(Symbol.Id, out var typeInfo))
                    {
                        var constructor = typeInfo.GetConstructor();
                        try
                        {
                            instance = (Instance)constructor.Invoke();
                            reason3 = string.Empty;
                            return true;
                        }
                        catch (Exception e)
                        {
                            reason3 = $"Failed to create instance of type {Symbol.InstanceType} with id {Id}: {e}";
                            instance = null;
                            return false;
                        }
                    }

                    Log.Error($"No constructor found for {Symbol.InstanceType}. This should never happen!! Please report this");

                    try
                    {
                        // create instance through reflection
                        instance = Activator.CreateInstance(Symbol.InstanceType,
                                                            AssemblyInformation.ConstructorBindingFlags,
                                                            binder: null,
                                                            args: Array.Empty<object>(),
                                                            culture: null) as Instance;

                        if (instance is null)
                        {
                            reason3 = $"(Instance creation fallback failure) Failed to create instance of type " +
                                      $"{Symbol.InstanceType} with id {Id} - result was null";
                            return false;
                        }

                        Log.Warning($"(Instance creation fallback) Created instance of type {Symbol.InstanceType} with id {Id} through reflection");

                        reason3 = string.Empty;
                        return true;
                    }
                    catch (Exception e)
                    {
                        reason3 = $"(Instance creation fallback failure) Failed to create instance of type {Symbol.InstanceType} with id {Id}: {e}";
                        instance = null;
                        return false;
                    }
                }
            }
        }

        internal void AddConnectionToInstances(Connection connection, int multiInputIndex)
        {
            foreach (var instance in _instancesOfSelf)
            {
                instance.TryAddConnection(connection, multiInputIndex);
            }
        }

        internal void RemoveConnectionFromInstances(in ConnectionEntry entry)
        {
            RemoveConnectionFromInstances(entry.Connection, entry.MultiInputIndex);
        }

        internal void RemoveConnectionFromInstances(Connection connection, int multiInputIndex)
        {
            foreach (var instance in _instancesOfSelf)
            {
                if (instance.TryGetTargetSlot(connection, out var targetSlot))
                {
                    targetSlot.RemoveConnection(multiInputIndex);
                }
            }
        }

        internal void InvalidateInputDefaultInInstances(in Guid inputId)
        {
            foreach (var instance in _instancesOfSelf)
            {
                var inputSlots = instance.Inputs;
                for (int i = 0; i < inputSlots.Count; i++)
                {
                    var slot = inputSlots[i];
                    if (slot.Id != inputId)
                        continue;

                    if (!slot.Input.IsDefault)
                        continue;

                    slot.DirtyFlag.Invalidate();
                    break;
                }
            }
        }

        internal void InvalidateInputInChildren(in Guid inputId, in Guid childId)
        {
            for (int i = 0; i < _instancesOfSelf.Count; i++)
            {
                var instance = _instancesOfSelf[i];
                //var child = instance.Children[childId];
                if (!instance.Children.TryGetValue(childId, out var child))
                {
                    Log.Debug("Failed to invalidate missing child");
                    continue;
                }

                var inputSlots = child.Inputs;
                for (int j = 0; j < inputSlots.Count; j++)
                {
                    var slot = inputSlots[j];
                    if (slot.Id != inputId)
                        continue;

                    slot.DirtyFlag.Invalidate();
                    break;
                }
            }
        }

        internal void SortInputSlotsByDefinitionOrder()
        {
            foreach (var instance in _instancesOfSelf)
            {
                Instance.SortInputSlotsByDefinitionOrder(instance);
            }
        }

        internal void RemoveInstance(Instance child, int index)
        {
            if (index == -1)
            {
                index = _instancesOfSelf.IndexOf(child);
                if (index == -1)
                {
                    Log.Error($"Could not find instance {child} to remove from {this}");
                    return;
                }
            }

            _instancesOfSelf.RemoveAt(index);
        }
    }
}