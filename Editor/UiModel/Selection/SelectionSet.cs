#nullable enable
using System;
using System.Collections.Generic;

namespace T3.Editor.UiModel.Selection;

/// <summary>
/// An ordered selection of value-typed targets: element 0 is the primary — what property panels edit and
/// the anchor for range operations. The backing store for both the setup entity plane and canvas
/// sub-element planes; <see cref="NodeSelection"/> predates it and stays object-reference based.
/// All operations are plain loops, so per-frame callers pay no closure or comparer allocations.
/// </summary>
internal sealed class SelectionSet<T> where T : struct, IEquatable<T>
{
    /// <summary>Replace the selection with a single target.</summary>
    public void Set(in T target)
    {
        _items.Clear();
        _items.Add(target);
    }

    /// <summary>Add a target (no-op if already present).</summary>
    public void Add(in T target)
    {
        if (IndexOf(target) < 0)
            _items.Add(target);
    }

    /// <summary>Toggle a target's membership.</summary>
    public void Toggle(in T target)
    {
        var index = IndexOf(target);
        if (index >= 0)
            _items.RemoveAt(index);
        else
            _items.Add(target);
    }

    public bool Remove(in T target)
    {
        var index = IndexOf(target);
        if (index < 0)
            return false;

        _items.RemoveAt(index);
        return true;
    }

    public void Clear() => _items.Clear();

    public bool Contains(in T target) => IndexOf(target) >= 0;

    public bool TryGetPrimary(out T primary)
    {
        if (_items.Count == 0)
        {
            primary = default;
            return false;
        }

        primary = _items[0];
        return true;
    }

    public int Count => _items.Count;

    /// <summary>The selection in order, primary first. Copy before acting on it — anything that deletes
    /// entities may prune this list as it goes.</summary>
    public IReadOnlyList<T> Items => _items;

    public T this[int index] => _items[index];

    /// <summary>For prune loops (iterate backwards); keeps validation policy with the owner, not here.</summary>
    public void RemoveAt(int index) => _items.RemoveAt(index);

    private int IndexOf(in T target)
    {
        for (var i = 0; i < _items.Count; i++)
        {
            if (_items[i].Equals(target))
                return i;
        }

        return -1;
    }

    private readonly List<T> _items = [];
}
