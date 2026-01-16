#nullable enable
using System.Reflection;
using ImGuiNET;
using T3.Core.DataTypes.Vector;
using T3.Core.UserData;
using T3.Editor.Gui.Input;
using T3.Editor.Gui.Interaction;
using T3.Editor.Gui.UiHelpers;
using T3.Editor.UiModel.InputsAndTypes;

namespace T3.Editor.Gui.Styling;

/// <summary>
/// User interface switching and adjusting color themes...
/// </summary>
internal static class ColorThemeEditor
{
    public static void DrawEditor()
    {
        if (!_initialized)
        {
            _currentTheme = ThemeHandling.GetUserOrFactoryTheme();
            _currentThemeWithoutChanges = _currentTheme.Clone();
            _initialized = true;

        }
        FormInputs.SetIndent(100);
        
        var colorFields = typeof(UiColors).GetFields();
        var colorVariationFields = typeof(ColorVariations).GetFields();

        var xxx = typeof(ColorVariations);
        var ff = xxx.UnderlyingSystemType;
        
        var xxx2 = ff.GetFields();
        
        if (FormInputs.AddDropdown(ref UserSettings.Config.ColorThemeName, 
                                   ThemeHandling.Themes.Select(t => t.Name), 
                                   "Theme",
                                   $"""
                                   Choose a color theme for editing, then apply your modifications and save them. 
                                   You have the option to create new themes by altering the name and save. 
                                   Themes are saved as files in {FileLocations.SettingsDirectory}.
                                   """))
        {
            var selectedTheme = ThemeHandling.Themes.FirstOrDefault(t => t.Name == UserSettings.Config.ColorThemeName);
            if (selectedTheme != null)
            {
                ThemeHandling.SetThemeAsUserTheme(selectedTheme);
                _currentTheme = selectedTheme;
                _currentThemeWithoutChanges = _currentTheme.Clone();
            }
        }
        
        FormInputs.AddVerticalSpace();
        FormInputs.AddStringInput("Name", ref _currentTheme.Name!);
        FormInputs.AddStringInput("Author", ref _currentTheme.Author!);
        _somethingChanged |= _currentTheme.Name != _currentThemeWithoutChanges.Name;
        _somethingChanged |= _currentTheme.Author != _currentThemeWithoutChanges.Author;
        
        
        FormInputs.AddVerticalSpace();
        FormInputs.ApplyIndent();
        if (CustomComponents.DisablableButton("Save", _somethingChanged))
        {
            ThemeHandling.SaveTheme(_currentTheme);
            UserSettings.Config.ColorThemeName = _currentTheme.Name;
            var currentFromName = ThemeHandling.Themes.FirstOrDefault(t => t.Name == UserSettings.Config.ColorThemeName);
            if (currentFromName == null)
            {
                Log.Error("Saving theme failed");
                return;
            }
            
            _currentTheme = currentFromName;
            _currentThemeWithoutChanges = currentFromName.Clone();
        }
        
        ImGui.SameLine();
        if (ImGui.Button("Delete"))
        {
            ThemeHandling.DeleteTheme(_currentTheme);
            _currentTheme = ThemeHandling.FactoryTheme.Clone();
            _currentThemeWithoutChanges = ThemeHandling.FactoryTheme.Clone();
        }
        
        FormInputs.AddVerticalSpace();
        ImGui.Separator();
        FormInputs.AddVerticalSpace();
        
        _somethingChanged = false;
        DrawColorEdits(colorFields);
        DrawVariations(colorVariationFields);
    }

