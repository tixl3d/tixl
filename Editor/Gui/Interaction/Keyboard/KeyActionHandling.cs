#nullable enable
using ImGuiNET;
using T3.Editor.Gui.UiHelpers;
using T3.SystemUi;

namespace T3.Editor.Gui.Interaction.Keyboard;

/// <summary>
/// A static class that helps to trigger user actions with assigned keyboard shortcuts  
/// </summary>
internal static class KeyActionHandling
{
    //private static readonly List<KeyBinding> _bindings = [];

    private static bool _initialized;
    private static bool _anyKeysPressed;
    private static bool _arrowKeysClaimedThisFrame;

    /** Holds the previous frame's claims, so the result doesn't depend on the order windows are drawn in. */
    internal static bool AreArrowKeysClaimed { get; private set; }

    /// <summary>
    /// Keeps the arrow keys from reaching graph navigation. Windows that read the arrow keys themselves call this
    /// every frame while they are focused.
    /// </summary>
    internal static void ClaimArrowKeys()
    {
        _arrowKeysClaimedThisFrame = true;
    }

    internal static void InitializeFrame()
    {
        AreArrowKeysClaimed = _arrowKeysClaimedThisFrame;
        _arrowKeysClaimedThisFrame = false;
        OperatorKeyboardCapture.BeginFrame(ImGui.GetIO().WantTextInput);

        if (!_initialized)
        {
            InitializeActionOptions();
            _initialized = true;
        }

        // ImGui's legacy KeysDown[] array was removed in 1.90, so we use TiXL's
        // own KeyHandler state (populated by WndProc) as the source of truth.
        _anyKeysPressed = false;
        var pressedKeys = KeyHandler.PressedKeys;
        for (var index = 0; index < pressedKeys.Count; index++)
        {
            if (!pressedKeys[index])
                continue;

            _anyKeysPressed = true;
            break;
        }
    }

    internal static bool Triggered(this UserActions action)
    {
        if (!_anyKeysPressed || !UserSettings.Config.EnableKeyboardShortCuts)
            return false;
        
        var io = ImGui.GetIO();
        var spaceEditsParameters = UserSettings.Config.UseSpaceToEditParameters;
        var isKeyboardCapturedByOperator = OperatorKeyboardCapture.CapturedBy != Guid.Empty;

        if (spaceEditsParameters && !isKeyboardCapturedByOperator)
        {
            var spaceBinding = action switch
                                   {
                                       UserActions.EditParameter  => _spaceEditsParameterBinding,
                                       UserActions.PlaybackToggle => _shiftSpaceTogglesPlaybackBinding,
                                       _                          => null
                                   };

            if (spaceBinding != null && spaceBinding.IsContextValid() && spaceBinding.IsTriggered(io))
                return true;
        }

        // Todo: Refactor this with lookup list
        foreach (var binding in KeyMapSwitching.CurrentKeymap.Bindings)
        {
            if (binding.Action != action)
                continue;

            if (spaceEditsParameters
                && action == UserActions.PlaybackToggle
                && binding.KeyCombination is { Key: Key.Space, Ctrl: false, Alt: false, Shift: false })
                continue;

            if (!binding.IsContextValid())
                continue;

            // Ctrl combinations are document commands (undo, save) that must keep working while an operator owns the keyboard.
            if (isKeyboardCapturedByOperator && !binding.KeyCombination.Ctrl)
                continue;

            if (binding.IsTriggered(io))
                return true;
        }

        return false;
    }

    internal static string ListShortcuts(this UserActions action)
    {
        if (UserSettings.Config.UseSpaceToEditParameters)
        {
            if (action == UserActions.EditParameter)
                return _spaceLabel;

            if (action == UserActions.PlaybackToggle)
                return _shiftSpaceLabel;
        }

        var currentKeymapShortCutsLabelsForActions = KeyMapSwitching.CurrentKeymap.ShortCutsLabelsForActions;
        var index = (int)action;
        if (index >= currentKeymapShortCutsLabelsForActions.Length)
        {
            return string.Empty;
        }

        return currentKeymapShortCutsLabelsForActions[index];
    }

