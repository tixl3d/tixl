#nullable enable
using System.Diagnostics.CodeAnalysis;
using System.IO;
using T3.Core.DataTypes.Vector;
using T3.Core.UserData;
using T3.Editor.Gui.UiHelpers;
using T3.Serialization;

namespace T3.Editor.Gui.Styling;

public static class ThemeHandling
{
    /// <summary>
    /// Requires user settings to be loaded already.
    /// </summary>
    internal static void Initialize()
    {
        InitializeFactoryDefault();
        LoadThemes();
        ApplyUserConfigTheme();
    }

    internal static void SetThemeAsUserTheme(ColorTheme theme)
    {
        UserSettings.Config.ColorThemeName = theme.Name;
        UserSettings.Save();
        ApplyTheme(theme);

        T3Style.Apply();
    }

    internal static void SaveTheme(ColorTheme theme)
    {
        Directory.CreateDirectory(ThemeFolder);

        theme.Name = theme.Name.Trim();
        if (string.IsNullOrEmpty(theme.Name))
        {
            theme.Name = "untitled";
        }

        var combine = GetThemeFilepath(theme);
        var filepath = combine;

        StoreAllColors(theme);

        JsonUtils.TrySaveJson(theme, filepath);
        LoadThemes();
    }

    internal static void DeleteTheme(ColorTheme theme)
    {
        var filepath = GetThemeFilepath(theme);
        if (!File.Exists(filepath))
        {
            Log.Warning($"{filepath} does not exist?");
            return;
        }

        File.Delete(filepath);
        ApplyTheme(FactoryTheme);
        LoadThemes();
        UserSettings.Config.ColorThemeName = string.Empty;
    }

    internal static ColorTheme GetUserOrFactoryTheme()
    {
        var selectedThemeName = UserSettings.Config.ColorThemeName;
        if (string.IsNullOrWhiteSpace(selectedThemeName))
        {
            return FactoryTheme;
        }

        var userTheme = Themes.FirstOrDefault(t => t.Name == selectedThemeName);
        if (userTheme == null)
        {
            Log.Warning($"Couldn't load {selectedThemeName}");
            return FactoryTheme;
        }

        return userTheme;
    }
    
    private static void LoadThemes()
    {
        Themes.Clear();
        
        Directory.CreateDirectory(ThemeFolder);
        Directory.CreateDirectory(DefaultThemeFolder);

        // copy default themes if not present
        foreach (var theme in Directory.EnumerateFiles(DefaultThemeFolder))
        {
            var targetPath = Path.Combine(ThemeFolder, Path.GetFileName(theme));
            if(!File.Exists(targetPath))
                File.Copy(theme, targetPath);
        }

        foreach (var filepath in Directory.EnumerateFiles(ThemeFolder))
        {
            try
            {
                var t = JsonUtils.TryLoadingJson<ColorTheme>(filepath);
                if (t == null)
                {
                    Log.Debug($"Failed to load theme {filepath}");
                    continue;
                }

                Themes.Add(t);
            }
            catch (Exception e)
            {
                Log.Error($"Failed to load {filepath} : {e.Message}");
            }
        }
    }

    private static void ApplyUserConfigTheme()
    {
        var userTheme = GetUserOrFactoryTheme();

        ApplyTheme(userTheme);
    }
    


    /// <summary>
    /// Applies the colors to T3StyleColors
    /// </summary>
    /// <param name="theme"></param>
    private static void ApplyTheme(ColorTheme theme)
    {
        var colorFields = typeof(UiColors).GetFields();
        foreach (var colorField in colorFields)
        {
            if (colorField.GetValue(ColorThemeEditor.Dummy) is not Color)
                continue;

            if (!theme.Colors.TryGetValue(colorField.Name, out var colorValue))
                continue;

            colorField.SetValue(ColorThemeEditor.Dummy, new Color(colorValue));
        }

        var variationFields = typeof(ColorVariations).GetFields();
        foreach (var varField in variationFields)
        {
            if (varField.GetValue(ColorThemeEditor.Dummy) is not ColorVariation)
                continue;

            if (!theme.Variations.TryGetValue(varField.Name, out var variation))
                continue;

            varField.SetValue(ColorThemeEditor.Dummy, variation.Clone());
        }

        FrameStats.Current.UiColorsChanged = true;
        T3Style.Apply();
    }

    private static string GetThemeFilepath(ColorTheme theme)
    {
        return Path.Combine(ThemeFolder, theme.Name + ".json");
    }

    private static void StoreAllColors(ColorTheme theme)
    {
        var colorFields = typeof(UiColors).GetFields();
        foreach (var colorField in colorFields)
        {
            if (colorField.GetValue(ColorThemeEditor.Dummy) is not Color color)
                continue;

            theme.Colors[colorField.Name] = color;
        }

        var variationFields = typeof(ColorVariations).GetFields();
        foreach (var varField in variationFields)
        {
            if (varField.GetValue(ColorThemeEditor.Dummy) is not ColorVariation variation)
                continue;

            theme.Variations[varField.Name] = variation;
        }
    }


    private static void InitializeFactoryDefault()
    {
        FactoryTheme = new ThemeHandling.ColorTheme();

        var colorFields = typeof(UiColors).GetFields();
        foreach (var f in colorFields)
        {
            if (f.GetValue(ColorThemeEditor.Dummy) is not Color color)
                continue;

            FactoryTheme.Colors[f.Name] = color;
        }

        var variationFields = typeof(ColorVariations).GetFields();
        foreach (var v in variationFields)
        {
            if (v.GetValue(ColorThemeEditor.Dummy) is not ColorVariation variation)
                continue;

            FactoryTheme.Variations[v.Name] = variation;
        }
    }

    internal static readonly List<ColorTheme> Themes = [];
    internal static string ThemeFolder => Path.Combine(FileLocations.SettingsDirectory, FileLocations.ThemeSubFolder);
    private static string DefaultThemeFolder => Path.Combine(FileLocations.ReadOnlySettingsPath, FileLocations.ThemeSubFolder);
    internal static ColorTheme FactoryTheme = null!;
    
    
    // This public for serialization
    [SuppressMessage("ReSharper", "MemberCanBeInternal")]
    public sealed class ColorTheme
    {
        public string Name = "untitled";
        public string Author = "unknown";
        public Dictionary<string, Vector4> Colors = new();
        public Dictionary<string, ColorVariation> Variations = new();

        public ColorTheme Clone()
        {
            return new ColorTheme()
                       {
                           Name = Name,
                           Author = Author,
                           Colors = Colors.ToDictionary(entry => entry.Key,
                                                        entry => entry.Value),
                           Variations = Variations.ToDictionary(entry => entry.Key,
                                                                entry => entry.Value),
                       };
        }
    }
}