#nullable enable
using ImGuiNET;
using T3.Editor.Gui.Graph.Window;
using T3.Editor.Gui.Interaction;
using T3.Editor.Gui.Interaction.Keyboard;
using T3.Editor.Gui.UiHelpers;
using T3.Editor.Gui.Windows.AssetLib;
using T3.Editor.Gui.Windows.Exploration;
using T3.Editor.Gui.Windows.Output;
using T3.Editor.Gui.Windows.RenderExport;
using T3.Editor.Gui.Windows.SymbolLib;
using T3.Editor.Gui.Windows.Variations;

namespace T3.Editor.Gui.Windows.Layouts;

internal static partial class WindowManager
{
    internal static void Draw()
    {
        TryToInitialize(); // We have to keep initializing until window sizes are initialized
        if (!_hasBeenInitialized)
            return;
            
        LayoutHandling.ProcessKeyboardShortcuts();

        if (UserActions.ToggleVariationsWindow.Triggered())
        {
            ToggleWindowTypeVisibility<VariationsWindow>();
        }

        UpdateAppWindowSize();

        foreach (var windowType in _windows)
        {
            windowType.Draw();
        }

        if (DemoWindowVisible)
            ImGui.ShowDemoWindow(ref DemoWindowVisible);

        if (MetricsWindowVisible)
            ImGui.ShowMetricsWindow(ref MetricsWindowVisible);
    }

    internal static readonly SettingsWindow SettingsWindow = new();
    internal static readonly UtilitiesWindow UtilitiesWindow = new();
    internal static readonly ScreenManager ScreenManager = new();


    private static void TryToInitialize()
    {
        // Wait first frame for ImGUI to initialize
        if (ImGui.GetTime() > 0.2f || _hasBeenInitialized)
            return;
        
        _windows =
            [
                new OutputWindow(),
                new GraphWindow(),
                new ParameterWindow(),
                new SymbolLibrary(),
                new AssetLibrary(),
                new VariationsWindow(),
                new ExplorationWindow(),
                new RenderWindow(),
                new IoViewWindow(),
                Program.ConsoleLogWindow,
                UtilitiesWindow,    // item shown in TiXL > Development menu
                SettingsWindow, // item shown in TiXL menu
                ScreenManager,
            ];


        ReApplyLayout();
        _appWindowSize = ImGui.GetIO().DisplaySize;
        _hasBeenInitialized = true;
    }

    private static void ReApplyLayout()
    {
        LayoutHandling.LoadAndApplyLayoutOrFocusMode(UserSettings.Config.WindowLayoutIndex);
    }

    internal static IEnumerable<Window> GetAllWindows()
    {
        foreach (var window in _windows)
        {
            if (window.AllowMultipleInstances)
            {
                foreach (var windowInstance in window.GetInstances())
                {
                    yield return windowInstance;
                }
            }
            else
            {
                yield return window;
            }
        }

        foreach (var window in GraphWindow.GraphWindowInstances)
            yield return window;
    }

    public static bool IsAnyInstanceVisible<T>() where T : Window
    {
        return GetAllWindows().OfType<T>().Any(w => w.Config.Visible);
    }
        
    public static void ToggleInstanceVisibility<T>() where T : Window
    {
        var foundFirst = false;
        var newVisibility = false;
        foreach (var w in GetAllWindows().OfType<T>())
        {
            if (!foundFirst)
            {
                newVisibility = !w.Config.Visible;
                foundFirst = true;
            }

            w.Config.Visible = newVisibility;
        }
    }

    private static void UpdateAppWindowSize()
    {
        var newSize = ImGui.GetIO().DisplaySize;
        if (newSize == _appWindowSize)
            return;

        _appWindowSize = newSize;

        LayoutHandling.UpdateAfterResize(newSize);
    }

    public static void ToggleWindowTypeVisibility<T>() where T : Window
    {
        var instances = GetAllWindows().OfType<T>().ToList();
        if (instances.Count != 1)
            return;

        instances[0].Config.Visible = !instances[0].Config.Visible;
    }

    internal static bool DemoWindowVisible;
    internal static bool MetricsWindowVisible;
    
    private static Vector2 _appWindowSize;
    
    /// <summary>
    /// Contains all possible window types and used for updating and layout management
    /// </summary>
    private static List<Window> _windows = [];
    
    public static bool ShowSecondaryRenderWindow { get; set; }
    private static bool _hasBeenInitialized;
}