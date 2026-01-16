using ImGuiNET;
using T3.Core.Utils;
using T3.Editor.Gui.Input;
using T3.Editor.Gui.UiHelpers;

namespace T3.Editor.Gui.Styling;

internal static partial class CustomComponents
{
    private static Action _cachedDrawMenuItems;

    public static void ContextMenuForItem(Action drawMenuItems, string title = null, string id = "context_menu",
                                          ImGuiPopupFlags flags = ImGuiPopupFlags.MouseButtonRight)
    {
        // prevent the context menu from opening when dragging
        {
            var wasDraggingRight = ImGui.GetMouseDragDelta(ImGuiMouseButton.Right).Length() > UserSettings.Config.ClickThreshold;
            if (wasDraggingRight)
                return;
        }

        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(6, 6));
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(6, 6));

        if (ImGui.BeginPopupContextItem(id, flags))
        {
            FrameStats.Current.IsItemContextMenuOpen = true;
            if (title != null)
            {
                ImGui.PushFont(Fonts.FontSmall);
                ImGui.PushStyleColor(ImGuiCol.Text, UiColors.Gray.Rgba);
                ImGui.TextUnformatted(title);
                ImGui.PopStyleColor();
                ImGui.PopFont();
            }

            // Assign to static field to avoid closure allocations
            _cachedDrawMenuItems = drawMenuItems;
            _cachedDrawMenuItems.Invoke();

            ImGui.EndPopup();
        }

        ImGui.PopStyleVar(2);
    }

    public static bool DrawMenuItem(int id, string label, ref bool isChecked, string keyboardShortCut = null)
    {
        var clicked = DrawMenuItem(id, label, keyboardShortCut, isChecked);
        if (clicked)
        {
            isChecked = !isChecked;
        }

        return clicked;
    }

    public static bool DrawMenuItem(int id, string label, string keyboardShortCut = null, bool isChecked = false, bool isEnabled = true)
    {
        var h = ImGui.GetFrameHeight();
        var imguiPadding = ImGui.GetStyle().ItemSpacing;

        var shortCutWidth = string.IsNullOrEmpty(keyboardShortCut) ? 0 : ImGui.CalcTextSize(keyboardShortCut).X;
        var labelWidth = ImGui.CalcTextSize(label).X;

        var paddingFactor = 1.4f;
        var leftPadding = imguiPadding.X + Icons.FontSize * paddingFactor;

        var width = leftPadding + labelWidth + imguiPadding.X * 2;
        if (shortCutWidth > 0)
        {
            width += shortCutWidth + h;
        }

        var windowWidth = ImGui.GetColumnWidth();
        //var windowWidth = ImGui.GetWindowWidth();

        if (width < windowWidth)
            width = windowWidth;

        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, Vector2.Zero);
        ImGui.PushID(id);
        //var clicked = ImGui.Selectable(string.Empty);
        var clicked = ImGui.InvisibleButton(string.Empty, new Vector2(width, h)) && isEnabled;
        ImGui.PopID();
        ImGui.PopStyleVar();

        var fade = isEnabled ? 1 : 0.5f;

        var min = ImGui.GetItemRectMin();
        var max = ImGui.GetItemRectMax();
        var drawList = ImGui.GetWindowDrawList();

        if (isEnabled && ImGui.IsItemHovered())
        {
            drawList.AddRectFilled(min, max, UiColors.BackgroundActive.Fade(0.25f), 4);
        }

        if (isChecked)
        {
            //Icons.DrawIconCenter(Icon.Checkmark, UiColors.Text.Fade(fade), 0);
            Icons.DrawIconAtScreenPosition(Icon.Checkmark,
                                           (min + new Vector2(imguiPadding.X,
                                                              h / 2 - Icons.FontSize / 2)).Floor(),
                                           drawList, UiColors.Text);
        }

        var textHeight = ImGui.GetFontSize();
        drawList.AddText(min + new Vector2(leftPadding,
                                           h / 2 - textHeight / 2),
                         UiColors.Text.Fade(fade),
                         label);

        if (!string.IsNullOrEmpty(keyboardShortCut))
        {
            drawList.AddText(min
                             + new Vector2(windowWidth - shortCutWidth,
                                           h / 2 - textHeight / 2),
                             UiColors.TextMuted.Fade(fade),
                             keyboardShortCut);
        }

        if (clicked)
        {
            ImGui.CloseCurrentPopup();
        }

        return clicked;
    }

    public static void DrawContextMenuForScrollCanvas(Action drawMenuContent, ref bool contextMenuIsOpen)
    {
        if (!contextMenuIsOpen)
        {
            if (FrameStats.Current.IsItemContextMenuOpen)
                return;

            var wasDraggingRight = ImGui.GetMouseDragDelta(ImGuiMouseButton.Right).Length() > UserSettings.Config.ClickThreshold;
            if (wasDraggingRight)
                return;

            if (!ImGui.IsWindowHovered(ImGuiHoveredFlags.AllowWhenBlockedByPopup))
                return;
        }

        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(6, 6));
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(6, 6));

        if (ImGui.BeginPopupContextWindow("windows_context_menu"))
        {
            ImGui.GetMousePosOnOpeningCurrentPopup();
            contextMenuIsOpen = true;

            // Assign to static field to avoid closure allocations
            _cachedDrawMenuItems = drawMenuContent;
            _cachedDrawMenuItems.Invoke();
            //drawMenuContent.Invoke();
            ImGui.EndPopup();
        }
        else
        {
            contextMenuIsOpen = false;
        }

        ImGui.PopStyleVar(2);
    }

    public static bool DrawMultilineTextEdit(ref string value)
    {
        var lineCount = value.LineCount().Clamp(1, 30) + 1;
        var lineHeight = Fonts.Code.FontSize;
        ImGui.PushStyleColor(ImGuiCol.FrameBg, UiColors.BackgroundInputField.Rgba);
        var requestedContentHeight = lineCount * lineHeight;
        var clampedToWindowHeight = MathF.Min(requestedContentHeight, ImGui.GetWindowSize().Y * 0.5f);

        var changed = ImGui.InputTextMultiline("##textEdit", ref value, 16384, new Vector2(-10, clampedToWindowHeight));
        ImGui.PopStyleColor();
        FormInputs.AddVerticalSpace(3);
        return changed;
    }
}