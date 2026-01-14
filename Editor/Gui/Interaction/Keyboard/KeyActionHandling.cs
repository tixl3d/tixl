#nullable enable
using ImGuiNET;
using T3.Editor.Gui.UiHelpers;

namespace T3.Editor.Gui.Interaction.Keyboard;

/// <summary>
/// A static class that helps to trigger user actions with assigned keyboard shortcuts  
/// </summary>
internal static class KeyActionHandling
{
    //private static readonly List<KeyBinding> _bindings = [];

    private static bool _initialized;
    private static bool _anyKeysPressed;

    internal static void InitializeFrame()
    {
        if (!_initialized)
        {
            InitializeActionOptions();
            _initialized = true;
        }

        var keysDown = ImGui.GetIO().KeysDown;
        _anyKeysPressed = false;
        for(var index =0 ; index< keysDown.Count;index++)
        {
            if (!keysDown[index]) 
                continue;
            
            _anyKeysPressed = true;
            break;

        }
        //_anyKeysPressed = ImGui.GetIO().KeysDown.Count > 0;
        //Log.Debug($" Any Active {_anyKeysPressed}  count: {ImGui.GetIO().k.Count}");
    }

    internal static bool Triggered(this UserActions action)
    {
        if (!_anyKeysPressed || !UserSettings.Config.EnableKeyboardShortCuts)
            return false;
        
        var io = ImGui.GetIO();

        // Todo: Refactor this with lookup list
        foreach (var binding in KeyMapSwitching.CurrentKeymap.Bindings)
        {
            if (binding.Action != action)
                continue;

            if (!binding.IsContextValid())
                continue;

            if (binding.IsTriggered(io))
                return true;
        }

        return false;
    }

    internal static string ListShortcuts(this UserActions action)
    {
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
        RegisterActionsFlags(UserActions.FocusSelection, Flags.NeedsWindowHover);
        RegisterActionsFlags(UserActions.Duplicate, Flags.NeedsWindowFocus);
        RegisterActionsFlags(UserActions.DeleteSelection, Flags.NeedsWindowFocus);
        RegisterActionsFlags(UserActions.DeleteSelection, Flags.NeedsWindowFocus);
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

        // Timeline actions
        RegisterActionsFlags(UserActions.InsertKeyframe, Flags.NeedsWindowFocus | Flags.KeyPressOnly);
        RegisterActionsFlags(UserActions.SetStartTime, Flags.KeyPressOnly);
        RegisterActionsFlags(UserActions.SetEndTime, Flags.KeyPressOnly);
        RegisterActionsFlags(UserActions.TapBeatSync, Flags.KeyPressOnly);
        RegisterActionsFlags(UserActions.TapBeatSyncMeasure, Flags.KeyPressOnly);

        // Graph window actions
        RegisterActionsFlags(UserActions.ToggleDisabled, Flags.NeedsWindowFocus);
        RegisterActionsFlags(UserActions.ToggleBypassed, Flags.NeedsWindowFocus);
        RegisterActionsFlags(UserActions.PinToOutputWindow, Flags.KeyPressOnly);
        RegisterActionsFlags(UserActions.ClearBackgroundImage, Flags.NeedsWindowFocus);

        RegisterActionsFlags(UserActions.AddAnnotation, Flags.NeedsWindowFocus | Flags.KeyPressOnly);
        RegisterActionsFlags(UserActions.AddComment, Flags.NeedsWindowFocus);
        RegisterActionsFlags(UserActions.OpenOperator, Flags.NeedsWindowFocus);
        RegisterActionsFlags(UserActions.CloseOperator, Flags.NeedsWindowFocus);
        //RegisterActionsFlags(UserActions.RenameChild, Flags.NeedsWindowFocus);

        // Navigation
        RegisterActionsFlags(UserActions.SelectToAbove, Flags.NeedsWindowFocus);
        RegisterActionsFlags(UserActions.SelectToRight, Flags.NeedsWindowFocus);
        RegisterActionsFlags(UserActions.SelectToBelow, Flags.NeedsWindowFocus);
        RegisterActionsFlags(UserActions.SelectToLeft, Flags.NeedsWindowFocus);

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

    private const int UserActionsCount = (int)UserActions.__Count;
    private static readonly Flags[] _flagsForActions = new Flags[UserActionsCount];
}