    private static void DrawColorEdits(IEnumerable<FieldInfo> colorFields)
    {
        FormInputs.AddSectionHeader("Colors");
        foreach (var f in colorFields)
        {
            var isChanged = false;
            ImGui.PushID(f.Name);
            if (f.GetValue(_currentTheme) is not Color color)
                continue;

            ImGui.AlignTextToFramePadding();

            if (!_currentThemeWithoutChanges.Colors.TryGetValue(f.Name, out var defaultColor))
            {
                defaultColor = color;
                isChanged = true;
            }
            else
            {
                if (color != defaultColor)
                    isChanged = true;
            }

            _somethingChanged |= isChanged;

            var hint = string.Empty;
            var groupTitle = string.Empty;
            foreach (var ca in f.GetCustomAttributes(true))
            {
                if (ca is not T3Style.HintAttribute hintAttribute)
                    continue;

                hint = hintAttribute.Description;
                groupTitle = hintAttribute.GroupTitle;
            }
            
            
            if (!string.IsNullOrWhiteSpace(groupTitle))
            {
                ImGui.PushFont(Fonts.FontBold);
                ImGui.PushStyleColor(ImGuiCol.Text, UiColors.TextMuted.Rgba);
                ImGui.TextUnformatted(groupTitle);
                ImGui.PopStyleColor();
                ImGui.PopFont();
            }

            var vec4Color = color.Rgba;
            ImGui.PushStyleColor(ImGuiCol.Text, isChanged ? UiColors.Text.Rgba : UiColors.TextMuted);

            if (ColorEditButton.Draw(ref vec4Color, new Vector2(24, 24)).HasFlag(InputEditStateFlags.Modified))
            {
                FrameStats.Current.UiColorsChanged = true;
                SetColor(f, vec4Color);
                T3Style.Apply();
            }

            ImGui.SameLine(0, 10);
            ImGui.Text(CustomComponents.HumanReadablePascalCase(f.Name));
            if (ImGui.IsItemHovered() && ImGui.IsMouseClicked(ImGuiMouseButton.Left))
            {
                FrameStats.Current.UiColorsChanged = true;
                SetColor(f, defaultColor);
                T3Style.Apply();
            }

            if (!string.IsNullOrWhiteSpace(hint))
            {
                CustomComponents.TooltipForLastItem(hint);
            }

            if (isChanged)
            {
                ImGui.SameLine();
                if (FloatingIconButton(Icon.Revert, Vector2.Zero))
                {
                    SetColor(f, defaultColor);
                    T3Style.Apply();
                }
            }
            ImGui.PopStyleColor();

            ImGui.PopID();
        }
    }

    private static void DrawVariations(IEnumerable<FieldInfo> variationFields)
    {
        ImGui.Separator();
        FormInputs.AddSectionHeader("Data type variations");
        CustomComponents.HelpText("These factors determine the Brightness, Saturation, and Opacity of each purpose. By applying them to the previously defined base colors, you can achieve a wide range of variety and contrast.");
        
        var vec3Width = ImGui.GetContentRegionAvail().X -200;

        ImGui.Text("Luma");
        ImGui.SameLine(vec3Width/3);
        ImGui.Text("Sat");
        ImGui.SameLine(2*vec3Width/3);
        ImGui.Text("Alpha");
        
        foreach (var f in variationFields)
        {
            var isChanged = false;
            ImGui.PushID(f.Name);
            if (f.GetValue(_currentTheme) is not ColorVariation variation)
                continue;

            ImGui.AlignTextToFramePadding();

            if (!_currentThemeWithoutChanges.Variations.TryGetValue(f.Name, out var defaultVariation))
            {
                defaultVariation = variation;
                isChanged = true;
            }
            else
            {
                if (variation != defaultVariation)
                    isChanged = true;
            }

            _somethingChanged |= isChanged;

            var hint = string.Empty;
            foreach (var ca in f.GetCustomAttributes(true))
            {
                if (ca is not T3Style.HintAttribute hintAttribute)
                    continue;

                hint = hintAttribute.Description;
            }

            var brightSaturationAlpha = new Vector3(variation.Brightness, variation.Saturation, variation.Opacity);
            
            ImGui.PushStyleColor(ImGuiCol.Text, isChanged ? UiColors.Text.Rgba : UiColors.TextMuted);

            ImGui.SetNextItemWidth(vec3Width);
            if(ImGui.DragFloat3(f.Name, ref brightSaturationAlpha, 0.01f))
            {
                variation.Brightness = brightSaturationAlpha.X;
                variation.Saturation = brightSaturationAlpha.Y;
                variation.Opacity = brightSaturationAlpha.Z;

                SetVariation(f, variation);
                T3Style.Apply();
                FrameStats.Current.UiColorsChanged = true;
            }
            
            if (!string.IsNullOrEmpty(hint) && ImGui.IsItemHovered())
            {
                ImGui.SetTooltip(hint);
            }

            if (isChanged)
            {
                ImGui.SameLine();
                if (FloatingIconButton(Icon.Revert, Vector2.Zero))
                {
                    SetVariation(f, defaultVariation.Clone());
                }
            }
            
            ImGui.PopStyleColor();
            ImGui.PopID();
        }
    }
    
