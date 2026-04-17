#nullable enable
using System.Collections;
using T3.Core.Animation;

namespace T3.Editor.Gui.Interaction.WithCurves;

/// <summary>
/// A set of selected keyframe <see cref="VDefinition"/>s that bumps <see cref="ChangeCounter"/>
/// on every mutation. Downstream UI caches can compare the counter instead of rehashing the set.
/// Mirrors the <c>HashSet</c>-style API used across the codebase so call sites stay unchanged.
/// </summary>
internal sealed class VersionedKeyframeSet : IEnumerable<VDefinition>
{
    public int ChangeCounter { get; private set; }

    public int Count => _set.Count;

    public bool Contains(VDefinition v) => _set.Contains(v);

    public bool Add(VDefinition v)
    {
        if (!_set.Add(v))
            return false;
        ChangeCounter++;
        return true;
    }

    public bool Remove(VDefinition v)
    {
        if (!_set.Remove(v))
            return false;
        ChangeCounter++;
        return true;
    }

    public void Clear()
    {
        if (_set.Count == 0)
            return;
        _set.Clear();
        ChangeCounter++;
    }

    public void UnionWith(IEnumerable<VDefinition> other)
    {
        var before = _set.Count;
        _set.UnionWith(other);
        if (_set.Count != before)
            ChangeCounter++;
    }

    public void ExceptWith(IEnumerable<VDefinition> other)
    {
        var before = _set.Count;
        _set.ExceptWith(other);
        if (_set.Count != before)
            ChangeCounter++;
    }

    public HashSet<VDefinition>.Enumerator GetEnumerator() => _set.GetEnumerator();
    IEnumerator<VDefinition> IEnumerable<VDefinition>.GetEnumerator() => _set.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => _set.GetEnumerator();

    private readonly HashSet<VDefinition> _set = [];
}
