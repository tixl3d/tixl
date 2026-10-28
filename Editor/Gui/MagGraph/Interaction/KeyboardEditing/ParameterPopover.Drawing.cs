#nullable enable
using ImGuiNET;
using T3.Core.DataTypes.Vector;
using T3.Core.Operator;
using T3.Core.Operator.Slots;
using T3.Core.Stats;
using T3.Core.Utils;
using T3.Editor.Gui.Styling;
using T3.Editor.Gui.UiHelpers;

namespace T3.Editor.Gui.MagGraph.Interaction.KeyboardEditing;

internal static partial class ParameterPopover
{
    /** Damped position of a visible row, so rows that survive filtering or scrolling glide to their new place */
    private struct RowAnimation
    {
        public float Y;
        public float Opacity;
        public int LastFrame;
    }

    /// <summary>
    /// Draws the popover centered at the bottom of the graph window: the parameter rows, a scrollbar when they
    /// don't fit, and a help area with the keys for the current step. Its top edge is placed for the unfiltered
    /// list and stays there, so filtering shrinks it from the bottom and the rows don't move.
    /// </summary>
    private static void DrawPopover(Instance instance)
    {
        var scale = T3Ui.UiScaleFactor;
        var padding = MathF.Round(PopoverPaddingPx * scale);
        var windowPos = ImGui.GetWindowPos();
        var windowSize = ImGui.GetWindowSize();
        var rowHeight = MathF.Round(ImGui.GetFrameHeight());

        var visibleRowCount = Math.Clamp(_matches.Count, 1, MaxVisibleRows);
        var listHeight = visibleRowCount * rowHeight;
        var width = MathF.Min(PopoverWidthPx * scale, windowSize.X - 2 * padding);

        // The key hints wrap, so the help area grows with them
        ImGui.PushFont(Fonts.FontSmall);
        var helpLineCount = KeyHintText.CountLines(_keysSegments, width - 2 * padding);
        var helpLineHeight = KeyHintText.GetLineHeight();
        ImGui.PopFont();
        _maxHelpLineCount = Math.Max(_maxHelpLineCount, helpLineCount);

        var blend = MathUtils.BlendForFrame(RowBlendAt60Fps, FrameTiming.VisualDeltaSec);
        _dampedListHeight = _dampedListHeight <= 0 ? listHeight : MathUtils.Lerp(_dampedListHeight, listHeight, blend);
        listHeight = MathF.Round(_dampedListHeight);

        // A full padding above and below the separator
        var helpHeight = helpLineCount * helpLineHeight + 2 * padding;
        var height = listHeight + helpHeight + 2 * padding;
        var fullHeight = Math.Min(_entries.Count, MaxVisibleRows) * rowHeight + _maxHelpLineCount * helpLineHeight + 4 * padding;
        var min = new Vector2(MathF.Floor(windowPos.X + (windowSize.X - width) / 2),
                              MathF.Floor(windowPos.Y + windowSize.Y - fullHeight - 2 * padding));
        var max = new Vector2(MathF.Floor(min.X + width), MathF.Floor(min.Y + height));
        _popoverRect = new ImRect(min, max);

        var drawList = ImGui.GetWindowDrawList();
        drawList.AddRectFilled(min, max, UiColors.BackgroundPopup, PopoverRoundingPx * scale);
        drawList.AddRect(min, max, UiColors.ForegroundFull.Fade(0.12f), PopoverRoundingPx * scale);

        DrawHiddenInput(min);
        UpdateValueLabelsIfNeeded(instance);

        // The list scrolls only when the focused row would leave it
        var listMin = new Vector2(min.X + padding, min.Y + padding);
        var rowWidth = width - 2 * padding - ScrollbarWidthPx * scale - padding * 0.5f;
        var focusIndex = Math.Max(_matches.IndexOf(_focusedInputId), 0);
        if (focusIndex < _firstVisibleRow)
            _firstVisibleRow = focusIndex;
        else if (focusIndex >= _firstVisibleRow + visibleRowCount)
            _firstVisibleRow = focusIndex - visibleRowCount + 1;

        _firstVisibleRow = Math.Clamp(_firstVisibleRow, 0, Math.Max(_matches.Count - visibleRowCount, 0));

        drawList.PushClipRect(listMin, new Vector2(listMin.X + rowWidth, listMin.Y + listHeight), true);
        if (_matches.Count == 0)
        {
            DrawNoMatchRow(drawList, listMin, new Vector2(listMin.X + rowWidth, listMin.Y + rowHeight));
        }

        var frame = ImGui.GetFrameCount();
        DrawLeavingRows(drawList, instance, listMin, rowWidth, rowHeight, visibleRowCount, frame, blend);

        for (var row = 0; row < visibleRowCount && _firstVisibleRow + row < _matches.Count; row++)
        {
            var listIndex = _firstVisibleRow + row;
            var inputId = _matches[listIndex];
            var targetY = row * rowHeight;

            // Rows that weren't visible last frame fade in at their place instead of sliding
            if (!_rowAnimations.TryGetValue(inputId, out var animation) || animation.LastFrame < frame - 1)
                animation = new RowAnimation { Y = targetY, Opacity = 0 };

            animation.Y = MathUtils.Lerp(animation.Y, targetY, blend);
            animation.Opacity = MathUtils.Lerp(animation.Opacity, 1, blend);
            animation.LastFrame = frame;
            _rowAnimations[inputId] = animation;

            var rowMin = new Vector2(listMin.X, listMin.Y + MathF.Round(animation.Y));
            var rowMax = new Vector2(listMin.X + rowWidth, rowMin.Y + rowHeight);
            DrawRow(drawList, instance, inputId, rowMin, rowMax, listIndex == focusIndex, animation.Opacity);
        }

        drawList.PopClipRect();
        DrawScrollbar(drawList, new Vector2(listMin.X + rowWidth + padding * 0.5f, listMin.Y), listHeight, visibleRowCount);

        // Help area
        var helpTop = MathF.Floor(listMin.Y + listHeight + padding);
        drawList.AddLine(new Vector2(min.X + padding, helpTop), new Vector2(max.X - padding, helpTop), UiColors.ForegroundFull.Fade(0.4f));
        ImGui.PushFont(Fonts.FontSmall);
        var helpTextPos = new Vector2(min.X + padding, helpTop + padding);
        drawList.PushClipRect(min, max, true);
        KeyHintText.Draw(drawList, helpTextPos, _keysSegments, width - 2 * padding);
        drawList.PopClipRect();
        ImGui.PopFont();
    }

