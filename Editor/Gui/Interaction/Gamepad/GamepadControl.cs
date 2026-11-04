#nullable enable
using ImGuiNET;
using T3.Editor.Gui.Interaction.Keyboard;
using T3.Editor.Gui.MagGraph.Interaction;
using T3.Editor.Gui.MagGraph.Interaction.KeyboardEditing;
using T3.Editor.Gui.UiHelpers;
using T3.Editor.UiModel.ProjectHandling;
using T3.IoServices;

namespace T3.Editor.Gui.Interaction.Gamepad;

/// <summary>
/// Drives the editor from the first game controller while <see cref="UserSettings.ConfigData.GamepadUsage"/> is
/// <see cref="UserSettings.GamepadUsages.Editor"/>. In the graph the buttons trigger user actions: the d-pad moves the
/// selection, A edits the selected operator's parameters or, without a selection, inserts one in the view centre,
/// B deselects or goes up, X inserts beside the selection on release (while it is held the d-pad picks the side),
/// Y shows the selection in the output (held: only peeks),
/// the shoulder buttons undo and redo, Menu toggles playback, Share focus mode, View switches to steering the camera
/// (left stick flies, right stick looks, triggers lift; View or B returns), and the left
/// stick pans, and holding the right stick down while tilting it moves the selected operator's snapped group. The symbol browser and the parameter popover are built around the keyboard, so while one of them is
/// open the pad presses their keys instead. The popover reads A (apply), B (apply and close), X (insert into the
/// focused input), Y (reset to default) and the sticks itself.
/// </summary>
internal static class GamepadControl
{
    [Flags]
    internal enum Buttons
    {
        None = 0,
        DPadUp = 1 << 0,
        DPadRight = 1 << 1,
        DPadDown = 1 << 2,
        DPadLeft = 1 << 3,
        A = 1 << 4,
        B = 1 << 5,
        X = 1 << 6,
        Y = 1 << 7,
        LeftShoulder = 1 << 8,
        RightShoulder = 1 << 9,
        Back = 1 << 10,
        Start = 1 << 11,
        LeftStick = 1 << 12,
        RightStick = 1 << 13,
        Share = 1 << 14,
    }

    /** Where X inserts relative to the selected operator; the default is what Tab does. */
    internal enum InsertSides
    {
        Default,
        Above,
        Right,
        Below,
        Left,
    }

    private enum Contexts
    {
        Inactive,
        Graph,
        Camera,
        SymbolBrowser,
        ParameterPopover,
    }

    /// <summary>True in the frame <paramref name="button"/> went down; held d-pad directions repeat.</summary>
    internal static bool WasPressed(Buttons button)
    {
        return (_pressed & button) != 0;
    }

    /** True while a connected controller drives the editor rather than operators. */
    internal static bool IsDrivingEditor => _lastContext != Contexts.Inactive && SdlGamepads.Count > 0;

    internal static bool IsHeld(Buttons button)
    {
        return (_lastActive & button) != 0;
    }

    /** True while View switched the controller from the graph to the camera of the output (or graph background). */
    internal static bool IsSteeringCamera => _lastContext == Contexts.Camera;

    /** Left stick while steering the camera: sideways and forward, zero otherwise. */
    internal static Vector2 CameraMoveStick { get; private set; }

    /** Right stick while steering the camera: looking around, zero otherwise. */
    internal static Vector2 CameraLookStick { get; private set; }

    /** Right minus left trigger while steering the camera: up and down. */
    internal static float CameraLift { get; private set; }

    /** True while the right stick is held down in the graph, which grabs the selected operator's snapped group. */
    internal static bool IsGrabbingGroup => _lastContext == Contexts.Graph && (_lastActive & Buttons.RightStick) != 0;

    /** The right stick's deflection while it grabs a group, 0 otherwise. */
    internal static Vector2 GroupDragStick { get; private set; }

    /** The right stick while the parameter popover is open, zero otherwise. */
    internal static Vector2 PopoverRightStick { get; private set; }

    /** The left stick while the parameter popover is open, zero otherwise. */
    internal static Vector2 PopoverLeftStick { get; private set; }

    /** From ×0.1 with the left trigger fully pulled to ×10 with the right one, for the infinity slider's range. */
    internal static double ValueRangeScaleFactor { get; private set; } = 1;