    internal static string ListKeyboardShortcutsForActionWithLabel(this UserActions action)
    {
        var shortCuts = action.ListShortcuts();
        if (string.IsNullOrEmpty(shortCuts))
            return shortCuts;
        
        var prefix = shortCuts.Contains(" and ") ? "Shortcuts: " : "Shortcut: ";
        return prefix + shortCuts;
    }


    internal static Flags GetActionFlags(UserActions action)
    {
        var index = (int)action;
        return index >= _flagsForActions.Length ? Flags.None : _flagsForActions[index];
    }
    
    [Flags]
    public enum Flags
    {
        None = 0,
        NeedsWindowFocus = 1 << 1,
        NeedsWindowHover = 1 << 2,
        RemainActiveWhenItemActive = 1<<3,  // Most shortcuts should be disabled when some input item is active, which few exceptions like WASD camera short cuts 
        KeyPressOnly = 1 << 4,
        KeyHoldOnly = 1 << 5,
    }

    /// <summary>
    /// Register special flags for user actions
    /// </summary>
    private static void InitializeActionOptions()
    {
        RegisterActionsFlags(UserActions.Save, Flags.KeyPressOnly);
        RegisterActionsFlags(UserActions.Exit, Flags.KeyPressOnly);
        RegisterActionsFlags(UserActions.FocusSelection, Flags.NeedsWindowHover);
        RegisterActionsFlags(UserActions.Duplicate, Flags.NeedsWindowFocus);
        RegisterActionsFlags(UserActions.DuplicateWithConnections, Flags.NeedsWindowFocus);
        RegisterActionsFlags(UserActions.DeleteSelection, Flags.NeedsWindowFocus);
        RegisterActionsFlags(UserActions.DeleteSelectionAndConnections, Flags.NeedsWindowFocus);
        RegisterActionsFlags(UserActions.CopyToClipboard, Flags.NeedsWindowFocus);
        RegisterActionsFlags(UserActions.PasteFromClipboard, Flags.NeedsWindowFocus);
        RegisterActionsFlags(UserActions.PasteValues, Flags.NeedsWindowFocus);
        RegisterActionsFlags(UserActions.Undo, Flags.KeyPressOnly);
        RegisterActionsFlags(UserActions.Redo, Flags.KeyPressOnly);

        // Playback controls
        RegisterActionsFlags(UserActions.PlaybackForward, Flags.KeyPressOnly);
        RegisterActionsFlags(UserActions.PlaybackForwardHalfSpeed, Flags.KeyPressOnly);
        RegisterActionsFlags(UserActions.PlaybackBackwards, Flags.KeyPressOnly);
        RegisterActionsFlags(UserActions.PlaybackStop, Flags.KeyPressOnly);
        RegisterActionsFlags(UserActions.PlaybackToggle, Flags.KeyPressOnly);
        RegisterActionsFlags(UserActions.PlaybackJumpToStartTime, Flags.KeyPressOnly);
        RegisterActionsFlags(UserActions.PlaybackScrubTime, Flags.KeyHoldOnly);
        RegisterActionsFlags(UserActions.ToggleIdleMotion, Flags.KeyPressOnly);

        // Timeline actions
        RegisterActionsFlags(UserActions.InsertKeyframe, Flags.NeedsWindowFocus | Flags.KeyPressOnly);
        RegisterActionsFlags(UserActions.SetStartTime, Flags.KeyPressOnly);
        RegisterActionsFlags(UserActions.SetEndTime, Flags.KeyPressOnly);
        RegisterActionsFlags(UserActions.TapBeatSync, Flags.KeyPressOnly);
        RegisterActionsFlags(UserActions.TapBeatSyncMeasure, Flags.KeyPressOnly);

        // Graph window actions
        RegisterActionsFlags(UserActions.ToggleDisabled, Flags.NeedsWindowFocus);
        RegisterActionsFlags(UserActions.ToggleBypassed, Flags.NeedsWindowFocus);
        RegisterActionsFlags(UserActions.Disconnect, Flags.NeedsWindowFocus);
        RegisterActionsFlags(UserActions.PinToOutputWindow, Flags.KeyPressOnly);
        RegisterActionsFlags(UserActions.ShowInGraph, Flags.KeyPressOnly);
        RegisterActionsFlags(UserActions.ToggleOutputSetup, Flags.KeyPressOnly);
        RegisterActionsFlags(UserActions.ToggleFocusMode, Flags.KeyPressOnly);
        RegisterActionsFlags(UserActions.DisplayImageAsBackground, Flags.KeyPressOnly);
        RegisterActionsFlags(UserActions.ClearBackgroundImage, Flags.NeedsWindowFocus);

        RegisterActionsFlags(UserActions.AddSection, Flags.NeedsWindowFocus | Flags.KeyPressOnly);
        RegisterActionsFlags(UserActions.AddComment, Flags.NeedsWindowFocus);
        RegisterActionsFlags(UserActions.OpenOperator, Flags.NeedsWindowFocus);
        RegisterActionsFlags(UserActions.CloseOperator, Flags.NeedsWindowFocus);
        //RegisterActionsFlags(UserActions.RenameChild, Flags.NeedsWindowFocus);
        RegisterActionsFlags(UserActions.EditParameter, Flags.KeyPressOnly);

        for (var index = 0; index < 10; index++)
        {
            RegisterActionsFlags(UserActions.SelectOperatorBookmark0 + index, Flags.NeedsWindowFocus | Flags.KeyPressOnly);
            RegisterActionsFlags(UserActions.SetOperatorBookmark0 + index, Flags.NeedsWindowFocus | Flags.KeyPressOnly);
        }

        // Camera controls
        RegisterActionsFlags(UserActions.CameraLeft, Flags.NeedsWindowHover | Flags.KeyHoldOnly | Flags.RemainActiveWhenItemActive);
        RegisterActionsFlags(UserActions.CameraRight, Flags.NeedsWindowHover | Flags.KeyHoldOnly | Flags.RemainActiveWhenItemActive);
        RegisterActionsFlags(UserActions.CameraForward, Flags.NeedsWindowHover | Flags.KeyHoldOnly | Flags.RemainActiveWhenItemActive);
        RegisterActionsFlags(UserActions.CameraBackward, Flags.NeedsWindowHover | Flags.KeyHoldOnly | Flags.RemainActiveWhenItemActive);
        RegisterActionsFlags(UserActions.CameraUp, Flags.NeedsWindowHover | Flags.KeyHoldOnly | Flags.RemainActiveWhenItemActive);
        RegisterActionsFlags(UserActions.CameraDown, Flags.NeedsWindowHover | Flags.KeyHoldOnly | Flags.RemainActiveWhenItemActive);
        
        // Camera reset and focus
        RegisterActionsFlags(UserActions.CameraReset, Flags.NeedsWindowHover);
        RegisterActionsFlags(UserActions.CameraFocusSelection, Flags.NeedsWindowHover);
        RegisterActionsFlags(UserActions.RenderAnimation, Flags.KeyPressOnly);

        // Created here because a binding reads its action's flags when constructed
        _spaceEditsParameterBinding = new KeyBinding(UserActions.EditParameter, new KeyCombination(Key.Space));
        _shiftSpaceTogglesPlaybackBinding = new KeyBinding(UserActions.PlaybackToggle, new KeyCombination(Key.Space, shift: true));

        return;

        void RegisterActionsFlags(UserActions action, Flags flags)
        {
            var index = (int)action;
            if (index >= _flagsForActions.Length)
            {
                Log.Warning($"Action index {index} for {action} exceeds expected max index {_flagsForActions.Length}");
                return;
            }

            _flagsForActions[index] = flags;
        }
    }

    private static KeyBinding? _spaceEditsParameterBinding;
    private static KeyBinding? _shiftSpaceTogglesPlaybackBinding;
    private static readonly string _spaceLabel = new KeyCombination(Key.Space).ToString();
    private static readonly string _shiftSpaceLabel = new KeyCombination(Key.Space, shift: true).ToString();

    private const int UserActionsCount = (int)UserActions.__Count;
    private static readonly Flags[] _flagsForActions = new Flags[UserActionsCount];
}