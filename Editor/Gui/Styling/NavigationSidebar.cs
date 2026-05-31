#nullable enable
using ImGuiNET;
using T3.Core.DataTypes.Vector;

namespace T3.Editor.Gui.Styling;

/// <summary>
/// Shared left-hand navigation column used by the Settings and Welcome windows. Keeps their sidebars
/// visually identical — background, selection highlight, hover, text alignment — so the two don't drift
/// apart. Items support an optional leading <see cref="Icon"/> and a trailing checkmark.
/// </summary>
internal static class NavigationSidebar
{
    internal const float DefaultWidth = 130f;

    internal static void BeginColumn(string id, float width = DefaultWidth)
    {
        ImGui.BeginChild(id, new Vector2(width * T3Ui.UiScaleFactor, 0),
                         ImGuiChildFlags.Borders,
                         ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoBackground);
    }

    internal static void EndColumn()
    {
        ImGui.EndChild();
    }

    /// <summary>Draws one navigation entry filling the column width. Returns true when clicked.</summary>
    internal static bool Item(string label, bool isSelected, Icon icon = Icon.None, bool showCheckmark = false)
    {
        var scale = T3Ui.UiScaleFactor;
        var height = ImGui.GetFrameHeight();
        var width = ImGui.GetContentRegionAvail().X;

        // Colors mirror FormInputs.DrawSelectButton so this matches the existing Settings sidebar.
        var normal = isSelected ? UiColors.BackgroundActive.Fade(0.7f) : UiColors.BackgroundButton;
        var hovered = isSelected ? UiColors.BackgroundActive : UiColors.BackgroundButton.Fade(0.7f);
        var activeCol = isSelected ? UiColors.BackgroundActive.Fade(0.7f) : UiColors.BackgroundButton.Fade(0.7f);

        ImGui.PushStyleColor(ImGuiCol.Button, normal.Rgba);
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, hovered.Rgba);
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, activeCol.Rgba);
        var clicked = ImGui.Button($"##{label}", new Vector2(width, height));
        ImGui.PopStyleColor(3);

        var min = ImGui.GetItemRectMin();
        var max = ImGui.GetItemRectMax();
        var dl = ImGui.GetWindowDrawList();
        var midY = (min.Y + max.Y) * 0.5f;
        var iconHalf = Fonts.FontNormal.FontSize * 0.5f;

        var cursorX = min.X + 8 * scale;
        if (icon != Icon.None)
        {
            Icons.DrawIconAtScreenPosition(icon, new Vector2(cursorX, midY - iconHalf), dl, UiColors.Text);
            cursorX += 20 * scale;
        }

        var textSize = ImGui.CalcTextSize(label);
        dl.AddText(new Vector2(cursorX, midY - textSize.Y * 0.5f), ImGui.GetColorU32(UiColors.Text.Rgba), label);

        if (showCheckmark)
            Icons.DrawIconAtScreenPosition(Icon.Checkmark, new Vector2(max.X - 20 * scale, midY - iconHalf), dl, UiColors.TextMuted);

        return clicked;
    }
}
