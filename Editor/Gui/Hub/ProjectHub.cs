using ImGuiNET;
using T3.Editor.Gui.Window;

namespace T3.Editor.Gui.Hub;

internal static class ProjectHub
{
    public static void Draw(GraphWindow window, bool reinitView)
    {
        ProjectsPanel.Draw(window);
        ImGui.Separator();
        
        SkillQuestPanel.Draw(window, reinitView);
    }
}