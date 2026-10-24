#nullable enable
using ImGuiNET;
using T3.Editor.App;
using T3.Editor.Gui.Styling;
using T3.Editor.SystemUi;

namespace T3.Editor.Gui.Windows.Layouts;

internal static partial class WindowManager
{
    public static void DrawWindowMenuContent()
    {
        foreach (var window in _windows)
        {
            // These windows live in other menus (TiXL / Help) and shouldn't be re-listed here.
            if (window == SettingsWindow
                || window == ProjectSettingsWindow
                || window == GuidedFeatureTestsWindow
                || window == WelcomeWindow
                || window == HelpWindow)
                continue;

            window.DrawMenuItemToggle();
        }

        CustomComponents.SeparatorLine();

        LayoutHandling.DrawMainMenuItems();
    }
}