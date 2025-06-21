#nullable enable
using System.Diagnostics;
using System.Runtime.CompilerServices;
using ImGuiNET;
using T3.Core.DataTypes.Vector;
using T3.Core.Operator;
using T3.Core.Utils;
using T3.Editor.Gui.MagGraph.Ui;
using T3.Editor.Gui.Styling;
using T3.Editor.Gui.UiHelpers;
using T3.Editor.Gui.Windows.TimeLine;
using T3.Editor.UiModel.ProjectHandling;

namespace T3.Editor.Gui.Interaction;

// hacky interface to extend IGraphCanvas
internal interface IScalableCanvas : ICanvas
{
    public void UpdateCanvas(out ScalableCanvas.InteractionState interactionState, T3Ui.EditingFlags flags = T3Ui.EditingFlags.None);
    public Vector2 ChildPosFromCanvas(Vector2 posOnCanvas);
    public void SetTargetView(Vector2 scale, Vector2 scroll);
    public void SetViewInstant(Vector2 scale, Vector2 scroll);
    public void SetScaleToMatchPixels();
    public void SetScopeWithTransition(Vector2 scale, Vector2 scroll, Transition transition);
    public void SetScopeToCanvasArea(ImRect area, bool flipY = false, IScalableCanvas? parent = null, float paddingX = 0, float paddingY = 0);
    public void SetVerticalScopeToCanvasArea(ImRect area, bool flipY = false, ScalableCanvas? parent = null);
    public void ZoomWithMouseWheel(MouseState mouseState, out bool zoomed);
    public Vector2 TransformPositionFloat(Vector2 posOnCanvas);
    public void FitAreaOnCanvas(ImRect areaOnCanvas, bool flipY = false);
    public void SetTargetScope(CanvasScope scope);
    public CanvasScope GetTargetScope();
    public bool EnableParentZoom { get; set; }
    public Vector2 ScaleTarget { get; }
}

/// <summary>
/// Implements transformations and interactions for a canvas that can be zoomed and panned.
/// </summary>
/// <todo>
/// This should be refactored into two parts:
/// - struct ViewTransform {Scroll, Zoom} -> For all transforms and conversion operations
/// - sealed class InteractiveCanvas -> For transitions, interactions, etc.
/// Other canvas should not inherit from this but use composition.
/// IScalableCanvas should be removed
/// </todo>
internal abstract class ScalableCanvas : IScalableCanvas
{
    protected ScalableCanvas(Vector2? initialScale = null)
    {
        if (initialScale == null)
            return;

        Scale = ScaleTarget = initialScale.Value;
    }

    /// <summary>
    /// This needs to be called by the inherited class before drawing its interface. 
    /// </summary>
    public void UpdateCanvas(out InteractionState interactionState, T3Ui.EditingFlags flags = T3Ui.EditingFlags.None)
    {
        UpdateWindowRect();

        var io = ImGui.GetIO();
        var mouse = new MouseState(io.MousePos, io.MouseDelta, io.MouseWheel);

        DampScaling(io.DeltaTime);

        HandleInteraction(flags, mouse, out var zoomed, out var panned);
        interactionState = new InteractionState(panned, zoomed, mouse);
    }

    private void UpdateWindowRect()
    {
        if (FillMode == FillModes.FillWindow)
        {
            var paddingForFocusBorder = UserSettings.Config.FocusMode ? 0 : 1;
            
            WindowPos = ImGui.GetWindowContentRegionMin() + ImGui.GetWindowPos() + paddingForFocusBorder * Vector2.One;
            WindowSize = ImGui.GetWindowContentRegionMax() - ImGui.GetWindowContentRegionMin() - paddingForFocusBorder * 2  * Vector2.One;
        }
        else
        {
            WindowSize = ImGui.GetContentRegionAvail();
            WindowPos = ImGui.GetCursorScreenPos();
        }
    }