    private static void SetColor(FieldInfo f, Vector4 vec4Color)
    {
        _currentTheme.Colors[f.Name] = vec4Color;
        f.SetValue(Dummy, new Color(vec4Color));
    }
    
    private static void SetVariation(FieldInfo f, ColorVariation variation)
    {
        _currentTheme.Variations[f.Name] = variation;
        f.SetValue(Dummy, variation.Clone());
    }
    
    // ReSharper disable once UnusedType.Local
    private static class DevHelpers
    {
        // ReSharper disable once UnusedMember.Local
        public static void Draw(FieldInfo[] fields)
        {
            // Debug utils for finding unused colors...
            if (FormInputs.AddCheckBox("Animate all colors", ref _animatedAllColors))
            {
                if (!_animatedAllColors)
                {
                    // ReSharper disable once NullCoalescingConditionIsAlwaysNotNullAccordingToAPIContract
                    _currentTheme ??= _currentThemeWithoutChanges.Clone();
                    ApplyBlinkToAll(fields, 0);
                }
            }

            var flashValues = _flashColor.Rgba;
            if (ColorEditButton.Draw(ref flashValues, new Vector2(16, 16)).HasFlag(InputEditStateFlags.Modified))
            {
                _flashColor.Rgba = flashValues;
                ApplyBlinkToAll(fields, flashValues.W);
            }

            if (_animatedAllColors)
            {
                var blinkAmount = MathF.Sin((float)ImGui.GetTime() * 10f) * 0.25f + 0.25f;
                ApplyBlinkToAll(fields, blinkAmount);
            }
        }

        private static void ApplyBlinkToAll(FieldInfo[] fields, float blinkAmount)
        {
            foreach (var f in fields)
            {
                if (!_currentThemeWithoutChanges.Colors.TryGetValue(f.Name, out var defaultColorValues))
                    continue;

                var blinkingColor = Color.Mix(new Color(defaultColorValues),
                                              _flashColor, blinkAmount);
                SetColor(f, blinkingColor);
            }

            T3Style.Apply();
            FrameStats.Current.UiColorsChanged = true;
        }

        private static bool _animatedAllColors;
        private static Color _flashColor = new(1, 1, 1, 0f);
    }
    
    
    private static bool _somethingChanged;
    private static bool _initialized;
    private static ThemeHandling.ColorTheme _currentTheme= new();
    private static ThemeHandling.ColorTheme _currentThemeWithoutChanges = new();
    public static readonly object Dummy = new();

    public static bool FloatingIconButton(Icon icon, Vector2 size)
    {
        if (size == Vector2.Zero)
        {
            var h = ImGui.GetFrameHeight();
            size = new Vector2(h, h);
        }

        ImGui.PushFont(Icons.IconFont);
        ImGui.PushStyleVar(ImGuiStyleVar.ButtonTextAlign, new Vector2(0.5f, 0.5f));
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, Vector2.Zero);
        ImGui.PushStyleColor(ImGuiCol.Button, Color.Transparent.Rgba);
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, Color.Transparent.Rgba);
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, Color.Transparent.Rgba);

        var clicked = ImGui.Button(((char)icon).ToString(), size);

        ImGui.PopStyleColor(3);
        ImGui.PopStyleVar(2);
        ImGui.PopFont();
        return clicked;
    }
}