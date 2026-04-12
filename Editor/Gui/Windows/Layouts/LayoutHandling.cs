#nullable enable

using System.IO;
using ImGuiNET;
using Newtonsoft.Json;
using T3.Core.Settings;
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
            {
                SaveLayout(i);
                break;
            }

            if (_loadLayoutActions[i].Triggered())
            {
                LoadAndApplyLayoutOrFocusMode((Layouts)i);
                break;
            }
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
        if (!UserData.TryLoading(relativePath, out var jsonBlob))
            return;

        var serializer = JsonSerializer.Create();
        if (serializer.Deserialize(new StringReader(jsonBlob), typeof(Layout)) is not Layout layout)
        {
            Log.Error("Can't load layout");
            return;
        }

        // Reset stale layouts saved against an older ImGui version. ImGui's ini
        // serialization format is not stable across versions, and dock-node entries
        // from a previous version are silently dropped, leaving windows undocked.
        if (!IsLayoutCompatibleWithCurrentImGui(layout))
        {
            var legacyTag = string.IsNullOrEmpty(layout.ImGuiVersion) ? "<missing>" : layout.ImGuiVersion;
            Log.Warning($"Layout {index} was saved with ImGui {legacyTag}; minimum supported is {MinSupportedImGuiVersion} (running {ImGui.GetVersion()}). Replacing with shipped default.");

            var userFilePath = Path.Combine(LayoutFolder, GetLayoutFilename(index));
            try
            {
                if (File.Exists(userFilePath))
                    File.Delete(userFilePath);
            }
            catch (Exception e)
            {
                Log.Warning($"Could not remove outdated layout file '{userFilePath}': {e.Message}");
            }

            if (!UserData.TryLoading(relativePath, out jsonBlob))
                return;

            if (serializer.Deserialize(new StringReader(jsonBlob), typeof(Layout)) is not Layout reloaded)
            {
                Log.Error("Can't load default layout");
                return;
            }
            layout = reloaded;
        }

        var switchingBackFromFocusMode = layoutId != Layouts.FocusMode && FocusMode;
        if (switchingBackFromFocusMode)
        {
            UiConfig.RestoreUiVisibilityAfterFocusMode();
        }
        
        if(layoutId != Layouts.FocusMode) 
        {
            UserSettings.Config.WindowLayoutIndex = index;
        }

        ApplyLayout(layout);
        foreach (var graphWindow in GraphWindow.GraphWindowInstances)
        {
            graphWindow.SetWindowToNormal();
        }

        // var isFocusMode = layoutId == Layouts.FocusMode;
        // UserSettings.Config.FocusMode = isFocusMode;
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

        ChangeCounter++;
        //UiConfig.RestoreUiVisibilityAfterFocusMode();
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
                             ImGuiVersion = ImGui.GetVersion(),
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

        /// <summary>
        /// ImGui version (from <see cref="ImGui.GetVersion"/>) the layout was saved with.
        /// Missing tag implies a pre-1.90 layout whose docking section is no longer parseable.
        /// </summary>
        public string? ImGuiVersion;
    }

    /// <summary>
    /// Lowest ImGui version whose ini/docking serialization is still readable by the
    /// currently bundled ImGui.NET. Bump this whenever upstream ImGui makes a breaking
    /// change to the settings format that we cannot migrate in place.
    /// </summary>
    private static readonly Version MinSupportedImGuiVersion = new(1, 91, 6);

    private static bool IsLayoutCompatibleWithCurrentImGui(Layout layout)
    {
        if (string.IsNullOrEmpty(layout.ImGuiVersion))
            return false;
        if (!TryParseImGuiVersion(layout.ImGuiVersion, out var saved))
            return false;
        return saved >= MinSupportedImGuiVersion;
    }

    private static bool TryParseImGuiVersion(string raw, out Version version)
    {
        // ImGui.GetVersion() returns strings like "1.90.9" or "1.91.0 WIP".
        var token = raw.Split(' ', '-')[0];
        return Version.TryParse(token, out version!);
    }

    private const string LayoutFileNameFormat = "layout{0}.json";
    private static string LayoutSubfolder => "Layouts";
    public static string LayoutFolder => Path.Combine(FileLocations.SettingsDirectory, LayoutSubfolder);
    public static int ChangeCounter { get; private set; }
    public static bool FocusMode = false;
}