#nullable enable
using System.Diagnostics;
using ImGuiNET;
using T3.Editor.Gui.UiHelpers;

namespace T3.Editor.Gui.Interaction;

/// <remarks>
/// Handling view transitions (e.g. to bring an operator or a certain part of graph into view) is
/// a very annoying problem, because it requires know of the current window size. Frequently this
/// is not available when the transition is requested (e.g. because the Window has not been initialized
/// yet or is not open). 
/// </remarks>
public partial class ScalableCanvas
{
    public CanvasScope GetTargetScope()
    {
        return new CanvasScope
                   {
                       Scale = ScaleTarget,
                       Scroll = ScrollTarget
                   };
    }

    internal void SetTargetScope(CanvasScope scope)
    {
        ScaleTarget = scope.Scale;
        ScrollTarget = scope.Scroll;
    }
    
    internal void SetScopeInstant(CanvasScope scope)
    {
        Scale = ScaleTarget = scope.Scale;
        Scroll = ScrollTarget = scope.Scroll;
    }

    internal void SetScaleToMatchPixels()
    {
        ScaleTarget = Vector2.One;
    }

    internal void FitAreaOnCanvas(ImRect areaOnCanvas, bool flipY = false)
    {
        var scope = GetScopeForCanvasArea(areaOnCanvas, flipY);
        ScaleTarget = scope.Scale;
        ScrollTarget = scope.Scroll;
    }
    
    // Todo: merge into GetScopeForCanvasArea
    internal void SetScopeToCanvasArea(ImRect areaOnCanvas, bool flipY = false, float paddingX = 0, float paddingY = 0)
    {
        WindowSize = ImGui.GetContentRegionMax() - ImGui.GetWindowContentRegionMin();

        var areaSize = areaOnCanvas.GetSize();
        if (areaSize.X == 0)
            areaSize.X = 1;

        if (areaSize.Y == 0)
            areaSize.Y = 1;
        
        var newScale = (WindowSize - new Vector2(paddingX, paddingY));
        newScale.X = MathF.Max(newScale.X, 20);
        newScale.Y = MathF.Max(newScale.Y, 20);

        newScale /= areaSize;

        if (flipY)
        {
            newScale.Y *= -1;
        }

        ScrollTarget = new Vector2(areaOnCanvas.Min.X - (paddingX / newScale.X) / 2,
                                   areaOnCanvas.Max.Y - (paddingY / newScale.Y) / 2);

        ScaleTarget = newScale;

        var isScaleTargetInvalid = ScaleTarget.X == 0
                                   || ScaleTarget.Y == 0
                                   || float.IsNaN(ScaleTarget.X)
                                   || float.IsNaN(ScaleTarget.Y)
                                   || float.IsInfinity(ScaleTarget.X)
                                   || float.IsInfinity(ScaleTarget.Y);
        if (isScaleTargetInvalid)
        {
            ScaleTarget = Scale;
        }

        if (float.IsNaN(ScrollTarget.X) || float.IsNaN(ScrollTarget.Y) || float.IsInfinity(ScrollTarget.X) || float.IsInfinity(ScrollTarget.Y))
        {
            //Debug.Assert(false); // should never happen
            Scroll = ScrollTarget;
        }
    }

    internal void SetVerticalScopeToCanvasArea(ImRect area, bool flipY = false, ScalableCanvas? parent = null)
    {
        WindowSize = ImGui.GetContentRegionMax() - ImGui.GetWindowContentRegionMin();
        var sizeY = area.GetSize().Y;
        if (MathF.Abs(area.GetSize().Y) < 0.0001f)
        {
            sizeY = 1;
        }
        
        ScaleTarget.Y = WindowSize.Y / sizeY;

        if (flipY)
        {
            ScaleTarget.Y *= -1;
        }

        if (parent != null)
        {
            ScaleTarget.Y /= parent.Scale.Y;
        }

        ScrollTarget.Y = area.Max.Y;
    }

    internal ImRect GetVisibleCanvasArea()
    {
        UpdateWindowRect();
        var rectWithSize = ImRect.RectWithSize(WindowPos, WindowSize);
        return InverseTransformRect(rectWithSize);
    }



    /// <summary>
    /// To accurately showing the requested area on a canvas, we need to have access to the current window's size.
    /// Frequently, this is only available after the window has been initialized on the next frame.
    /// So we first have to call <see cref="RequestTargetViewAreaWithTransition"/>
    /// </summary>
    internal void RequestTargetViewAreaWithTransition(ImRect targetCanvasArea, Transition transition)
    {
        if (_requestedTransition != null)
            Log.Warning("Requesting transition twice?");

        _requestedTransition = new TransitionToArea(targetCanvasArea, transition);
    }

    private void HandleRequestedTransitions()
    {
        if (_requestedTransition == null)
            return;

        UpdateWindowRect();
        var scope = GetScopeForCanvasArea(_requestedTransition.CanvasArea);
        SetScopeWithTransition(scope, _requestedTransition.Transition);
        _requestedTransition = null;
    }

