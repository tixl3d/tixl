// TODO: fix nullable here
//#nullable enable

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using T3.Core.Logging;
using T3.Core.Model;
using T3.Core.Operator.Slots;
using T3.Core.Resource;

namespace T3.Core.Operator;

/// <summary>
/// Represents the definition of an operator. It can include:
/// - <see cref="Child"/>s that references other Symbols
/// - <see cref="Connection"/>s that connect these children
/// </summary>
/// <remarks>
/// - There can be multiple <see cref="Instance"/>s of a symbol.
/// </remarks>
public sealed partial class Symbol : IDisposable, IResource
{
    #region Saved Properties
    public readonly Guid Id;
    public IReadOnlyDictionary<Guid, Child> Children => _children;
    public IReadOnlyDictionary<Guid, Child> ChildrenCreatedFromMe => _childrenCreatedFromMe;
    public IEnumerable<Instance> InstancesOfSelf
    {
        get
        {
            lock (_creationLock)
            {
                return _childrenCreatedFromMe.Values.SelectMany(x => x.Instances);
            }
        }
    }

    public readonly List<Connection> Connections = [];

    /// <summary>
    /// Inputs of this symbol. input values are the default values (exist only once per symbol)
    /// </summary>
    public readonly List<InputDefinition> InputDefinitions = new();

    public readonly List<OutputDefinition> OutputDefinitions = new();

    #endregion Saved Properties

    public string Name => InstanceType?.Name;
    public string Namespace => InstanceType.Namespace ?? SymbolPackage.AssemblyInformation.Name;
    public Animator Animator { get; private set; } = new();
    public PlaybackSettings PlaybackSettings { get; set; } = new();

    public SymbolPackage SymbolPackage { get; set; }
    IResourcePackage IResource.OwningPackage => SymbolPackage;

    public Type InstanceType { get; private set; }

    private bool IsGeneric => InstanceType.IsGenericTypeDefinition;

    internal Symbol(Type instanceType, Guid symbolId, SymbolPackage symbolPackage)
    {
        Id = symbolId;
        _parentlessIdPath = [Child.CreateIdDeterministically(this, null)];

        UpdateTypeWithoutUpdatingDefinitionsOrInstances(instanceType, symbolPackage);

        if (instanceType == typeof(object))
            return;

        if (symbolPackage != null)
        {
            UpdateInstanceType(false);
        }
    }

    internal void UpdateTypeWithoutUpdatingDefinitionsOrInstances(Type type, SymbolPackage symbolPackage)
    {
        SymbolPackage = symbolPackage; // we re-assign this here because symbols can be moved from one package to another
        ApplyInstanceType(type);
        NeedsTypeUpdate = true;
    }

    private void ApplyInstanceType(Type value)
    {
        InstanceType = value;

        if (value == null)
            return;
        
        if(!value.IsAssignableTo(typeof(Instance)))
        {
            return;
        }
        
        // set type Symbol static field (TypeClass.StaticSymbol field)
        const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.FlattenHierarchy;
        var field = value.GetField("StaticSymbol", flags);
        field!.SetValue(null, this);
    }

    public void Dispose()
    {
        lock (_creationLock)
        {
            var children = _children.Values.ToArray();
            for (var index = 0; index < children.Length; index++)
            {
                var child = children[index];
                _children.Remove(child.Id, out _);
                child.Dispose();
            }

            foreach (var child in _childrenCreatedFromMe.Values)
            {
                child.Dispose();
            }

            _childrenCreatedFromMe.Clear();
        }
    }

    public int GetMultiInputIndexFor(Connection con)
    {
        return Connections.FindAll(c => c.TargetParentOrChildId == con.TargetParentOrChildId
                                        && c.TargetSlotId == con.TargetSlotId)
                          .FindIndex(cc => cc == con); // todo: fix this mess! connection rework!
    }


