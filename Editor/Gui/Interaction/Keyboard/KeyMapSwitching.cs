#nullable enable
using System.IO;
using T3.Core.Settings;
using T3.Editor.Gui.UiHelpers;
using T3.Serialization;

namespace T3.Editor.Gui.Interaction.Keyboard;

/// <summary>
/// Manages loading and switching of keyboard layouts
/// </summary>
internal static class KeyMapSwitching
{
    private static readonly KeyMap _factoryKeymap = FactoryKeyMap.CreateFactoryKeymap();
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

        // Load from both default and user folders. User files with the same
        // name take precedence over shipped defaults — but defaults are NOT
        // copied into the user folder until the user explicitly saves.
        var userFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var filepath in Directory.EnumerateFiles(KeyMapFolder))
            userFiles.Add(Path.GetFileName(filepath));

        if (Directory.Exists(DefaultKeyMapFolder))
        {
            foreach (var filepath in Directory.EnumerateFiles(DefaultKeyMapFolder))
            {
                if (userFiles.Contains(Path.GetFileName(filepath)))
                    continue; // user override exists

                LoadKeyMapFile(filepath);
            }
        }

        foreach (var filepath in Directory.EnumerateFiles(KeyMapFolder))
        {
            LoadKeyMapFile(filepath);
        }
    }

    private static void LoadKeyMapFile(string filepath)
    {
        try
        {
            var t = JsonUtils.TryLoadingJson<KeyMap>(filepath);
            if (t == null)
            {
                Log.Debug($"Failed to load Keymap {filepath}");
                return;
            }

            t.UpdateShortcutLabels();
            KeyMaps.Add(t);
        }
        catch (Exception e)
        {
            Log.Error($"Failed to load {filepath} : {e.Message}");
        }
    }

    internal static readonly List<KeyMap> KeyMaps = [_factoryKeymap];
    private static string KeyMapFolder => Path.Combine(FileLocations.SettingsDirectory, FileLocations.KeyBindingSubFolder);
    private static string DefaultKeyMapFolder => Path.Combine(FileLocations.ReadOnlySettingsPath, FileLocations.KeyBindingSubFolder);

    public static void CloneCurrentKeymap()
    {
        var newKeymap = CurrentKeymap.Clone();

        newKeymap.Name += "Custom";
        newKeymap.Author = UserSettings.Config.UserName;
        KeyMaps.Add(newKeymap);
        CurrentKeymap = newKeymap;
    }
}