    #region implement ICanvas =================================================================
    /// <summary>
    /// Convert canvas position (e.g. of an Operator) into screen position  
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public virtual Vector2 TransformPositionFloat(Vector2 posOnCanvas)
    {
        return (posOnCanvas - Scroll) * Scale + WindowPos;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Vector2 TransformPosition(Vector2 posOnCanvas)
    {
        var v = TransformPositionFloat(posOnCanvas);
        return new Vector2((int)v.X, (int)v.Y);
    }

    /// <summary>
    /// Convert canvas position (e.g. of an Operator) to screen position  
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public float TransformX(float xOnCanvas)
    {
        return TransformPosition(new Vector2(xOnCanvas, 0)).X;
    }

    /// <summary>
    ///  Convert canvas position (e.g. of an Operator) to screen position 
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public float TransformY(float yOnCanvas)
    {
        return TransformPositionFloat(new Vector2(0, yOnCanvas)).Y;
    }

    /// <summary>
    /// Convert a screen space position (e.g. from mouse) to canvas coordinates  
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public virtual Vector2 InverseTransformPositionFloat(Vector2 screenPos)
    {
        return (screenPos - WindowPos) / (Scale) + Scroll;
    }

    /// <summary>
    /// Convert screen position to canvas position
    /// </summary>
    public virtual float InverseTransformX(float xOnScreen)
    {
        return InverseTransformPositionFloat(new Vector2(xOnScreen, 0)).X;
    }

    /// <summary>
    /// Convert screen position to canvas position
    /// </summary>
    public float InverseTransformY(float yOnScreen)
    {
        return InverseTransformPositionFloat(new Vector2(0, yOnScreen)).Y;
    }

    /// <summary>
    /// Convert a direction (e.g. MouseDelta) from ScreenSpace to Canvas
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Vector2 TransformDirection(Vector2 vectorInCanvas)
    {
        return TransformPositionFloat(vectorInCanvas) -
               TransformPositionFloat(new Vector2(0, 0));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Vector2 TransformDirectionFloored(Vector2 vectorInCanvas)
    {
        var s = TransformDirection(vectorInCanvas);
        return new Vector2((int)s.X, (int)s.Y);
    }

    /// <summary>
    /// Convert a direction (e.g. MouseDelta) from ScreenSpace to Canvas
    /// </summary>
    /// [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Vector2 InverseTransformDirection(Vector2 vectorInScreen)
    {
        return vectorInScreen / (Scale);
    }

    public ImRect TransformRect(ImRect canvasRect)
    {
        // NOTE: We have to floor the size instead to min max position to avoid jitter  
        var min = TransformPositionFloat(canvasRect.Min);
        var max = TransformPositionFloat(canvasRect.Max);
        var size = max - min;
        min.X = (int)min.X;
        min.Y = (int)min.Y;
        size.X = (int)size.X;
        size.Y = (int)size.Y;
        return new ImRect(min, min + size);
    }

    public ImRect InverseTransformRect(ImRect screenRect)
    {
        return new ImRect(InverseTransformPositionFloat(screenRect.Min),
                          InverseTransformPositionFloat(screenRect.Max));
    }

    public virtual void UpdateScaleAndTranslation(Instance compositionOp, ICanvas.Transition transition)
    {
        // by default do nothing, override in subclasses
    }

    /// <summary>
    /// Transform a canvas position to relative position within ImGui-window (e.g. to set ImGui context) 
    /// </summary>
    public Vector2 ChildPosFromCanvas(Vector2 posOnCanvas)
    {
        return TransformPositionFloat(posOnCanvas) - WindowPos;
    }

    public Vector2 WindowPos { get; private set; }
    public Vector2 WindowSize { get; private set; } = new Vector2(200, 200);

    public Vector2 Scale { get; protected set; } = Vector2.One;
    protected Vector2 ScaleTarget = Vector2.One;

    Vector2 IScalableCanvas.ScaleTarget => ScaleTarget;

    public Vector2 Scroll { get; protected set; } = new(0.0f, 0.0f);
    protected Vector2 ScrollTarget = new(0.0f, 0.0f);
    #endregion

    public CanvasScope GetCurrentScope()
    {
        return new CanvasScope
                   {
                       Scale = Scale,
                       Scroll = Scroll
                   };
    }

    public CanvasScope GetTargetScope()
    {
        return new CanvasScope
                   {
                       Scale = ScaleTarget,
                       Scroll = ScrollTarget
                   };
    }

    public void SetTargetScope(CanvasScope scope)
    {
        ScaleTarget = scope.Scale;
        ScrollTarget = scope.Scroll;
    }

    public void SetTargetView(Vector2 scale, Vector2 scroll)
    {
        ScaleTarget = scale;
        ScrollTarget = scroll;
    }

    public void SetTargetViewAreaWithTransition(ImRect viewArea, ICanvas.Transition transition)
    {
        var scope = GetScopeForCanvasArea(viewArea);
        SetScopeWithTransition(scope, transition);
    }

    public void SetViewInstant(Vector2 scale, Vector2 scroll)
    {
        Scale = ScaleTarget = scale;
        Scroll = ScrollTarget = scroll;
    }

    public void SetScaleToMatchPixels()
    {
        ScaleTarget = Vector2.One;
    }

    public void SetScopeToCanvasArea(ImRect area, bool flipY = false, IScalableCanvas? parent = null, float paddingX = 0, float paddingY = 0)
    {
        var areaSize = area.GetSize();
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

        ScrollTarget = new Vector2(area.Min.X - (paddingX / newScale.X) / 2,
                                   area.Max.Y - (paddingY / newScale.Y) / 2);

        ScaleTarget = newScale;

        // FIXME: This is looks like a bug...
        var isScaleTargetInvalid = ScaleTarget.X == 0
                                   || ScaleTarget.Y == 0
                                   || float.IsNaN(ScaleTarget.X)
                                   || float.IsNaN(ScaleTarget.Y)
                                   || float.IsInfinity(ScaleTarget.X)
                                   || float.IsInfinity(ScaleTarget.Y);
        if (isScaleTargetInvalid)
        {
            Debug.Assert(false); // should never happen
            Scale = ScaleTarget;
        }

        if (float.IsNaN(ScrollTarget.X) || float.IsNaN(ScrollTarget.Y) || float.IsInfinity(ScrollTarget.X) || float.IsInfinity(ScrollTarget.Y))
        {
            Debug.Assert(false); // should never happen
            Scroll = ScrollTarget;
        }
    }

    public void SetVerticalScopeToCanvasArea(ImRect area, bool flipY = false, ScalableCanvas? parent = null)
    {
        WindowSize = ImGui.GetContentRegionMax() - ImGui.GetWindowContentRegionMin();
        ScaleTarget.Y = WindowSize.Y / area.GetSize().Y;

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

    public CanvasScope GetScopeForCanvasArea(ImRect areaOnCanvas, bool flipY = false)
    {
        UpdateWindowRect();
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

    public ImRect GetCanvasAreaForScope(CanvasScope scope)
    {
        var scale = scope.Scale;
        if (scale.Y < 0) // Handle flipped Y
        {
            scale.Y = -scale.Y;
        }

        float widthOnCanvas = WindowSize.X / scale.X;
        float heightOnCanvas = WindowSize.Y / scale.Y;

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

    public ImRect GetVisibleCanvasArea()
    {
        UpdateWindowRect();
        var rectWithSize = ImRect.RectWithSize(WindowPos, WindowSize);
        return InverseTransformRect(rectWithSize);
    }

    public void FitAreaOnCanvas(ImRect areaOnCanvas, bool flipY = false)
    {
        var scope = GetScopeForCanvasArea(areaOnCanvas, flipY);
        ScaleTarget = scope.Scale;
        ScrollTarget = scope.Scroll;
    }

    public void SetScopeWithTransition(CanvasScope scope, ICanvas.Transition transition)
    {
        SetScopeWithTransition(scope.Scale, scope.Scroll, transition);
    }

    public void SetScopeWithTransition(Vector2 scale, Vector2 scroll, ICanvas.Transition transition)
    {
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
            case ICanvas.Transition.JumpIn:
                SetZoomedScope(14);
                break;

            case ICanvas.Transition.JumpOut:
                SetZoomedScope(0.05f);
                break;

            case ICanvas.Transition.Instant:
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

    private void DampScaling(float deltaTime)
    {
        var completed = Scale.X > 1000 || Math.Abs(Scroll.X - ScrollTarget.X) < 1f
                        && Math.Abs(Scroll.Y - ScrollTarget.Y) < 1f
                        && Math.Abs(Scale.X - ScaleTarget.X) < 0.05f
                        && Math.Abs(Scale.Y - ScaleTarget.Y) < 0.05f;

        if (completed)
        {
            Scroll = ScrollTarget;
            Scale = ScaleTarget;
            return;
        }

        // Damp scaling
        var minInCanvas = Scroll;
        var maxInCanvas = Scroll + WindowSize / Scale;
        var minTargetInCanvas = ScrollTarget;
        var maxTargetInCanvas = ScrollTarget + WindowSize / ScaleTarget;

        var f = Math.Min(deltaTime / UserSettings.Config.ScrollSmoothing.Clamp(0.01f, 0.99f), 1);

        var min = Vector2.Lerp(minInCanvas, minTargetInCanvas, f);
        var max = Vector2.Lerp(maxInCanvas, maxTargetInCanvas, f);
        Scale = WindowSize / (max - min);
        Scroll = min;

        if (float.IsNaN(ScaleTarget.X))
            ScaleTarget.X = 1;

        if (float.IsNaN(ScaleTarget.Y))
            ScaleTarget.Y = 1;

        if (float.IsNaN(Scale.X) || float.IsNaN(Scale.Y) || MathF.Sign(ScaleTarget.Y) != MathF.Sign(Scale.Y))
            Scale = ScaleTarget;

        if (float.IsNaN(ScrollTarget.X))
            ScrollTarget.X = 0;

        if (float.IsNaN(ScrollTarget.Y))
            ScrollTarget.Y = 0;

        if (float.IsNaN(Scroll.X) || float.IsNaN(Scroll.Y))
            Scroll = ScrollTarget;
    }

    private void HandleInteraction(T3Ui.EditingFlags flags, in MouseState mouseState, out bool zoomed, out bool panned)
    {
        zoomed = false;
        panned = false;
        
        if (_draggedCanvas == this && !ImGui.IsMouseDragging(ImGuiMouseButton.Right))
            _draggedCanvas = null;

        var isDirectlyHovered = ImGui.IsWindowHovered(ImGuiHoveredFlags.RectOnly | ImGuiHoveredFlags.ChildWindows);
        var isInteractable = isDirectlyHovered && !FrameStats.Last.OpenedPopupHovered;
        var isPanning = _draggedCanvas == this;
        var isAnotherWindowPanning = _draggedCanvas != null && _draggedCanvas != this
                                     || CustomComponents.IsAnotherWindowDragScrolling(this);

        //DrawCanvasDebugInfos(mouseState.Position, isDirectlyHovered, isInteractable, isPanning);
            
        if ((flags & T3Ui.EditingFlags.PreventMouseInteractions) != 0)
            return;

        var isAnotherItemActive = FrameStats.Last.OpenedPopupCapturedMouse;//ImGui.IsAnyItemActive();
        
        if (
            !(isAnotherWindowPanning || isAnotherItemActive)
            && (isInteractable || isPanning)
            && (flags&T3Ui.EditingFlags.PreventPanningWithMouse) == 0
            && ((
                    ImGui.IsMouseDragging(ImGuiMouseButton.Left) && ImGui.GetIO().KeyAlt)
                || (!UserSettings.Config.MiddleMouseButtonZooms && ImGui.IsMouseDragging(ImGuiMouseButton.Middle) && !ImGui.GetIO().KeyAlt)
                || (ImGui.IsMouseDragging(ImGuiMouseButton.Right) && !ImGui.GetIO().KeyAlt))
            )
        {
            var parentScaleTarget = Parent?.ScaleTarget ?? Vector2.One;

            ScrollTarget -= mouseState.Delta / (parentScaleTarget * ScaleTarget);
            _draggedCanvas = this;
        }

        var preventZoom = (flags & T3Ui.EditingFlags.PreventZoomWithMouseWheel) != 0;

        if (isInteractable && !preventZoom)
        {
            ZoomWithMouseWheel(mouseState, out zoomed);
            ZoomWithDrag();
            ScaleTarget = ClampScaleToValidRange(ScaleTarget);
        }
    }

    private static ScalableCanvas? _draggedCanvas;
    public static bool IsAnyCanvasDragged => _draggedCanvas != null;

    private Vector2 ClampScaleToValidRange(Vector2 scale)
    {
        if (IsCurveCanvas)
            return scale;

        return this is TimeLineCanvas
                   ? new Vector2(scale.X.Clamp(0.01f, 5000), scale.Y.Clamp(0.01f, 5000))
                   : new Vector2(scale.X.Clamp(0.02f, 40), scale.Y.Clamp(0.02f, 40));
    }

    public void ZoomWithMouseWheel(MouseState mouseState, out bool zoomed)
    {
        var zoomDelta = ComputeZoomDeltaFromMouseWheel(mouseState);
        ApplyZoomDelta(mouseState.Position, zoomDelta, out zoomed);
    }

    protected void ApplyZoomDelta(Vector2 position, float zoomDelta, out bool zoomed)
    {
        var clamped = ClampScaleToValidRange(ScaleTarget * zoomDelta);
        zoomed = false;
        if (clamped == ScaleTarget)
            return;

        if (Math.Abs(zoomDelta - 1) < 0.001f)
            return;

        var zoom = zoomDelta * Vector2.One;
        if (IsCurveCanvas)
        {
            if (ImGui.GetIO().KeyAlt)
            {
                zoom.X = 1;
            }
            else if (ImGui.GetIO().KeyShift)
            {
                zoom.Y = 1;
            }
        }

        ScaleTarget *= zoom;

        if (Math.Abs(zoomDelta) > 0.1f)
            zoomed = true;

        var focusCenterOnCanvas = InverseTransformPositionFloat(position);
        ScrollTarget += (focusCenterOnCanvas - ScrollTarget) * (zoom - Vector2.One) / zoom;
    }

    private void DrawCanvasDebugInfos(Vector2 mousePos, bool directlyHovered, bool isInteractable, bool isAlreadyActive)
    {
        var focusCenterOnCanvas = InverseTransformPositionFloat(mousePos);
        var dl = ImGui.GetForegroundDrawList();

        var wp = ImGui.GetWindowPos();
        if (directlyHovered)
        {
            var focusOnScreen = TransformPosition(focusCenterOnCanvas);
            dl.AddCircle(focusOnScreen, 30, Color.Green.Fade(0.2f));
            dl.AddText(focusOnScreen + new Vector2(10, -10), Color.Green, $"{focusCenterOnCanvas.X:0.0} {focusCenterOnCanvas.Y:0.0} ");
            dl.AddRect(wp, wp + ImGui.GetWindowSize(), Color.Green.Fade(0.4f));
        }
            
        dl.AddRectFilled(wp, wp + new Vector2(200, 100), UiColors.WindowBackground.Fade(0.4f));
        dl.AddText(wp + new Vector2(160,0), Color.Green,$"hovered:{directlyHovered}\ninteractable:{isInteractable}\nactive:{isAlreadyActive}");

        dl.AddText(wp + new Vector2(0, 0), Color.Green, $"Scale: {ScaleTarget.X:0.0} {ScaleTarget.Y:0.0} ");
        dl.AddText(wp + new Vector2(0, 16), Color.Green, $"Scroll: {ScrollTarget.X:0.0} {ScrollTarget.Y:0.0} ");
        dl.AddText(wp + new Vector2(0, 32), Color.Green, $"Canvas: {focusCenterOnCanvas.X:0.0} {focusCenterOnCanvas.Y:0.0} ");
        var hovered = ImGui.IsWindowHovered() ? "hovered" : "";
        var hoveredChild = ImGui.IsWindowHovered(ImGuiHoveredFlags.ChildWindows) ? "hoveredChildWindows" : "";
        dl.AddText(wp + new Vector2(0, 48), Color.Green, $"{hovered} {hoveredChild}");
            
        var focused = ImGui.IsWindowFocused() ? "focused" : "";
        var focusedChild = ImGui.IsWindowFocused(ImGuiFocusedFlags.ChildWindows) ? "focusedChildWindows" : "";
        dl.AddText(wp + new Vector2(0, 64), Color.Green, $"{focused} {focusedChild}");
        
        // // Test window content region:
        // var paddingForFocusBorder = 1;
        // var pos = ImGui.GetWindowContentRegionMin() + ImGui.GetWindowPos() + paddingForFocusBorder * Vector2.One;
        // var size = ImGui.GetWindowContentRegionMax() - ImGui.GetWindowContentRegionMin() - paddingForFocusBorder * 2  * Vector2.One;
        // dl.AddRect(pos, pos + size, Color.Red);
        
        
    }

    private bool IsCurveCanvas => Scale.Y < 0;

    private static float ComputeZoomDeltaFromMouseWheel(in MouseState mouseState)
    {
        var ioMouseWheel = mouseState.ScrollWheel;
        if (ioMouseWheel == 0)
            return 1;

        const float zoomSpeed = 1.2f;
        var zoomSum = 1f;

        if (ioMouseWheel < 0.0f)
        {
            for (var zoom = ioMouseWheel; zoom < 0.0f; zoom += 1.0f)
            {
                zoomSum /= zoomSpeed;
            }
        }

        if (ioMouseWheel > 0.0f)
        {
            for (var zoom = ioMouseWheel; zoom > 0.0f; zoom -= 1.0f)
            {
                zoomSum *= zoomSpeed;
            }
        }

        zoomSum = zoomSum.Clamp(0.02f, 100f);
        return zoomSum;
    }

    private Vector2 _mousePosWhenDragZoomStarted;
    private bool _isDragZooming;
    private float _lastZoomDelta;

    private void ZoomWithDrag()
    {
        var mouseButton = UserSettings.Config.MiddleMouseButtonZooms
                              ? ImGuiMouseButton.Middle
                              : ImGuiMouseButton.Right;

        var hotkeysMatch = UserSettings.Config.MiddleMouseButtonZooms || ImGui.GetIO().KeyShift;

        if (ImGui.IsMouseClicked(mouseButton) && hotkeysMatch)
        {
            _isDragZooming = true;
            _lastZoomDelta = 1;
            _mousePosWhenDragZoomStarted = ImGui.GetMousePos();
        }

        if (ImGui.IsMouseReleased(mouseButton))
            _isDragZooming = false;

        if (!ImGui.IsMouseDragging(mouseButton, 0))
            return;

        var delta = ImGui.GetMousePos() - _mousePosWhenDragZoomStarted;
        var deltaMax = Math.Abs(delta.X) > Math.Abs(delta.Y)
                           ? -delta.X
                           : delta.Y;

        if (!_isDragZooming)
            return;

        var f = (float)Math.Pow(1.13f, -deltaMax / 40f);
        var delta2 = f / _lastZoomDelta;
        ApplyZoomDelta(_mousePosWhenDragZoomStarted, delta2, out var zoomed); // FIXME: unclear what this does
        _lastZoomDelta = f;
    }

    public enum FillModes
    {
        FillWindow,
        FillAvailableContentRegion,
    }

    public FillModes FillMode = FillModes.FillWindow;

    public bool EnableParentZoom { get; set; } = true;

    protected abstract IScalableCanvas? Parent { get; }

    public readonly record struct InteractionState(bool UserPannedCanvas, bool UserZoomedCanvas, MouseState MouseState);

    // public static string PrintInteractionState(in InteractionState state)
    // {
    //     var sb = new StringBuilder();
    //     sb.Append("Panned: ");
    //     sb.Append(state.UserPannedCanvas ? "Yes" : "No");
    //     sb.Append("  Zoomed: ");
    //     sb.Append(state.UserZoomedCanvas ? "Yes" : "No");
    //     sb.Append("  Mouse: ");
    //     sb.Append(state.MouseState.Position);
    //     var str = sb.ToString();
    //     Log.Debug(str);
    //     return str;
    // }
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

internal readonly record struct MouseState(Vector2 Position, Vector2 Delta, float ScrollWheel);