    /// <summary>
    /// Keeps drawing rows that left the visible list until they faded out: filtered out rows fade in place, rows
    /// scrolled out of view slide on past the edge. Drawn before the visible rows so those stay on top.
    /// </summary>
    private static void DrawLeavingRows(ImDrawListPtr drawList, Instance instance, Vector2 listMin, float rowWidth, float rowHeight,
                                        int visibleRowCount, int frame, float blend)
    {
        _leavingRowIds.Clear();
        foreach (var (inputId, animation) in _rowAnimations)
        {
            var listIndex = _matches.IndexOf(inputId);
            var isVisible = listIndex >= _firstVisibleRow && listIndex < _firstVisibleRow + visibleRowCount;
            if (!isVisible && animation.LastFrame >= frame - 1)
                _leavingRowIds.Add(inputId);
        }

        foreach (var inputId in _leavingRowIds)
        {
            var animation = _rowAnimations[inputId];
            var listIndex = _matches.IndexOf(inputId);
            if (listIndex >= 0)
                animation.Y = MathUtils.Lerp(animation.Y, (listIndex - _firstVisibleRow) * rowHeight, blend);

            animation.Opacity = MathUtils.Lerp(animation.Opacity, 0, blend);
            animation.LastFrame = frame;
            if (animation.Opacity < MinRowOpacity)
            {
                _rowAnimations.Remove(inputId);
                continue;
            }

            _rowAnimations[inputId] = animation;
            var rowMin = new Vector2(listMin.X, listMin.Y + MathF.Round(animation.Y));
            DrawRow(drawList, instance, inputId, rowMin, new Vector2(listMin.X + rowWidth, rowMin.Y + rowHeight), false, animation.Opacity);
        }
    }

