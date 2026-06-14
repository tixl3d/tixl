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
        ImGui.PushStyleVar(ImGuiStyleVar.PopupRounding, 6);

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

        ImGui.PopStyleVar(3);
    }

    /// <summary>
    /// Small muted label to group menu items into sections, aligned with the icon column.
    /// </summary>
    public static void DrawMenuGroupLabel(string label)
    {
        FormInputs.AddVerticalSpace(2);
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + ImGui.GetStyle().ItemSpacing.X + Icons.FontSize * 1.4f);
        HintLabel(label);
    }

    public static bool DrawMenuItem(int id, string label, ref bool isChecked, string keyboardShortCut = null, bool reserveIconColumn = true)
    {
        var clicked = DrawMenuItem(id, label, keyboardShortCut, isChecked, reserveIconColumn: reserveIconColumn);
        if (clicked)
        {
            isChecked = !isChecked;
        }

        return clicked;
    }

    public static bool DrawMenuItem(int id, string label, string keyboardShortCut = null, bool isChecked = false, bool isEnabled = true,
                                    bool reserveIconColumn = true)
    {
        return DrawMenuItem(id, Icon.None, label, keyboardShortCut, isChecked, isEnabled, reserveIconColumn: reserveIconColumn);
    }

    /// <summary>
    /// Menu item with the checkbox slot on the left, an optional icon, label and an optional
    /// right-aligned keyboard shortcut.
    /// </summary>
    public static bool DrawMenuItem(int id, Icon icon, string label, string keyboardShortCut = null, bool isChecked = false, bool isEnabled = true,
                                    bool reserveCheckmarkColumn = true, bool reserveIconColumn = true)
    {
        var h = ImGui.GetFrameHeight();
        var imguiPadding = ImGui.GetStyle().ItemSpacing;

        var shortCutWidth = string.IsNullOrEmpty(keyboardShortCut) ? 0 : ImGui.CalcTextSize(keyboardShortCut).X;
        var labelWidth = ImGui.CalcTextSize(label).X;

        var paddingFactor = 1.4f;
        var iconSlotWidth = Icons.FontSize * paddingFactor;
        // Each column is reserved only when the menu uses it: the checkmark column for menus with
        // toggles, the icon column for menus with icons. A menu without either sits flush left.
        var checkmarkSlot = reserveCheckmarkColumn ? iconSlotWidth : 0f;
        var leftPaddingIcon = imguiPadding.X + checkmarkSlot;
        var hasIcon = icon != Icon.None;
        // Reserve the icon column so icon-less items (e.g. Rename) stay aligned with icon ones.
        var leftPaddingText = leftPaddingIcon + (reserveIconColumn ? iconSlotWidth : 0f);

        var width = leftPaddingText + labelWidth + imguiPadding.X * 2;
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
        // The label seeds the imgui id too: int ids alone can collapse to 0 after hot reload
        // (initializers of newly added static fields don't run), which made all items conflict.
        var clicked = ImGui.InvisibleButton(label, new Vector2(width, h)) && isEnabled;
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

        if (hasIcon)
        {
            // Emphasized when actionable, Default (faded) when disabled.
            var iconColor = GetStateColor(isEnabled ? ButtonStates.Emphasized : ButtonStates.Default);
            Icons.DrawIconAtScreenPosition(icon,
                                           (min + new Vector2(leftPaddingIcon,
                                                              h / 2 - Icons.FontSize / 2)).Floor(),
                                           drawList, iconColor);
        }

        var textHeight = ImGui.GetFontSize();
        drawList.AddText(min + new Vector2(leftPaddingText,
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