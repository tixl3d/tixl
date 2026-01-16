using T3.Core.Animation;
using T3.Core.DataTypes;
using T3.Core.Operator;
using T3.Editor.Gui.Interaction.WithCurves;

namespace T3.Editor.Gui.Windows.TimeLine;

/// <summary>
/// Links to AnimationParameters to editors like DopeSheets or <see cref="TimelineCurveEditArea"/>>
/// </summary>
internal abstract class AnimationParameterEditing : CurveEditing
{
    protected override IEnumerable<Curve> GetAllCurves()
    {
        foreach (TimeLineCanvas.AnimationParameter param in AnimationParameters)
        {
            if (param.Curves == null)
                continue;
                
            foreach (var curve in param.Curves)
            {
                if (curve == null)
                    continue;
                    
                yield return curve;
            }
        }
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
            if (dopeSheet.TimeLineCanvas.LayersArea.TryGetBounds(out var clipBounds, !hasSomeKeys))
            {
                if (hasSomeKeys)
                {
                    bounds.Min.X = MathF.Min(bounds.Min.X, clipBounds.Min.X);
                    bounds.Max.X = MathF.Max(bounds.Max.X, clipBounds.Max.X);
                }
                else
                {
                    bounds= clipBounds;
                }
            }
        }
        
        
        TimeLineCanvas.Current.SetScopeToCanvasArea(bounds, flipY:true, 300, 50);
    }

    protected List<TimeLineCanvas.AnimationParameter> AnimationParameters;
    protected TimeLineCanvas TimeLineCanvas; // This gets initialized in constructor of implementations 
    public static bool CurvesTablesNeedsRefresh;
}