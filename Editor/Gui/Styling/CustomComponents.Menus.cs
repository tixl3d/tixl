using ImGuiNET;
using T3.Core.DataTypes.Vector;
using T3.Core.Utils;
using T3.Editor.Gui.Input;
using T3.Editor.Gui.UiHelpers;

namespace T3.Editor.Gui.Styling;

internal static partial class CustomComponents
{
    private static Action _cachedDrawMenuItems;

    /// <summary>Screen position of the right-click that opened the most recent scroll-canvas context menu.</summary>
    internal static Vector2 ScreenPosOnOpeningContextMenu { get; private set; }

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
        // Menu rows measure themselves with GetFrameHeight, so a caller that lowered FramePadding for its
        // own content (e.g. the asset library's tree rows) would otherwise squash the whole menu.
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, T3Style.DefaultFramePadding);

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

        ImGui.PopStyleVar(4);
    }

    /// <summary>
    /// Small muted label to group menu items into sections, aligned with the icon column.
    /// </summary>
    public static void DrawMenuGroupLabel(string label)
    {
        FormInputs.AddVerticalSpace(2);
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + ImGui.GetStyle().ItemSpacing.X + Icons.FontSize * 1.4f);
        ImGui.PushFont(Fonts.FontSmall);
        ImGui.PushStyleColor(ImGuiCol.Text, UiColors.TextMuted.Rgba);
        ImGui.TextUnformatted(label);
        ImGui.PopStyleColor();
        ImGui.PopFont();
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
    /// right-aligned keyboard shortcut. <paramref name="state"/> controls the label brightness:
    /// <see cref="ButtonStates.Emphasized"/> reads as the primary/active row, <see cref="ButtonStates.Default"/>
    /// as a muted secondary row. A disabled item always renders greyed regardless of <paramref name="state"/>.
    /// </summary>
    public static bool DrawMenuItem(int id, Icon icon, string label, string keyboardShortCut = null, bool isChecked = false, bool isEnabled = true,
                                    bool reserveCheckmarkColumn = true, bool reserveIconColumn = true,
                                    ButtonStates state = ButtonStates.Default)
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

        var min = ImGui.GetItemRectMin();
        var max = ImGui.GetItemRectMax();
        var drawList = ImGui.GetWindowDrawList();

        if (isEnabled && ImGui.IsItemHovered())
        {
            drawList.AddRectFilled(min, max, UiColors.BackgroundActive.Fade(0.25f), 4);
        }

        var textColor = GetMenuTextColor(isEnabled, state);

        if (isChecked)
        {
            Icons.DrawIconAtScreenPosition(Icon.Checkmark,
                                           (min + new Vector2(imguiPadding.X,
                                                              h / 2 - Icons.FontSize / 2)).Floor(),
                                           drawList, textColor);
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
                         textColor,
                         label);

        if (!string.IsNullOrEmpty(keyboardShortCut))
        {
            drawList.AddText(min
                             + new Vector2(windowWidth - shortCutWidth,
                                           h / 2 - textHeight / 2),
                             UiColors.TextMuted.Fade(isEnabled ? 0.5f : 0.2f),
                             keyboardShortCut);
        }

        if (clicked)
        {
            ImGui.CloseCurrentPopup();
        }

        return clicked;
    }

    /// <summary>
    /// A submenu header styled to match <see cref="DrawMenuItem"/>: it reserves the same left padding and
    /// draws a right chevron instead of ImGui's default triangle. Returns true while the submenu is open —
    /// pair it with <see cref="ImGui.EndMenu"/> exactly like <see cref="ImGui.BeginMenu(string)"/>.
    /// </summary>
    public static bool DrawSubMenu(int id, string label, bool isEnabled = true, bool reserveCheckmarkColumn = true)
    {
        var iconSlotWidth = Icons.FontSize * 1.4f;
        var imguiPadding = ImGui.GetStyle().ItemSpacing;
        // Match DrawMenuItem's label start (checkmark column reserved, no icon column) so headers align with rows.
        var leftPaddingText = imguiPadding.X + (reserveCheckmarkColumn ? iconSlotWidth : 0f);

        // ImGui's menu selectable bleeds into the window padding, so GetItemRectMin sits a few px left of a
        // normal row. Anchor instead to the row's start cursor — the same reference DrawMenuItem uses — so
        // headers line up with rows exactly. Capture before BeginMenu, which switches to the child window when open.
        var rowStart = ImGui.GetCursorScreenPos();
        var columnWidth = ImGui.GetColumnWidth();
        var parentDrawList = ImGui.GetWindowDrawList();
        var h = ImGui.GetFrameHeight();

        // Hide ImGui's own label, arrow and selectable highlight; we overdraw to match DrawMenuItem.
        // No PushID here: when the submenu opens, BeginMenu switches to the child window before we could
        // pop, so a PushID/PopID pair would straddle two per-window id stacks ("Missing PopID()"). The
        // label (plus ##id for uniqueness) is the menu's id; the style-color stack is global, so its pops are safe.
        var transparent = UiColors.ForegroundFull.Fade(0f).Rgba;
        ImGui.PushStyleColor(ImGuiCol.Text, transparent);
        ImGui.PushStyleColor(ImGuiCol.Header, transparent);
        ImGui.PushStyleColor(ImGuiCol.HeaderHovered, transparent);
        ImGui.PushStyleColor(ImGuiCol.HeaderActive, transparent);
        var isOpen = ImGui.BeginMenu($"{label}##{id}", isEnabled);
        ImGui.PopStyleColor(4);

        if (isEnabled && (isOpen || ImGui.IsItemHovered()))
        {
            parentDrawList.AddRectFilled(rowStart, rowStart + new Vector2(columnWidth, h), UiColors.BackgroundActive.Fade(0.25f), 4);
        }

        var textColor = GetMenuTextColor(isEnabled, ButtonStates.Default);
        var textHeight = ImGui.GetFontSize();
        parentDrawList.AddText(rowStart + new Vector2(leftPaddingText, h / 2 - textHeight / 2), textColor, label);

        Icons.DrawIconAtScreenPosition(Icon.ChevronRight,
                                       (rowStart + new Vector2(columnWidth - iconSlotWidth, h / 2 - Icons.FontSize / 2)).Floor(),
                                       parentDrawList, textColor);

        return isOpen;
    }

    // Menu-row label color. Distinct from GetStateColor (tool-icon tints) so the menu look can evolve on its own.
    private static Color GetMenuTextColor(bool isEnabled, ButtonStates state)
    {
        if (!isEnabled)
            return UiColors.Text.Fade(0.15f);

        return state == ButtonStates.Emphasized
                   ? UiColors.ForegroundFull
                   : UiColors.Text.Fade(0.7f);
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
        ImGui.PushStyleVar(ImGuiStyleVar.PopupRounding, 6);
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, T3Style.DefaultFramePadding);

        if (ImGui.BeginPopupContextWindow("windows_context_menu"))
        {
            ScreenPosOnOpeningContextMenu = ImGui.GetMousePosOnOpeningCurrentPopup();
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

        ImGui.PopStyleVar(4);
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