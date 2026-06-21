#nullable enable
using ImGuiNET;
using T3.Core.DataTypes.Vector;
using T3.Editor.Gui.Input;

namespace T3.Editor.Gui.Styling;

/// <summary>
/// Shared "sidebar + content panel" layout used by the Settings, Welcome, Project Settings and Render
/// windows so they stay visually identical. The window background shows through behind the (unfilled)
/// sidebar; the selected entry and the content panel both take <see cref="UiColors.WindowBackground"/>,
/// so the active tab reads as one connected surface that floats above the darker chrome.
///
/// Usage:
/// <code>
/// NavigationSidebar.BeginLayout();
/// NavigationSidebar.BeginColumn("id");
///   if (NavigationSidebar.Item("Section", active == X)) active = X;
/// NavigationSidebar.EndColumn();
/// NavigationSidebar.BeginContentPanel("Section");
///   // section content
/// NavigationSidebar.EndContentPanel();
/// </code>
/// </summary>
internal static class NavigationSidebar
{
    internal const float DefaultWidth = 130f;

    /// <summary>
    /// Paints the dark window background across the whole content area. Call once at the top of the
    /// window's draw method (before the sidebar / panel children) so the unfilled sidebar and the
    /// margins around the content panel read as the window background.
    /// </summary>
    internal static void BeginLayout()
    {
        var dl = ImGui.GetWindowDrawList();
        dl.AddRectFilled(ImGui.GetWindowPos(), ImGui.GetWindowPos() + ImGui.GetWindowSize(), UiColors.BackgroundGaps);
    }

    internal static void BeginColumn(string id, float width = DefaultWidth)
    {
        ImGui.BeginChild(id, new Vector2(width * T3Ui.UiScaleFactor, 0),
                         ImGuiChildFlags.None,
                         ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoBackground);
        FormInputs.AddVerticalSpace();
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

        var clicked = ImGui.InvisibleButton($"##{label}", new Vector2(width, height));
        var isHovered = ImGui.IsItemHovered();

        var min = ImGui.GetItemRectMin();
        var max = ImGui.GetItemRectMax();
        var dl = ImGui.GetWindowDrawList();

        // Rounded only on the left so the right edge butts flush against the content panel — the selected
        // entry and the panel become one continuous surface.
        if (isSelected)
            dl.AddRectFilled(min, max, UiColors.WindowBackground, 5 * scale, ImDrawFlags.RoundCornersLeft);
        else if (isHovered)
            dl.AddRectFilled(min, max, UiColors.WindowBackground.Fade(0.4f), 5 * scale, ImDrawFlags.RoundCornersLeft);

        var textColor = isSelected ? UiColors.Text : UiColors.TextMuted;
        var font = isSelected ? Fonts.FontBold : Fonts.FontNormal;
        var midY = (min.Y + max.Y) * 0.5f;
        var iconHalf = Fonts.FontNormal.FontSize * 0.5f;

        var cursorX = min.X + 10 * scale;
        if (icon != Icon.None)
        {
            Icons.DrawIconAtScreenPosition(icon, new Vector2(cursorX, midY - iconHalf), dl, textColor);
            cursorX += 20 * scale;
        }

        ImGui.PushFont(font);
        var textSize = ImGui.CalcTextSize(label);
        dl.AddText(new Vector2(cursorX, midY - textSize.Y * 0.5f), ImGui.GetColorU32(textColor.Rgba), label);
        ImGui.PopFont();

        if (showCheckmark)
            Icons.DrawIconAtScreenPosition(Icon.Checkmark, new Vector2(max.X - 20 * scale, midY - iconHalf), dl, UiColors.TextMuted);

        return clicked;
    }

    /// <summary>
    /// Begins the content panel to the right of the sidebar: a rounded <see cref="UiColors.WindowBackground"/>
    /// surface with a margin to the window border, inner padding, and a muted section header.
    /// </summary>
    // Carries the per-window key from BeginContentPanel to EndContentPanel so the drag-scroll state stays
    // distinct when several sidebar windows are open at once. Begin/End are paired and drawn sequentially.
    private static object? _contentPanelScrollKey;

    /// <param name="drawHeaderTools">Optional right-aligned content drawn on the header line (e.g. a help
    /// button). The callback is responsible for its own right-alignment (e.g. <c>CustomComponents.RightAlign</c>).</param>
    internal static void BeginContentPanel(string title, object? scrollKey = null, Action? drawHeaderTools = null)
    {
        _contentPanelScrollKey = scrollKey;

        var scale = T3Ui.UiScaleFactor;
        var margin = 3 * scale;

        // No gap on the left: the panel sits flush against the selected sidebar entry.
        ImGui.SameLine(0, 0);

        ImGui.PushStyleColor(ImGuiCol.ChildBg, UiColors.WindowBackground.Rgba);
        ImGui.PushStyleVar(ImGuiStyleVar.ChildRounding, 8 * scale);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(16, 12) * scale);
        ImGui.BeginChild("##contentPanel", new Vector2(-margin, -margin),
                         ImGuiChildFlags.AlwaysUseWindowPadding | ImGuiChildFlags.Borders);

        if (!string.IsNullOrEmpty(title))
        {
            ImGui.PushFont(Fonts.FontLarge);
            ImGui.PushStyleColor(ImGuiCol.Text, UiColors.TextMuted.Rgba);
            ImGui.TextUnformatted(title);
            ImGui.PopStyleColor();
            ImGui.PopFont();

            drawHeaderTools?.Invoke();

            FormInputs.AddVerticalSpace(8);
        }
    }

    internal static void EndContentPanel()
    {
        if (_contentPanelScrollKey != null)
        {
            CustomComponents.HandleDragScrolling(_contentPanelScrollKey);
            _contentPanelScrollKey = null;
        }

        ImGui.EndChild();
        ImGui.PopStyleVar(2);
        ImGui.PopStyleColor();
    }
}