    /// <summary>
    /// Pans the graph with the left stick. Speed grows with the square of the deflection: a light touch nudges the
    /// view, full deflection crosses a screen in a few seconds.
    /// </summary>
    internal static void PanView(ScalableCanvas view)
    {
        if (_lastContext != Contexts.Graph || _panStick == Vector2.Zero)
            return;

        var speedPx = MaxPanSpeedPx * T3Ui.UiScaleFactor * _panStick.Length() * ImGui.GetIO().DeltaTime;
        view.ScrollByScreenPixels(new Vector2(_panStick.X, -_panStick.Y) * speedPx);
    }

    /// <summary>
    /// Returns true once after an insert was requested in the graph (X released, or A without a selection); the
    /// focused graph window then opens the symbol browser on <paramref name="side"/> of the selection.
    /// </summary>
    internal static bool TryTakeInsertRequest(out InsertSides side)
    {
        side = _insertSide;
        if (!_isInsertRequested)
            return false;

        _isInsertRequested = false;
        return true;
    }

    /** True while X is held in the graph, for previewing where the operator will be inserted. */
    internal static bool IsChoosingInsertSide(out InsertSides side)
    {
        side = _insertSide;
        return _lastContext == Contexts.Graph && (_lastActive & Buttons.X) != 0;
    }

    /** Call once per frame after <see cref="KeyActionHandling.InitializeFrame"/> and the controller update. */
    internal static void Update()
    {
        _isInsertRequested = false;

        var context = GetContext();
        var held = Buttons.None;
        _panStick = Vector2.Zero;
        GroupDragStick = Vector2.Zero;
        CameraMoveStick = Vector2.Zero;
        CameraLookStick = Vector2.Zero;
        CameraLift = 0;
        PopoverRightStick = Vector2.Zero;
        PopoverLeftStick = Vector2.Zero;
        ValueRangeScaleFactor = 1;
        if (context != Contexts.Inactive && SdlGamepads.TryGetState(0, out var state))
        {
            held = GetButtons(state);
            if (context == Contexts.Graph)
            {
                _panStick = state.LeftThumb;
                if (state.RightStickButton)
                    GroupDragStick = state.RightThumb;
            }

            if (context == Contexts.Camera)
            {
                CameraMoveStick = state.LeftThumb;
                CameraLookStick = state.RightThumb;
                CameraLift = state.RightTrigger - state.LeftTrigger;
            }

            if (context == Contexts.ParameterPopover)
            {
                PopoverRightStick = state.RightThumb;
                PopoverLeftStick = state.LeftThumb;
                ValueRangeScaleFactor = Math.Pow(10, state.RightTrigger - state.LeftTrigger);
            }
        }

        // A button that opened a popup is still down and must not also act inside it
        if (context != _lastContext)
        {
            ReleaseInjectedKeys();
            _ignoredUntilReleased = held;
            _lastContext = context;
        }

        _ignoredUntilReleased &= held;
        var active = held & ~_ignoredUntilReleased;
        UpdatePressed(active);
        _released = _lastActive & ~active;

        switch (context)
        {
            case Contexts.Graph:
                TriggerGraphActions();
                break;

            case Contexts.Camera:
                TriggerCameraActions();
                break;

            case Contexts.SymbolBrowser:
            case Contexts.ParameterPopover:
                InjectKeys(active, context);
                break;
        }

        _lastActive = active;
    }

    private static Contexts GetContext()
    {
        if (UserSettings.Config.GamepadUsage != UserSettings.GamepadUsages.Editor || ProjectView.Focused == null)
            return Contexts.Inactive;

        if (PlaceHolderUi.IsOpen)
            return Contexts.SymbolBrowser;

        if (ParameterPopover.IsOpen)
            return Contexts.ParameterPopover;

        return _isSteeringCameraRequested ? Contexts.Camera : Contexts.Graph;
    }

