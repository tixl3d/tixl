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
            foreach (var child in _asChild.Symbol.Children)
            {
                if (TryGetChildInstance(child.Key, out var instance))
                {
                    yield return child.Key;
                }
                else
                {
                    Log.Error($"Child {child.Key} not found in {this}");
                }
            }
        }
    }

    public IEnumerable<Instance> Values 
    {
        get
        {
            if(_asChild.Symbol.Children.Count == 0)
                yield break;
            
            foreach (var child in _asChild.Symbol.Children.Values)
            {
                if (TryGetChildInstance(child.Id, out var instance))
                {
                    yield return instance;
                }
                else
                {
                    Log.Error($"Child {child.Id} not found in {this}");
                }
            }
        }
    }
    
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
        foreach (var child in _asChild.Symbol.Children)
        {
            if (TryGetChildInstance(child.Key, out var instance))
            {
                yield return new KeyValuePair<Guid, Instance>(child.Key, instance);
            }
            else
            {
                Log.Error($"Child {child.Key} not found in {this}");
            }
        }
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }
}