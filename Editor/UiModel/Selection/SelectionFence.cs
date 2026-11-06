using ImGuiNET;
using T3.Core.DataTypes.Vector;
using T3.Editor.Gui;
using T3.Editor.Gui.Interaction;
using T3.Editor.Gui.UiHelpers;

namespace T3.Editor.UiModel.Selection;

/// <summary>
/// Provides fence selection interaction
/// </summary>
public sealed class SelectionFence
{
    /// <param name="canvas">When given, the fence's start stays pinned to the canvas content while the view
    /// moves, and dragging to the edge of the view scrolls the canvas so the fence can reach content
    /// outside of it.</param>
    /// <param name="scrollVertically">Whether that edge scrolling also moves the canvas vertically.</param>
    internal States UpdateAndDraw(out SelectModes selectMode, bool allowRectOutOfBounds = true,
                                  ScalableCanvas canvas = null, bool scrollVertically = true)
    {
        if (_state is States.CompletedAsArea or States.CompletedAsClick)
            _state = States.Inactive;
            
        var io = ImGui.GetIO();
        // ImGui's position, not the desktop cursor's: the two only agree on an unscaled display with the window
        // at the origin, and elsewhere the fence would start at a fraction of the distance to the pointer.
        var imguiMousePos = io.MousePos;
        selectMode = GetSelectMode(io);
        // ImGui's button state, not the desktop's: a tablet pen is no pointer button to the windowing system, and
        // SDL keeps the pointer captured while pressed, so a release off-window still arrives here.
        var isLeftButtonDown = ImGui.IsMouseDown(ImGuiMouseButton.Left);
            
        const ImGuiHoveredFlags hoverRules = ImGuiHoveredFlags.AllowWhenBlockedByPopup | ImGuiHoveredFlags.ChildWindows;
            
        if (_state == States.Inactive)
        {
            if (ImGui.IsAnyItemHovered()
                || !ImGui.IsWindowHovered(hoverRules)
                || !isLeftButtonDown
                || ImGui.GetIO().KeyAlt
                || FrameStats.Last.IsItemContextMenuOpen
                // Dragging a modal dialog across the canvas must not start a fence below it.
                || FrameStats.Last.IsModalDialogOpen)
            {
                return States.Inactive;
            }

            _startPositionInScreen = imguiMousePos;
            if (canvas != null)
            {
                _startPositionOnCanvas = canvas.InverseTransformPositionFloat(imguiMousePos);
                EdgeScrolling.Reset();
            }

            _state = States.PressedButNotMoved;
            return States.PressedButNotMoved;
        }

        if (canvas != null)
            _startPositionInScreen = canvas.TransformPositionFloat(_startPositionOnCanvas);

        var interactionMin = Vector2.Max(_startPositionInScreen, imguiMousePos);
        var interactionMax = Vector2.Min(_startPositionInScreen, imguiMousePos);
            
        // TiXL runs with style.WindowPadding = 0, so the window rect equals the inner content rect.
        var windowMin = ImGui.GetWindowPos();
        var windowMax = windowMin + ImGui.GetWindowSize();
        var onScreenMin = Vector2.Max(windowMin, interactionMin);
        var onScreenMax = Vector2.Min(windowMax, interactionMax);
        
        BoundsInScreen = ImRect.RectBetweenPoints(onScreenMin, onScreenMax);
        BoundsUnclamped = ImRect.RectBetweenPoints(_startPositionInScreen, imguiMousePos);

        if (!isLeftButtonDown) // check for release - even if released off-window
        {
            _state = _state == States.PressedButNotMoved ? States.CompletedAsClick : States.CompletedAsArea;
            return _state;
        }

        var clickThreshold = UserSettings.Config.ClickThreshold;
            
        var totalPositionDelta = imguiMousePos - _startPositionInScreen;
        if (_state == States.PressedButNotMoved && totalPositionDelta.LengthSquared() < clickThreshold * clickThreshold)
            return _state;

        if (canvas != null)
            EdgeScrolling.Update(canvas, scrollVertically);

        var drawList = ImGui.GetWindowDrawList();
        var drawRect = allowRectOutOfBounds ? BoundsUnclamped : BoundsInScreen;
        drawList.AddRectFilled(drawRect.Min, drawRect.Max, new Color(0.1f), 1);
        drawList.AddRect(drawRect.Min - Vector2.One, drawRect.Max + Vector2.One, new Color(0,0,0, 0.4f), 1);
        _state = States.Updated;
        return _state;

        static SelectModes GetSelectMode(in ImGuiIOPtr io)
        {
            if (io.KeyShift)
            {
                return SelectModes.Add;
            }

            if (io.KeyCtrl)
            {
                return SelectModes.Remove;
            }

            return SelectModes.Replace;
        }
    }

    internal void Reset()
    {
        _state = States.Inactive;
    }

    public enum SelectModes
    {
        Add = 0,
        Remove,
        Replace
    }

    internal enum States
    {
        Inactive,
        PressedButNotMoved,
        Updated,
        CompletedAsArea,
        CompletedAsClick,
    }

    internal States State => _state;
    private States _state;

    internal ImRect BoundsInScreen;
    internal ImRect BoundsUnclamped;
    private Vector2 _startPositionInScreen;
    private Vector2 _startPositionOnCanvas;
}