    private static Buttons GetButtons(in GamePadState state)
    {
        var buttons = Buttons.None;
        if (state.DirectionalPad.Up) buttons |= Buttons.DPadUp;
        if (state.DirectionalPad.Right) buttons |= Buttons.DPadRight;
        if (state.DirectionalPad.Down) buttons |= Buttons.DPadDown;
        if (state.DirectionalPad.Left) buttons |= Buttons.DPadLeft;
        if (state.Buttons.A) buttons |= Buttons.A;
        if (state.Buttons.B) buttons |= Buttons.B;
        if (state.Buttons.X) buttons |= Buttons.X;
        if (state.Buttons.Y) buttons |= Buttons.Y;
        if (state.LeftShoulder) buttons |= Buttons.LeftShoulder;
        if (state.RightShoulder) buttons |= Buttons.RightShoulder;
        if (state.Back) buttons |= Buttons.Back;
        if (state.Start) buttons |= Buttons.Start;
        if (state.LeftStickButton) buttons |= Buttons.LeftStick;
        if (state.RightStickButton) buttons |= Buttons.RightStick;
        if (state.Share) buttons |= Buttons.Share;
        return buttons;
    }

    /** Edges of all buttons, plus key-like repeats for the d-pad directions while they are held. */
    private static void UpdatePressed(Buttons active)
    {
        _pressed = active & ~_lastActive;

        var now = Environment.TickCount64;
        for (var directionIndex = 0; directionIndex < DirectionCount; directionIndex++)
        {
            var direction = (Buttons)(1 << directionIndex);
            if ((_pressed & direction) != 0)
            {
                _nextRepeatTicksMs[directionIndex] = now + RepeatDelayMs;
            }
            else if ((active & direction) != 0 && now >= _nextRepeatTicksMs[directionIndex])
            {
                _pressed |= direction;
                _nextRepeatTicksMs[directionIndex] = now + RepeatIntervalMs;
            }
        }
    }

    private static void TriggerGraphActions()
    {
        if (WasPressed(Buttons.X))
            _insertSide = InsertSides.Default;

        if ((_lastActive & Buttons.X) != 0 || WasPressed(Buttons.X))
        {
            // While X is held the d-pad picks the side instead of moving the selection
            if (WasPressed(Buttons.DPadUp)) _insertSide = InsertSides.Above;
            if (WasPressed(Buttons.DPadRight)) _insertSide = InsertSides.Right;
            if (WasPressed(Buttons.DPadDown)) _insertSide = InsertSides.Below;
            if (WasPressed(Buttons.DPadLeft)) _insertSide = InsertSides.Left;
        }
        else
        {
            TriggerOnPress(Buttons.DPadUp, UserActions.SelectToAbove);
            TriggerOnPress(Buttons.DPadRight, UserActions.SelectToRight);
            TriggerOnPress(Buttons.DPadDown, UserActions.SelectToBelow);
            TriggerOnPress(Buttons.DPadLeft, UserActions.SelectToLeft);
        }

        if ((_released & Buttons.X) != 0)
            _isInsertRequested = true;
        TriggerOnPress(Buttons.Y, UserActions.PinToOutputWindow);
        TriggerOnPress(Buttons.LeftShoulder, UserActions.Undo);
        TriggerOnPress(Buttons.RightShoulder, UserActions.Redo);
        TriggerOnPress(Buttons.Start, UserActions.PlaybackToggle);
        TriggerOnPress(Buttons.Share, UserActions.ToggleFocusMode);

        if (WasPressed(Buttons.Back))
            SetSteeringCamera(true);

        // Parameters need a single operator; with nothing to edit, A starts building instead
        if (WasPressed(Buttons.A))
        {
            if (ProjectView.Focused?.NodeSelection.Selection.Count == 1)
            {
                KeyActionHandling.TriggerForThisFrame(UserActions.EditParameter);
            }
            else
            {
                _insertSide = InsertSides.Default;
                _isInsertRequested = true;
            }
        }

        if (WasPressed(Buttons.B))
            DeselectOrGoUp();
    }

    /** The sticks fly the camera through <see cref="Camera.GamepadCamera"/>; these are the buttons around it. */
    private static void TriggerCameraActions()
    {
        TriggerOnPress(Buttons.Start, UserActions.PlaybackToggle);
        TriggerOnPress(Buttons.Share, UserActions.ToggleFocusMode);
        TriggerOnPress(Buttons.LeftShoulder, UserActions.Undo);
        TriggerOnPress(Buttons.RightShoulder, UserActions.Redo);

        if (WasPressed(Buttons.Back) || WasPressed(Buttons.B))
            SetSteeringCamera(false);
    }

    private static void SetSteeringCamera(bool isSteering)
    {
        _isSteeringCameraRequested = isSteering;
        ActionToast.Show(isSteering
                             ? "{Controller steers the camera}   [View] back to the graph"
                             : "{Controller drives the graph}");
    }

