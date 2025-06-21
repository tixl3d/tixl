#nullable enable
using ImGuiNET;
using T3.Editor.Gui.UiHelpers;
using T3.SystemUi;

namespace T3.Editor.Gui.Interaction;

/// <summary>
/// UserAction represent single atomic commands that can be mapped to a keyboard shortcuts
/// </summary>
/// 
internal enum UserActions
{
    // General
    Undo,
    Redo,
    Save,
    FocusSelection,
    DeleteSelection,
    CopyToClipboard,
    PasteFromClipboard,
    PasteValues,
    New,

    // Playback
    PlaybackForward,
    PlaybackForwardHalfSpeed,
    PlaybackBackwards,
    PlaybackNextFrame,
    PlaybackPreviousFrame,
    PlaybackStop,
    PlaybackToggle,
    PlaybackJumpBack,
    PlaybackJumpToNextKeyframe,
    PlaybackJumpToPreviousKeyframe,
    PlaybackJumpToEndTime,
    PlaybackJumpToStartTime,

    // Timeline
    SetStartTime,
    SetEndTime,
    InsertKeyframe,
    InsertKeyframeWithIncrement,
    TapBeatSync,
    TapBeatSyncMeasure,

    ToggleAnimationPinning,

    // Graph
    PinToOutputWindow,
    DisplayImageAsBackground,
    ClearBackgroundImage,
    Duplicate,
    LayoutSelection,
    ToggleDisabled,
    ToggleBypassed,
    AddAnnotation,
    AddComment,
    RenameChild,

    ToggleSnapshotControl,

    ScrollLeft,
    ScrollRight,
    ScrollUp,
    ScrollDown,
    ZoomIn,
    ZoomOut,

    AlignSelectionLeft,
    
    NavigateBackwards,
    NavigateForward,

    SelectToAbove,
    SelectToRight,
    SelectToBelow,
    SelectToLeft,

    SearchGraph,
    OpenOperator,
    CloseOperator,

    // Layout and window management
    ToggleAllUiElements,
    ToggleVariationsWindow,
    ToggleFullscreen,
    ToggleFocusMode,

    LoadLayout0,
    LoadLayout1,
    LoadLayout2,
    LoadLayout3,
    LoadLayout4,
    LoadLayout5,
    LoadLayout6,
    LoadLayout7,
    LoadLayout8,
    LoadLayout9,
    SaveLayout0,
    SaveLayout1,
    SaveLayout2,
    SaveLayout3,
    SaveLayout4,
    SaveLayout5,
    SaveLayout6,
    SaveLayout7,
    SaveLayout8,
    SaveLayout9,

    LoadBookmark0,
    LoadBookmark1,
    LoadBookmark2,
    LoadBookmark3,
    LoadBookmark4,
    LoadBookmark5,
    LoadBookmark6,
    LoadBookmark7,
    LoadBookmark8,
    LoadBookmark9,
    SaveBookmark0,
    SaveBookmark1,
    SaveBookmark2,
    SaveBookmark3,
    SaveBookmark4,
    SaveBookmark5,
    SaveBookmark6,
    SaveBookmark7,
    SaveBookmark8,
    SaveBookmark9,
}

internal static class UserActionRegistry
{
    public static readonly HashSet<UserActions> DeferredActions = [];
    
    public static bool WasActionQueued(UserActions action)
    {
        if (!DeferredActions.Contains(action))
            return false;
    
        DeferredActions.Remove(action);
        return true;
    }
}

internal sealed class KeyboardBinding
{
    internal static void InitFrame()
    {
        if (!_initialized)
        {
            InitializeShortcutLabels();
            _initialized = true;
        }
        
        _anyKeysPressed = ImGui.GetIO().KeysDown.Count > 0;
    }

    private readonly UserActions _action;
    private bool _needsWindowFocus;
    private bool _needsWindowHover;
    private readonly bool _keyPressOnly;
    private readonly KeyCombination _combination;
    private static bool _initialized;

    internal static bool Triggered(UserActions action)
    {
        // Checking all bindings is expensive, so we only do this if any keys are pressed.
        if (!_anyKeysPressed)
            return false;

        if (!UserSettings.Config.EnableKeyboardShortCuts)
            return false;

        // Prevent keyboard short-cuts while inputs are active
        if (ImGui.IsAnyItemActive())
            return false;

        var io = ImGui.GetIO();
        foreach (var binding in _bindings)
        {
            if (binding._action != action)
                continue;

            if (binding._needsWindowFocus && !ImGui.IsWindowFocused(ImGuiFocusedFlags.ChildWindows))
                continue;

            if (binding._needsWindowHover && !ImGui.IsWindowHovered(ImGuiHoveredFlags.ChildWindows))
                continue;

            var kc = binding._combination;

            var isKeyPressed = (!binding._keyPressOnly || ImGui.IsKeyPressed((ImGuiKey)kc.Key, false));
            if (ImGui.IsKeyPressed((ImGuiKey)kc.Key, false)
                && isKeyPressed
                && (!kc.Alt && !io.KeyAlt || kc.Alt && io.KeyAlt)
                && (!kc.Ctrl && !io.KeyCtrl || kc.Ctrl && io.KeyCtrl)
                && (!kc.Shift && !io.KeyShift || kc.Shift && io.KeyShift)
               )
            {
                return true;
            }
        }

        return false;
    }