    /** Typing goes into an invisible input; the rows show what was typed. */
    private static void DrawHiddenInput(Vector2 position)
    {
        ImGui.SetCursorScreenPos(position);
        if (_needsKeyboardFocus)
        {
            ImGui.SetKeyboardFocusHere();
            _needsKeyboardFocus = false;
        }

        ImGui.PushStyleColor(ImGuiCol.Text, Color.Transparent.Rgba);
        ImGui.PushStyleColor(ImGuiCol.FrameBg, Color.Transparent.Rgba);
        ImGui.PushStyleColor(ImGuiCol.TextSelectedBg, Color.Transparent.Rgba);
        ImGui.SetNextItemWidth(1);
        ImGui.InputText(_inputLabel, ref _text, MaxTextLength,
                        ImGuiInputTextFlags.CallbackAlways | ImGuiInputTextFlags.CallbackEdit | ImGuiInputTextFlags.CallbackCharFilter,
                        _inputCallback);
        ImGui.PopStyleColor(3);
        _isInputActive = ImGui.IsItemActive();
    }

    private static void DrawRow(ImDrawListPtr drawList, Instance instance, Guid inputId, Vector2 rowMin, Vector2 rowMax, bool isFocused,
                                float opacity)
    {
        if (!TryGetSlot(instance, inputId, out var slot))
            return;

        var scale = T3Ui.UiScaleFactor;
        var lineHeight = ImGui.GetTextLineHeight();
        var textPos = new Vector2(rowMin.X + RowPaddingPx * scale, MathF.Floor(rowMin.Y + (rowMax.Y - rowMin.Y - lineHeight) / 2));

        if (isFocused)
            drawList.AddRectFilled(rowMin, rowMax, UiColors.BackgroundFull.Fade(0.3f * opacity), 3 * scale);

        // Bold name with the characters matching the filter underlined
        var name = slot.Input.Name;
        var nameColor = (isFocused ? UiColors.Text : UiColors.Text.Fade(0.65f)).Fade(opacity);
        ImGui.PushFont(Fonts.FontBold);
        drawList.AddText(textPos, nameColor, name);
        var nameWidth = ImGui.CalcTextSize(name).X;
        _matchMasks.TryGetValue(inputId, out var matchMask);
        var lastMatchEnd = DrawMatchUnderlines(drawList, name, matchMask, textPos, lineHeight, nameColor);
        var caretOffset = lastMatchEnd > 0 ? ImGui.CalcTextSize(name.AsSpan(0, lastMatchEnd)).X : 0;
        ImGui.PopFont();

        var valuePos = new Vector2(textPos.X + nameWidth + ValueGapPx * scale, textPos.Y);
        if (isFocused && !_isValueMode && !_isNudging)
            DrawCaret(drawList, new Vector2(textPos.X + caretOffset, textPos.Y), lineHeight);

        if (isFocused && _isValueMode)
        {
            DrawValueEdit(drawList, valuePos, lineHeight);
            return;
        }

        if (slot.Input.Value is InputValue<Vector4> colorValue && !slot.HasInputConnections)
        {
            var swatchSize = MathF.Round(lineHeight * 0.7f);
            var swatchMin = new Vector2(MathF.Floor(valuePos.X), MathF.Floor(valuePos.Y + (lineHeight - swatchSize) / 2));
            DrawColorSwatch(drawList, colorValue.Value, swatchMin, swatchSize, opacity);
            valuePos.X += swatchSize + 4 * scale;
        }

        _valueLabels.TryGetValue(inputId, out var valueLabel);
        valueLabel ??= string.Empty;
        drawList.AddText(valuePos, GetValueColor(instance, slot, isFocused).Fade(opacity), valueLabel);
        if (!isFocused)
            return;

        // After the value: what is being adjusted, or else the parameter's description
        var notePos = new Vector2(valuePos.X + ImGui.CalcTextSize(valueLabel).X + ValueGapPx * 1.5f * scale, valuePos.Y);
        if (_isNudging && _nudgeLabel.Length > 0)
            drawList.AddText(notePos, UiColors.StatusActivated.Fade(0.8f * opacity), _nudgeLabel);
        else if (_focusedDescription.Length > 0)
            drawList.AddText(notePos, UiColors.TextMuted.Fade(0.6f * opacity), _focusedDescription);
    }

