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
            // Settings window is show in help menu...
            if (window == SettingsWindow)
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