    private static readonly Dictionary<UserActions, string> _keyboardShortCutLabels = new();

    private static void InitializeShortcutLabels()
    {
        _keyboardShortCutLabels.Clear();
        foreach (var action in Enum.GetValues<UserActions>())
        {
            var bindings = _bindings.FindAll(b => b._action == action);
            var shortCuts = bindings.Select(binding => binding._combination.ToString()).ToList();

            if (shortCuts.Count == 0)
                continue;

            _keyboardShortCutLabels[action] = string.Join(" and ", shortCuts);
        }
    }

    internal static string ListKeyboardShortcuts(UserActions action, bool showLabel = true)
    {
        if (!_keyboardShortCutLabels.TryGetValue(action, out var shortCuts))
            return string.Empty;

        return (showLabel
                    ? (shortCuts.Contains("and")
                           ? "Short cut: "
                           : "Short cuts ")
                    : String.Empty)
               + string.Join(" and ", shortCuts);
    }

    private sealed class KeyCombination
    {
        public KeyCombination(Key key, bool ctrl = false, bool alt = false, bool shift = false)
        {
            Key = key;
            Ctrl = ctrl;
            Alt = alt;
            Shift = shift;
        }

        public readonly bool Ctrl;
        public readonly bool Alt;
        public readonly bool Shift;
        public readonly Key Key;

        public override string ToString()
        {
            return (Ctrl ? "Ctrl+" : "")
                   + (Alt ? "Alt+" : "")
                   + (Shift ? "Shift+" : "")
                   + Key;
        }
    }

    private KeyboardBinding(UserActions action, KeyCombination combination, bool needsWindowFocus = false, bool keyPressOnly = false)
    {
        _action = action;
        _combination = combination;
        _needsWindowFocus = needsWindowFocus;
        _keyPressOnly = keyPressOnly;
    }

