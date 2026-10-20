#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using T3.Core.Logging;
using T3.Core.Model;

namespace T3.Core.Operator;

// TODO: Making this a IReadOnlyDictionary and causes excessive GC allocations. It's cleaned but causes frame drops on larger graphs. 
public sealed class InstanceChildren : IReadOnlyDictionary<Guid, Instance>
{
    /** Swapped as a whole, so threads never see an instance list paired with the wrong version */
    private sealed class ResolvedChildInstances(int childrenVersion, Instance[] instances, bool isComplete)
    {
        public readonly int ChildrenVersion = childrenVersion;
        public readonly Instance[] Instances = instances;

        /** False if a child failed to instantiate; it is retried on the next access, as it was before caching */
        public readonly bool IsComplete = isComplete;
    }

    /** The symbol child of the instance this collection belongs to */
    private readonly Symbol.Child _asChild; 
    
    /** A cached search path to avoid creating new arrays for each child lookup */
    private readonly Guid[] _childSearchPath; 
    
    public int Count => _asChild.Symbol.Children.Count;
    
    internal InstanceChildren(IReadOnlyList<Guid> instancePath, Symbol.Child symbolChild)
    {
        _asChild = symbolChild;
        _childSearchPath = new Guid[instancePath.Count + 1];
        for (var i = 0; i < instancePath.Count; i++)
        {
            _childSearchPath[i] = instancePath[i];
        }
        _childSearchPath[^1] = Guid.Empty;
    }
    
    /* Dictionary accessor overload */
    public Instance this[Guid sourceParentOrChildId]
    {
        get
        {
            if (TryGetChildInstance(sourceParentOrChildId, out var sourceInstance))
                return sourceInstance;

            throw new KeyNotFoundException($"Child with id {sourceParentOrChildId} not found in {this}");
        }
    }

    IEnumerable<Guid> IReadOnlyDictionary<Guid, Instance>.Keys
    {
        get
        {
            foreach (var instance in GetResolvedChildInstances())
            {
                yield return instance.SymbolChildId;
            }
        }
    }

    /// <summary>
    /// All child instances, created on demand. Resolving each child walks the instance path under the symbol's
    /// creation lock, so the result is cached until the symbol's children change or one of the instances is disposed.
    /// </summary>
    public IEnumerable<Instance> Values => GetResolvedChildInstances();
    
    internal IEnumerable<Instance> PreExistingValues
    {
        get
        {
            if (_asChild.Symbol.Children.Count == 0)
                yield break;

            foreach (var child in _asChild.Symbol.Children.Values)
            {
                if (TryGetChildInstance(child.Id, out var instance, allowCreate: false))
                {
                    yield return instance;
                }
            }
        }
    }

    public void GetExistingCounts(out int count, out int existingCount, out Instance.InstanceStatus childFlags,  out int nestedCount, out int nestedExistingCount, out Instance.InstanceStatus nestedFlags)
    {
        count = Count;
        existingCount = 0;
        nestedCount = count;
        nestedExistingCount = 0;
        childFlags = default;
        nestedFlags = default;
        foreach (var child in _asChild.Symbol.Children.Values)
        {
            if (!TryGetChildInstance(child.Id, out var instance, allowCreate: false))
            {
                continue;
            }
            
            childFlags |= instance.Status;
            
            existingCount++;
            nestedExistingCount++;
            instance.Children.GetExistingCounts(out _, out _, out var childFlag, out var childTotalCount, out var childTotalExistingCounts, out var flags);
            nestedCount += childTotalCount;
            nestedExistingCount += childTotalExistingCounts;
            nestedFlags |= flags | childFlag;
        }
    }
    
    bool IReadOnlyDictionary<Guid, Instance>.ContainsKey(Guid key)
    {
        return _asChild.Symbol.Children.ContainsKey(key);
    }
    
    bool IReadOnlyDictionary<Guid, Instance>.TryGetValue(Guid key, [NotNullWhen(true)] out Instance? value)
    {
        if (TryGetChildInstance(key, out value))
            return true;

        value = null;
        return false;
    }

    public bool TryGetChildInstance(Guid childId, [NotNullWhen(true)] out Instance? instance, bool allowCreate = true)
    {
        var symbol = _asChild.Symbol;
        if (!symbol.Children.TryGetValue(childId, out var sourceChild))
        {
            // This happens frequently after deleting a child and some views like console or output are trying to access it.
            //Log.Error($"{_asChild} failed to find child : {childId}");
            instance = null;
            return false;
        }

        lock (_childSearchPath) // prevent concurrent modifications to the search path
        {
            _childSearchPath[^1] = childId;
            if (sourceChild.TryGetOrCreateInstance(_childSearchPath, out instance, out _, allowCreate))
            {
                return true;
            }
        }

        if (allowCreate)
        {
            Log.Error($"{_asChild} Failed to find or create child instance : {childId}");
        }
                
        return false;
    }

    internal void DisposePackageInChildren(SymbolPackage? packageToDispose)
    {
        lock (_childSearchPath)
        {
            foreach (var child in _asChild.Symbol.Children.Values)
            {
                var childId = child.Id;
                _childSearchPath[^1] = childId;
                if (child.TryGetOrCreateInstance(_childSearchPath, out var inst, out _, false))
                {
                    inst.DisposePackage(packageToDispose);
                }
            }
        }
    }

    public IEnumerator<KeyValuePair<Guid, Instance>> GetEnumerator()
    {
        foreach (var instance in GetResolvedChildInstances())
        {
            yield return new KeyValuePair<Guid, Instance>(instance.SymbolChildId, instance);
        }
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }

    private Instance[] GetResolvedChildInstances()
    {
        var symbol = _asChild.Symbol;
        var cached = _resolvedChildInstances;
        if (cached != null
            && cached.IsComplete
            && cached.ChildrenVersion == symbol.ChildrenVersion
            && !ContainsDisposed(cached.Instances))
            return cached.Instances;

        // Read before resolving: a change that lands while resolving bumps past it and triggers another rebuild.
        var childrenVersion = symbol.ChildrenVersion;

        var children = symbol.Children;
        var instances = new List<Instance>(children.Count);
        var isComplete = true;
        foreach (var child in children.Values)
        {
            if (TryGetChildInstance(child.Id, out var instance))
            {
                instances.Add(instance);
            }
            else
            {
                isComplete = false;
                Log.Error($"Child {child.Id} not found in {this}");
            }
        }

        var resolved = new ResolvedChildInstances(childrenVersion, instances.ToArray(), isComplete);
        _resolvedChildInstances = resolved;
        return resolved.Instances;
    }

    private static bool ContainsDisposed(Instance[] instances)
    {
        foreach (var instance in instances)
        {
            if (instance.IsDisposed)
                return true;
        }

        return false;
    }

    private volatile ResolvedChildInstances? _resolvedChildInstances;
}