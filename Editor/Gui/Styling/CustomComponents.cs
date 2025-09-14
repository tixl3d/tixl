using System.Text.RegularExpressions;
using ImGuiNET;
using T3.Core.DataTypes.Vector;
using T3.Core.Operator;
using T3.Core.SystemUi;
using T3.Core.Utils;
using T3.Editor.Gui.UiHelpers;
using T3.Editor.UiModel;
using T3.SystemUi;

namespace T3.Editor.Gui.Styling;

/// <summary>
/// A set of special wrappers for ImGui components.
/// Also, checkout the FormInputs class. 
/// </summary>
internal static class CustomComponents
{
    /// <summary>
    /// This needs to be called once a frame
    /// </summary>
    public static void BeginFrame()
    {
        var frameDuration = 1 / ImGui.GetIO().Framerate;
        if (FrameStats.Last.SomethingWithTooltipHovered)
        {
            _toolTipHoverDelay -= frameDuration;
            _timeSinceTooltipHover = 0;
        }
        else
        {
            _timeSinceTooltipHover += frameDuration;
            if (_timeSinceTooltipHover > 0.2)
                _toolTipHoverDelay = 0.6f;
        }
    }

    public static bool JogDial(string label, ref double delta, Vector2 size)
    {
        ImGui.PushStyleVar(ImGuiStyleVar.ButtonTextAlign, new Vector2(1, 0.5f));
        var isActive = ImGui.Button(label + "###dummy", size);
        ImGui.PopStyleVar();
        var io = ImGui.GetIO();
        if (ImGui.IsItemActive())
        {
            var center = (ImGui.GetItemRectMin() + ImGui.GetItemRectMax()) * 0.5f;
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
            ImGui.GetForegroundDrawList().AddCircle(center, 100, UiColors.Gray, 50);
            isActive = true;

            var pLast = io.MousePos - io.MouseDelta - center;
            var pNow = io.MousePos - center;
            var aLast = Math.Atan2(pLast.X, pLast.Y);
            var aNow = Math.Atan2(pNow.X, pNow.Y);
            delta = aLast - aNow;
            if (delta > 1.5)
            {
                delta -= 2 * Math.PI;
            }
            else if (delta < -1.5)
            {
                delta += 2 * Math.PI;
            }
        }

        return isActive;
    }