    public void SortInputSlotsByDefinitionOrder()
    {
        lock (_creationLock)
        {
            foreach (var child in _childrenCreatedFromMe.Values)
            {
                child.SortInputSlotsByDefinitionOrder();
            }
        }
    }
        
    public override string ToString() => $"{Namespace}.[{Name}]";

    public bool IsTargetMultiInput(Connection connection)
    {
        return Children.TryGetValue(connection.TargetParentOrChildId, out var child) 
               && child.Inputs.TryGetValue(connection.TargetSlotId, out var targetSlot) 
               && targetSlot.IsMultiInput;
    }

    /// <summary>
    /// Add connection to symbol and its instances
    /// </summary>
    /// <remarks>All connections of a symbol are stored in a single List, from which sorting of multi-inputs
    /// is define. That why inserting connections for those requires to first find the correct index within that
    /// larger list. 
    /// </remarks>
    public void AddConnection(Connection connection, int multiInputIndex = 0)
    {
        var isMultiInput = IsTargetMultiInput(connection);

        // Check if another connection is already existing to the target input, ignoring multi inputs for now
        var connectionsAtInput = Connections.FindAll(c =>
                                                         (c.TargetParentOrChildId == connection.TargetParentOrChildId
                                                          || c.TargetParentOrChildId == Guid.Empty) &&
                                                         c.TargetSlotId == connection.TargetSlotId);

        if (multiInputIndex > connectionsAtInput.Count)
        {
            // todo - solve is to ensure that the multi-input slots aren't cleared of quantity when recompiling, or rather are populated in order
            Log.Error($"Trying to add a connection at the index {multiInputIndex}. Out of bound of the {connectionsAtInput.Count} existing connections.");
            return;
        }

        if (!isMultiInput)
        {
            // Replace existing on single inputs
            if (connectionsAtInput.Count > 0)
            {
                RemoveConnection(connectionsAtInput[0]);
            }

            Connections.Add(connection);
        }
        else
        {
            var append = multiInputIndex == connectionsAtInput.Count;
            if (append)
            {
                if (connectionsAtInput.Count == 0)
                {
                    Connections.Add(connection);
                }
                else
                {
                    Connections.Add( connection);
                }
            }
            else
            {
                // Use the target index to find the existing successor among the connections
                var existingConnection = connectionsAtInput[multiInputIndex];

                // ReSharper disable once PossibleUnintendedReferenceComparison
                var insertIndex = Connections.FindIndex(c => c == existingConnection);

                Connections.Insert(insertIndex, connection);
            }
        }

        lock (_creationLock)
        {
            foreach (var child in _childrenCreatedFromMe.Values)
            {
                child.AddConnectionToInstances(connection, multiInputIndex, true);
            }
        }
    }

    public void RemoveConnection(Connection connection, int multiInputIndex = 0)
    {
        var targetParentOrChildId = connection.TargetParentOrChildId;
        var targetSlotId = connection.TargetSlotId;

        List<Connection> connectionsAtInput = new();
            
        var connections = Connections;

        foreach (var potentialConnection in connections)
        {
            if (potentialConnection.TargetParentOrChildId == targetParentOrChildId &&
                potentialConnection.TargetSlotId == targetSlotId)
            {
                connectionsAtInput.Add(potentialConnection);
            }
        }

        var connectionsAtInputCount = connectionsAtInput.Count;
        if (connectionsAtInputCount == 0 || multiInputIndex >= connectionsAtInputCount)
        {
            Log.Error($"Trying to remove a connection that doesn't exist. Index {multiInputIndex} of {connectionsAtInput.Count}");
            return;
        }

        var existingConnection = connectionsAtInput[multiInputIndex];

        // ReSharper disable once PossibleUnintendedReferenceComparison
        bool removed = false;
        var connectionCount = connections.Count;
        for (var index = 0; index < connectionCount; index++)
        {
            if (connections[index] == existingConnection)
            {
                connections.RemoveAt(index);
                removed = true;
                break;
            }
        }

        if (!removed)
        {
            Log.Warning($"Failed to remove connection.");
            return;
        }

        lock (_creationLock)
        {
            foreach (var child in _childrenCreatedFromMe.Values)
            {
                child.RemoveConnectionFromInstances(connection, multiInputIndex);
            }
        }
    }

