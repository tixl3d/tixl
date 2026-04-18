#nullable enable
using T3.Core.Animation;
using T3.Core.DataTypes;

namespace T3.Editor.Gui.Windows.TimeLine;

/// <summary>
/// Aggregates across every currently-active <see cref="AnimationParameterEditing"/> on the timeline
/// (DopeSheetArea, TimelineCurveEditArea, …). One editor today, potentially several when the
/// dope-sheet and curve editor are shown side by side. The group mirrors the pattern
/// <see cref="T3.Editor.Gui.Interaction.WithCurves.AnimationCanvas"/> uses for <c>ITimeObjectManipulation</c>:
/// cross-editor state lives here so callers never need to pick one "active" editor.
/// </summary>
internal sealed class KeyframeEditorGroup
{
    public KeyframeEditorGroup(List<AnimationParameterEditing> editors)
    {
        _editors = editors;
    }

    public IReadOnlyList<AnimationParameterEditing> All => _editors;
    public int Count => _editors.Count;

    //
    // Read-only aggregates ---------------------------------------------------
    //

    public TimeRange GetSelectionTimeRange()
    {
        var range = TimeRange.Undefined;
        for (var i = 0; i < _editors.Count; i++)
        {
            var r = _editors[i].GetSelectionTimeRange();
            if (r.IsValid)
            {
                range.Unite(r.Start);
                range.Unite(r.End);
            }
        }
        return range;
    }

    public TimeRange GetAllKeyframesTimeRange()
    {
        var range = TimeRange.Undefined;
        for (var i = 0; i < _editors.Count; i++)
        {
            var r = _editors[i].GetAllKeyframesTimeRange();
            if (r.IsValid)
            {
                range.Unite(r.Start);
                range.Unite(r.End);
            }
        }
        return range;
    }

    /// <summary>Sum of per-editor change counters so cache hashes refresh whenever any selection changes.</summary>
    public int AggregateSelectionChangeCounter
    {
        get
        {
            var sum = 0;
            for (var i = 0; i < _editors.Count; i++)
                sum += _editors[i].SelectionChangeCounter;
            return sum;
        }
    }

    public bool IsKeyframeSelectedInAny(VDefinition v)
    {
        for (var i = 0; i < _editors.Count; i++)
            if (_editors[i].IsKeyframeSelected(v))
                return true;
        return false;
    }

    public int TotalSelectedKeyframeCount
    {
        get
        {
            var n = 0;
            for (var i = 0; i < _editors.Count; i++)
                n += _editors[i].SelectedKeyframeCount;
            return n;
        }
    }

    //
    // Enumerations -----------------------------------------------------------
    //

    public IEnumerable<VDefinition> EnumerateAllSelectedKeyframes()
    {
        for (var i = 0; i < _editors.Count; i++)
            foreach (var v in _editors[i].EnumerateSelectedKeyframes())
                yield return v;
    }

    public IEnumerable<VDefinition> EnumerateAllKeyframes()
    {
        for (var i = 0; i < _editors.Count; i++)
            foreach (var v in _editors[i].EnumerateAllKeyframes())
                yield return v;
    }

    public void CopyAllSelectedKeyframesTo(List<VDefinition> buffer)
    {
        buffer.Clear();
        for (var i = 0; i < _editors.Count; i++)
            foreach (var v in _editors[i].EnumerateSelectedKeyframes())
                buffer.Add(v);
    }

    public void CopyAllCurvesTo(List<Curve> buffer)
    {
        buffer.Clear();
        for (var i = 0; i < _editors.Count; i++)
            foreach (var curve in EnumerateCurvesOf(_editors[i]))
                buffer.Add(curve);
    }

    private static IEnumerable<Curve> EnumerateCurvesOf(AnimationParameterEditing editor)
    {
        // CopyAllCurvesTo overrides its buffer; use a throwaway to iterate without clobbering caller's.
        _scratchCurves.Clear();
        editor.CopyAllCurvesTo(_scratchCurves);
        for (var i = 0; i < _scratchCurves.Count; i++)
            yield return _scratchCurves[i];
    }

    [ThreadStatic] private static List<Curve>? _scratchCurvesBacking;
    private static List<Curve> _scratchCurves => _scratchCurvesBacking ??= new List<Curve>(32);

    //
    // Mutations -------------------------------------------------------------
    //

    public void SelectAllKeyframes()
    {
        for (var i = 0; i < _editors.Count; i++)
            _editors[i].SelectAllKeyframes();
    }

    public void ReplaceKeyframeSelection(IEnumerable<VDefinition> keys)
    {
        // Materialize once so every editor sees the same set (avoids re-enumerating a generator).
        _scratchKeys.Clear();
        foreach (var k in keys)
            _scratchKeys.Add(k);

        for (var i = 0; i < _editors.Count; i++)
            _editors[i].ReplaceKeyframeSelection(_scratchKeys);
        _scratchKeys.Clear();
    }

    public void AddToKeyframeSelection(IReadOnlyList<VDefinition> keys)
    {
        for (var i = 0; i < _editors.Count; i++)
            _editors[i].AddToKeyframeSelection(keys);
    }

    public void RemoveFromKeyframeSelection(IReadOnlyList<VDefinition> keys)
    {
        for (var i = 0; i < _editors.Count; i++)
            _editors[i].RemoveFromKeyframeSelection(keys);
    }

    public void ApplyKeyframeTimeOffset(IReadOnlyList<VDefinition> keys, double deltaU)
    {
        for (var i = 0; i < _editors.Count; i++)
            _editors[i].ApplyKeyframeTimeOffset(keys, deltaU);
    }

    /// <summary>
    /// Consume the static <see cref="AnimationParameterEditing.CurvesTablesNeedsRefresh"/> flag
    /// by rebuilding curve tables on every active editor. Each editor owns different parameter
    /// subsets (and therefore different curves), so all need to rebuild before the flag is cleared.
    /// </summary>
    public void ProcessPendingCurveTableRebuild()
    {
        if (!AnimationParameterEditing.CurvesTablesNeedsRefresh)
            return;
        for (var i = 0; i < _editors.Count; i++)
            _editors[i].RebuildCurves();
        AnimationParameterEditing.CurvesTablesNeedsRefresh = false;
    }

    private readonly List<AnimationParameterEditing> _editors;
    private static readonly List<VDefinition> _scratchKeys = new(64);
}