    /// <summary>Draw a splitter</summary>
    /// <remarks>
    /// Take from https://github.com/ocornut/imgui/issues/319#issuecomment-147364392
    /// </remarks>
    public static bool SplitFromBottom(ref float offsetFromBottom)
    {
        const float thickness = 3;
        var hasBeenDragged = false;

        var backupPos = ImGui.GetCursorPos();

        var size = ImGui.GetWindowContentRegionMax() - ImGui.GetWindowContentRegionMin();
        var contentMin = ImGui.GetWindowContentRegionMin() + ImGui.GetWindowPos();

        var pos = new Vector2(contentMin.X, contentMin.Y + size.Y - offsetFromBottom - thickness - 1);
        ImGui.SetCursorScreenPos(pos);

        ImGui.PushStyleColor(ImGuiCol.Button, UiColors.BackgroundGaps.Rgba);
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, UiColors.BackgroundActive.Rgba);
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, UiColors.BackgroundActive.Rgba);

        ImGui.Button("##Splitter", new Vector2(-1, thickness));

        ImGui.PopStyleColor(3);

        // Disabled for now, since Setting MouseCursor wasn't working reliably
        // if (ImGui.IsItemHovered() )
        // {
        //     //ImGui.SetMouseCursor(ImGuiMouseCursor.ResizeNS);
        // }

        if (ImGui.IsItemActive())
        {
            if (Math.Abs(ImGui.GetIO().MouseDelta.Y) > 0)
            {
                hasBeenDragged = true;
                offsetFromBottom =
                    (offsetFromBottom - ImGui.GetIO().MouseDelta.Y)
                   .Clamp(0, size.Y - thickness);
            }
        }

        ImGui.SetCursorPos(backupPos);
        return hasBeenDragged;
    }

    public static bool ToggleButton(string label, ref bool isSelected, Vector2 size, bool trigger = false)
    {
        var wasSelected = isSelected;
        var clicked = false;
        if (isSelected)
        {
            ImGui.PushStyleColor(ImGuiCol.Button, isSelected ? UiColors.BackgroundActive.Fade(0.7f).Rgba : UiColors.BackgroundButton.Rgba);
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, isSelected ? UiColors.BackgroundActive.Rgba : UiColors.BackgroundButton.Fade(0.7f).Rgba);
            ImGui.PushStyleColor(ImGuiCol.Text, UiColors.Text.Rgba);
            ImGui.PushStyleColor(ImGuiCol.ButtonActive, isSelected ? UiColors.BackgroundActive.Fade(0.7f).Rgba : UiColors.BackgroundButton.Fade(0.7f).Rgba);
        }

        if (ImGui.Button(label, size) || trigger)
        {
            isSelected = !isSelected;
            clicked = true;
        }

        if (wasSelected)
        {
            ImGui.PopStyleColor(4);
        }

        return clicked;
    }

    public static bool ToggleIconButton(Icon icon, string label, ref bool isSelected, Vector2 size, bool trigger = false)
    {
        var clicked = false;

        var stateTextColor = isSelected
                                 ? UiColors.StatusActivated.Rgba
                                 : UiColors.TextDisabled.Rgba;
        ImGui.PushStyleColor(ImGuiCol.Text, stateTextColor);

        var align = string.IsNullOrEmpty(label) ? new Vector2(0.1f, 0.5f) : new Vector2(0.5f, 0.5f);
        ImGui.PushStyleVar(ImGuiStyleVar.ButtonTextAlign, align);
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, Vector2.Zero);

        ImGui.PushFont(Icons.IconFont);

        if (ImGui.Button($"{(char)icon}##label", size))
        {
            isSelected = !isSelected;
            clicked = true;
        }

        ImGui.PopFont();

        ImGui.PopStyleVar(2);
        ImGui.PopStyleColor(1);

        return clicked;
    }

    public enum ButtonStates
    {
        Normal,
        Dimmed,
        Disabled,
        Activated,
        NeedsAttention,
    }

    public static bool FloatingIconButton(Icon icon, Vector2 size)
    {
        if (size == Vector2.Zero)
        {
            var h = ImGui.GetFrameHeight();
            size = new Vector2(h, h);
        }

        ImGui.PushFont(Icons.IconFont);
        ImGui.PushStyleVar(ImGuiStyleVar.ButtonTextAlign, new Vector2(0.5f, 0.5f));
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, Vector2.Zero);
        ImGui.PushStyleColor(ImGuiCol.Button, Color.Transparent.Rgba);
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, Color.Transparent.Rgba);
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, Color.Transparent.Rgba);

        var clicked = ImGui.Button(((char)icon).ToString(), size);

        ImGui.PopStyleColor(3);
        ImGui.PopStyleVar(2);
        ImGui.PopFont();
        return clicked;
    }

    public static bool StateButton(string label, ButtonStates state = ButtonStates.Normal)
    {
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, UiColors.BackgroundButtonActivated.Rgba);

        if (state != ButtonStates.Normal)
        {
            ImGui.PushStyleColor(ImGuiCol.Text, GetStateColor(state).Rgba);
            if (state == ButtonStates.Activated)
            {
                ImGui.PushStyleColor(ImGuiCol.Button, UiColors.BackgroundButtonActivated.Rgba);
                ImGui.PushStyleColor(ImGuiCol.ButtonHovered, UiColors.BackgroundButtonActivated.Fade(0.8f).Rgba);
            }
        }

        ImGui.AlignTextToFramePadding();
        var clicked = ImGui.Button(label);

        if (state != ButtonStates.Normal)
            ImGui.PopStyleColor();

        if (state == ButtonStates.Activated)
            ImGui.PopStyleColor(2);

        ImGui.PopStyleColor(1);
        return clicked;
    }

    public static bool IconButton(Icon icon, Vector2 size, ButtonStates state = ButtonStates.Normal, bool triggered = false)
    {
        if (size == Vector2.Zero)
        {
            var h = ImGui.GetFrameHeight();
            size = new Vector2(h, h);
        }

        ImGui.PushFont(Icons.IconFont);
        ImGui.PushStyleVar(ImGuiStyleVar.ButtonTextAlign, new Vector2(0.5f, 0.5f));
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, Vector2.Zero);

        ImGui.PushStyleColor(ImGuiCol.ButtonActive, UiColors.BackgroundButtonActivated.Rgba);

        if (state != ButtonStates.Normal)
        {
            ImGui.PushStyleColor(ImGuiCol.Text, GetStateColor(state).Rgba);
            if (state == ButtonStates.Activated)
            {
                ImGui.PushStyleColor(ImGuiCol.Button, UiColors.BackgroundButtonActivated.Rgba);
                ImGui.PushStyleColor(ImGuiCol.ButtonHovered, UiColors.BackgroundButtonActivated.Fade(0.8f).Rgba);
            }
        }

        var clicked = ImGui.Button("" + (char)icon, size) || triggered;

        if (state != ButtonStates.Normal)
            ImGui.PopStyleColor();

        if (state == ButtonStates.Activated)
            ImGui.PopStyleColor(2);

        ImGui.PopStyleColor(1);
        ImGui.PopStyleVar(2);
        ImGui.PopFont();
        return clicked;
    }

    public static bool IconButton(string id, Icon icon, float width, ImDrawFlags corners = ImDrawFlags.RoundCornersNone,
                                  ButtonStates state = ButtonStates.Normal, bool triggered = false)
    {
        var iconColor = GetStateColor(state);

        var size = new Vector2(width, ImGui.GetFrameHeight());
        if (width == 0)
            size.X = size.Y;

        triggered |= ImGui.InvisibleButton(id, size);

        var dl = ImGui.GetWindowDrawList();
        dl.AddRectFilled(ImGui.GetItemRectMin(), ImGui.GetItemRectMax(), GetButtonStateBackgroundColor(), 7, corners);

        Icons.DrawIconOnLastItem(icon, iconColor);
        return triggered;
    }

    private static Color GetStateColor(ButtonStates state)
    {
        return state switch
                   {
                       ButtonStates.Dimmed         => UiColors.TextMuted.Fade(0.5f),
                       ButtonStates.Disabled       => UiColors.TextDisabled,
                       ButtonStates.Activated      => UiColors.StatusActivated,
                       ButtonStates.NeedsAttention => UiColors.StatusAttention,
                       _                           => UiColors.Text
                   };
    }

    private static Color GetButtonStateBackgroundColor()
    {
        Color backgroundColor;

        if (ImGui.IsItemActive())
        {
            backgroundColor = ImGuiCol.ButtonActive.GetStyleColor();
        }
        else if (ImGui.IsItemHovered())
        {
            backgroundColor = ImGuiCol.ButtonHovered.GetStyleColor();
        }
        else
        {
            backgroundColor = ImGuiCol.Button.GetStyleColor();
        }

        return backgroundColor;
    }

    private static Action _cachedDrawMenuItems;

    public static void ContextMenuForItem(Action drawMenuItems, string title = null, string id = "context_menu",
                                          ImGuiPopupFlags flags = ImGuiPopupFlags.MouseButtonRight)
    {
        // prevent context menu from opening when dragging
        {
            var wasDraggingRight = ImGui.GetMouseDragDelta(ImGuiMouseButton.Right).Length() > UserSettings.Config.ClickThreshold;
            if (wasDraggingRight)
                return;
        }

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

        ImGui.PopStyleVar(1);
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

    public static bool DisablableButton(string label, bool isEnabled, bool enableTriggerWithReturn = false)
    {
        if (isEnabled)
        {
            ImGui.PushFont(Fonts.FontBold);
            if (ImGui.Button(label)
                || (enableTriggerWithReturn && ImGui.IsKeyPressed((ImGuiKey)Key.Return)))
            {
                ImGui.PopFont();
                return true;
            }

            ImGui.PopFont();
        }
        else
        {
            ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.3f, 0.3f, 0.3f, 0.1f));
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1, 1, 1, 0.15f));
            ImGui.Button(label);
            ImGui.PopStyleColor(2);
        }

        return false;
    }

    public static void HelpText(string text)
    {
        ImGui.PushFont(Fonts.FontSmall);
        ImGui.PushStyleColor(ImGuiCol.Text, UiColors.TextMuted.Rgba);
        //ImGui.TextUnformatted(text);
        ImGui.TextWrapped(text);
        ImGui.PopStyleColor();
        ImGui.PopFont();
        ImGui.Dummy(new Vector2(0, 4 * T3Ui.DisplayScaleFactor));
    }

    public static void SmallGroupHeader(string text)
    {
        FormInputs.AddVerticalSpace(5);
        ImGui.PushFont(Fonts.FontSmall);
        ImGui.PushStyleColor(ImGuiCol.Text, UiColors.TextMuted.Rgba);
        ImGui.SetCursorPosX(4);
        ImGui.TextUnformatted(text.ToUpperInvariant());
        ImGui.PopStyleColor();
        ImGui.PopFont();
        FormInputs.AddVerticalSpace(2);
    }
    
    
    
    public static void MenuGroupHeader(string text)
    {
        FormInputs.AddVerticalSpace(1);
        ImGui.PushFont(Fonts.FontSmall);
        ImGui.PushStyleColor(ImGuiCol.Text, UiColors.TextMuted.Rgba);
        ImGui.TextUnformatted(text);
        ImGui.PopStyleColor();
        ImGui.PopFont();
        
    }

    /// <summary>
    /// A small label that can be used to structure context menus
    /// </summary>
    public static void HintLabel(string label)
    {
        ImGui.PushFont(Fonts.FontSmall);
        ImGui.PushStyleColor(ImGuiCol.Text, UiColors.Gray.Rgba);
        ImGui.TextUnformatted(label);
        ImGui.PopStyleColor();
        ImGui.PopFont();
    }

    public static void FillWithStripes(ImDrawListPtr drawList, ImRect areaOnScreen, float canvasScale, float patternWidth = 16)
    {
        drawList.PushClipRect(areaOnScreen.Min, areaOnScreen.Max, true);
        var lineColor = new Color(0f, 0f, 0f, 0.2f);
        var stripeOffset = (patternWidth / 2 * canvasScale);
        var lineWidth = stripeOffset / 2.7f;

        var h = areaOnScreen.GetHeight();
        var stripeCount = (int)((areaOnScreen.GetWidth() + h + 3 * lineWidth) / stripeOffset);
        var p = areaOnScreen.Min - new Vector2(h + lineWidth, +lineWidth);
        var offset = new Vector2(h + 2 * lineWidth,
                                 h + 2 * lineWidth);

        for (var i = 0; i < stripeCount; i++)
        {
            drawList.AddLine(p, p + offset, lineColor, lineWidth);
            p.X += stripeOffset;
        }

        drawList.PopClipRect();
    }

    public static bool EmptyWindowMessage(string message, string buttonLabel = null)
    {
        var center = (ImGui.GetWindowContentRegionMax() + ImGui.GetWindowContentRegionMin()) / 2 + ImGui.GetWindowPos();
        var lines = message.Split('\n').ToArray();

        var lineCount = lines.Length;
        if (!string.IsNullOrEmpty(buttonLabel))
            lineCount++;

        var textLineHeight = ImGui.GetTextLineHeight();
        var y = center.Y - lineCount * textLineHeight / 2;
        var drawList = ImGui.GetWindowDrawList();

        var emptyMessageColor = UiColors.TextMuted;

        foreach (var line in lines)
        {
            if (!string.IsNullOrEmpty(line))
            {
                var textSize = ImGui.CalcTextSize(line);
                var position = new Vector2(center.X - textSize.X / 2, y);
                drawList.AddText(position, emptyMessageColor, line);
            }

            y += textLineHeight;
        }

        if (!string.IsNullOrEmpty(buttonLabel))
        {
            y += 10;
            var style = ImGui.GetStyle();
            var textSize = ImGui.CalcTextSize(buttonLabel) + style.FramePadding;
            var position = new Vector2(center.X - textSize.X / 2, y);
            ImGui.SetCursorScreenPos(position);
            return ImGui.Button(buttonLabel);
        }

        return false;
    }

    public static void TooltipForLastItem(Color color, string message, string additionalNotes = null, bool useHoverDelay = true)
    {
        if (!ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            return;

        FrameStats.Current.SomethingWithTooltipHovered = true;
        if (!useHoverDelay)
            _toolTipHoverDelay = 0;

        if (_toolTipHoverDelay > 0)
            return;

        BeginTooltip();
        ImGui.TextColored(color, message);
        if (!string.IsNullOrEmpty(additionalNotes))
        {
            ImGui.TextColored(color.Fade(0.7f), additionalNotes);
        }

        ImGui.PopTextWrapPos();

        EndTooltip();
    }

    /** Should be used for drawing consistently styled tooltips */
    public static bool BeginTooltip(float wrapPos = 300)
    {
        var isHovered = false;
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(6, 6));
        isHovered = ImGui.BeginTooltip();
        ImGui.PushTextWrapPos(wrapPos);
        return isHovered;
    }

    public static void EndTooltip()
    {
        ImGui.EndTooltip();
        ImGui.PopStyleVar();
    }

    public static void TooltipForLastItem(Action drawContent, bool useHoverDelay = true)
    {
        if (!ImGui.IsItemHovered())
            return;

        FrameStats.Current.SomethingWithTooltipHovered = true;
        if (!useHoverDelay)
            _toolTipHoverDelay = 0;

        if (_toolTipHoverDelay > 0)
            return;

        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(6, 6));
        ImGui.BeginTooltip();

        drawContent.Invoke();

        ImGui.EndTooltip();
        ImGui.PopStyleVar();
    }

    public static void TooltipForLastItem(string message, string additionalNotes = null, bool useHoverDelay = true)
    {
        TooltipForLastItem(UiColors.Text, message, additionalNotes, useHoverDelay);
    }

    private static double _toolTipHoverDelay;
    private static double _timeSinceTooltipHover;

    // TODO: this should be merged with FormInputs.SegmentedEnumButton
    public static bool DrawSegmentedToggle(ref int currentIndex, List<string> options)
    {
        var changed = false;
        for (var index = 0; index < options.Count; index++)
        {
            var isActive = currentIndex == index;
            var option = options[index];

            ImGui.SameLine(0);
            ImGui.PushFont(isActive ? Fonts.FontBold : Fonts.FontNormal);
            ImGui.PushStyleColor(ImGuiCol.Button, Color.Transparent.Rgba);
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, UiColors.ForegroundFull.Fade(0.1f).Rgba);
            ImGui.PushStyleColor(ImGuiCol.Text, isActive ? UiColors.ForegroundFull : UiColors.ForegroundFull.Fade(0.5f).Rgba);

            if (ImGui.Button(option))
            {
                if (!isActive)
                {
                    currentIndex = index;
                    changed = true;
                }
            }

            ImGui.PopFont();
            ImGui.PopStyleColor(3);
        }

        return changed;
    }

    public static bool AddSegmentedIconButton<T>(ref T selectedValue, List<Icon> icons) where T : struct, Enum
    {
        //DrawInputLabel(label);

        var modified = false;
        var selectedValueString = selectedValue.ToString();
        var isFirst = true;
        var enums = Enum.GetValues<T>();
        //Debug.Assert(enums.Length != icons.Count,"Icon enum mismatch");

        for (var index = 0; index < enums.Length; index++)
        {
            var icon = icons[index];
            var value = enums[index];
            var name = Enum.GetName(value);
            if (!isFirst)
            {
                ImGui.SameLine();
            }

            var isSelected = selectedValueString == value.ToString();

            var clicked = DrawIconToggle(name, icon, ref isSelected);
            if (clicked)
            {
                modified = true;
                selectedValue = value;
            }

            if (isSelected)
            {
                var min = ImGui.GetItemRectMin();
                var max = ImGui.GetItemRectMax();
                var drawList = ImGui.GetWindowDrawList();
                drawList.AddRectFilled(new Vector2(min.X - 2, max.Y), new Vector2(max.X + 2, max.Y + 2), UiColors.StatusActivated);
            }

            isFirst = false;
        }

        return modified;
    }

    public static bool DrawIconToggle(string name, Icon iconOff, Icon iconOn, ref bool isSelected, bool needsAttention = false, bool isEnabled = true)
    {
        var clicked = ImGui.InvisibleButton(name, new Vector2(17, 17));
        if (!isEnabled)
        {
            Icons.DrawIconOnLastItem(isSelected ? iconOn : iconOff, isSelected
                                                                        ? (needsAttention ? UiColors.StatusAttention : UiColors.BackgroundActive)
                                                                        : UiColors.TextDisabled.Fade(0.5f));
            return false;
        }

        Icons.DrawIconOnLastItem(isSelected ? iconOn : iconOff,
                                 isSelected ? (needsAttention ? UiColors.StatusAttention : UiColors.BackgroundActive) : UiColors.TextMuted);
        if (clicked)
            isSelected = !isSelected;

        return clicked;
    }

    public static bool DrawIconToggle(string name, Icon icon, ref bool isSelected, bool needsAttention = false)
    {
        var clicked = ImGui.InvisibleButton(name, new Vector2(17, 17));
        Icons.DrawIconOnLastItem(icon, isSelected ? (needsAttention ? UiColors.StatusAttention : UiColors.BackgroundActive) : UiColors.TextMuted);
        if (clicked)
            isSelected = !isSelected;

        return clicked;
    }

    public static bool DrawInputFieldWithPlaceholder(string placeHolderLabel, ref string value, float width = 0, bool showClear = true,
                                                     ImGuiInputTextFlags inputFlags = ImGuiInputTextFlags.None)
    {
        var notEmpty = !string.IsNullOrEmpty(value);
        var wasNull = value == null;
        if (wasNull)
            value = string.Empty;

        ImGui.SetNextItemWidth(width - FormInputs.ParameterSpacing - (notEmpty ? ImGui.GetFrameHeight() : 0));
        var modified = ImGui.InputText("##" + placeHolderLabel, ref value, 1000, inputFlags);
        if (!modified && wasNull)
            value = null;

        if (notEmpty)
        {
            if (showClear)
            {
                ImGui.SameLine(0, 0);
                if (ImGui.Button("×" + "##" + placeHolderLabel))
                {
                    value = null;
                    modified = true;
                }
            }
        }
        else
        {
            var drawList = ImGui.GetWindowDrawList();
            var minPos = ImGui.GetItemRectMin();
            var maxPos = ImGui.GetItemRectMax();
            drawList.PushClipRect(minPos, maxPos);
            drawList.AddText(minPos + new Vector2(8, 5), UiColors.ForegroundFull.Fade(0.25f), placeHolderLabel);
            drawList.PopClipRect();
        }

        return modified;
    }

    /// <summary>
    /// Draws a frame that indicates if the current window is focused.
    /// This is useful for windows that have window specific keyboard short cuts.
    /// Returns true if the window is focused
    /// </summary>
    public static void DrawWindowFocusFrame()
    {
        if (!ImGui.IsWindowFocused())
            return;

        var min = ImGui.GetWindowPos();
        ImGui.GetWindowDrawList().AddRect(min, min + ImGui.GetWindowSize() + new Vector2(0, 0), UiColors.ForegroundFull.Fade(0.2f));
    }

    public static string HumanReadablePascalCase(string f)
    {
        return Regex.Replace(f, "(\\B[A-Z])", " $1");
    }

    public static bool RoundedButton(string id, float width, ImDrawFlags roundedCorners)
    {
        var size = new Vector2(width, ImGui.GetFrameHeight());
        if (width == 0)
            size.X = size.Y;

        var clicked = ImGui.InvisibleButton(id, size);
        var dl = ImGui.GetWindowDrawList();
        var color = ImGui.IsItemHovered() ? ImGuiCol.ButtonHovered.GetStyleColor() : ImGuiCol.Button.GetStyleColor();
        dl.AddRectFilled(ImGui.GetItemRectMin(), ImGui.GetItemRectMax(), color, 7, roundedCorners);
        return clicked;
    }

    private static Vector2 _dragScrollStart;

    public static bool IsDragScrolling => _draggedWindowObject != null;
    private static object _draggedWindowObject;

    public static bool IsAnotherWindowDragScrolling(object windowObject)
    {
        return _draggedWindowObject != null && _draggedWindowObject != windowObject;
    }

    public static void HandleDragScrolling(object windowObject)
    {
        if (_draggedWindowObject == windowObject)
        {
            if (ImGui.IsMouseReleased(ImGuiMouseButton.Right))
            {
                _draggedWindowObject = null;
            }

            if (ImGui.IsMouseDragging(ImGuiMouseButton.Right))
            {
                ImGui.SetScrollY(_dragScrollStart.Y - ImGui.GetMouseDragDelta(ImGuiMouseButton.Right).Y);
            }

            return;
        }

        if (ImGui.IsWindowHovered() && !T3Ui.DragFieldWasHoveredLastFrame && ImGui.IsMouseClicked(ImGuiMouseButton.Right))
        {
            _dragScrollStart = new Vector2(ImGui.GetScrollX(), ImGui.GetScrollY());
            _draggedWindowObject = windowObject;
        }
    }

    internal static bool DrawProjectDropdown(ref EditableSymbolProject selectedValue)
    {
        return FormInputs.AddDropdown(ref selectedValue,
                                      EditableSymbolProject.AllProjects.OrderBy(x => x.DisplayName),
                                      "Project",
                                      x => x.DisplayName,
                                      "Project to edit symbols in.");
    }

    public static void DrawSymbolCodeContextMenuItem(Symbol symbol)
    {
        var symbolPackage = symbol.SymbolPackage;
        var project = symbolPackage as EditableSymbolProject;
        var enabled = project != null;
        if (ImGui.MenuItem("Open C# code", enabled))
        {
            if (!project!.TryOpenCSharpInEditor(symbol))
            {
                BlockingWindow.Instance.ShowMessageBox($"Failed to open C# code for {symbol.Name}\nCheck the logs for details.", "Error");
            }
        }
    }

    public static void StylizedText(string text, ImFontPtr imFont, Color color, bool addPadding= false)
    {
        ImGui.PushFont(imFont);
        ImGui.PushStyleColor(ImGuiCol.Text, color.Rgba);
        ImGui.TextUnformatted(text);
        ImGui.PopStyleColor();
        ImGui.PopFont();
        
        if(addPadding)
            ImGui.Dummy(new Vector2(1, 5 * T3Ui.UiScaleFactor));
    }
}