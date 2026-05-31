#nullable enable
using ImGuiNET;
using T3.Editor.App;
using T3.Editor.SystemUi;

namespace T3.Editor.Gui.Windows.Layouts;

internal static partial class WindowManager
{
    public static void DrawWindowMenuContent()
    {
        foreach (var window in _windows)
        {
            // These windows live in the TiXL menu and shouldn't be re-listed here.
            if (window == SettingsWindow
                || window == ProjectSettingsWindow
                || window == GuidedFeatureTestsWindow)
                continue;

            window.DrawMenuItemToggle();
        }

        ImGui.Separator();
        {
            var screens = EditorUi.Instance.AllScreens;

            if (ImGui.MenuItem("Output Window", "", ShowSecondaryRenderWindow))
            {
                ShowSecondaryRenderWindow = !ShowSecondaryRenderWindow;
                ProgramWindows.UpdateViewerWindowState();
            }
                
        }

        ImGui.Separator();

        LayoutHandling.DrawMainMenuItems();
    }
}