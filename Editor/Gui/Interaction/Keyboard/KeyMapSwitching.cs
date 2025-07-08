#nullable enable
using System.IO;
using T3.Core.UserData;
using T3.Editor.Gui.UiHelpers;
using T3.Serialization;
using T3.SystemUi;

namespace T3.Editor.Gui.Interaction.Keyboard;

/// <summary>
/// Manages loading and switching of keyboard layouts
/// </summary>
internal static class KeyMapSwitching
{
    private static readonly KeyMap _factoryKeymap = CreateFactoryKeymap();
    public static KeyMap CurrentKeymap = _factoryKeymap;

    /// <summary>
    /// Requires user settings to be loaded already.
    /// </summary>
    internal static void Initialize()
    {
        LoadKeyMaps();
        CurrentKeymap = GetUserOrFactoryKeyMap();
    }

    public static bool TrySetKeyMap(string name)
    {
        var selectedKeyMap = KeyMaps.FirstOrDefault(t => t.Name == name);
        if (selectedKeyMap == null)
        {
            CurrentKeymap = _factoryKeymap;
            return false;
        }

        CurrentKeymap = selectedKeyMap;
        return true;
    }

    internal static void SaveKeyMap(KeyMap keyMap)
    {
        Directory.CreateDirectory(KeyMapFolder);

        keyMap.Name = keyMap.Name.Trim();
        if (string.IsNullOrEmpty(keyMap.Name))
        {
            keyMap.Name = "untitled";
        }

        var filepath = GetKeyMapFilePath(keyMap);
        JsonUtils.TrySaveJson(keyMap, filepath);
    }

    internal static void DeleteKeyMap(KeyMap keyMap)
    {
        var filepath = GetKeyMapFilePath(keyMap);
        if (!File.Exists(filepath))
        {
            Log.Warning($"{filepath} does not exist?");
            return;
        }

        File.Delete(filepath);
        LoadKeyMaps();
        UserSettings.Config.KeyBindingName = string.Empty;
        GetUserOrFactoryKeyMap();
    }    
    
    private static string GetKeyMapFilePath(KeyMap keyMap)
    {
        return Path.Combine(KeyMapFolder, keyMap.Name + ".json");
    }

    private static KeyMap GetUserOrFactoryKeyMap()
    {
        var selectedKeyBindingName = UserSettings.Config.KeyBindingName;
        if (string.IsNullOrWhiteSpace(selectedKeyBindingName))
        {
            return _factoryKeymap;
        }

        var userKeyBinding = KeyMaps.FirstOrDefault(t => t.Name == selectedKeyBindingName);
        if (userKeyBinding == null)
        {
            Log.Warning($"Couldn't load {selectedKeyBindingName}");
            return _factoryKeymap;
        }

        return userKeyBinding;
    }

    private static void LoadKeyMaps()
    {
        KeyMaps.Clear();
        
        KeyMaps.Add(_factoryKeymap);
        Directory.CreateDirectory(KeyMapFolder);
        Directory.CreateDirectory(DefaultKeyMapFolder);

        // copy default KeyBindings if not present
        foreach (var keyMap in Directory.EnumerateFiles(DefaultKeyMapFolder))
        {
            var targetPath = Path.Combine(KeyMapFolder, Path.GetFileName(keyMap));
            if (!File.Exists(targetPath))
                File.Copy(keyMap, targetPath);
        }

        foreach (var filepath in Directory.EnumerateFiles(KeyMapFolder))
        {
            try
            {
                var t = JsonUtils.TryLoadingJson<KeyMap>(filepath);
                if (t == null)
                {
                    Log.Debug($"Failed to load Keymap {filepath}");
                    continue;
                }

                t.UpdateShortcutLabels();
                KeyMaps.Add(t);
            }
            catch (Exception e)
            {
                Log.Error($"Failed to load {filepath} : {e.Message}");
            }
        }
    }

    internal static readonly List<KeyMap> KeyMaps = [_factoryKeymap];
    private static string KeyMapFolder => Path.Combine(FileLocations.SettingsDirectory, FileLocations.KeyBindingSubFolder);
    private static string DefaultKeyMapFolder => Path.Combine(FileLocations.ReadOnlySettingsPath, FileLocations.KeyBindingSubFolder);

