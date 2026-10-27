#nullable enable
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;
using ImGuiNET;
using T3.Core.DataTypes.Vector;
using T3.Core.Operator;
using T3.Core.Operator.Slots;
using T3.Editor.Gui.InputUi.VectorInputs;
using T3.Editor.Gui.Interaction;
using T3.Editor.Gui.MagGraph.States;
using T3.Editor.Gui.Styling;
using T3.Editor.Gui.UiHelpers;
using T3.Editor.UiModel;
using T3.Editor.UiModel.Commands;
using T3.Editor.UiModel.Commands.Graph;
using T3.Editor.UiModel.InputsAndTypes;

namespace T3.Editor.Gui.MagGraph.Interaction;

/// <summary>
/// Keyboard popover for editing the parameters of the selected operator, opened with S. It lists the parameters
/// with their values; the focused row stays in place while Up/Down move through the list, like a spreadsheet:
/// typing a value previews it live, and Down applies it and moves on.
/// Letters filter the parameters (name prefix, word prefix, initials). Tab (or Space) edits the value: numbers and
/// expressions ("10", "*2" which becomes "100*2", "=-5"), vectors ("0 1 0", "x 1", "*2" for all), colors
/// ("orange", "#f80", "a 0.5"), bools toggle, and on a dropdown the value filters its options. A leading "-" is a
/// negative number; the other operators continue from the current value. Shift+R resets the parameter to its
/// default, Shift+I inserts an operator connected to it; both keep letters free for values.
/// Left/Right change the value: numbers with the infinity slider (Shift fine, Ctrl coarse), "x" then Left/Right a
/// vector component, on colors brightness, or "h" hue, "s" saturation and "r g b a" channels; dropdowns step;
/// bools toggle. The input has no caret: typing always appends.
/// Enter applies and closes; Esc steps back: the typed value, then the filter, then the popover.
/// </summary>
internal static class ParameterPrompt
{
    private readonly record struct ParameterEntry(Guid InputId, string Name, string LowerInitials);

    private readonly record struct EnumOption(string Name, int Value);

    /** What Left/Right adjust with the slider */
    private enum NudgeTargets
    {
        Number,
        Component,
        Hue,
        Saturation,
        Brightness,
    }

    private enum ValueKinds
    {
        Unsupported,
        Number,
        Vector,
        Color,
        Bool,
        Options,
    }

    internal static bool IsOpen => _childId != Guid.Empty;

    /// <summary>
    /// Opens the popover for the single selected operator, on the parameter last edited there or else on the first
    /// one that isn't connected.
    /// </summary>
    internal static void Open(GraphUiContext context)
    {
        if (context.Selector.Selection.Count != 1)
            return;

        var instance = context.Selector.GetFirstSelectedInstance();
        if (instance?.Parent == null || instance == context.CompositionInstance)
            return;

        _entries.Clear();
        foreach (var inputSlot in instance.Inputs)
        {
            var name = inputSlot.Input.Name;
            _entries.Add(new ParameterEntry(inputSlot.Id, name, GetLowerInitials(name)));
        }

        if (_entries.Count == 0)
            return;

        _childId = instance.SymbolChildId;
        _compositionSymbolId = instance.Parent.Symbol.Id;
        _selectionChangeCounterAtOpen = context.Selector.ChangeCounter;
        _openFrame = ImGui.GetFrameCount();
        _focusedInputId = GetInitialFocus(instance);
        _appliedTimes.Clear();
        _valueLabelsNeedUpdate = true;
        UpdateMatches(string.Empty);
        StartNewEntry(instance);
    }

    internal static void Draw(GraphUiContext context)
    {
        if (!IsOpen)
        {
            // Closing with the keyboard hands the keyboard back to the graph
            if (_refocusGraph)
            {
                ImGui.SetWindowFocus();
                _refocusGraph = false;
            }

            return;
        }

        if (context.Selector.ChangeCounter != _selectionChangeCounterAtOpen
            || !TryGetInstance(context, out var instance))
        {
            Cancel(refocusGraph: false);
            return;
        }

        // The key that opened the popover is still in this frame's character queue and would be typed into it.
        if (ImGui.GetFrameCount() == _openFrame)
            return;

        DrawPopover(instance);

        if (ImGui.IsKeyPressed(ImGuiKey.Escape, false))
        {
            StepBack(instance);
            return;
        }

        if (ImGui.GetIO().KeyShift && ImGui.IsKeyPressed(ImGuiKey.I, false))
        {
            InsertConnectedOperator(context, instance);
            return;
        }

        if (ImGui.GetIO().KeyShift && ImGui.IsKeyPressed(ImGuiKey.R, false))
        {
            ResetToDefault(instance);
            return;
        }

        if (ImGui.IsKeyPressed(ImGuiKey.Enter, false) || ImGui.IsKeyPressed(ImGuiKey.KeypadEnter, false))
        {
            Commit(instance);
            Close(refocusGraph: true);
            return;
        }

        if (_text != _lastText)
        {
            _lastText = _text;
            ProcessText(instance);
        }

        HandleNudging(instance);

        var isPickingOption = _isValueMode && _valueKind == ValueKinds.Options;
        if (ImGui.IsKeyPressed(ImGuiKey.Tab, false))
        {
            // Tab completes the parameter and starts its value; on a value it applies and moves on
            if (_isValueMode)
                CommitAndMove(instance, 1);
            else
                SetInputText(_text + " ");

            // ImGui also moves keyboard focus to the next widget on Tab
            RequestKeyboardFocus();
        }
        else if (ImGui.IsKeyPressed(ImGuiKey.UpArrow))
        {
            if (isPickingOption)
                MoveOptionFocus(instance, -1);
            else
                CommitAndMove(instance, -1);
        }
        else if (ImGui.IsKeyPressed(ImGuiKey.DownArrow))
        {
            if (isPickingOption)
                MoveOptionFocus(instance, 1);
            else
                CommitAndMove(instance, 1);
        }

        HandleMouse();
    }