    public void CreateOrUpdateActionsForAnimatedChildren()
    {
        foreach (var instance in InstancesOfSelf)
        {
            Animator.CreateUpdateActionsForExistingCurves(instance.Children.Values);
        }
    }

    internal void CreateAnimationUpdateActionsForSymbolInstances()
    {
        var foundParentsOfMyInstances = new HashSet<Symbol>();
        foreach (var instance in InstancesOfSelf)
        {
            var parent = instance.Parent;
            if (parent != null)
            {
                var parentSymbol = parent.Symbol;
                if (foundParentsOfMyInstances.Add(parentSymbol))
                {
                    parentSymbol.CreateOrUpdateActionsForAnimatedChildren();
                }
            }
        }
    }

    public bool RemoveChild(Guid childId)
    {
        // first remove all connections to or from the child
        Connections.RemoveAll(c => c.SourceParentOrChildId == childId || c.TargetParentOrChildId == childId);

        if (!_children.Remove(childId, out var symbolChild)) 
            return false;
        
        lock (_creationLock)
        {
            foreach (var me in _childrenCreatedFromMe.Values)
            {
                me.RemoveChildInstancesOf(symbolChild);
            }
        }

        SymbolPackage.RemoveDependencyOn(symbolChild.Symbol);

        return true;
    }

    public InputDefinition GetInputMatchingType(Type type)
    {
        foreach (var inputDefinition in InputDefinitions)
        {
            if (type == null || inputDefinition.DefaultValue.ValueType == type)
                return inputDefinition;
        }

        return null;
    }

    public OutputDefinition GetOutputMatchingType(Type type)
    {
        foreach (var outputDefinition in OutputDefinitions)
        {
            if (type == null || outputDefinition.ValueType == type)
                return outputDefinition;
        }

        return null;
    }

    public void InvalidateInputInAllChildInstances(IInputSlot inputSlot)
    {
        var childId = inputSlot.Parent.SymbolChildId;
        var inputId = inputSlot.Id;
        InvalidateInputInAllChildInstances(inputId, childId);
    }

    public void InvalidateInputInAllChildInstances(Guid inputId, Guid childId)
    {
        lock (_creationLock)
        {
            foreach (var parent in _childrenCreatedFromMe.Values)
            {
                parent.InvalidateInputInChildren(inputId, childId);
            }
        }
    }

    /// <summary>
    /// Invalidates all instances of a symbol input (e.g. if that input's default was modified)
    /// </summary>
    public void InvalidateInputDefaultInInstances(IInputSlot inputSlot)
    {
        var inputId = inputSlot.Id;
        lock (_creationLock)
        {
            foreach (var child in _childrenCreatedFromMe.Values)
            {
                child.InvalidateInputDefaultInInstances(inputId);
            }
        }
    }

    internal bool NeedsTypeUpdate { get; private set; } = true;
    private readonly ConcurrentDictionary<Guid, Child> _children = new();
    private readonly Dictionary<Guid, Child> _childrenCreatedFromMe = new();
    
    private void ReplaceConnection(Connection con)
    {
        if(TryGetMultiInputIndexOf(con, out var foundAtConnectionIndex, out _))
        {
            Connections.RemoveAt(foundAtConnectionIndex);
            Connections.Insert(foundAtConnectionIndex, con);
        }
        else
        {
            Log.Error($"Failed to replace connection {con} in symbol {this}. Connection not found.");
        }
    }

    internal void ReconnectAll()
    {
        lock (_creationLock)
        {
            foreach(var child in _childrenCreatedFromMe.Values)
            {
                child.ReconnectAllChildren();
            }
        }
    }
}