using ImGuiNET;
using T3.Core.DataTypes.Vector;
using T3.Editor.Gui;
using T3.Editor.Gui.UiHelpers;
using T3.Editor.SystemUi;
using T3.SystemUi;

namespace T3.Editor.UiModel.Selection;

/// <summary>
/// Provides fence selection interaction
/// </summary>
public sealed class SelectionFence
{
    internal States UpdateAndDraw(out SelectModes selectMode, bool allowRectOutOfBounds = true)
    {
        if (_state is States.CompletedAsArea or States.CompletedAsClick)
            _state = States.Inactive;
            
        var globalMouse = EditorUi.Instance.Cursor;

        var io = ImGui.GetIO();
        var imguiMousePos = io.MousePos;
        selectMode = GetSelectMode(io);
        var isLeftButtonDown = globalMouse.IsButtonDown(MouseButtons.Left);
            
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
            _state = States.PressedButNotMoved;
            return States.PressedButNotMoved;
        }

        var globalMousePos = globalMouse.PositionVec;
        var interactionMin = Vector2.Max(_startPositionInScreen, globalMousePos);
        var interactionMax = Vector2.Min(_startPositionInScreen, globalMousePos);
            
        // TiXL runs with style.WindowPadding = 0, so the window rect equals the inner content rect.
        var windowMin = ImGui.GetWindowPos();
        var windowMax = windowMin + ImGui.GetWindowSize();
        var onScreenMin = Vector2.Max(windowMin, interactionMin);
        var onScreenMax = Vector2.Min(windowMax, interactionMax);
        
        BoundsInScreen = ImRect.RectBetweenPoints(onScreenMin, onScreenMax);
        BoundsUnclamped = ImRect.RectBetweenPoints(_startPositionInScreen, globalMousePos);

        if (!isLeftButtonDown) // check for release - even if released off-window
        {
            _state = _state == States.PressedButNotMoved ? States.CompletedAsClick : States.CompletedAsArea;
            return _state;
        }

        var clickThreshold = UserSettings.Config.ClickThreshold;
            
        var totalPositionDelta = globalMousePos - _startPositionInScreen;
        if (_state == States.PressedButNotMoved && totalPositionDelta.LengthSquared() < clickThreshold * clickThreshold)
            return _state;

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
}