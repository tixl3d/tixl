#nullable enable
using ImGuiNET;
using T3.Core.DataTypes.Vector;
using T3.Core.Utils;
using T3.Editor.Gui.Styling;
using T3.Editor.Gui.UiHelpers;

namespace T3.Editor.Gui.Interaction.Snapping;

internal sealed class ValueSnapHandler
{
    public ValueSnapHandler(SnapResult.Orientations orientation)
    {
        _orientation = orientation;
        _snapResult.Orientation = orientation;
    }
    
    public void AddSnapAttractor(IValueSnapAttractor sp)
    {
        _snapAttractors.Add(sp);
    }

    public void RemoveSnapAttractor(IValueSnapAttractor sp)
    {
        _snapAttractors.Remove(sp);
    }

    /// <summary>
    /// Uses all registered snap providers to test for snapping
    /// </summary>
    public bool TryCheckForSnapping(double targetValue, 
                                    out double snappedValue,
                                    float canvasScale = 1,
                                    IEnumerable<IValueSnapAttractor>? ignoredAttractors = null,
                                    IEnumerable<IValueSnapAttractor>? moreAttractors = null)
    {
        _snapResult.ResetForTargetValue(targetValue, canvasScale);
        _snapResult.Orientation = _orientation;

        foreach (var attractor in _snapAttractors)
        {
            if (ignoredAttractors != null && ignoredAttractors.Contains(attractor))
                continue;

            attractor.CheckForSnap(ref _snapResult);
        }

        if (moreAttractors != null)
        {
            foreach (var attractor in moreAttractors)
            {
                if (ignoredAttractors != null && ignoredAttractors.Contains(attractor))
                    continue;

                attractor.CheckForSnap(ref _snapResult);
            }
        }

        if (_snapResult.IsValid)
        {
            _lastSnapTime = ImGui.GetTime();
            _lastSnapValue = _snapResult.BestAnchorValue;

            snappedValue = _snapResult.BestAnchorValue;
            return true;
        }

        snappedValue = double.NaN;
        return false;
    }
    
    public void DrawSnapIndicator(ICanvas canvas, Color colorOverride= default)
    {
        if (ImGui.GetTime() - _lastSnapTime > SnapIndicatorDuration)
            return;

        var opacity = (1 - ((float)(ImGui.GetTime() - _lastSnapTime) / SnapIndicatorDuration).Clamp(0, 1)) * 0.4f;
        var color = colorOverride == 0 ?  UiColors.StatusAnimated : colorOverride;
        
        color.Rgba.W *= opacity;

        switch (_orientation)
        {
            case SnapResult.Orientations.Vertical:
            {
                var p = new Vector2(0, canvas.TransformY((float)_lastSnapValue));
                p.Y = (int)p.Y - 1;
                //Log.Debug("Drawing " + p);
                ImGui.GetWindowDrawList().AddRectFilled(p, p + new Vector2(4000, 1), color);
                break;
            }
            case SnapResult.Orientations.Horizontal:
            {
                var p = new Vector2(canvas.TransformX((float)_lastSnapValue), 0);
                ImGui.GetWindowDrawList().AddRectFilled(p, p + new Vector2(1, 2000), color);
                break;
            }
        }
    }

    private const float SnapIndicatorDuration = 1;
    private readonly SnapResult.Orientations _orientation;
    private readonly HashSet<IValueSnapAttractor> _snapAttractors = [];
    private double _lastSnapValue;
    private double _lastSnapTime;
    private SnapResult _snapResult = new(0);
}