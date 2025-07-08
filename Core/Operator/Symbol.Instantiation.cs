#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using T3.Core.Logging;

namespace T3.Core.Operator;

public sealed partial class Symbol
{
    public Child AddChild(Symbol symbol, Guid addedChildId, string? name = null, bool isBypassed = false, Action<Child>? modifyAction = null)
    {
        var newChild = symbol.GetAsChild(addedChildId, this, name, isBypassed, out var replacedId);
        
        modifyAction?.Invoke(newChild);

        if (!_children.TryAdd(newChild.Id, newChild))
        {
            throw new InvalidOperationException("The ID for symbol child must be unique.");
        }
        
        SymbolPackage.AddDependencyOn(symbol);

        List<Instance> newChildInstances;
        lock (_creationLock) // this is accessed in a multithreaded context when loading etc, so we should lock to avoid missing any potential children
        {
            newChildInstances = new List<Instance>(_childrenCreatedFromMe.Count);
            foreach (var child in _childrenCreatedFromMe.Values)
            {
                child.AddChildInstances(newChild, newChildInstances);
            }
        }

        Animator.CreateUpdateActionsForExistingCurves(newChildInstances);
        return newChild;
    }
    
    private Child GetAsChild(Guid childId, Symbol? parent, string? name, bool isBypassed, out bool replacedId)
    {
        lock (_creationLock)
        {
            if (_childrenCreatedFromMe.TryGetValue(childId, out var child))
            {
                // we have already created a child for this id, so we can just return it? maybe?
                if (parent != child.Parent)
                {
                    if (parent != null)
                    {
                        // todo - refactor this for readability
                        if (parent.Id == child.Parent?.Id) 
                        {
                            // this should never happen, but if it does, we are assuming that 
                            Log.Warning($"Duplicate parent id {parent.Id} for {Name}, but different parent objects. " +
                                        $"If this happens at any time other than during copy/paste operations, then this is an error that should be reported.");
                            replacedId = false;
                            var newChild = new Child(child.Symbol, child.Id, parent, child.Name, child.IsBypassed, _creationLock, child.Id);
                            _childrenCreatedFromMe[child.Id] = newChild;
                            child.Dispose();
                            return child;
                        }

                        Log.Warning($"Parent mismatch for {childId} in {Name}");
                        child = CreateWithNewId(child, parent);
                        _childrenCreatedFromMe.TryAdd(child.Id, child);
                        replacedId = true;

                        return child;
                    }

                    // this is a parentless instance, so all we need is a new id - there will be no connections to update
                    // we cant change a parentless instance's id (as they are deterministic), so the *other* instance would need to be updated
                    // or we would need to remove the whole deterministic id thing. either way, not implemented atm so throw an exception
                    throw new Exception($"Child id conflict with a parentless instance {childId} of {Name}");
                }
                
                replacedId = false;
                return child;
            }

            child = new Child(this, childId, parent, name, isBypassed, _creationLock);
            _childrenCreatedFromMe.TryAdd(childId, child);
            replacedId = false;
            return child;
        }

        Child CreateWithNewId(Child original, Symbol? childsParent)
        {
            var newId = Child.CreateIdDeterministically(original.Symbol, childsParent, original.Id);
            var originalId = original.Id;
            var newChild = new Child(this, newId, childsParent, original.Name, original.IsBypassed, _creationLock, originalId);
            _childrenCreatedFromMe.TryAdd(newId, newChild);

            if (childsParent != null)
            {
                var connections = childsParent.Connections!;
                for (var index = 0; index < connections.Count; index++)
                {
                    var connection = connections[index];
                    var replaceSource = connection.SourceParentOrChildId == originalId;
                    var replaceTarget = connection.TargetParentOrChildId == originalId;
                    if (!replaceSource && !replaceTarget)
                        continue;
                    var source = replaceSource ? newId : connection.SourceParentOrChildId;
                    var target = replaceTarget ? newId : connection.TargetParentOrChildId;
                    var sourceSlot = connection.SourceSlotId;
                    var targetSlot = connection.TargetSlotId;
                    var newConnection = new Connection(source, sourceSlot, target, targetSlot);
                    connections[index] = newConnection;
                }

                if (parent._children.TryRemove(originalId, out var og))
                {
                    Debug.Assert(og == original);
                    parent._children.TryAdd(newId, newChild);
                }
            }
        
            return newChild;
        }
    }

    public bool TryGetOrCreateInstance(IReadOnlyList<Guid> path, Symbol? parent, [NotNullWhen(true)] out Instance? instance, bool allowCreate = true)
    {
        if (path.Count == 0)
        {
            throw new ArgumentException("Path cannot be empty.", nameof(path));
        }

        var id = path[^1];
        var child = GetAsChild(id, parent, null, false, out var replacedId);
        if (replacedId)
        {
            var newPath = path.ToArray();
            newPath[^1] = child.Id;
            path = newPath;
        }
        
        return child.TryGetOrCreateInstance(path, out instance, out _, allowCreate);
    }

    public bool TryGetParentlessInstance([NotNullWhen(true)] out Instance? instance) => TryGetOrCreateInstance(_parentlessIdPath, null, out instance);

    private readonly Guid[] _parentlessIdPath;
    private readonly object _creationLock = new();
}