    private static void TriggerOnPress(Buttons button, UserActions action)
    {
        if (WasPressed(button))
            KeyActionHandling.TriggerForThisFrame(action);
    }

    private static void DeselectOrGoUp()
    {
        var projectView = ProjectView.Focused;
        if (projectView == null)
            return;

        if (projectView.NodeSelection.Selection.Count > 0)
        {
            projectView.NodeSelection.Clear();
            return;
        }

        projectView.TrySetCompositionOpToParent();
    }

    /// <summary>
    /// Mirrors the buttons as held keys, so ImGui's key repeat and the hold-to-nudge of the popover work as with the
    /// keyboard. In the browser A or X picks, Y switches to the operator's presets (read by the browser itself), and B steps back one namespace and closes it at the top level, as Backspace
    /// and Esc do. The popover handles A and B itself: Enter would close it and Esc revert.
    /// </summary>
    private static void InjectKeys(Buttons active, Contexts context)
    {
        InjectKey(active, Buttons.DPadUp, ImGuiKey.UpArrow);
        InjectKey(active, Buttons.DPadRight, ImGuiKey.RightArrow);
        InjectKey(active, Buttons.DPadDown, ImGuiKey.DownArrow);
        InjectKey(active, Buttons.DPadLeft, ImGuiKey.LeftArrow);
        if (context != Contexts.SymbolBrowser)
            return;

        InjectKey(active, Buttons.A, ImGuiKey.Enter);

        // X opened the browser, so pressing it again to accept keeps the thumb in place
        InjectKey(active, Buttons.X, ImGuiKey.Enter);

        // Decided when B goes down, so the key released is the one pressed. From presets the browser goes back itself.
        if ((active & Buttons.B) != 0 && (_lastActive & Buttons.B) == 0)
        {
            _backKey = PlaceHolderUi.IsChoosingPreset
                           ? ImGuiKey.None
                           : SymbolBrowsing.IsFilterActive
                               ? ImGuiKey.Backspace
                               : ImGuiKey.Escape;
        }

        if (_backKey != ImGuiKey.None)
            InjectKey(active, Buttons.B, _backKey);
    }

    private static void InjectKey(Buttons active, Buttons button, ImGuiKey key)
    {
        var isDown = (active & button) != 0;
        var wasDown = (_injectedButtons & button) != 0;
        if (isDown == wasDown)
            return;

        ImGui.GetIO().AddKeyEvent(key, isDown);
        if (isDown)
        {
            _injectedButtons |= button;
            _injectedKeys[GetButtonIndex(button)] = key;
        }
        else
        {
            _injectedButtons &= ~button;
        }
    }

    /** A key left down when a popup closes would stay pressed for ImGui. */
    private static void ReleaseInjectedKeys()
    {
        if (_injectedButtons == Buttons.None)
            return;

        var io = ImGui.GetIO();
        for (var index = 0; index < ButtonCount; index++)
        {
            if (((int)_injectedButtons & (1 << index)) != 0)
                io.AddKeyEvent(_injectedKeys[index], false);
        }

        _injectedButtons = Buttons.None;
    }

    private static int GetButtonIndex(Buttons button)
    {
        return System.Numerics.BitOperations.TrailingZeroCount((int)button);
    }

    internal const float MaxPanSpeedPx = 375;

    private const int DirectionCount = 4;
    private const int ButtonCount = 15;

    /** Like a keyboard's repeat: long enough for single steps, then fast enough to cross a long column. */
    private const long RepeatDelayMs = 400;

    private const long RepeatIntervalMs = 100;

    private static Contexts _lastContext;
    private static Buttons _lastActive;
    private static Buttons _pressed;
    private static Buttons _released;
    private static InsertSides _insertSide;
    private static Buttons _ignoredUntilReleased;
    private static Buttons _injectedButtons;
    private static ImGuiKey _backKey = ImGuiKey.Escape;
    private static bool _isInsertRequested;
    private static bool _isSteeringCameraRequested;
    private static Vector2 _panStick;
    private static readonly long[] _nextRepeatTicksMs = new long[DirectionCount];
    private static readonly ImGuiKey[] _injectedKeys = new ImGuiKey[ButtonCount];
}