    private static readonly List<KeyboardBinding> _bindings
        = new()
              {
                  // Global
                  new KeyboardBinding(UserActions.Save, new KeyCombination(Key.S, ctrl: true)),
                  new KeyboardBinding(UserActions.FocusSelection, new KeyCombination(Key.F)) { _needsWindowHover = true },
                  new KeyboardBinding(UserActions.Duplicate, new KeyCombination(Key.D, ctrl: true)) { _needsWindowFocus = true },
                  new KeyboardBinding(UserActions.DeleteSelection, new KeyCombination(Key.Delete)) { _needsWindowFocus = true },
                  new KeyboardBinding(UserActions.DeleteSelection, new KeyCombination(Key.Backspace)) { _needsWindowFocus = true },
                  new KeyboardBinding(UserActions.CopyToClipboard, new KeyCombination(Key.C, ctrl: true)) { _needsWindowFocus = true },
                  new KeyboardBinding(UserActions.PasteFromClipboard, new KeyCombination(Key.V, ctrl: true)) { _needsWindowFocus = true },
                  new KeyboardBinding(UserActions.PasteValues, new KeyCombination(Key.V, ctrl: true, shift:true)) { _needsWindowFocus = true },

                  // Playback
                  new KeyboardBinding(UserActions.PlaybackForward, new KeyCombination(Key.L)),
                  new KeyboardBinding(UserActions.PlaybackForwardHalfSpeed, new KeyCombination(Key.L, shift: true)),
                  new KeyboardBinding(UserActions.PlaybackBackwards, new KeyCombination(Key.J)),
                  new KeyboardBinding(UserActions.PlaybackStop, new KeyCombination(Key.K)),
                  new KeyboardBinding(UserActions.PlaybackToggle, new KeyCombination(Key.Space)), // TODO: Fixme!
                  new KeyboardBinding(UserActions.PlaybackPreviousFrame, new KeyCombination(Key.CursorLeft, shift: true)),
                  new KeyboardBinding(UserActions.PlaybackNextFrame, new KeyCombination(Key.CursorRight, shift: true)),
                  new KeyboardBinding(UserActions.PlaybackJumpToStartTime, new KeyCombination(Key.Home)),
                  new KeyboardBinding(UserActions.PlaybackJumpToNextKeyframe, new KeyCombination(Key.Period)),
                  new KeyboardBinding(UserActions.PlaybackJumpToPreviousKeyframe, new KeyCombination(Key.Comma)),

                  new KeyboardBinding(UserActions.Undo, new KeyCombination(Key.Z, ctrl: true)),
                  new KeyboardBinding(UserActions.Redo, new KeyCombination(Key.Z, ctrl: true, shift: true)),

                  // Timeline
                  new KeyboardBinding(UserActions.InsertKeyframe, new KeyCombination(Key.C)) { _needsWindowFocus = true },
                  new KeyboardBinding(UserActions.InsertKeyframeWithIncrement, new KeyCombination(Key.C, shift: true)),
                  new KeyboardBinding(UserActions.ToggleAnimationPinning, new KeyCombination(Key.K, shift: true)),

                  new KeyboardBinding(UserActions.SetStartTime, new KeyCombination(Key.B)),
                  new KeyboardBinding(UserActions.SetEndTime, new KeyCombination(Key.N)),
                  new KeyboardBinding(UserActions.TapBeatSync, new KeyCombination(Key.Z)),
                  new KeyboardBinding(UserActions.TapBeatSyncMeasure, new KeyCombination(Key.X)),
                  
                  // Graph window
                  new KeyboardBinding(UserActions.ToggleDisabled, new KeyCombination(Key.D, shift: true)) { _needsWindowFocus = true },
                  new KeyboardBinding(UserActions.ToggleBypassed, new KeyCombination(Key.B, shift: true)) { _needsWindowFocus = true },
                  new KeyboardBinding(UserActions.PinToOutputWindow, new KeyCombination(Key.P)) { _needsWindowFocus = true },
                  new KeyboardBinding(UserActions.DisplayImageAsBackground, new KeyCombination(Key.P, ctrl: true)) { _needsWindowFocus = false },
                  new KeyboardBinding(UserActions.ClearBackgroundImage, new KeyCombination(Key.P, ctrl: true, shift: true)) { _needsWindowFocus = true },
                  new KeyboardBinding(UserActions.LayoutSelection, new KeyCombination(Key.G)),

                  new KeyboardBinding(UserActions.AddAnnotation, new KeyCombination(Key.A, shift: true)) { _needsWindowFocus = true },
                  new KeyboardBinding(UserActions.AddComment, new KeyCombination(Key.C, shift: true, ctrl:true)) { _needsWindowFocus = true },
                  new KeyboardBinding(UserActions.ToggleVariationsWindow, new KeyCombination(Key.V, alt: true)) { _needsWindowFocus = false },
                  new KeyboardBinding(UserActions.SearchGraph, new KeyCombination(Key.F, ctrl: true)) { _needsWindowFocus = false },
                  new KeyboardBinding(UserActions.OpenOperator, new KeyCombination(Key.I)) { _needsWindowFocus = true },
                  new KeyboardBinding(UserActions.CloseOperator, new KeyCombination(Key.U)) { _needsWindowFocus = true },
                  new KeyboardBinding(UserActions.RenameChild, new KeyCombination(Key.Return)) { _needsWindowFocus = true },

                  new KeyboardBinding(UserActions.NavigateBackwards, new KeyCombination(Key.CursorLeft, alt: true)) { _needsWindowFocus = false },
                  new KeyboardBinding(UserActions.NavigateForward, new KeyCombination(Key.CursorRight, alt: true)) { _needsWindowFocus = false },

                  new KeyboardBinding(UserActions.SelectToAbove, new KeyCombination(Key.CursorUp)) { _needsWindowFocus = true },
                  new KeyboardBinding(UserActions.SelectToRight, new KeyCombination(Key.CursorRight)) { _needsWindowFocus = true },
                  new KeyboardBinding(UserActions.SelectToBelow, new KeyCombination(Key.CursorDown)) { _needsWindowFocus = true },
                  new KeyboardBinding(UserActions.SelectToLeft, new KeyCombination(Key.CursorLeft)) { _needsWindowFocus = true },

                  new KeyboardBinding(UserActions.AlignSelectionLeft, new KeyCombination(Key.A, alt:true)) { _needsWindowFocus = true },
                  
                  // Layout and window management
                  new KeyboardBinding(UserActions.ToggleAllUiElements, new KeyCombination(Key.Esc, shift: true)),
                  new KeyboardBinding(UserActions.ToggleFullscreen, new KeyCombination(Key.F11)),
                  new KeyboardBinding(UserActions.ToggleFocusMode, new KeyCombination(Key.F12)),

                  new KeyboardBinding(UserActions.LoadBookmark1, new KeyCombination(Key.D1, ctrl: true)),
                  new KeyboardBinding(UserActions.LoadBookmark2, new KeyCombination(Key.D2, ctrl: true)),
                  new KeyboardBinding(UserActions.LoadBookmark3, new KeyCombination(Key.D3, ctrl: true)),
                  new KeyboardBinding(UserActions.LoadBookmark4, new KeyCombination(Key.D4, ctrl: true)),
                  new KeyboardBinding(UserActions.LoadBookmark5, new KeyCombination(Key.D5, ctrl: true)),
                  new KeyboardBinding(UserActions.LoadBookmark6, new KeyCombination(Key.D6, ctrl: true)),
                  new KeyboardBinding(UserActions.LoadBookmark7, new KeyCombination(Key.D7, ctrl: true)),
                  new KeyboardBinding(UserActions.LoadBookmark8, new KeyCombination(Key.D8, ctrl: true)),
                  new KeyboardBinding(UserActions.LoadBookmark9, new KeyCombination(Key.D9, ctrl: true)),
                  new KeyboardBinding(UserActions.LoadBookmark0, new KeyCombination(Key.D0, ctrl: true)),

                  new KeyboardBinding(UserActions.SaveBookmark1, new KeyCombination(Key.D1, ctrl: true, shift: true)),
                  new KeyboardBinding(UserActions.SaveBookmark2, new KeyCombination(Key.D2, ctrl: true, shift: true)),
                  new KeyboardBinding(UserActions.SaveBookmark3, new KeyCombination(Key.D3, ctrl: true, shift: true)),
                  new KeyboardBinding(UserActions.SaveBookmark4, new KeyCombination(Key.D4, ctrl: true, shift: true)),
                  new KeyboardBinding(UserActions.SaveBookmark5, new KeyCombination(Key.D5, ctrl: true, shift: true)),
                  new KeyboardBinding(UserActions.SaveBookmark6, new KeyCombination(Key.D6, ctrl: true, shift: true)),
                  new KeyboardBinding(UserActions.SaveBookmark7, new KeyCombination(Key.D7, ctrl: true, shift: true)),
                  new KeyboardBinding(UserActions.SaveBookmark8, new KeyCombination(Key.D8, ctrl: true, shift: true)),
                  new KeyboardBinding(UserActions.SaveBookmark9, new KeyCombination(Key.D9, ctrl: true, shift: true)),
                  new KeyboardBinding(UserActions.SaveBookmark0, new KeyCombination(Key.D0, ctrl: true, shift: true)),

                  new KeyboardBinding(UserActions.LoadLayout0, new KeyCombination(Key.F1)),
                  new KeyboardBinding(UserActions.LoadLayout1, new KeyCombination(Key.F2)),
                  new KeyboardBinding(UserActions.LoadLayout2, new KeyCombination(Key.F3)),
                  new KeyboardBinding(UserActions.LoadLayout3, new KeyCombination(Key.F4)),
                  new KeyboardBinding(UserActions.LoadLayout4, new KeyCombination(Key.F5)),
                  new KeyboardBinding(UserActions.LoadLayout5, new KeyCombination(Key.F6)),
                  new KeyboardBinding(UserActions.LoadLayout6, new KeyCombination(Key.F7)),
                  new KeyboardBinding(UserActions.LoadLayout7, new KeyCombination(Key.F8)),
                  new KeyboardBinding(UserActions.LoadLayout8, new KeyCombination(Key.F9)),
                  new KeyboardBinding(UserActions.LoadLayout9, new KeyCombination(Key.F10)),

                  new KeyboardBinding(UserActions.SaveLayout0, new KeyCombination(Key.F1, ctrl: true)),
                  new KeyboardBinding(UserActions.SaveLayout1, new KeyCombination(Key.F2, ctrl: true)),
                  new KeyboardBinding(UserActions.SaveLayout2, new KeyCombination(Key.F3, ctrl: true)),
                  new KeyboardBinding(UserActions.SaveLayout3, new KeyCombination(Key.F4, ctrl: true)),
                  new KeyboardBinding(UserActions.SaveLayout4, new KeyCombination(Key.F5, ctrl: true)),
                  new KeyboardBinding(UserActions.SaveLayout5, new KeyCombination(Key.F6, ctrl: true)),
                  new KeyboardBinding(UserActions.SaveLayout6, new KeyCombination(Key.F7, ctrl: true)),
                  new KeyboardBinding(UserActions.SaveLayout7, new KeyCombination(Key.F8, ctrl: true)),
                  new KeyboardBinding(UserActions.SaveLayout8, new KeyCombination(Key.F9, ctrl: true)),
                  new KeyboardBinding(UserActions.SaveLayout9, new KeyCombination(Key.F10, ctrl: true)),
              };

    private static bool _anyKeysPressed;
}