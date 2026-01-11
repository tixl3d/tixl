using System.Text.RegularExpressions;
using ImGuiNET;
using T3.Core.DataTypes.Vector;
using T3.Core.Operator;
using T3.Core.SystemUi;
using T3.Core.Utils;
using T3.Editor.Gui.Input;
using T3.Editor.Gui.UiHelpers;
using T3.Editor.UiModel;
using T3.SystemUi;

namespace T3.Editor.Gui.Styling;

/// <summary>
/// A set of special wrappers for ImGui components.
/// Also, checkout the FormInputs class. 
/// </summary>
internal static partial class CustomComponents
{


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

    public static void HelpText(string text)
    {
        ImGui.PushFont(Fonts.FontSmall);
        ImGui.PushStyleColor(ImGuiCol.Text, UiColors.TextMuted.Rgba);
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
        ImGui.TextUnformatted(text.ToUpperInvariant());
        ImGui.PopStyleColor();
        ImGui.PopFont();
        FormInputs.AddVerticalSpace(2);
    }

    public static void MenuGroupHeader(string text)
    {
        FormInputs.AddVerticalSpace();
        ImGui.PushFont(Fonts.FontSmall);
        ImGui.PushStyleColor(ImGuiCol.Text, UiColors.TextMuted.Rgba);
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + ImGui.GetFrameHeight());
            ImGui.TextUnformatted(text);
        ImGui.PopStyleColor();
        ImGui.PopFont();
    }

    /// <summary>
    /// Uses slightly different styling than ImGui.Separator()
    /// </summary>
    public static void SeparatorLine()
    {
        FormInputs.AddVerticalSpace(4);
        var x = ImGui.GetCursorPosX();
        ImGui.SetCursorPosX(0);
        var p = ImGui.GetCursorScreenPos();

        //var p = ImGui.GetWindowContentRegionMin() + ImGui.GetWindowPos() + new Vector2(1,1);
        ImGui.GetWindowDrawList()
             .AddRectFilled(p,
                            p + new Vector2(ImGui.GetWindowSize().X, 1), UiColors.ForegroundFull.Fade(0.1f));

        FormInputs.AddVerticalSpace(5);
        ImGui.SetCursorPosX(x);
        
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

    public static bool DrawInputFieldWithPlaceholder(string placeHolderLabel, ref string value, float width = 0, bool showClear = true,
                                                     ImGuiInputTextFlags inputFlags = ImGuiInputTextFlags.None)
    {
        ImGui.PushID(placeHolderLabel.GetHashCode(StringComparison.Ordinal));
        var notEmpty = !string.IsNullOrEmpty(value);
        var wasNull = value == null;
        if (wasNull)
            value = string.Empty;

        ImGui.SetNextItemWidth(width - FormInputs.ParameterSpacing - (notEmpty ? ImGui.GetFrameHeight() : 0));

        var modified = ImGui.InputText("##", ref value, 1000, inputFlags);
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
        ImGui.PopID();

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

    public static void StylizedText(string text, ImFontPtr imFont, Color color, bool addPadding = false)
    {
        ImGui.PushFont(imFont);
        ImGui.PushStyleColor(ImGuiCol.Text, color.Rgba);
        ImGui.TextUnformatted(text);
        ImGui.PopStyleColor();
        ImGui.PopFont();

        if (addPadding)
            ImGui.Dummy(new Vector2(1, 5 * T3Ui.UiScaleFactor));
    }

    public static void RightAlign(float itemWidth, bool sameLine = true)
    {
        if(sameLine)
            ImGui.SameLine();

        var padding = ImGui.GetStyle().WindowPadding.X;
        var avail = ImGui.GetContentRegionAvail().X;
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + avail - itemWidth - padding);
    }

    /// <summary>
    /// A reusable popover/popup component. Draws a trigger button and opens a popup
    /// where you can draw any custom content via the <paramref name="drawContent"/> action.
    /// </summary>
    /// <param name="id">Unique identifier for the popup.</param>
    /// <param name="triggerLabel">Label displayed on the trigger button.</param>
    /// <param name="drawContent">Action to draw the popup content. Return true to close the popup.</param>
    /// <param name="triggerWidth">Width of the trigger button. Use 0 for auto-size.</param>
    /// <returns>True if the popup was just opened this frame.</returns>
    public static bool DrawPopover(string id, string triggerLabel, Func<bool> drawContent, float triggerWidth = 0)
    {
        var popupId = $"##Popover_{id}";
        var wasOpened = false;

        if (triggerWidth > 0)
            ImGui.SetNextItemWidth(triggerWidth);

        if (ImGui.Button(triggerLabel + "##" + id))
        {
            ImGui.OpenPopup(popupId);
            wasOpened = true;
        }

        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(8 * T3Ui.UiScaleFactor));
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(6 * T3Ui.UiScaleFactor));
        if (ImGui.BeginPopup(popupId))
        {
            var shouldClose = drawContent();
            if (shouldClose)
                ImGui.CloseCurrentPopup();
            ImGui.EndPopup();
        }
        ImGui.PopStyleVar(2);

        return wasOpened;
    }

    /// <summary>
    /// Overload that uses an Action instead of Func, for content that doesn't need to close programmatically.
    /// </summary>
    public static bool DrawPopover(string id, string triggerLabel, Action drawContent, float triggerWidth = 0)
    {
        return DrawPopover(id, triggerLabel, () =>
        {
            drawContent();
            return false;
        }, triggerWidth);
    }
}