    private static KeyMap CreateFactoryKeymap()
    {
        List<KeyBinding> keyBindings =
            [
                new(UserActions.Save, new KeyCombination(Key.S, ctrl: true)),
                new(UserActions.FocusSelection, new KeyCombination(Key.F)),
                new(UserActions.Duplicate, new KeyCombination(Key.D, ctrl: true)),
                new(UserActions.DeleteSelection, new KeyCombination(Key.Delete)),
                new(UserActions.DeleteSelection, new KeyCombination(Key.Backspace)),
                new(UserActions.CopyToClipboard, new KeyCombination(Key.C, ctrl: true)),
                new(UserActions.PasteFromClipboard, new KeyCombination(Key.V, ctrl: true)),
                new(UserActions.PasteValues, new KeyCombination(Key.V, ctrl: true, shift: true)),
                new(UserActions.Undo, new KeyCombination(Key.Z, ctrl: true)),
                new(UserActions.Redo, new KeyCombination(Key.Z, ctrl: true, shift: true)),

                // Playback controls
                new(UserActions.PlaybackForward, new KeyCombination(Key.L)),
                new(UserActions.PlaybackForwardHalfSpeed, new KeyCombination(Key.L, shift: true)),
                new(UserActions.PlaybackBackwards, new KeyCombination(Key.J)),
                new(UserActions.PlaybackStop, new KeyCombination(Key.K)),
                new(UserActions.PlaybackToggle, new KeyCombination(Key.Space)),
                new(UserActions.PlaybackPreviousFrame, new KeyCombination(Key.CursorLeft, shift: true)),
                new(UserActions.PlaybackNextFrame, new KeyCombination(Key.CursorRight, shift: true)),
                new(UserActions.PlaybackJumpToStartTime, new KeyCombination(Key.Home)),
                new(UserActions.PlaybackJumpToNextKeyframe, new KeyCombination(Key.Period)),
                new(UserActions.PlaybackJumpToPreviousKeyframe, new KeyCombination(Key.Comma)),

                // Timeline actions
                new(UserActions.InsertKeyframe, new KeyCombination(Key.C)),
                new(UserActions.InsertKeyframeWithIncrement, new KeyCombination(Key.C, shift: true)),
                new(UserActions.ToggleAnimationPinning, new KeyCombination(Key.K, shift: true)),
                new(UserActions.SplitSelectedOrHoveredClips, new KeyCombination(Key.X, ctrl: true)),
                new(UserActions.SetStartTime, new KeyCombination(Key.B)),
                new(UserActions.SetEndTime, new KeyCombination(Key.N)),
                new(UserActions.TapBeatSync, new KeyCombination(Key.Z)),
                new(UserActions.TapBeatSyncMeasure, new KeyCombination(Key.X)),

                // Graph window actions
                new(UserActions.ToggleDisabled, new KeyCombination(Key.D, shift: true)),
                new(UserActions.ToggleBypassed, new KeyCombination(Key.B, shift: true)),
                new(UserActions.PinToOutputWindow, new KeyCombination(Key.P)),
                new(UserActions.DisplayImageAsBackground, new KeyCombination(Key.P, ctrl: true)),
                new(UserActions.ClearBackgroundImage, new KeyCombination(Key.P, ctrl: true, shift: true)),
                new(UserActions.LayoutSelection, new KeyCombination(Key.G)),
                new(UserActions.AddAnnotation, new KeyCombination(Key.A, shift: true)),
                new(UserActions.AddComment, new KeyCombination(Key.C, shift: true, ctrl: true)),
                new(UserActions.ToggleVariationsWindow, new KeyCombination(Key.V, alt: true)),
                new(UserActions.SearchGraph, new KeyCombination(Key.F, ctrl: true)),
                new(UserActions.OpenOperator, new KeyCombination(Key.I)),
                new(UserActions.CloseOperator, new KeyCombination(Key.U)),
                new(UserActions.RenameChild, new KeyCombination(Key.Return)),
                new(UserActions.AlignSelectionLeft, new KeyCombination(Key.A, alt:true)),

                // Navigation
                new(UserActions.NavigateBackwards, new KeyCombination(Key.CursorLeft, alt: true)),
                new(UserActions.NavigateForward, new KeyCombination(Key.CursorRight, alt: true)),
                new(UserActions.SelectToAbove, new KeyCombination(Key.CursorUp)),
                new(UserActions.SelectToRight, new KeyCombination(Key.CursorRight)),
                new(UserActions.SelectToBelow, new KeyCombination(Key.CursorDown)),
                new(UserActions.SelectToLeft, new KeyCombination(Key.CursorLeft)),

                // Camera controls
                new(UserActions.CameraLeft, new KeyCombination(Key.A)),
                new(UserActions.CameraRight, new KeyCombination(Key.D)),
                new(UserActions.CameraForward, new KeyCombination(Key.W)),
                new(UserActions.CameraBackward, new KeyCombination(Key.S)),
                new(UserActions.CameraUp, new KeyCombination(Key.E)),
                new(UserActions.CameraDown, new KeyCombination(Key.Q)),
                // Camera reset and focus
                new(UserActions.CameraReset, new KeyCombination(Key.F)),
                new(UserActions.CameraFocusSelection, new KeyCombination(Key.C)),

                // Layout and window management
                new(UserActions.ToggleAllUiElements, new KeyCombination(Key.Esc, shift: true)),
                new(UserActions.ToggleFullscreen, new KeyCombination(Key.F11)),
                new(UserActions.ToggleFocusMode, new KeyCombination(Key.F12))
            ];

        var numberKeys = new[] { Key.D0, Key.D1, Key.D2, Key.D3, Key.D4, Key.D5, Key.D6, Key.D7, Key.D8, Key.D9 };

        for (var i = 0; i < 10; i++)
        {
            var loadAction = UserActions.LoadBookmark0 + i;
            var saveAction = UserActions.SaveBookmark0 + i;

            keyBindings.Add(new KeyBinding(loadAction, new KeyCombination(numberKeys[i], ctrl: true)));
            keyBindings.Add(new KeyBinding(saveAction, new KeyCombination(numberKeys[i], ctrl: true, shift: true)));
        }

        var fKeys = new[] { Key.F1, Key.F2, Key.F3, Key.F4, Key.F5, Key.F6, Key.F7, Key.F8, Key.F9, Key.F10 };

        for (var i = 0; i < 10; i++)
        {
            keyBindings.Add(new KeyBinding(UserActions.LoadLayout0 + i, new KeyCombination(fKeys[i])));
            keyBindings.Add(new KeyBinding(UserActions.SaveLayout0 + i, new KeyCombination(fKeys[i], ctrl: true)));
        }

        var map = new KeyMap
                      {
                          Bindings =
                              keyBindings,
                          Name = "Default (QUERTY)",
                          Author = "TiXL Community",
                          ReadOnly = true,
                      };

        map.UpdateShortcutLabels();
        return map;
    }

    public static void CloneCurrentKeymap()
    {
        var newKeymap = CurrentKeymap.Clone();

        newKeymap.Name += "Custom";
        newKeymap.Author = UserSettings.Config.UserName;
        KeyMaps.Add(newKeymap);
        CurrentKeymap = newKeymap;
    }
}