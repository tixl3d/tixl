#nullable enable
using ImGuiNET;
using T3.Core.DataTypes.Vector;
using T3.Editor.Gui.Input;

namespace T3.Editor.Gui.Styling;

/// <summary>
/// Shared structure for settings panels and tool windows: spacing tokens, section cards, aligned
/// value rows and status pills, so every panel reads as one app. All values are unscaled; the helpers
/// apply <see cref="T3Ui.UiScaleFactor"/> themselves.
/// </summary>
internal static class PanelLayout
{
    internal const float SpaceXS = 4;
    internal const float SpaceS = 8;
    internal const float SpaceM = 12;
    internal const float SpaceL = 16;
    internal const float SpaceXL = 24;

    internal static void Space(float unscaledSize)
    {
        FormInputs.AddVerticalSpace(unscaledSize);
    }

    /// <summary>
    /// Opens a card with a title and optional helper text. Inputs inside take the card's padded width.
    /// Must be closed with <see cref="EndSection"/>.
    /// </summary>
    internal static void BeginSection(string title, string? help = null)
    {
        var scale = T3Ui.UiScaleFactor;

        ImGui.PushID(title);
        ImGui.PushStyleColor(ImGuiCol.ChildBg, UiColors.BackgroundButton.Rgba);
        ImGui.PushStyleColor(ImGuiCol.Border, UiColors.ForegroundFull.Fade(0.06f).Rgba);
        ImGui.PushStyleVar(ImGuiStyleVar.ChildRounding, 6 * scale);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(SpaceM, SpaceM) * scale);
        ImGui.BeginChild("##section", Vector2.Zero,
                         ImGuiChildFlags.AutoResizeY | ImGuiChildFlags.AlwaysUseWindowPadding | ImGuiChildFlags.Borders,
                         ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse);
        ImGui.PopStyleVar(2);
        ImGui.PopStyleColor(2);

        CustomComponents.StylizedText(title, Fonts.FontBold, UiColors.Text);
        if (!string.IsNullOrEmpty(help))
        {
            Space(SpaceXS);
            CustomComponents.HelpText(help);
        }

        Space(SpaceS);
    }

    /// <summary>
    /// A card whose body folds away under its title. Returns whether the body is open; call
    /// <see cref="EndSection"/> only in that case (a closed card has already closed itself).
    /// </summary>
    internal static bool BeginCollapsibleSection(string title, string? help = null, bool defaultOpen = false)
    {
        var scale = T3Ui.UiScaleFactor;

        ImGui.PushID(title);
        var storage = ImGui.GetStateStorage();
        var openId = ImGui.GetID("##open");
        var isOpen = storage.GetInt(openId, defaultOpen ? 1 : 0) != 0;

        ImGui.PushStyleColor(ImGuiCol.ChildBg, UiColors.BackgroundButton.Rgba);
        ImGui.PushStyleColor(ImGuiCol.Border, UiColors.ForegroundFull.Fade(0.06f).Rgba);
        ImGui.PushStyleVar(ImGuiStyleVar.ChildRounding, 6 * scale);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(SpaceM, SpaceM) * scale);
        ImGui.BeginChild("##section", Vector2.Zero,
                         ImGuiChildFlags.AutoResizeY | ImGuiChildFlags.AlwaysUseWindowPadding | ImGuiChildFlags.Borders,
                         ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse);
        ImGui.PopStyleVar(2);
        ImGui.PopStyleColor(2);

        var dl = ImGui.GetWindowDrawList();
        var lineHeight = ImGui.GetTextLineHeight();
        var arrowSize = lineHeight * 0.4f;
        var titleMin = ImGui.GetCursorScreenPos();
        var arrowCenter = new Vector2(titleMin.X + arrowSize, titleMin.Y + lineHeight * 0.5f);
        if (isOpen)
        {
            dl.AddTriangleFilled(arrowCenter + new Vector2(-arrowSize, -arrowSize * 0.5f),
                                 arrowCenter + new Vector2(arrowSize, -arrowSize * 0.5f),
                                 arrowCenter + new Vector2(0, arrowSize * 0.7f),
                                 UiColors.TextMuted);
        }
        else
        {
            dl.AddTriangleFilled(arrowCenter + new Vector2(-arrowSize * 0.5f, -arrowSize),
                                 arrowCenter + new Vector2(-arrowSize * 0.5f, arrowSize),
                                 arrowCenter + new Vector2(arrowSize * 0.7f, 0),
                                 UiColors.TextMuted);
        }

        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + arrowSize * 3);
        CustomComponents.StylizedText(title, Fonts.FontBold, UiColors.Text);
        var titleHovered = ImGui.IsItemHovered();
        ImGui.SetCursorScreenPos(titleMin);
        ImGui.InvisibleButton("##toggle", new Vector2(ImGui.GetContentRegionAvail().X, lineHeight));
        if (ImGui.IsItemClicked())
        {
            isOpen = !isOpen;
            storage.SetInt(openId, isOpen ? 1 : 0);
        }

        if (titleHovered || ImGui.IsItemHovered())
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);

        if (!isOpen)
        {
            ImGui.EndChild();
            ImGui.PopID();
            Space(SpaceL);
            return false;
        }

        if (!string.IsNullOrEmpty(help))
        {
            Space(SpaceXS);
            CustomComponents.HelpText(help);
        }

        Space(SpaceS);
        return true;
    }

    internal static void EndSection()
    {
        ImGui.EndChild();
        ImGui.PopID();
        Space(SpaceL);
    }

    /// <summary>A read-only "label: value" row in the same label column the form inputs use.</summary>
    internal static void ValueRow(string label, string value, Color? valueColor = null)
    {
        FormInputs.DrawInputLabel(label);
        ImGui.PushStyleColor(ImGuiCol.Text, (valueColor ?? UiColors.Text).Rgba);
        ImGui.TextWrapped(value);
        ImGui.PopStyleColor();
    }

    /// <summary>A button for actions that end or remove something; set apart from the primary action by its color.</summary>
    internal static bool DestructiveButton(string label, float unscaledWidth = 0)
    {
        ImGui.PushStyleColor(ImGuiCol.Text, UiColors.StatusAttention.Rgba);
        var clicked = ImGui.Button(label, new Vector2(unscaledWidth * T3Ui.UiScaleFactor, 0));
        ImGui.PopStyleColor();
        return clicked;
    }

    /// <summary>A small rounded status label; advances the cursor like a regular item.</summary>
    internal static void StatusPill(string text, Color color)
    {
        var dl = ImGui.GetWindowDrawList();
        var padding = new Vector2(SpaceS, SpaceXS) * T3Ui.UiScaleFactor;
        var size = ImGui.CalcTextSize(text) + padding * 2;
        var pos = ImGui.GetCursorScreenPos();
        dl.AddRectFilled(pos, pos + size, color.Fade(0.18f), size.Y * 0.5f);
        dl.AddText(pos + padding, color, text);
        ImGui.Dummy(size);
    }
}
