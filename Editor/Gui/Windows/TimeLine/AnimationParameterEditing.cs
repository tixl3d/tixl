#nullable enable

using T3.Core.Animation;
using T3.Core.DataTypes;
using T3.Core.Operator;
using T3.Editor.Gui.Interaction.WithCurves;

namespace T3.Editor.Gui.Windows.TimeLine;

/// <summary>
/// Links to AnimationParameters to editors like DopeSheets or <see cref="TimelineCurveEditor"/>>
/// </summary>
internal abstract class AnimationParameterEditing : CurveEditing
{
    protected AnimationParameterEditing(VersionedKeyframeSet sharedSelection) : base(sharedSelection) { }

    protected override IEnumerable<Curve> GetAllCurves()
    {
        foreach (TimeLineCanvas.AnimationParameter param in AnimationParameters)
        {
            foreach (var curve in param.Curves)
            {
                yield return curve;
            }
        }
    }

    /// <summary>
    /// For some operations like copy and paste between curves, we need more context.
    /// </summary>
    protected override IEnumerable<KeyframeCopyAndPasting.CurveWithDetails> GetAllCurvesWithDetails()
    {
        foreach (var param in AnimationParameters)
        {
            var index = 0;
            foreach (var curve in param.Curves)
            {
                yield return new KeyframeCopyAndPasting.CurveWithDetails(curve, param.Instance.SymbolChildId,  param.Input.Id, index++);
            }
        }
    }

    protected override void PasteKeyframes()
    {
        if (!KeyframeCopyAndPasting.TryPasteTo(AnimationParameters, out var newKeyframes)) 
            return;
        
        RebuildCurveTables();
        SelectedKeyframes.Clear();
        SelectedKeyframes.UnionWith(newKeyframes);
    }

    protected override void DeleteSelectedKeyframes(Instance composition)
    {
        TimeLineCanvas.DeleteSelectedElements(composition);
    }

    public TimeRange GetSelectionTimeRange()
    {
        var timeRange = TimeRange.Undefined;
        foreach (var s in SelectedKeyframes)
        {
            timeRange.Unite((float)s.U);
        }

        return timeRange;
    }

    public void UpdateDragStretchCommand(double scaleU, double scaleV, double originU, double originV)
    {
        foreach (var vDefinition in SelectedKeyframes)
        {
            vDefinition.U = originU + (vDefinition.U - originU) * scaleU;
        }

        RebuildCurveTables();
    }

    protected override void ViewAllOrSelectedKeys(bool alsoChangeTimeRange = false)
    {
        var hasSomeKeys = TryGetBoundsOnCanvas(GetSelectedOrAllPoints(), out var bounds);
        if (this is DopeSheetArea dopeSheet)
        {
            if (dopeSheet.TimeLineCanvas.ClipArea.TryGetBounds(out var clipBounds, !hasSomeKeys))
            {
                if (hasSomeKeys)
                {
                    bounds.Min.X = MathF.Min(bounds.Min.X, clipBounds.Min.X);
                    bounds.Max.X = MathF.Max(bounds.Max.X, clipBounds.Max.X);
                }
                else
                {
                    bounds = clipBounds;
                }
            }
        }

        TimeLineCanvas.Current?.SetScopeToCanvasArea(bounds, flipY: true, 300, 50);
    }

    //
    // Selection helpers — shared by DopeSheetArea and TimelineCurveEditor so the timeline's
    // SelectionRangeIndicator / TimeSelectionArea can operate in either mode.
    //

    public bool IsKeyframeSelected(VDefinition v) => SelectedKeyframes.Contains(v);
    public int SelectionChangeCounter => SelectedKeyframes.ChangeCounter;
    public int SelectedKeyframeCount => SelectedKeyframes.Count;

    public IEnumerable<VDefinition> EnumerateSelectedKeyframes() => SelectedKeyframes;
    public IEnumerable<VDefinition> EnumerateAllKeyframes() => GetAllKeyframes();

    public void CopyAllCurvesTo(List<Curve> buffer)
    {
        buffer.Clear();
        foreach (var curve in GetAllCurves())
            buffer.Add(curve);
    }

    public void CopyKeyframeSelectionTo(List<VDefinition> buffer)
    {
        buffer.Clear();
        foreach (var v in SelectedKeyframes)
            buffer.Add(v);
    }

    public TimeRange GetAllKeyframesTimeRange()
    {
        var range = TimeRange.Undefined;
        foreach (var v in GetAllKeyframes())
            range.Unite((float)v.U);
        return range;
    }

    public void ReplaceKeyframeSelection(IEnumerable<VDefinition> keys)
    {
        SelectedKeyframes.Clear();
        SelectedKeyframes.UnionWith(keys);
        TimeLineCanvas.Current?.OnKeyframeSelectionReplaced();
    }

    public void AddToKeyframeSelection(IReadOnlyList<VDefinition> keys)
    {
        for (var i = 0; i < keys.Count; i++)
            SelectedKeyframes.Add(keys[i]);
    }

    public void RemoveFromKeyframeSelection(IReadOnlyList<VDefinition> keys)
    {
        for (var i = 0; i < keys.Count; i++)
            SelectedKeyframes.Remove(keys[i]);
    }

    public void SelectAllKeyframes()
    {
        SelectedKeyframes.Clear();
        SelectedKeyframes.UnionWith(GetAllKeyframes());
    }

    /// <summary>Shifts the given keyframes in time and rebuilds curve tables. Caller owns undo wrapping.</summary>
    public void ApplyKeyframeTimeOffset(IReadOnlyList<VDefinition> keys, double deltaU)
    {
        for (var i = 0; i < keys.Count; i++)
            keys[i].U += deltaU;
        RebuildCurveTables();
    }

    /// <summary>Public pass-through to the protected <see cref="CurveEditing.RebuildCurveTables"/>
    /// so that <see cref="KeyframeEditorGroup"/> can trigger a rebuild on this editor.</summary>
    public void RebuildCurves() => RebuildCurveTables();

    protected List<TimeLineCanvas.AnimationParameter> AnimationParameters = [];
    protected TimeLineCanvas TimeLineCanvas = null!; // This gets initialized in constructor of implementations
    public static bool CurvesTablesNeedsRefresh;
}