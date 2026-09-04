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
        var indent = 10 * T3Ui.UiScaleFactor;
        ImGui.BeginChild(title, new Vector2(0, height), ImGuiChildFlags.None, ImGuiWindowFlags.NoBackground);
        ImGui.Indent(indent);
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

        // Inner child width = parent_remaining - indent → leaves symmetric
        // padding on the right matching the left Indent.
        ImGui.BeginChild(title, new Vector2(-indent, 0), ImGuiChildFlags.None, ImGuiWindowFlags.NoBackground);
    }

    internal static void End()
    {
        ImGui.EndChild();
        ImGui.Unindent(10 * T3Ui.UiScaleFactor);

        ImGui.EndChild();
    }
}