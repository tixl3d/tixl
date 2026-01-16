#nullable enable

using System.IO;
using ImGuiNET;
using Newtonsoft.Json;
using T3.Core.UserData;
using T3.Editor.Gui.Window;
using T3.Editor.Gui.Interaction;
using T3.Editor.Gui.Interaction.Keyboard;
using T3.Editor.Gui.UiHelpers;
using T3.Editor.Gui.Windows.Output;

namespace T3.Editor.Gui.Windows.Layouts;

/// <summary>
/// Manages visibility and layout of windows including...
/// - switching between Layouts
/// - toggling visibility from the main menu
/// - Graph over content mode
/// </summary>    
internal static class LayoutHandling
{
    public static void ProcessKeyboardShortcuts()
    {
        // Process Keyboard shortcuts
        for (var i = 0; i < _saveLayoutActions.Length; i++)
        {
            if (_saveLayoutActions[i].Triggered())
                SaveLayout(i);

            if (_loadLayoutActions[i].Triggered())
                LoadAndApplyLayoutOrFocusMode((Layouts)i);
        }
    }

    public static void DrawMainMenuItems()
    {
        if (ImGui.BeginMenu("Load layout"))
        {
            for (int i = 0; i < 10; i++)
            {
                if (ImGui.MenuItem("Layout " + (i + 1), "F" + (i + 1), false, enabled: DoesLayoutExists(i)))
                {
                    LoadAndApplyLayoutOrFocusMode((Layouts)i);
                }
            }

            ImGui.EndMenu();
        }

        if (ImGui.BeginMenu("Save layouts"))
        {
            for (int i = 0; i < 10; i++)
            {
                if (ImGui.MenuItem("Layout " + (i + 1), "Ctrl+F" + (i + 1)))
                {
                    SaveLayout(i);
                }
            }

            ImGui.EndMenu();
        }

        if (ImGui.MenuItem("Save current layout", ""))
            SaveLayout(0);
    }

    public static void UpdateAfterResize(Vector2 newSize)
    {
        if (newSize == Vector2.Zero)
            return;

        ApplyLayout(new Layout
                        {
                            WindowConfigs = WindowManager
                                           .GetAllWindows()
                                           .Select(window => window.Config)
                                           .Where(config => config != null)
                                           .ToList()
                        });
    }

    public static void LoadAndApplyLayoutOrFocusMode(Layouts layoutId)
    {
        var index = (int)layoutId;

        var relativePath = Path.Combine(LayoutSubfolder, GetLayoutFilename(index));
        if (!UserData.TryLoadingOrWriteDefaults(relativePath, out var jsonBlob))
            return;

        var serializer = JsonSerializer.Create();
        var fileTextReader = new StringReader(jsonBlob);
        if (serializer.Deserialize(fileTextReader, typeof(Layout)) is not Layout layout)
        {
            Log.Error("Can't load layout");
            return;
        }

        ApplyLayout(layout);
        foreach (var graphWindow in GraphWindow.GraphWindowInstances)
        {
            graphWindow.SetWindowToNormal();
        }

        var isFocusMode = layoutId == Layouts.FocusMode;
        UserSettings.Config.FocusMode = isFocusMode;
        if (!isFocusMode)
            UserSettings.Config.WindowLayoutIndex = index;
    }

    public static string GraphPrefix => "Graph View##";
    public static string OutputPrefix => "Output View##";
    public static string ParametersPrefix => "Parameters##";

    private static void ApplyLayout(Layout layout)
    {
        // First update windows settings
        foreach (var config in layout.WindowConfigs)
        {
            if (string.IsNullOrEmpty(config.Title))
            {
                Log.Warning("Skipping invalid view layout configuration.");
                continue;
            }

            var matchingWindow = WindowManager.GetAllWindows()
                                              .FirstOrDefault(window => window.Config.Title == config.Title);
            if (matchingWindow == null)
            {
                if (config.Title.StartsWith(GraphPrefix))
                {
                    // new window
                    // TODO: check userSettings to check if we should try loading a project.
                    matchingWindow = new GraphWindow();
                    matchingWindow.Config = config;
                }
                else if (config.Title.StartsWith(OutputPrefix))
                {
                    matchingWindow = new OutputWindow();
                    matchingWindow.Config = config;
                }
                else if (config.Title.StartsWith(ParametersPrefix))
                {
                    matchingWindow = new ParameterWindow();
                    matchingWindow.Config = config;
                }
            }
            else
            {
                matchingWindow.Config = config;
            }
        }

        // Close Windows without configurations
        foreach (var w in WindowManager.GetAllWindows())
        {
            var hasConfig = layout.WindowConfigs.Any(config => config.Title == w.Config.Title);
            if (!hasConfig)
            {
                w.Config.Visible = false;
            }
        }

        // Apply ImGui settings
        if (!string.IsNullOrEmpty(layout.ImGuiSettings))
        {
            Program.NewImGuiLayoutDefinition = layout.ImGuiSettings;
        }
    }

    private static void SaveLayout(int index)
    {
        Directory.CreateDirectory(LayoutFolder);

        var serializer = JsonSerializer.Create();
        serializer.Formatting = Formatting.Indented;

        var completePath = Path.Combine(LayoutFolder, GetLayoutFilename(index));
        using var file = File.CreateText(completePath);
        var layout = new Layout
                         {
                             WindowConfigs = WindowManager.GetAllWindows().Select(window => window.Config).ToList(),
                             ImGuiSettings = ImGui.SaveIniSettingsToMemory(),
                         };

        serializer.Serialize(file, layout);
        UserSettings.Config.WindowLayoutIndex = index;
    }

    private static string GetLayoutFilename(int index)
    {
        return string.Format(LayoutFileNameFormat, index);
    }

    private static bool DoesLayoutExists(int index)
    {
        return UserData.CanLoad(Path.Combine(LayoutSubfolder, GetLayoutFilename(index)));
    }

    private static readonly UserActions[] _loadLayoutActions =
        {
            UserActions.LoadLayout0,
            UserActions.LoadLayout1,
            UserActions.LoadLayout2,
            UserActions.LoadLayout3,
            UserActions.LoadLayout4,
            UserActions.LoadLayout5,
            UserActions.LoadLayout6,
            UserActions.LoadLayout7,
            UserActions.LoadLayout8,
            UserActions.LoadLayout9,
        };

    private static readonly UserActions[] _saveLayoutActions =
        {
            UserActions.SaveLayout0,
            UserActions.SaveLayout1,
            UserActions.SaveLayout2,
            UserActions.SaveLayout3,
            UserActions.SaveLayout4,
            UserActions.SaveLayout5,
            UserActions.SaveLayout6,
            UserActions.SaveLayout7,
            UserActions.SaveLayout8,
            UserActions.SaveLayout9,
        };

    public enum Layouts
    {
        Custom0 = 0,
        Custom1 = 1,
        Custom2 = 2,
        Custom3 = 3,
        Custom4 = 4,
        Custom5 = 5,
        Custom6 = 6,
        Custom7 = 7,
        Custom8 = 8,
        Custom9 = 9,
        FocusMode = 11,
        SkillQuest = 12,
    }

    /// <summary>
    /// Defines a layout that can be then serialized to file  
    /// </summary>
    private sealed class Layout
    {
        public List<Window.WindowConfig> WindowConfigs = [];
        public string? ImGuiSettings;
    }

    private const string LayoutFileNameFormat = "layout{0}.json";
    private static string LayoutSubfolder => "Layouts";
    public static string LayoutFolder => Path.Combine(FileLocations.SettingsDirectory, LayoutSubfolder);
}