    /// <summary>
    /// HDR colors are scaled down by their brightest channel so the swatch keeps their hue, and get the same
    /// triangle marker as the color buttons in the Parameter Window.
    /// </summary>
    private static void DrawColorSwatch(ImDrawListPtr drawList, Vector4 color, Vector2 min, float size, float opacity)
    {
        var isHdr = MathUtils.HasHdrRange(color, out var intensity);
        if (isHdr)
        {
            var brightest = MathF.Max(color.X, MathF.Max(color.Y, color.Z));
            if (brightest > 1)
                color = new Vector4(color.X / brightest, color.Y / brightest, color.Z / brightest, color.W);
        }

        var max = min + new Vector2(size);
        drawList.AddRectFilled(min, max, new Color(color).Fade(opacity), 2 * T3Ui.UiScaleFactor);
        if (isHdr)
            drawList.DrawTriangleUp((min + max) / 2, Color.Black.Fade(opacity), intensity * 4 * T3Ui.UiScaleFactor);
    }

    /** Typed value with the caret, followed by the faint suggestion or result ("= 200"). */
    private static void DrawValueEdit(ImDrawListPtr drawList, Vector2 position, float lineHeight)
    {
        var valueStart = GetValueStart(_text);
        var typed = valueStart >= 0 ? _text.AsSpan(valueStart) : ReadOnlySpan<char>.Empty;
        drawList.AddText(position, UiColors.Text, typed);
        position.X += ImGui.CalcTextSize(typed).X;
        DrawCaret(drawList, position, lineHeight);
        position.X += 3 * T3Ui.UiScaleFactor;

        if (_valueError.Length > 0)
        {
            drawList.AddText(position, UiColors.StatusAttention.Fade(0.8f), _valueError);
            return;
        }

        KeyHintText.Draw(drawList, position, _valueGhostSegments);
    }

    private static void DrawNoMatchRow(ImDrawListPtr drawList, Vector2 rowMin, Vector2 rowMax)
    {
        var scale = T3Ui.UiScaleFactor;
        var lineHeight = ImGui.GetTextLineHeight();
        var textPos = new Vector2(rowMin.X + RowPaddingPx * scale, MathF.Floor(rowMin.Y + (rowMax.Y - rowMin.Y - lineHeight) / 2));
        drawList.AddRectFilled(rowMin, rowMax, UiColors.BackgroundFull.Fade(0.3f), 3 * scale);
        drawList.AddText(textPos, UiColors.StatusAttention, _text);
        textPos.X += ImGui.CalcTextSize(_text).X;
        DrawCaret(drawList, textPos, lineHeight);
        drawList.AddText(textPos + new Vector2(ValueGapPx * scale, 0), UiColors.TextMuted, "no matching parameter");
    }

    /// <returns>The index after the last underlined character, 0 without a match</returns>
    private static int DrawMatchUnderlines(ImDrawListPtr drawList, string name, ulong matchMask, Vector2 textPos, float lineHeight, Color color)
    {
        if (matchMask == 0)
            return 0;

        var lastMatchEnd = 0;
        var y = MathF.Floor(textPos.Y + lineHeight);
        var index = 0;
        var length = Math.Min(name.Length, 64);
        while (index < length)
        {
            if ((matchMask & 1UL << index) == 0)
            {
                index++;
                continue;
            }

            var runStart = index;
            while (index < length && (matchMask & 1UL << index) != 0)
            {
                index++;
            }

            var x0 = textPos.X + ImGui.CalcTextSize(name.AsSpan(0, runStart)).X;
            var x1 = textPos.X + ImGui.CalcTextSize(name.AsSpan(0, index)).X;
            drawList.AddLine(new Vector2(MathF.Floor(x0), y), new Vector2(MathF.Floor(x1), y), color, 1);
            lastMatchEnd = index;
        }

        return lastMatchEnd;
    }

    private static void DrawCaret(ImDrawListPtr drawList, Vector2 position, float lineHeight)
    {
        if (ImGui.GetTime() % CaretBlinkSec > CaretBlinkSec * 0.6)
            return;

        var x = MathF.Floor(position.X);
        drawList.AddLine(new Vector2(x, position.Y), new Vector2(x, position.Y + lineHeight), UiColors.Text, 1);
    }