    #region drawing
    /// <summary>
    /// Draws the popover centered at the bottom of the graph window: the parameter rows with the focused one fixed
    /// in place, a scrollbar, and a help area with the keys for the current step.
    /// </summary>
    private static void DrawPopover(Instance instance)
    {
        var scale = T3Ui.UiScaleFactor;
        var padding = MathF.Round(PopoverPaddingPx * scale);
        var windowPos = ImGui.GetWindowPos();
        var windowSize = ImGui.GetWindowSize();
        var rowHeight = MathF.Round(ImGui.GetFrameHeight());

        ImGui.PushFont(Fonts.FontSmall);
        var smallLineHeight = ImGui.GetTextLineHeight();
        ImGui.PopFont();

        var listHeight = MaxVisibleRows * rowHeight;
        var helpHeight = 2 * smallLineHeight + padding * 1.5f;
        var width = MathF.Min(PopoverWidthPx * scale, windowSize.X - 2 * padding);
        var height = listHeight + helpHeight + 2 * padding;
        var min = new Vector2(MathF.Floor(windowPos.X + (windowSize.X - width) / 2),
                              MathF.Floor(windowPos.Y + windowSize.Y - height - 2 * padding));
        var max = new Vector2(MathF.Floor(min.X + width), MathF.Floor(min.Y + height));
        _popoverRect = new ImRect(min, max);

        var drawList = ImGui.GetWindowDrawList();
        drawList.AddRectFilled(min, max, UiColors.BackgroundPopup, PopoverRoundingPx * scale);
        drawList.AddRect(min, max, UiColors.ForegroundFull.Fade(0.12f), PopoverRoundingPx * scale);

        DrawHiddenInput(min);
        UpdateValueLabelsIfNeeded(instance);

        // Rows around the focused one, which stays at a fixed slot
        var listMin = new Vector2(min.X + padding, min.Y + padding);
        var rowWidth = width - 2 * padding - ScrollbarWidthPx * scale - padding * 0.5f;
        var focusIndex = Math.Max(_matches.IndexOf(_focusedInputId), 0);
        drawList.PushClipRect(listMin, new Vector2(listMin.X + rowWidth, listMin.Y + listHeight), true);
        for (var slot = 0; slot < MaxVisibleRows; slot++)
        {
            var listIndex = focusIndex - FocusRowSlot + slot;
            var rowMin = new Vector2(listMin.X, listMin.Y + slot * rowHeight);
            var rowMax = new Vector2(listMin.X + rowWidth, rowMin.Y + rowHeight);
            if (listIndex >= 0 && listIndex < _matches.Count)
            {
                DrawRow(drawList, instance, _matches[listIndex], rowMin, rowMax, listIndex == focusIndex);
            }
            else if (slot == FocusRowSlot && _matches.Count == 0)
            {
                DrawNoMatchRow(drawList, rowMin, rowMax);
            }
        }

        drawList.PopClipRect();
        DrawScrollbar(drawList, new Vector2(listMin.X + rowWidth + padding * 0.5f, listMin.Y), listHeight, focusIndex);

        // Help area
        var helpTop = MathF.Floor(listMin.Y + listHeight + padding * 0.5f);
        drawList.AddLine(new Vector2(min.X + padding, helpTop), new Vector2(max.X - padding, helpTop), UiColors.ForegroundFull.Fade(0.08f));
        ImGui.PushFont(Fonts.FontSmall);
        var helpTextPos = new Vector2(min.X + padding, helpTop + padding * 0.5f);
        drawList.PushClipRect(min, max, true);
        KeyHintText.Draw(drawList, helpTextPos, _keysSegments);
        if (_focusedDescription.Length > 0)
            drawList.AddText(helpTextPos + new Vector2(0, smallLineHeight + 2 * scale), UiColors.TextMuted.Fade(0.8f), _focusedDescription);

        drawList.PopClipRect();
        ImGui.PopFont();
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

    private static void DrawRow(ImDrawListPtr drawList, Instance instance, Guid inputId, Vector2 rowMin, Vector2 rowMax, bool isFocused)
    {
        if (!TryGetSlot(instance, inputId, out var slot))
            return;

        var scale = T3Ui.UiScaleFactor;
        var lineHeight = ImGui.GetTextLineHeight();
        var textPos = new Vector2(rowMin.X + RowPaddingPx * scale, MathF.Floor(rowMin.Y + (rowMax.Y - rowMin.Y - lineHeight) / 2));

        if (isFocused)
            drawList.AddRectFilled(rowMin, rowMax, UiColors.ForegroundFull.Fade(0.07f), 3 * scale);

        // Name with the characters matching the filter underlined
        var name = slot.Input.Name;
        var nameColor = isFocused ? UiColors.Text : UiColors.Text.Fade(0.65f);
        drawList.AddText(textPos, nameColor, name);
        var nameWidth = ImGui.CalcTextSize(name).X;
        _matchMasks.TryGetValue(inputId, out var matchMask);
        var lastMatchEnd = DrawMatchUnderlines(drawList, name, matchMask, textPos, lineHeight, nameColor);

        var valuePos = new Vector2(textPos.X + nameWidth + ValueGapPx * scale, textPos.Y);
        if (isFocused && !_isValueMode && !_isNudging)
        {
            var caretX = textPos.X + (lastMatchEnd > 0 ? ImGui.CalcTextSize(name.AsSpan(0, lastMatchEnd)).X : 0);
            DrawCaret(drawList, new Vector2(caretX, textPos.Y), lineHeight);
        }

        if (isFocused && _isValueMode)
        {
            DrawValueEdit(drawList, valuePos, lineHeight);
            return;
        }

        if (slot.Input.Value is InputValue<Vector4> colorValue && !slot.HasInputConnections)
        {
            var swatchSize = MathF.Round(lineHeight * 0.7f);
            var swatchMin = new Vector2(MathF.Floor(valuePos.X), MathF.Floor(valuePos.Y + (lineHeight - swatchSize) / 2));
            drawList.AddRectFilled(swatchMin, swatchMin + new Vector2(swatchSize), new Color(colorValue.Value), 2 * scale);
            valuePos.X += swatchSize + 4 * scale;
        }

        _valueLabels.TryGetValue(inputId, out var valueLabel);
        valueLabel ??= string.Empty;
        drawList.AddText(valuePos, GetValueColor(instance, slot, isFocused), valueLabel);

        if (isFocused && _isNudging && _nudgeLabel.Length > 0)
        {
            var labelX = valuePos.X + ImGui.CalcTextSize(valueLabel).X + ValueGapPx * scale;
            drawList.AddText(new Vector2(labelX, valuePos.Y), UiColors.StatusActivated.Fade(0.8f), _nudgeLabel);
        }
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
        drawList.AddRectFilled(rowMin, rowMax, UiColors.ForegroundFull.Fade(0.07f), 3 * scale);
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

    private static void DrawScrollbar(ImDrawListPtr drawList, Vector2 trackMin, float trackHeight, int focusIndex)
    {
        if (_matches.Count <= MaxVisibleRows)
            return;

        var scale = T3Ui.UiScaleFactor;
        var thumbHeight = MathF.Max(trackHeight * MaxVisibleRows / _matches.Count, 8 * scale);
        var progress = focusIndex / (float)Math.Max(_matches.Count - 1, 1);
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
    #endregion

    #region nudging
    /// <summary>
    /// Left/Right adjust numbers, vector components and color properties with the keyboard-driven infinity slider,
    /// starting from a typed value if there is one. The adjustment is a pending value like a typed one: Down, Tab or
    /// Enter apply it, Esc reverts it.
    /// </summary>
    private static void HandleNudging(Instance instance)
    {
        if (_valueKind is ValueKinds.Options or ValueKinds.Bool)
        {
            var stepDirection = ImGui.IsKeyPressed(ImGuiKey.RightArrow) ? 1 : ImGui.IsKeyPressed(ImGuiKey.LeftArrow) ? -1 : 0;
            if (stepDirection == 0)
                return;

            if (_isValueMode && _valueKind == ValueKinds.Options)
                MoveOptionFocus(instance, stepDirection);
            else
                StepValue(instance, stepDirection);

            return;
        }

        var direction = ImGui.IsKeyDown(ImGuiKey.RightArrow) ? 1 : ImGui.IsKeyDown(ImGuiKey.LeftArrow) ? -1 : 0;
        if (!_isNudging)
        {
            if (direction == 0
                || _valueKind is not (ValueKinds.Number or ValueKinds.Vector or ValueKinds.Color)
                || !TryStartNudging(instance))
                return;
        }

        var wasPressed = ImGui.IsKeyPressed(ImGuiKey.RightArrow, false) || ImGui.IsKeyPressed(ImGuiKey.LeftArrow, false);
        var anchor = new Vector2((_popoverRect.Min.X + _popoverRect.Max.X) / 2, _popoverRect.Min.Y - SliderGapPx * T3Ui.UiScaleFactor);
        var value = _nudgeValue;
        InfinitySliderOverlay.DrawForKeyboard(ref value, _restartNudging, anchor, direction, wasPressed,
                                              _nudgeMin, _nudgeMax, _nudgeScale, _nudgeClampMin, _nudgeClampMax);
        _restartNudging = false;

        if (_isNudgingInt)
            value = Math.Round(value);

        if (value == _nudgeValue || _previewCommand == null || !TryGetFocusedSlot(instance, out var slot))
            return;

        _nudgeValue = value;
        var newValue = CreateNudgedValue(value);
        if (newValue == null)
            return;

        _previewCommand.AssignNewValue(newValue);
        _valueLabelsNeedUpdate = true;
        _nudgeLabel = _nudgeTarget == NudgeTargets.Number
                          ? string.Empty
                          : $"{_nudgeTargetLabel} {FormatNumber((float)value)}";
    }

    private static InputValue? CreateNudgedValue(double value)
    {
        switch (_nudgeTarget)
        {
            case NudgeTargets.Number:
                return _isNudgingInt ? new InputValue<int>((int)value) : new InputValue<float>((float)value);

            case NudgeTargets.Component:
                Span<double> components = stackalloc double[4];
                var count = GetComponents(_nudgeBaseValue, components);
                if (_nudgeComponent >= count)
                    return null;

                components[_nudgeComponent] = value;
                return CreateFromComponents(_nudgeBaseValue, components);

            default:
                var (hue, saturation, brightness, alpha) = (_nudgeHsva.X, _nudgeHsva.Y, _nudgeHsva.Z, _nudgeHsva.W);
                switch (_nudgeTarget)
                {
                    case NudgeTargets.Hue:
                        hue = (float)(value - Math.Floor(value));
                        break;
                    case NudgeTargets.Saturation:
                        saturation = (float)value;
                        break;
                    default:
                        brightness = (float)value;
                        break;
                }

                return new InputValue<Vector4>(Color.FromHSV(hue, saturation, brightness, alpha).Rgba);
        }
    }

    /** Fills the components of a vector or color value and returns how many it has, 0 for other types */
    private static int GetComponents(InputValue? value, Span<double> components)
    {
        switch (value)
        {
            case InputValue<Vector2> v:
                (components[0], components[1]) = (v.Value.X, v.Value.Y);
                return 2;
            case InputValue<Vector3> v:
                (components[0], components[1], components[2]) = (v.Value.X, v.Value.Y, v.Value.Z);
                return 3;
            case InputValue<Vector4> v:
                (components[0], components[1], components[2], components[3]) = (v.Value.X, v.Value.Y, v.Value.Z, v.Value.W);
                return 4;
            case InputValue<Int2> v:
                (components[0], components[1]) = (v.Value.X, v.Value.Y);
                return 2;
            case InputValue<Int3> v:
                (components[0], components[1], components[2]) = (v.Value.X, v.Value.Y, v.Value.Z);
                return 3;
            default:
                return 0;
        }
    }

    private static InputValue? CreateFromComponents(InputValue? template, ReadOnlySpan<double> c)
    {
        return template switch
                   {
                       InputValue<Vector2> => new InputValue<Vector2>(new Vector2((float)c[0], (float)c[1])),
                       InputValue<Vector3> => new InputValue<Vector3>(new Vector3((float)c[0], (float)c[1], (float)c[2])),
                       InputValue<Vector4> => new InputValue<Vector4>(new Vector4((float)c[0], (float)c[1], (float)c[2], (float)c[3])),
                       InputValue<Int2>    => new InputValue<Int2>(new Int2((int)Math.Round(c[0]), (int)Math.Round(c[1]))),
                       InputValue<Int3>    => new InputValue<Int3>(new Int3((int)Math.Round(c[0]), (int)Math.Round(c[1]), (int)Math.Round(c[2]))),
                       _                   => null,
                   };
    }

    /// <summary>
    /// Steps a dropdown to the previous or next option, or toggles a bool. The change is pending like a typed value.
    /// </summary>
    private static void StepValue(Instance instance, int direction)
    {
        if (instance.Parent == null || !TryGetFocusedSlot(instance, out var slot) || slot.HasInputConnections)
            return;

        InputValue? newValue = null;
        if (_valueKind == ValueKinds.Bool && slot.Input.Value is InputValue<bool> boolValue)
        {
            newValue = new InputValue<bool>(!boolValue.Value);
        }
        else if (_valueKind == ValueKinds.Options && _options.Count > 0)
        {
            var currentValue = GetCurrentOptionValue(slot);
            var index = 0;
            for (var optionIndex = 0; optionIndex < _options.Count; optionIndex++)
            {
                if (_options[optionIndex].Value == currentValue)
                    index = optionIndex;
            }

            var option = _options[Math.Clamp(index + direction, 0, _options.Count - 1)];
            if (option.Value == currentValue)
                return;

            newValue = slot.Input.Value.ValueType.IsEnum
                           ? CreateEnumValue(slot.Input.Value.ValueType, option.Value)
                           : new InputValue<int>(option.Value);
        }

        if (newValue == null)
            return;

        if (_previewCommand == null)
        {
            _previewOriginalLabel = FormatValue(slot.Input.Value);
            _previewCommand = new ChangeInputValueCommand(instance.Parent.Symbol, instance.SymbolChildId, slot.Input, null, instance);
        }

        _previewCommand.AssignNewValue(newValue);
        if (_text.Length > 0)
        {
            SetInputText(string.Empty);
            if (_matches.Count != _entries.Count)
                UpdateMatches(string.Empty);
        }

        _valueLabelsNeedUpdate = true;
        UpdateTexts();
    }

    /// <summary>
    /// Picks what Left/Right adjust from the typed text: a component letter on vectors ("x"), a channel or color
    /// property on colors ("r g b a", "h", "s", nothing for brightness). Other typed values stay pending and the
    /// adjustment continues from them.
    /// </summary>
    private static bool TryStartNudging(Instance instance)
    {
        if (instance.Parent == null
            || !TryGetFocusedSlot(instance, out var slot)
            || slot.HasInputConnections
            || !SymbolUiRegistry.TryGetSymbolUi(instance.Symbol.Id, out var symbolUi)
            || !symbolUi.InputUis.TryGetValue(slot.Id, out var inputUi))
            return false;

        var selector = _isValueMode ? _valueText.ToLowerInvariant() : string.Empty;
        var isSelector = false;
        _nudgeTarget = NudgeTargets.Number;
        _nudgeTargetLabel = string.Empty;
        switch (_valueKind)
        {
            case ValueKinds.Vector:
                _nudgeComponent = selector.Length == 1 ? ComponentLetters.IndexOf(selector[0]) : -1;
                if (_nudgeComponent < 0)
                    return false;

                _nudgeTarget = NudgeTargets.Component;
                isSelector = true;
                break;

            case ValueKinds.Color:
                (_nudgeTarget, _nudgeTargetLabel, isSelector) = selector switch
                                                                    {
                                                                        "h" => (NudgeTargets.Hue, "hue", true),
                                                                        "s" => (NudgeTargets.Saturation, "saturation", true),
                                                                        "v" => (NudgeTargets.Brightness, "brightness", true),
                                                                        _   => (NudgeTargets.Brightness, "brightness", false),
                                                                    };
                if (selector.Length == 1 && ChannelLetters.Contains(selector[0]))
                {
                    _nudgeTarget = NudgeTargets.Component;
                    _nudgeComponent = ChannelLetters.IndexOf(selector[0]);
                    isSelector = true;
                }

                break;
        }

        if (_nudgeTarget == NudgeTargets.Component)
            _nudgeTargetLabel = selector;

        // A selector isn't a value: drop what it previewed (" g" previews gray, " r" a reset)
        if (isSelector)
            RevertPreview();

        var value = slot.Input.Value;
        _nudgeBaseValue = value.Clone();
        _isNudgingInt = value is InputValue<int> or InputValue<Int2> or InputValue<Int3>;
        switch (_nudgeTarget)
        {
            case NudgeTargets.Number:
                if (value is InputValue<float> floatValue)
                    _nudgeValue = floatValue.Value;
                else if (value is InputValue<int> intValue)
                    _nudgeValue = intValue.Value;
                else
                    return false;

                if (!TryGetSliderRange(inputUi))
                    return false;

                break;

            case NudgeTargets.Component:
                Span<double> components = stackalloc double[4];
                if (_nudgeComponent >= GetComponents(value, components))
                    return false;

                _nudgeValue = components[_nudgeComponent];
                if (_valueKind == ValueKinds.Color)
                    SetColorSliderRange(0, double.PositiveInfinity, clampMax: false);
                else if (!TryGetSliderRange(inputUi))
                    return false;

                break;

            default:
                if (value is not InputValue<Vector4> colorValue)
                    return false;

                new Color(colorValue.Value).GetHSV(out var hue, out var saturation, out var brightness);
                _nudgeHsva = new Vector4(hue, saturation, brightness, colorValue.Value.W);
                switch (_nudgeTarget)
                {
                    case NudgeTargets.Hue:
                        _nudgeValue = hue;
                        SetColorSliderRange(double.NegativeInfinity, double.PositiveInfinity, clampMax: false, clampMin: false);
                        break;
                    case NudgeTargets.Saturation:
                        _nudgeValue = saturation;
                        SetColorSliderRange(0, 1, clampMax: true);
                        break;
                    default:
                        _nudgeValue = brightness;
                        SetColorSliderRange(0, double.PositiveInfinity, clampMax: false);
                        break;
                }

                break;
        }

        // A typed value stays pending and the adjustment continues from it
        if (_previewCommand != null)
        {
            _nudgeOriginalLabel = _nudgeTarget == NudgeTargets.Number ? _previewOriginalLabel : FormatNumber((float)_nudgeValue);
        }
        else
        {
            _nudgeOriginalLabel = _nudgeTarget == NudgeTargets.Number ? FormatValue(value) : FormatNumber((float)_nudgeValue);
            _previewCommand = new ChangeInputValueCommand(instance.Parent.Symbol, instance.SymbolChildId, slot.Input, null, instance);
        }

        SetInputText(string.Empty);
        if (_matches.Count != _entries.Count)
            UpdateMatches(string.Empty);

        _isNudging = true;
        _restartNudging = true;
        _nudgeLabel = _nudgeTarget == NudgeTargets.Number ? string.Empty : $"{_nudgeTargetLabel} {_nudgeOriginalLabel}";
        UpdateTexts();
        return true;
    }

    /** Range, step scale and clamping of the parameter's own number or vector settings */
    private static bool TryGetSliderRange(IInputUi inputUi)
    {
        switch (inputUi)
        {
            case FloatVectorInputValueUi<float> ui:
                (_nudgeMin, _nudgeMax, _nudgeScale, _nudgeClampMin, _nudgeClampMax) = (ui.Min, ui.Max, ui.Scale, ui.ClampMin, ui.ClampMax);
                return true;
            case FloatVectorInputValueUi<Vector2> ui:
                (_nudgeMin, _nudgeMax, _nudgeScale, _nudgeClampMin, _nudgeClampMax) = (ui.Min, ui.Max, ui.Scale, ui.ClampMin, ui.ClampMax);
                return true;
            case FloatVectorInputValueUi<Vector3> ui:
                (_nudgeMin, _nudgeMax, _nudgeScale, _nudgeClampMin, _nudgeClampMax) = (ui.Min, ui.Max, ui.Scale, ui.ClampMin, ui.ClampMax);
                return true;
            case IntVectorInputValueUi<int> ui:
                (_nudgeMin, _nudgeMax, _nudgeScale, _nudgeClampMin, _nudgeClampMax) = (ui.Min, ui.Max, ui.Scale, ui.ClampMin, ui.ClampMax);
                return true;
            case IntVectorInputValueUi<Int2> ui:
                (_nudgeMin, _nudgeMax, _nudgeScale, _nudgeClampMin, _nudgeClampMax) = (ui.Min, ui.Max, ui.Scale, ui.ClampMin, ui.ClampMax);
                return true;
            case IntVectorInputValueUi<Int3> ui:
                (_nudgeMin, _nudgeMax, _nudgeScale, _nudgeClampMin, _nudgeClampMax) = (ui.Min, ui.Max, ui.Scale, ui.ClampMin, ui.ClampMax);
                return true;
            default:
                return false;
        }
    }

    private static void SetColorSliderRange(double min, double max, bool clampMax, bool clampMin = true)
    {
        (_nudgeMin, _nudgeMax, _nudgeScale, _nudgeClampMin, _nudgeClampMax) = (min, max, ColorNudgeScale, clampMin, clampMax);
    }
    #endregion

    #region text input
    /// <summary>
    /// Keeps typing at the end, and turns an operator typed as the first character of a number into an
    /// expression with the current value: "*" becomes "100*". A leading "-" stays a negative number.
    /// </summary>
    private static unsafe int OnInputCallback(ImGuiInputTextCallbackData* rawData)
    {
        var data = new ImGuiInputTextCallbackDataPtr(rawData);

        // Shift+I and Shift+R are commands, not text
        if (data.EventFlag == ImGuiInputTextFlags.CallbackCharFilter)
            return data.EventChar is 'I' or 'R' && ImGui.GetIO().KeyShift ? 1 : 0;

        if (data.EventFlag == ImGuiInputTextFlags.CallbackEdit && _valueKind == ValueKinds.Number && _editableValueLabel.Length > 0)
        {
            var text = Encoding.UTF8.GetString((byte*)data.Buf, data.BufTextLen);
            var valueStart = GetValueStart(text);
            if (valueStart >= 0 && valueStart == text.Length - 1 && ContinuingOperators.Contains(text[valueStart]))
                data.InsertChars(valueStart, _editableValueLabel);
        }

        data.CursorPos = data.BufTextLen;
        data.SelectionStart = data.BufTextLen;
        data.SelectionEnd = data.BufTextLen;
        return 0;
    }

    private static void ProcessText(Instance instance)
    {
        SplitText(_text, out var filter, out _valueText, out _isValueMode);
        if (filter != _lastFilter)
        {
            _lastFilter = filter;
            UpdateMatches(filter);
            UpdateFocusedKind(instance);
        }

        UpdatePreview(instance);
    }

    private static void SplitText(string text, out string filter, out string valueText, out bool isValueMode)
    {
        var valueStart = GetValueStart(text);
        if (valueStart < 0)
        {
            filter = text;
            valueText = string.Empty;
            isValueMode = false;
            return;
        }

        var filterEnd = valueStart > 0 && text[valueStart - 1] == ' ' ? valueStart - 1 : valueStart;
        filter = text[..filterEnd];
        valueText = text[valueStart..].Trim();
        isValueMode = true;
    }

    /// <summary>
    /// The filter ends at the first space, sign or digit, because parameter names never start with those. A digit
    /// still belongs to the filter while it continues a parameter name ("F1"); "F 1" then sets F to 1.
    /// </summary>
    /// <returns>Index where the value starts, -1 while the text is only a filter</returns>
    private static int GetValueStart(string text)
    {
        for (var index = 0; index < text.Length; index++)
        {
            var c = text[index];
            if (c == ' ')
                return index + 1;

            var isValueStart = ValueStartCharacters.Contains(c)
                               || char.IsDigit(c) && (index == 0 || !AnyParameterStartsWith(text, index + 1));
            if (isValueStart)
                return index;
        }

        return -1;
    }

    /** Name or word prefix match for the first <paramref name="length"/> characters of the text */
    private static bool AnyParameterStartsWith(string text, int length)
    {
        var filter = text[..length];
        foreach (var entry in _entries)
        {
            var rank = GetMatchRank(entry.Name, entry.LowerInitials, filter);
            if (rank is 0 or 1)
                return true;
        }

        return false;
    }

    private static void UpdateMatches(string filter)
    {
        _matches.Clear();
        _matchedIds.Clear();
        _matchMasks.Clear();
        for (var rank = 0; rank <= LowestRank; rank++)
        {
            foreach (var entry in _entries)
            {
                if (GetMatchRank(entry.Name, entry.LowerInitials, filter) != rank)
                    continue;

                _matches.Add(entry.InputId);
                _matchedIds.Add(entry.InputId);
                if (filter.Length > 0)
                    _matchMasks[entry.InputId] = GetMatchMask(entry.Name, filter, rank);
            }
        }

        // An empty filter keeps the cursor where it is
        if (filter.Length > 0 && _matches.Count > 0)
            _focusedInputId = _matches[0];
    }

    /** Bit per character of the name that the filter matched, for underlining */
    private static ulong GetMatchMask(string name, string filter, int rank)
    {
        var start = -1;
        switch (rank)
        {
            case 0:
                start = 0;
                break;

            case 1:
                for (var index = 1; index < name.Length && start < 0; index++)
                {
                    if (IsWordStart(name, index)
                        && string.Compare(name, index, filter, 0, filter.Length, StringComparison.OrdinalIgnoreCase) == 0)
                        start = index;
                }

                break;

            case 2:
                var mask = 0UL;
                var matchedInitials = 0;
                for (var index = 0; index < name.Length && index < 64 && matchedInitials < filter.Length; index++)
                {
                    if (index == 0 ? char.IsLetterOrDigit(name[0]) : IsWordStart(name, index))
                    {
                        mask |= 1UL << index;
                        matchedInitials++;
                    }
                }

                return mask;

            default:
                start = name.IndexOf(filter, StringComparison.OrdinalIgnoreCase);
                break;
        }

        if (start < 0)
            return 0;

        var result = 0UL;
        for (var index = start; index < start + filter.Length && index < 64; index++)
        {
            result |= 1UL << index;
        }

        return result;
    }

    /// <returns>0 name prefix, 1 word prefix, 2 initials, 3 substring, -1 no match</returns>
    private static int GetMatchRank(string name, string lowerInitials, string filter)
    {
        if (filter.Length == 0 || name.StartsWith(filter, StringComparison.OrdinalIgnoreCase))
            return 0;

        for (var index = 1; index < name.Length; index++)
        {
            if (IsWordStart(name, index)
                && string.Compare(name, index, filter, 0, filter.Length, StringComparison.OrdinalIgnoreCase) == 0)
                return 1;
        }

        if (lowerInitials.StartsWith(filter, StringComparison.OrdinalIgnoreCase))
            return 2;

        return name.Contains(filter, StringComparison.OrdinalIgnoreCase) ? LowestRank : -1;
    }

    private static bool IsWordStart(string name, int index)
    {
        var c = name[index];
        var previous = name[index - 1];
        return char.IsUpper(c) && !char.IsUpper(previous)
               || char.IsLetterOrDigit(c) && !char.IsLetterOrDigit(previous);
    }

    private static string GetLowerInitials(string name)
    {
        var initials = new System.Text.StringBuilder();
        for (var index = 0; index < name.Length; index++)
        {
            if (index == 0 ? char.IsLetterOrDigit(name[0]) : IsWordStart(name, index))
                initials.Append(char.ToLowerInvariant(name[index]));
        }

        return initials.ToString();
    }
    #endregion

    #region cursor and commits
    private static Guid GetInitialFocus(Instance instance)
    {
        if (_lastEditedInputIds.TryGetValue(instance.SymbolChildId, out var lastEditedId)
            && TryGetSlot(instance, lastEditedId, out var lastEditedSlot)
            && !lastEditedSlot.HasInputConnections)
            return lastEditedId;

        foreach (var slot in instance.Inputs)
        {
            if (!slot.HasInputConnections)
                return slot.Id;
        }

        return _entries[0].InputId;
    }

    /// <summary>
    /// Applies a pending value and moves the cursor through all parameters. Without a typed value it only
    /// moves within the current filter matches.
    /// </summary>
    private static void CommitAndMove(Instance instance, int delta)
    {
        var hadValue = _isValueMode;
        Commit(instance);

        var list = hadValue ? AllInputIds() : _matches;
        if (list.Count == 0)
            return;

        var index = list.IndexOf(_focusedInputId);
        index = index < 0 ? 0 : Math.Clamp(index + delta, 0, list.Count - 1);
        _focusedInputId = list[index];

        if (hadValue)
        {
            UpdateMatches(string.Empty);
            StartNewEntry(instance);
        }
        else
        {
            UpdateFocusedKind(instance);
            UpdatePreview(instance);
        }
    }

    /** Esc steps back: an adjustment or typed value first, then the filter, then the popover. */
    private static void StepBack(Instance instance)
    {
        if (_isNudging || _text.Length > 0)
        {
            RevertPreview();
            if (_matches.Count != _entries.Count)
                UpdateMatches(string.Empty);

            StartNewEntry(instance);
            return;
        }

        Cancel(refocusGraph: true);
    }

    private static List<Guid> AllInputIds()
    {
        _allInputIds.Clear();
        foreach (var entry in _entries)
        {
            _allInputIds.Add(entry.InputId);
        }

        return _allInputIds;
    }

    private static void StartNewEntry(Instance instance)
    {
        SetInputText(string.Empty);
        UpdateFocusedKind(instance);
        UpdatePreview(instance);
    }

    /** A new widget id is the reliable way to replace the text of an ImGui input. */
    private static void SetInputText(string text)
    {
        _inputGeneration++;
        _inputLabel = "##parameterPrompt" + _inputGeneration;
        _text = text;
        if (text.Length == 0)
        {
            _lastText = string.Empty;
            _lastFilter = string.Empty;
            _valueText = string.Empty;
            _isValueMode = false;
        }

        RequestKeyboardFocus();
    }

    private static void RequestKeyboardFocus()
    {
        _needsKeyboardFocus = true;
        _focusRequestFrame = ImGui.GetFrameCount();
    }

    private static void MoveOptionFocus(Instance instance, int delta)
    {
        if (_optionMatches.Count == 0)
            return;

        _optionFocus = Math.Clamp(_optionFocus + delta, 0, _optionMatches.Count - 1);
        UpdatePreview(instance, keepOptionFocus: true);
    }

    private static void Commit(Instance instance)
    {
        _isNudging = false;
        if (_previewCommand != null)
        {
            UndoRedoStack.Add(_previewCommand);
            _previewCommand = null;
            MarkApplied(instance);
            return;
        }

    }

    private static void ResetToDefault(Instance instance)
    {
        RevertPreview();
        if (instance.Parent == null || !TryGetFocusedSlot(instance, out var slot) || slot.HasInputConnections)
            return;

        UndoRedoStack.AddAndExecute(new ResetInputToDefault(instance.Parent.Symbol, instance.SymbolChildId, slot.Input));
        MarkApplied(instance);
        if (_matches.Count != _entries.Count)
            UpdateMatches(string.Empty);

        StartNewEntry(instance);
    }

    /** Flashes the applied value and remembers the parameter for the next time the popover opens on this operator */
    private static void MarkApplied(Instance instance)
    {
        _appliedTimes[_focusedInputId] = ImGui.GetTime();
        _lastEditedInputIds[instance.SymbolChildId] = _focusedInputId;
        _valueLabelsNeedUpdate = true;
    }

    /// <summary>
    /// Closes the popover and opens the symbol browser for an operator connected to the focused input, like
    /// clicking the input in the Parameter Window.
    /// </summary>
    private static void InsertConnectedOperator(GraphUiContext context, Instance instance)
    {
        var hasTarget = TryGetFocusedSlot(instance, out var slot)
                        & SymbolUiRegistry.TryGetSymbolUi(_compositionSymbolId, out var compositionUi);
        Cancel(refocusGraph: false);

        if (!hasTarget || slot == null || compositionUi == null || !compositionUi.ChildUis.TryGetValue(instance.SymbolChildId, out var childUi))
            return;

        ((IGraphView)context.View).CreatePlaceHolderConnectedToInput(childUi, slot.Input.InputDefinition);
    }

    private static void Cancel(bool refocusGraph)
    {
        RevertPreview();
        Close(refocusGraph);
    }

    private static void Close(bool refocusGraph)
    {
        _previewCommand = null;
        _isNudging = false;
        _childId = Guid.Empty;
        _text = string.Empty;
        _matches.Clear();
        _matchedIds.Clear();
        _matchMasks.Clear();
        _refocusGraph = refocusGraph;
    }
    #endregion

    #region values
    private static void UpdateFocusedKind(Instance instance)
    {
        _valueKind = ValueKinds.Unsupported;
        _options.Clear();
        _focusedDescription = string.Empty;
        _editableValueLabel = string.Empty;
        if (!TryGetFocusedSlot(instance, out var slot))
            return;

        if (SymbolUiRegistry.TryGetSymbolUi(instance.Symbol.Id, out var symbolUi)
            && symbolUi.InputUis.TryGetValue(slot.Id, out var inputUi)
            && !string.IsNullOrEmpty(inputUi.Description))
        {
            var description = inputUi.Description;
            var lineEnd = description.IndexOf('\n');
            _focusedDescription = lineEnd < 0 ? description : description[..lineEnd];
        }

        _editableValueLabel = slot.Input.Value switch
                                  {
                                      InputValue<float> floatValue => floatValue.Value.ToString("0.#####", CultureInfo.InvariantCulture),
                                      InputValue<int> intValue     => intValue.Value.ToString(CultureInfo.InvariantCulture),
                                      _                            => string.Empty,
                                  };

        var value = slot.Input.Value;
        var enumType = value.ValueType.IsEnum ? value.ValueType : value is InputValue<int> ? slot.MappedType : null;
        if (enumType is { IsEnum: true })
        {
            _valueKind = ValueKinds.Options;
            var names = Enum.GetNames(enumType);
            var values = Enum.GetValues(enumType);
            for (var index = 0; index < names.Length; index++)
            {
                _options.Add(new EnumOption(names[index], Convert.ToInt32(values.GetValue(index), CultureInfo.InvariantCulture)));
            }

            return;
        }

        // All Vector4 parameters are edited as RGBA colors in TiXL
        _valueKind = value switch
                         {
                             InputValue<float> or InputValue<int>                                                         => ValueKinds.Number,
                             InputValue<Vector4>                                                                          => ValueKinds.Color,
                             InputValue<Vector2> or InputValue<Vector3> or InputValue<Int2> or InputValue<Int3>           => ValueKinds.Vector,
                             InputValue<bool>                                                                             => ValueKinds.Bool,
                             _                                                                                            => ValueKinds.Unsupported,
                         };
    }

    private static void UpdatePreview(Instance instance, bool keepOptionFocus = false)
    {
        RevertPreview();
        _valueCompletion = string.Empty;
        _valueError = string.Empty;
        _valueResult = string.Empty;

        if (!TryGetFocusedSlot(instance, out var slot) || instance.Parent == null || !_isValueMode)
        {
            UpdateTexts();
            return;
        }

        if (slot.HasInputConnections)
        {
            _valueError = "is connected,  Shift+I replaces the connection";
            UpdateTexts();
            return;
        }

        InputValue? newValue;
        switch (_valueKind)
        {
            case ValueKinds.Options:
                if (!keepOptionFocus)
                    UpdateOptionMatches(slot);

                if (_optionMatches.Count == 0)
                {
                    _valueError = "no matching option";
                    UpdateTexts();
                    return;
                }

                var option = _options[_optionMatches[_optionFocus]];
                _valueCompletion = option.Name.StartsWith(_valueText, StringComparison.OrdinalIgnoreCase)
                                       ? option.Name[_valueText.Length..]
                                       : "  → " + option.Name;

                // Staying on the current option is not an edit
                if (option.Value == GetCurrentOptionValue(slot))
                {
                    UpdateTexts();
                    return;
                }

                newValue = slot.Input.Value.ValueType.IsEnum
                               ? CreateEnumValue(slot.Input.Value.ValueType, option.Value)
                               : new InputValue<int>(option.Value);
                break;

            case ValueKinds.Bool when _valueText.Length == 0:
                newValue = new InputValue<bool>(!((InputValue<bool>)slot.Input.Value).Value);
                break;

            case ValueKinds.Unsupported:
                if (_valueText.Length > 0)
                    _valueError = "a " + slot.Input.Value.ValueType.Name + " can't be typed";

                UpdateTexts();
                return;

            default:
                if (_valueText.Length == 0)
                {
                    UpdateTexts();
                    return;
                }

                if (_valueKind == ValueKinds.Color && _valueText.IndexOf(' ') < 0
                    && ColorValueParsing.TryFindName(_valueText, out var colorName, out _))
                {
                    _valueCompletion = colorName[_valueText.Length..];
                }

                if (!TryParseValue(slot.Input.Value, _valueText, out newValue))
                {
                    // A single letter may still become a component or channel selector for Left/Right, and an
                    // expression ending with an operator is still being typed
                    var mayBeSelector = _valueText.Length == 1 && (ComponentLetters + ChannelLetters + "hsv").Contains(char.ToLowerInvariant(_valueText[0]));
                    var isIncomplete = ExpressionOperators.Contains(_valueText[^1]) || _valueText[^1] == '(';
                    if (!mayBeSelector && !isIncomplete)
                        _valueError = "invalid value";

                    UpdateTexts();
                    return;
                }

                // Show the result of a calculation
                if (_valueText.AsSpan(1).IndexOfAny(ExpressionOperators) >= 0 || _valueText[0] is '*' or '/' or '+' or '^' or '=')
                    _valueResult = "= " + FormatValue(newValue);

                break;
        }

        _previewOriginalLabel = FormatValue(slot.Input.Value);
        _previewCommand = new ChangeInputValueCommand(instance.Parent.Symbol, instance.SymbolChildId, slot.Input, null, instance);
        _previewCommand.AssignNewValue(newValue);
        _valueLabelsNeedUpdate = true;
        UpdateTexts();
    }

    private static void UpdateOptionMatches(IInputSlot slot)
    {
        _optionMatches.Clear();
        for (var rank = 0; rank <= LowestRank; rank++)
        {
            for (var index = 0; index < _options.Count; index++)
            {
                if (GetMatchRank(_options[index].Name, string.Empty, _valueText) == rank)
                    _optionMatches.Add(index);
            }
        }

        // Without a filter the list starts at the current option
        _optionFocus = 0;
        if (_valueText.Length > 0)
            return;

        var currentValue = GetCurrentOptionValue(slot);
        for (var index = 0; index < _optionMatches.Count; index++)
        {
            if (_options[_optionMatches[index]].Value == currentValue)
                _optionFocus = index;
        }
    }

    private static int GetCurrentOptionValue(IInputSlot slot)
    {
        return slot.Input.Value is InputValue<int> intValue
                   ? intValue.Value
                   : Convert.ToInt32(GetEnumValue(slot.Input.Value), CultureInfo.InvariantCulture);
    }

    private static void RevertPreview()
    {
        if (_previewCommand != null)
            _valueLabelsNeedUpdate = true;

        _previewCommand?.Undo();
        _previewCommand = null;
        _isNudging = false;
    }

    private static bool TryParseValue(InputValue current, string text, [NotNullWhen(true)] out InputValue? result)
    {
        result = null;
        switch (current)
        {
            case InputValue<float> floatValue:
                if (!TryEvaluateNumber(floatValue.Value, text, out var floatResult))
                    return false;

                result = new InputValue<float>((float)floatResult);
                return true;

            case InputValue<int> intValue:
                if (!TryEvaluateNumber(intValue.Value, text, out var intResult))
                    return false;

                result = new InputValue<int>((int)Math.Round(intResult));
                return true;

            case InputValue<bool>:
                if (!TryParseBool(text, out var boolResult))
                    return false;

                result = new InputValue<bool>(boolResult);
                return true;

            case InputValue<Vector2> v2:
                Span<double> c2 = [v2.Value.X, v2.Value.Y];
                if (!TryEvaluateComponents(c2, text))
                    return false;

                result = new InputValue<Vector2>(new Vector2((float)c2[0], (float)c2[1]));
                return true;

            case InputValue<Vector3> v3:
                Span<double> c3 = [v3.Value.X, v3.Value.Y, v3.Value.Z];
                if (!TryEvaluateComponents(c3, text))
                    return false;

                result = new InputValue<Vector3>(new Vector3((float)c3[0], (float)c3[1], (float)c3[2]));
                return true;

            case InputValue<Vector4> v4:
                if (!TryEvaluateColor(v4.Value, text, out var color))
                    return false;

                result = new InputValue<Vector4>(color);
                return true;

            case InputValue<Int2> i2:
                Span<double> ci2 = [i2.Value.X, i2.Value.Y];
                if (!TryEvaluateComponents(ci2, text))
                    return false;

                result = new InputValue<Int2>(new Int2((int)Math.Round(ci2[0]), (int)Math.Round(ci2[1])));
                return true;

            case InputValue<Int3> i3:
                Span<double> ci3 = [i3.Value.X, i3.Value.Y, i3.Value.Z];
                if (!TryEvaluateComponents(ci3, text))
                    return false;

                result = new InputValue<Int3>(new Int3((int)Math.Round(ci3[0]), (int)Math.Round(ci3[1]), (int)Math.Round(ci3[2])));
                return true;
        }

        return false;
    }

    /// <summary>
    /// "0 1 0" sets components in order, a single value ("*2") applies to all of them, and a component letter
    /// addresses one: "x 1", "y+2". Components that aren't mentioned keep their value.
    /// </summary>
    private static bool TryEvaluateComponents(Span<double> components, string text, string componentLetters = ComponentLetters)
    {
        var tokens = text.Split([' ', ','], StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 0)
            return false;

        if (tokens.Length == 1 && componentLetters.IndexOf(char.ToLowerInvariant(tokens[0][0])) < 0)
        {
            for (var index = 0; index < components.Length; index++)
            {
                if (!TryEvaluateNumber(components[index], tokens[0], out components[index]))
                    return false;
            }

            return true;
        }

        var positionalIndex = 0;
        for (var tokenIndex = 0; tokenIndex < tokens.Length; tokenIndex++)
        {
            var token = tokens[tokenIndex];
            var componentIndex = componentLetters.IndexOf(char.ToLowerInvariant(token[0]));
            if (componentIndex >= 0)
            {
                token = token.Length > 1 ? token[1..] : tokenIndex + 1 < tokens.Length ? tokens[++tokenIndex] : string.Empty;
            }
            else
            {
                componentIndex = positionalIndex++;
            }

            if (componentIndex >= components.Length || !TryEvaluateNumber(components[componentIndex], token, out components[componentIndex]))
                return false;
        }

        return true;
    }

    /// <summary>
    /// A color name or hex code ("orange", "#f80") optionally followed by channels ("orange a 0.5"), or numbers like
    /// vectors where r g b a address channels. A single number ("*0.5") changes RGB and keeps alpha.
    /// </summary>
    private static bool TryEvaluateColor(Vector4 current, string text, out Vector4 color)
    {
        color = current;
        var firstTokenEnd = text.IndexOf(' ');
        var firstToken = firstTokenEnd < 0 ? text : text[..firstTokenEnd];
        var rest = firstTokenEnd < 0 ? string.Empty : text[(firstTokenEnd + 1)..];

        // A single channel letter with a value ("b 0.5") addresses that channel, not a name starting with it
        var isChannel = firstToken.Length == 1 && ChannelLetters.IndexOf(char.ToLowerInvariant(firstToken[0])) >= 0 && rest.Length > 0;
        if (!isChannel
            && (ColorValueParsing.TryParseHex(firstToken, out var parsedColor)
                || ColorValueParsing.TryFindName(firstToken, out _, out parsedColor)))
        {
            color = parsedColor;
            if (rest.Length == 0)
                return true;

            text = rest;
        }
        else if (firstTokenEnd < 0 && ChannelLetters.IndexOf(char.ToLowerInvariant(text[0])) < 0)
        {
            Span<double> rgb = [color.X, color.Y, color.Z];
            if (!TryEvaluateComponents(rgb, text))
                return false;

            color = new Vector4((float)rgb[0], (float)rgb[1], (float)rgb[2], color.W);
            return true;
        }

        Span<double> components = [color.X, color.Y, color.Z, color.W];
        if (!TryEvaluateComponents(components, text, ChannelLetters))
            return false;

        color = new Vector4((float)components[0], (float)components[1], (float)components[2], (float)components[3]);
        return true;
    }

    /// <summary>
    /// A number or expression ("100*2+1"). A leading + * / or ^ applies it to the current value, which vectors and
    /// colors use for all components ("*2"); a leading "=" makes it absolute ("=-5").
    /// </summary>
    private static bool TryEvaluateNumber(double current, string text, out double result)
    {
        result = current;
        if (text.Length == 0)
            return false;

        if (text[0] == '=')
            return ValueExpression.TryEvaluate(text[1..], out result);

        if (text[0] is '+' or '*' or '/' or '^')
            return ValueExpression.TryEvaluate(current.ToString("R", CultureInfo.InvariantCulture) + text, out result);

        return ValueExpression.TryEvaluate(text, out result);
    }

    private static bool TryParseBool(string text, out bool result)
    {
        switch (text.ToLowerInvariant())
        {
            case "1" or "true" or "on" or "y" or "yes":
                result = true;
                return true;
            case "0" or "false" or "off" or "n" or "no":
                result = false;
                return true;
            default:
                result = false;
                return false;
        }
    }

    private static InputValue CreateEnumValue(Type enumType, int value)
    {
        var valueType = typeof(InputValue<>).MakeGenericType(enumType);
        return (InputValue)Activator.CreateInstance(valueType, Enum.ToObject(enumType, value))!;
    }

    private static object GetEnumValue(InputValue value)
    {
        return value.GetType().GetField("Value")?.GetValue(value) ?? 0;
    }

    private static string FormatValue(InputValue value)
    {
        return value switch
                   {
                       InputValue<float> f   => FormatNumber(f.Value),
                       InputValue<int> i     => GetOptionName(i.Value) ?? i.Value.ToString(CultureInfo.InvariantCulture),
                       InputValue<bool> b    => b.Value ? "true" : "false",
                       InputValue<Vector2> v => $"{FormatNumber(v.Value.X)} {FormatNumber(v.Value.Y)}",
                       InputValue<Vector3> v => $"{FormatNumber(v.Value.X)} {FormatNumber(v.Value.Y)} {FormatNumber(v.Value.Z)}",
                       InputValue<Vector4> v => $"{FormatNumber(v.Value.X)} {FormatNumber(v.Value.Y)} {FormatNumber(v.Value.Z)} {FormatNumber(v.Value.W)}",
                       InputValue<Int2> v    => $"{v.Value.X} {v.Value.Y}",
                       InputValue<Int3> v    => $"{v.Value.X} {v.Value.Y} {v.Value.Z}",
                       InputValue<string> t  => FormatString(t.Value),
                       _ when value.ValueType.IsEnum => GetEnumValue(value).ToString() ?? string.Empty,
                       _                     => value.ValueType.Name,
                   };
    }

    /** Like <see cref="FormatValue"/>, with dropdown names for any int parameter mapped to an enum */
    private static string FormatSlotValue(IInputSlot slot)
    {
        if (slot.HasInputConnections)
            return "← connected";

        if (slot.Input.Value is InputValue<int> intValue && slot.MappedType is { IsEnum: true } mappedType)
            return Enum.GetName(mappedType, intValue.Value) ?? intValue.Value.ToString(CultureInfo.InvariantCulture);

        return FormatValue(slot.Input.Value);
    }

    private static string FormatString(string? text)
    {
        if (text == null)
            return "\"\"";

        var firstLine = text.AsSpan();
        var lineEnd = firstLine.IndexOf('\n');
        if (lineEnd >= 0)
            firstLine = firstLine[..lineEnd];

        return firstLine.Length > MaxStringLabelLength
                   ? "\"" + firstLine[..MaxStringLabelLength].ToString() + "...\""
                   : "\"" + firstLine.ToString() + "\"";
    }

    private static string FormatNumber(float value) => value.ToString("0.####", CultureInfo.InvariantCulture);

    private static string? GetOptionName(int value)
    {
        if (_valueKind != ValueKinds.Options)
            return null;

        foreach (var option in _options)
        {
            if (option.Value == value)
                return option.Name;
        }

        return null;
    }
    #endregion

    #region suggestions and keys
    /// <summary>
    /// Rebuilds the faint text after the typed value and the key hints of the help area. Called on changes only.
    /// </summary>
    private static void UpdateTexts()
    {
        KeyHintText.Parse(GetValueGhost(), _valueGhostSegments);
        KeyHintText.Parse(GetKeysLine(), _keysSegments);
    }

    private static string GetValueGhost()
    {
        if (_valueResult.Length > 0)
            return _valueResult;

        if (_valueCompletion.Length > 0)
            return _valueKind == ValueKinds.Options ? "{" + _valueCompletion + "}   [↑↓] more" : "{" + _valueCompletion + "}";

        if (_valueText.Length > 0)
            return string.Empty;

        return _valueKind switch
                   {
                       ValueKinds.Number      => _editableValueLabel + "    {* / + ^} calculate   {-5} negative",
                       ValueKinds.Vector      => "{0 0 0}   {x 1} one   {*2} all   {x}[←→]",
                       ValueKinds.Color       => "{orange}   {#f80}   {a 0.5}   {h} {s} {r} {g} {b} {a}[←→]",
                       ValueKinds.Options     => "[↑↓] options",
                       ValueKinds.Bool        => "toggled",
                       ValueKinds.Unsupported => "[⇧I] insert connected operator",
                       _                      => string.Empty,
                   };
    }

    private static string GetKeysLine()
    {
        if (_isNudging)
            return "[←→] Adjust   [Shift] Fine   [Ctrl] Coarse   [↓] Apply and Next   [Enter] Apply   [Esc] Revert";

        if (_isValueMode)
        {
            return _valueKind == ValueKinds.Options
                       ? "[↑↓] Option   [⇥] Apply and Next   [Enter] Apply   [Esc] Back"
                       : "[↓] Apply and Next   [↑] Apply and Previous   [Enter] Apply   [⇧R] Reset   [Esc] Back";
        }

        var escape = _text.Length > 0 ? "[Esc] Back" : "[Esc] Close";
        return _valueKind switch
                   {
                       ValueKinds.Number      => "[↑↓] Parameter   [⇥] Edit   [←→] Adjust   [⇧R] Reset   [⇧I] Input   type to filter   " + escape,
                       ValueKinds.Color       => "[↑↓] Parameter   [⇥] Edit   [←→] Brightness   [⇧R] Reset   [⇧I] Input   type to filter   " + escape,
                       ValueKinds.Options     => "[↑↓] Parameter   [⇥] Options   [←→] Change   [⇧R] Reset   [⇧I] Input   type to filter   " + escape,
                       ValueKinds.Bool        => "[↑↓] Parameter   [⇥] [←→] Toggle   [⇧R] Reset   [⇧I] Input   type to filter   " + escape,
                       ValueKinds.Unsupported => "[↑↓] Parameter   [⇧I] Insert Connected   [⇧R] Reset   type to filter   " + escape,
                       _                      => "[↑↓] Parameter   [⇥] Edit   [⇧R] Reset   [⇧I] Input   type to filter   " + escape,
                   };
    }
    #endregion

    private static bool TryGetInstance(GraphUiContext context, [NotNullWhen(true)] out Instance? instance)
    {
        var composition = context.CompositionInstance;
        instance = null;
        return composition.Symbol.Id == _compositionSymbolId
               && composition.Children.TryGetChildInstance(_childId, out instance, allowCreate: false);
    }

    private static bool TryGetFocusedSlot(Instance instance, [NotNullWhen(true)] out IInputSlot? slot)
    {
        foreach (var inputSlot in instance.Inputs)
        {
            if (inputSlot.Id != _focusedInputId)
                continue;

            slot = inputSlot;
            return true;
        }

        slot = null;
        return false;
    }

    private static bool TryGetSlot(Instance instance, Guid inputId, [NotNullWhen(true)] out IInputSlot? slot)
    {
        foreach (var inputSlot in instance.Inputs)
        {
            if (inputSlot.Id != inputId)
                continue;

            slot = inputSlot;
            return true;
        }

        slot = null;
        return false;
    }

    private const int MaxTextLength = 64;
    private const int MaxVisibleRows = 9;
    private const int FocusRowSlot = 3;
    private const float PopoverWidthPx = 460;
    private const float PopoverPaddingPx = 8;
    private const float PopoverRoundingPx = 6;
    private const float RowPaddingPx = 6;
    private const float ValueGapPx = 10;
    private const float ScrollbarWidthPx = 4;
    private const float SliderGapPx = 24;
    private const double CaretBlinkSec = 1.0;
    private const double AppliedFlashSec = 0.6;
    private const double ValueLabelsUpdateIntervalSec = 0.2;
    private const int MaxStringLabelLength = 30;

    /** Slider scale for color channels and properties, which mostly live in 0..1 */
    private const float ColorNudgeScale = 0.002f;
    private const int LowestRank = 3;
    private const string ValueStartCharacters = "+-*/.#=";
    private const string ExpressionOperators = "*/+-^%";

    /** Operators that continue from the current value when typed first; "-" starts a negative number instead */
    private const string ContinuingOperators = "*/+^%";
    private const string ComponentLetters = "xyzw";
    private const string ChannelLetters = "rgba";

    private static readonly unsafe ImGuiInputTextCallback _inputCallback = OnInputCallback;

    private static readonly List<ParameterEntry> _entries = new(32);
    private static readonly List<Guid> _matches = new(32);
    private static readonly List<Guid> _allInputIds = new(32);
    private static readonly HashSet<Guid> _matchedIds = [];
    private static readonly Dictionary<Guid, ulong> _matchMasks = new(32);
    private static readonly Dictionary<Guid, string> _valueLabels = new(32);
    private static readonly Dictionary<Guid, double> _appliedTimes = new(8);
    private static readonly Dictionary<Guid, Guid> _lastEditedInputIds = new();
    private static readonly List<EnumOption> _options = new(16);
    private static readonly List<int> _optionMatches = new(16);

    private static Guid _childId;
    private static Guid _compositionSymbolId;
    private static int _selectionChangeCounterAtOpen;
    private static int _openFrame;
    private static Guid _focusedInputId;
    private static ValueKinds _valueKind;
    private static string _focusedDescription = string.Empty;
    private static string _editableValueLabel = string.Empty;
    private static bool _refocusGraph;
    private static ImRect _popoverRect;

    private static string _text = string.Empty;
    private static string _lastText = string.Empty;
    private static string _lastFilter = string.Empty;
    private static string _valueText = string.Empty;
    private static bool _isValueMode;
    private static int _inputGeneration;
    private static string _inputLabel = "##parameterPrompt";
    private static bool _needsKeyboardFocus;
    private static int _focusRequestFrame;
    private static bool _isInputActive;

    private static int _optionFocus;
    private static string _valueCompletion = string.Empty;
    private static string _valueResult = string.Empty;
    private static string _valueError = string.Empty;
    private static ChangeInputValueCommand? _previewCommand;
    private static bool _valueLabelsNeedUpdate;
    private static double _nextValueLabelsUpdateTime;

    private static bool _isNudging;
    private static bool _restartNudging;
    private static bool _isNudgingInt;
    private static NudgeTargets _nudgeTarget;
    private static string _nudgeTargetLabel = string.Empty;
    private static int _nudgeComponent;
    private static InputValue? _nudgeBaseValue;
    private static Vector4 _nudgeHsva;
    private static double _nudgeValue;
    private static double _nudgeMin;
    private static double _nudgeMax;
    private static float _nudgeScale;
    private static bool _nudgeClampMin;
    private static bool _nudgeClampMax;
    private static string _nudgeOriginalLabel = string.Empty;
    private static string _nudgeLabel = string.Empty;
    private static string _previewOriginalLabel = string.Empty;

    private static readonly List<KeyHintText.Segment> _valueGhostSegments = new(8);
    private static readonly List<KeyHintText.Segment> _keysSegments = new(8);
}
