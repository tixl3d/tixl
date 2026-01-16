#nullable enable
using ImGuiNET;
using T3.Editor.Gui.Input;
using T3.Editor.Gui.Styling;

namespace T3.Editor.Gui.Hub;

/// <summary>
/// Helper layout to structure a content area with a header, a comment and some tool icons 
/// </summary>
internal static class ContentPanel
{
    internal static void Begin(string title, string? subtitle = null, Action? drawTools = null, float height = 0)
    {
        ImGui.BeginChild(title, new Vector2(0, height), false, ImGuiWindowFlags.NoBackground);
        ImGui.Indent(10 * T3Ui.UiScaleFactor);
        FormInputs.AddVerticalSpace();
        FormInputs.AddSectionHeader(title);
        
        if (drawTools != null)
        {
            ImGui.SameLine();
            drawTools?.Invoke();
        }
        
        if (!string.IsNullOrEmpty(subtitle))
        {
            ImGui.PushStyleColor(ImGuiCol.Text, UiColors.TextMuted.Rgba);
            ImGui.PushFont(Fonts.FontSmall);
            ImGui.TextUnformatted(subtitle);
            ImGui.PopFont();
            ImGui.PopStyleColor();
            FormInputs.AddVerticalSpace();
        }
        
        ImGui.BeginChild(title, Vector2.Zero,false, ImGuiWindowFlags.NoBackground);
    }

    internal static void End()
    {
        ImGui.EndChild();
        ImGui.Unindent(10 * T3Ui.UiScaleFactor);
        
        ImGui.EndChild();
    }
}