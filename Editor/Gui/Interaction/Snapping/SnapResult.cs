#nullable enable
using T3.Editor.Gui.UiHelpers;

namespace T3.Editor.Gui.Interaction.Snapping;

/// <summary>
/// Helper struct to pass return values
/// </summary>
/// <remarks>
/// Snap behaviour is implemented through an instance of a ValueSnapHandler and 
/// any number of ValueSnapProviders registering to the SnapHandler. When manipulating 
/// the value by dragging or other interactions, you can constantly check the SnapHandler
/// if the current value would snap to a new value coming from any of the registered
/// SnapProviders.
/// </remarks>
public sealed class SnapResult
{
    internal SnapResult(double targetValue, float canvasScale = 1)
    {
        TargetValue = targetValue;
        CanvasScale = canvasScale;
    }
    
    internal void TryToImproveWithAnchorValueList(IEnumerable<double> anchorValue)
    {
        foreach (var v in anchorValue)
        {
            TryToImproveWithAnchorValue(v);
        }
    }
    
    internal void TryToImproveWithAnchorValue(double anchorValue)
    {
        var snapThresholdOnCanvas = UserSettings.Config.SnapStrength / CanvasScale;
        var distance = Math.Abs(anchorValue - TargetValue);
            
        var newForce = Math.Max(0, Math.Abs(snapThresholdOnCanvas) - distance);
        if (newForce < 0.00001)
            return;

        if (IsValid && BestForce > newForce)
            return;

        BestForce = newForce;
        BestAnchorValue = anchorValue;
        IsValid = true;
    }

    public void ResetForTargetValue(double targetValue, float canvasScale = 1)
    {
        TargetValue = targetValue;
        CanvasScale = canvasScale;
        IsValid = false;
        BestAnchorValue = double.NaN;
        BestForce = 0;
    }

    internal float CanvasScale;
    internal double TargetValue;
    internal bool IsValid;
    internal double BestAnchorValue;
    internal double BestForce;
    internal Orientations Orientation = Orientations.Unknown;

    public enum Orientations
    {
        Unknown,
        Horizontal,
        Vertical,
    }
}