#nullable enable
using ImGuiNET;
using T3.Editor.Gui.Interaction;
using T3.Editor.Gui.Styling;
using T3.Editor.UiModel.InputsAndTypes;
using Vector2 = System.Numerics.Vector2;

namespace T3.Editor.Gui.Input;

/// <summary>
/// Compact single-column form inputs for narrow side panels: a small (<see cref="Fonts.FontSmall"/>) label
/// with a right-aligned help icon, above a full-width input. Deliberately lighter than <see cref="FormInputs"/>
/// (which carries a heavy style stack for the parameter window). Used by the output setup properties cards.
/// </summary>
internal static class FormInputsNarrow
{
    /// <summary>Card frame with a title and a close (×) button. Returns true if × was clicked.</summary>
    public static bool DrawCardHeader(string title)
    {
        ImGui.PushFont(Fonts.FontBold);
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted(title);
        ImGui.PopFont();

        CustomComponents.RightAlign(ImGui.GetFrameHeight());
        var closed = CustomComponents.IconButton(Icon.Close, Vector2.Zero);
        return closed;
    }

    /// <summary>A checkbox as a rounded TiXL-style button with a checkmark + a clickable label (whole element hits).</summary>
    public static bool DrawCheckbox(string label, ref bool value, string? tooltip = null)
    {
        var scale = T3Ui.UiScaleFactor;
        var dl = ImGui.GetWindowDrawList();
        var pos = ImGui.GetCursorScreenPos();
        var boxSize = ImGui.GetFrameHeight();
        var labelSize = ImGui.CalcTextSize(label);
        var gap = 6 * scale;

        var clicked = ImGui.InvisibleButton("##" + label, new Vector2(boxSize + gap + labelSize.X, boxSize));
        if (clicked)
            value = !value;

        var boxMax = pos + new Vector2(boxSize, boxSize);
        var bg = value
                     ? UiColors.StatusActivated
                     : (ImGui.IsItemHovered() ? UiColors.BackgroundHover : UiColors.BackgroundButton);
        dl.AddRectFilled(pos, boxMax, bg, 4 * scale);
        if (value)
            Icons.DrawIconAtScreenPosition(Icon.Checkmark, pos + new Vector2(boxSize * 0.15f), new Vector2(boxSize * 0.7f), dl, UiColors.ForegroundFull);

        dl.AddText(new Vector2(boxMax.X + gap, pos.Y + (boxSize - labelSize.Y) * 0.5f), UiColors.Text, label);

        DrawHelpIcon(tooltip);
        return clicked;
    }

    public static bool DrawString(string label, ref string value, string hint = "", string? tooltip = null, bool readOnly = false)
    {
        DrawLabel(label, tooltip);
        ImGui.SetNextItemWidth(-RightMargin * T3Ui.UiScaleFactor);
        var flags = readOnly ? ImGuiInputTextFlags.ReadOnly : ImGuiInputTextFlags.None;
        return ImGui.InputTextWithHint("##" + label, hint, ref value, 256, flags);
    }

    /// <summary>Row of float fields using the editor's custom drag-edit (range indicator, consistent style).
    /// Returns the combined edit state — apply the value on <see cref="InputEditStateFlags.Modified"/>, persist
    /// on <see cref="InputEditStateFlags.Finished"/>.</summary>
    /// <param name="reserveRight">Extra unscaled width kept free at the right, so the caller can put a trailing
    /// button on the same line.</param>
    public static InputEditStateFlags DrawFloats(string label, System.Span<float> values, string? tooltip = null, float speed = 0.01f,
                                                 bool readOnly = false, string format = "{0:0.###}", float reserveRight = 0)
    {
        DrawLabel(label, tooltip);
        ImGui.PushID(label);
        var result = InputEditStateFlags.Nothing;
        var spacing = 4 * T3Ui.UiScaleFactor;
        var size = new Vector2((ImGui.GetContentRegionAvail().X - (RightMargin + reserveRight) * T3Ui.UiScaleFactor - spacing * (values.Length - 1)) / values.Length,
                               ImGui.GetFrameHeight());
        if (readOnly)
            ImGui.BeginDisabled();

        for (var i = 0; i < values.Length; i++)
        {
            if (i > 0)
                ImGui.SameLine(0, spacing);

            ImGui.PushID(i);
            var v = values[i];
            result |= SingleValueEdit.Draw(ref v, size, scale: speed, format: format);
            values[i] = v;
            ImGui.PopID();
        }

        if (readOnly)
            ImGui.EndDisabled();

        ImGui.PopID();
        return result;
    }

    public static InputEditStateFlags DrawInts(string label, System.Span<int> values, string? tooltip = null, bool readOnly = false)
    {
        DrawLabel(label, tooltip);
        ImGui.PushID(label);
        var result = InputEditStateFlags.Nothing;
        var spacing = 4 * T3Ui.UiScaleFactor;
        var size = new Vector2((ImGui.GetContentRegionAvail().X - RightMargin * T3Ui.UiScaleFactor - spacing * (values.Length - 1)) / values.Length, ImGui.GetFrameHeight());
        if (readOnly)
            ImGui.BeginDisabled();

        for (var i = 0; i < values.Length; i++)
        {
            if (i > 0)
                ImGui.SameLine(0, spacing);

            ImGui.PushID(i);
            var v = values[i];
            result |= SingleValueEdit.Draw(ref v, size);
            values[i] = v;
            ImGui.PopID();
        }

        if (readOnly)
            ImGui.EndDisabled();

        ImGui.PopID();
        return result;
    }

    /// <summary>A read-only relationship row (drag handle + name + ×), muted. For the "Sending to…" lists.</summary>
    public static void DrawListItem(string name)
    {
        ImGui.AlignTextToFramePadding();
        ImGui.PushStyleColor(ImGuiCol.Text, UiColors.TextMuted.Fade(0.6f).Rgba);
        DrawGlyph(Icon.DragIndicator);
        ImGui.SameLine(0, 5 * T3Ui.UiScaleFactor);
        DrawSmallText(name);
        ImGui.PopStyleColor();
    }

    public static void DrawLabel(string label, string? tooltip = null)
    {
        FormInputs.AddVerticalSpace(3);
        ImGui.AlignTextToFramePadding();
        DrawSmallText(label);
        DrawHelpIcon(tooltip);
    }

    private static void DrawSmallText(string text)
    {
        ImGui.PushFont(Fonts.FontSmall);
        ImGui.PushStyleColor(ImGuiCol.Text, UiColors.TextMuted.Rgba);
        ImGui.TextUnformatted(text);
        ImGui.PopStyleColor();
        ImGui.PopFont();
    }

    private static void DrawHelpIcon(string? tooltip)
    {
        if (tooltip == null)
            return;

        ImGui.SameLine();
        CustomComponents.RightAlign(ImGui.GetFrameHeight());
        ImGui.PushStyleColor(ImGuiCol.Text, UiColors.TextMuted.Fade(0.5f).Rgba);
        DrawGlyph(Icon.Help);
        ImGui.PopStyleColor();
        if (ImGui.IsItemHovered())
            CustomComponents.TooltipForLastItem(tooltip);
    }

    private static void DrawGlyph(Icon icon)
    {
        ImGui.PushFont(Icons.IconFont);
        ImGui.TextUnformatted(((char)icon).ToString());
        ImGui.PopFont();
    }

    // Right inset (unscaled px) so inputs don't touch the sidebar edge; the caller indents the left by the same.
    private const float RightMargin = 6;
}