    private void SetScopeWithTransition(CanvasScope scope, Transition transition)
    {
        var scale = scope.Scale;
        var scroll = scope.Scroll;

        if (float.IsInfinity(scale.X) || float.IsNaN(scale.X)
                                      || float.IsInfinity(scale.Y) || float.IsNaN(scale.Y)
                                      || float.IsInfinity(scroll.X) || float.IsNaN(scroll.X)
                                      || float.IsInfinity(scroll.Y) || float.IsNaN(scroll.Y)
           )
        {
            scale = Vector2.One;
            scroll = Vector2.Zero;
        }

        ScaleTarget = scale;
        ScrollTarget = scroll;

        switch (transition)
        {
            case Transition.JumpIn:
                SetZoomedScope(14);
                break;

            case Transition.JumpOut:
                SetZoomedScope(0.05f);
                break;

            case Transition.Instant:
                Scroll = ScaleTarget;
                Scroll = ScrollTarget;
                break;
        }

        return;

        void SetZoomedScope(float factor)
        {
            var targetArea = GetCanvasAreaForScope(GetTargetScope());
            var zoomedInSize = targetArea.GetSize() * factor;
            var zoomedInArea = ImRect.RectWithSize(targetArea.GetCenter() - zoomedInSize * 0.5f, zoomedInSize);
            var zoomedInScope = GetScopeForCanvasArea(zoomedInArea);
            Scale = zoomedInScope.Scale;
            Scroll = zoomedInScope.Scroll;
        }
    }

    private CanvasScope GetScopeForCanvasArea(ImRect areaOnCanvas, bool flipY = false)
    {
        var heightOnCanvas = areaOnCanvas.GetHeight();
        var widthOnCanvas = areaOnCanvas.GetWidth();
        var aspectOnCanvas = widthOnCanvas / heightOnCanvas;

        Vector2 scrollTarget;
        float scale;
        if (aspectOnCanvas > WindowSize.X / WindowSize.Y)
        {
            // Center in a high window...
            scale = WindowSize.X / widthOnCanvas;
            scrollTarget = new Vector2(
                                       areaOnCanvas.Min.X,
                                       areaOnCanvas.Min.Y - (WindowSize.Y / scale - heightOnCanvas) / 2);
        }
        else
        {
            // Center in a wide window... 
            scale = WindowSize.Y / heightOnCanvas;
            scrollTarget = new Vector2(
                                       areaOnCanvas.Min.X - (WindowSize.X / scale - widthOnCanvas) / 2,
                                       areaOnCanvas.Min.Y);
        }

        var scaleTarget = new Vector2(scale, scale);
        if (flipY)
        {
            scaleTarget.Y *= -1;
        }

        return new CanvasScope { Scale = scaleTarget, Scroll = scrollTarget };
    }

    /// <summary>
    /// Careful! This requires the window's size to be initialized.
    /// </summary>
    private ImRect GetCanvasAreaForScope(CanvasScope scope)
    {
        var scale = scope.Scale;
        if (scale.Y < 0) // Handle flipped Y
        {
            scale.Y = -scale.Y;
        }

        var widthOnCanvas = WindowSize.X / scale.X;
        var heightOnCanvas = WindowSize.Y / scale.Y;

        Vector2 minOnCanvas;
        if (WindowSize.X / WindowSize.Y > widthOnCanvas / heightOnCanvas)
        {
            // Inverse of centering in a high window
            minOnCanvas = new Vector2(
                                      scope.Scroll.X,
                                      scope.Scroll.Y + (WindowSize.Y / scale.Y - heightOnCanvas) / 2
                                     );
        }
        else
        {
            // Inverse of centering in a wide window
            minOnCanvas = new Vector2(
                                      scope.Scroll.X + (WindowSize.X / scale.X - widthOnCanvas) / 2,
                                      scope.Scroll.Y
                                     );
        }

        Vector2 maxOnCanvas = minOnCanvas + new Vector2(widthOnCanvas, heightOnCanvas);
        return new ImRect(minOnCanvas, maxOnCanvas);
    }

    private TransitionToArea? _requestedTransition;

    private sealed record TransitionToArea(ImRect CanvasArea, Transition Transition);

    internal enum Transition
    {
        Instant,
        JumpIn,
        JumpOut,
        Smooth, // Only set target
    }
}

public struct CanvasScope
{
    internal Vector2 Scale;
    internal Vector2 Scroll;

    internal bool IsValid()
    {
        return Scale.X != 0
               && Scale.Y != 0
               && !float.IsNaN(Scale.X)
               && !float.IsNaN(Scale.Y)
               && !float.IsInfinity(Scale.X)
               && !float.IsInfinity(Scale.Y)
               && !float.IsNaN(Scroll.X)
               && !float.IsNaN(Scroll.Y)
               && !float.IsInfinity(Scroll.X)
               && !float.IsInfinity(Scroll.Y);
    }

    public override string ToString()
    {
        return $"[{Scroll:0} ×{Scale:0.00}]";
    }
}