#nullable enable
using T3.Core.Animation;

namespace T3.Editor.Gui.Windows.TimeLine;

/// <summary>
/// Sorted list of TimeWarp handle U positions plus a per-frame visibility buffer.
/// Owns only the handle data — drag mechanics live in <see cref="TimeWarpDrag"/>,
/// and drawing / hit-testing live in <see cref="SelectionRangeIndicator"/>.
/// </summary>
internal sealed class TimeWarpHandleSet
{
    public int Count => _handles.Count;
    public double this[int index] => _handles[index];

    public IReadOnlyList<int> VisibleIndices => _visible;
    public bool HasVisible => _visible.Count > 0;

    /// <summary>Recompute the set of handle indices that fall inside the given range.</summary>
    public void RebuildVisible(TimeRange range)
    {
        _visible.Clear();
        for (var i = 0; i < _handles.Count; i++)
        {
            var u = _handles[i];
            if (u >= range.Start && u <= range.End)
                _visible.Add(i);
        }
    }

    /// <summary>Insert a handle at <paramref name="u"/>; no-op if outside or exactly on the range edges.</summary>
    public void Insert(double u, TimeRange range)
    {
        if (u <= range.Start || u >= range.End)
            return;
        _handles.Add(u);
        _handles.Sort();
    }

    public void RemoveAt(int index)
    {
        if (index < 0 || index >= _handles.Count)
            return;
        _handles.RemoveAt(index);
    }

    public void Clear() => _handles.Clear();

    /// <summary>Write a new U value to the handle at <paramref name="index"/> without re-sorting.</summary>
    public void SetValueUnsorted(int index, double u) => _handles[index] = u;

    public void RestoreFromSnapshot(IReadOnlyList<double> snapshot)
    {
        _handles.Clear();
        for (var i = 0; i < snapshot.Count; i++)
            _handles.Add(snapshot[i]);
    }

    public void CopyTo(List<double> buffer)
    {
        buffer.Clear();
        for (var i = 0; i < _handles.Count; i++)
            buffer.Add(_handles[i]);
    }

    /// <summary>Previous and next neighbour U for a given handle, bounded by the selection range.</summary>
    public (double prev, double next) GetNeighbors(int handleIndex, TimeRange range)
    {
        var u = _handles[handleIndex];
        double prev = range.Start;
        double next = range.End;
        for (var i = 0; i < _handles.Count; i++)
        {
            if (i == handleIndex) continue;
            var other = _handles[i];
            if (other < u && other > prev) prev = other;
            else if (other > u && other < next) next = other;
        }
        return (prev, next);
    }

    public double GetFirstVisibleU()
    {
        var best = double.PositiveInfinity;
        for (var vi = 0; vi < _visible.Count; vi++)
        {
            var u = _handles[_visible[vi]];
            if (u < best) best = u;
        }
        return best;
    }

    public double GetLastVisibleU()
    {
        var best = double.NegativeInfinity;
        for (var vi = 0; vi < _visible.Count; vi++)
        {
            var u = _handles[_visible[vi]];
            if (u > best) best = u;
        }
        return best;
    }

    private readonly List<double> _handles = new(8);
    private readonly List<int> _visible = new(8);
}