    private static void DrawScrollbar(ImDrawListPtr drawList, Vector2 trackMin, float trackHeight, int visibleRowCount)
    {
        if (_matches.Count <= visibleRowCount)
            return;

        var scale = T3Ui.UiScaleFactor;
        var thumbHeight = MathF.Max(trackHeight * visibleRowCount / _matches.Count, 8 * scale);
        var progress = _firstVisibleRow / (float)Math.Max(_matches.Count - visibleRowCount, 1);
        var thumbTop = MathF.Floor(trackMin.Y + (trackHeight - thumbHeight) * progress);
        var trackWidth = ScrollbarWidthPx * scale;
        drawList.AddRectFilled(trackMin, trackMin + new Vector2(trackWidth, trackHeight), UiColors.ForegroundFull.Fade(0.04f), trackWidth / 2);
        drawList.AddRectFilled(new Vector2(trackMin.X, thumbTop), new Vector2(trackMin.X + trackWidth, thumbTop + thumbHeight),
                               UiColors.ForegroundFull.Fade(0.25f), trackWidth / 2);
    }

    /// <summary>
    /// Animated values are orange, connected blue, modified ones brighter than defaults. A just applied value
    /// flashes and fades back to its normal color.
    /// </summary>
    private static Color GetValueColor(Instance instance, IInputSlot slot, bool isFocused)
    {
        Color color;
        if (slot.HasInputConnections)
            color = UiColors.StatusAutomated;
        else if (instance.Parent != null && instance.Parent.Symbol.Animator.IsAnimated(instance.SymbolChildId, slot.Id))
            color = UiColors.StatusAnimated;
        else if (isFocused && _previewCommand != null)
            color = UiColors.StatusActivated;
        else if (!slot.Input.IsDefault)
            color = UiColors.Text.Fade(0.85f);
        else
            color = UiColors.Text.Fade(0.4f);

        if (_appliedTimes.TryGetValue(slot.Id, out var appliedTime))
        {
            var progress = (float)((ImGui.GetTime() - appliedTime) / AppliedFlashSec);
            if (progress < 1)
                color = Color.Mix(UiColors.StatusActivated, color, progress);
        }

        return color;
    }

    /** Value labels are formatted a few times per second, not every frame */
    private static void UpdateValueLabelsIfNeeded(Instance instance)
    {
        var time = ImGui.GetTime();
        if (!_valueLabelsNeedUpdate && time < _nextValueLabelsUpdateTime)
            return;

        _valueLabelsNeedUpdate = false;
        _nextValueLabelsUpdateTime = time + ValueLabelsUpdateIntervalSec;
        foreach (var slot in instance.Inputs)
        {
            _valueLabels[slot.Id] = FormatSlotValue(slot);
        }
    }

    /** Clicks inside the popover keep it open and must not reach the graph below */
    private static void HandleMouse()
    {
        if (_popoverRect.Contains(ImGui.GetMousePos()))
        {
            FrameStats.Current.OpenedPopupHovered = true;
            FrameStats.Current.OpenedPopupCapturedMouse = true;
            if (ImGui.IsMouseClicked(ImGuiMouseButton.Left))
                RequestKeyboardFocus();

            return;
        }

        // Clicking elsewhere ends the popover
        if (!_isInputActive && ImGui.GetFrameCount() > _focusRequestFrame + 2)
            Cancel(refocusGraph: false);
    }

    private const int MaxVisibleRows = 9;
    private const float PopoverWidthPx = 460;
    private const float PopoverPaddingPx = 8;
    private const float PopoverRoundingPx = 6;
    private const float RowPaddingPx = 6;
    private const float ValueGapPx = 10;
    private const float ScrollbarWidthPx = 4;
    private const double CaretBlinkSec = 1.0;

    /** Share of the remaining distance rows move per 60 fps frame; settles in about 130 ms */
    private const float RowBlendAt60Fps = 0.3f;
    private const float MinRowOpacity = 0.02f;
    private const double AppliedFlashSec = 0.6;
    private const double ValueLabelsUpdateIntervalSec = 0.2;

    private static readonly Dictionary<Guid, string> _valueLabels = new(32);
    private static readonly Dictionary<Guid, double> _appliedTimes = new(8);
    private static ImRect _popoverRect;
    private static int _firstVisibleRow;
    private static int _maxHelpLineCount;
    private static float _dampedListHeight;
    private static readonly Dictionary<Guid, RowAnimation> _rowAnimations = new(32);
    private static readonly List<Guid> _leavingRowIds = new(32);
    private static bool _valueLabelsNeedUpdate;
    private static double _nextValueLabelsUpdateTime;
}
