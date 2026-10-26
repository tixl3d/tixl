#nullable enable
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using ImGuiNET;
using T3.Core.DataTypes.Vector;
using T3.Core.Operator;
using T3.Core.Operator.Slots;
using T3.Editor.Gui.MagGraph.States;
using T3.Editor.Gui.Styling;
using T3.Editor.UiModel.Commands;
using T3.Editor.UiModel.Commands.Graph;

namespace T3.Editor.Gui.MagGraph.Interaction;

/// <summary>
/// Keyboard prompt for setting a parameter of the selected operator. Typed letters filter its parameters in the
/// Parameter Window (name prefix, then word prefix, then initials); Up/Down move the focus. A space, digit or sign
/// ends the filter and starts the value, which is previewed live: "c 100", "c100", "le +.01", "s *2".
/// "r" after a space resets to default. Enter applies as one undo step; Esc reverts and closes.
/// </summary>
internal static class ParameterPrompt
{
    internal enum RowStates
    {
        None,
        Focused,
        Matching,
        NotMatching,
    }

    private readonly record struct ParameterEntry(Guid InputId, string Name, string LowerInitials);

    internal static bool IsOpen => _childId != Guid.Empty;

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

        _childId = instance.SymbolChildId;
        _compositionSymbolId = instance.Parent.Symbol.Id;
        _selectionChangeCounterAtOpen = context.Selector.ChangeCounter;
        _openFrame = ImGui.GetFrameCount();
        _text = string.Empty;
        _lastText = string.Empty;
        _lastFilter = string.Empty;
        _valueText = string.Empty;
        _needsKeyboardFocus = true;
        UpdateMatches(string.Empty);
        UpdateStatus(instance);
    }

    internal static void Draw(GraphUiContext context)
    {
        if (!IsOpen)
            return;

        if (context.Selector.ChangeCounter != _selectionChangeCounterAtOpen
            || !TryGetInstance(context, out var instance))
        {
            Cancel();
            return;
        }

        // The key that opened the prompt is still in this frame's character queue and would be typed into it.
        if (ImGui.GetFrameCount() == _openFrame)
            return;

        var scale = T3Ui.UiScaleFactor;
        var padding = 8 * scale;
        var windowPos = ImGui.GetWindowPos();
        var windowSize = ImGui.GetWindowSize();
        var frameHeight = ImGui.GetFrameHeight();
        var min = new Vector2(windowPos.X + padding, windowPos.Y + windowSize.Y - frameHeight - 3 * padding);
        var max = new Vector2(windowPos.X + windowSize.X - padding, windowPos.Y + windowSize.Y - padding);
        ImGui.GetWindowDrawList().AddRectFilled(min, max, UiColors.BackgroundPopup, 4 * scale);

        ImGui.SetCursorScreenPos(min + new Vector2(padding, padding));
        CustomComponents.StylizedText("Set", Fonts.FontNormal, UiColors.TextMuted);
        ImGui.SameLine();

        if (_needsKeyboardFocus)
        {
            ImGui.SetKeyboardFocusHere();
            _needsKeyboardFocus = false;
        }

        ImGui.SetNextItemWidth(InputWidth * scale);
        ImGui.InputText("##parameterPrompt", ref _text, MaxTextLength);
        var isInputActive = ImGui.IsItemActive();

        ImGui.SameLine(0, padding);
        CustomComponents.StylizedText(_statusLine, Fonts.FontNormal, _statusColor);
        ImGui.SameLine(0, 2 * padding);
        CustomComponents.StylizedText(HintsLine, Fonts.FontSmall, UiColors.TextMuted);

        if (ImGui.IsKeyPressed(ImGuiKey.Escape, false))
        {
            Cancel();
            return;
        }

        if (ImGui.IsKeyPressed(ImGuiKey.Enter, false) || ImGui.IsKeyPressed(ImGuiKey.KeypadEnter, false))
        {
            Apply(instance);
            Close();
            return;
        }

        // Clicking elsewhere ends the prompt
        if (!isInputActive && ImGui.GetFrameCount() > _openFrame + 2)
        {
            Cancel();
            return;
        }

        if (ImGui.IsKeyPressed(ImGuiKey.UpArrow))
            MoveFocus(instance, -1);

        if (ImGui.IsKeyPressed(ImGuiKey.DownArrow))
            MoveFocus(instance, 1);

        if (_text != _lastText)
        {
            _lastText = _text;
            SplitText(_text, out var filter, out _valueText);
            if (filter != _lastFilter)
            {
                _lastFilter = filter;
                UpdateMatches(filter);
            }

            UpdatePreview(instance);
        }
    }

    /** How the Parameter Window should draw a row while the prompt is open */
    internal static RowStates GetRowState(Instance instance, Guid inputId)
    {
        if (!IsOpen || instance.SymbolChildId != _childId || instance.Parent?.Symbol.Id != _compositionSymbolId)
            return RowStates.None;

        if (_matches.Count > 0 && _matches[_focusIndex] == inputId)
            return RowStates.Focused;

        return _matchedIds.Contains(inputId) ? RowStates.Matching : RowStates.NotMatching;
    }

    /** True once after the focused row changed, so the Parameter Window can scroll it into view */
    internal static bool ConsumeScrollRequest()
    {
        if (!_scrollToFocusedRow)
            return false;

        _scrollToFocusedRow = false;
        return true;
    }

    private static bool TryGetInstance(GraphUiContext context, [NotNullWhen(true)] out Instance? instance)
    {
        var composition = context.CompositionInstance;
        instance = null;
        return composition.Symbol.Id == _compositionSymbolId
               && composition.Children.TryGetChildInstance(_childId, out instance, allowCreate: false);
    }

    private static bool TryGetFocusedSlot(Instance instance, [NotNullWhen(true)] out IInputSlot? slot)
    {
        slot = null;
        if (_matches.Count == 0)
            return false;

        var inputId = _matches[_focusIndex];
        foreach (var inputSlot in instance.Inputs)
        {
            if (inputSlot.Id != inputId)
                continue;

            slot = inputSlot;
            return true;
        }

        return false;
    }

    private static void MoveFocus(Instance instance, int delta)
    {
        if (_matches.Count == 0)
            return;

        RevertPreview();
        _focusIndex = (_focusIndex + delta + _matches.Count) % _matches.Count;
        _scrollToFocusedRow = true;
        UpdatePreview(instance);
    }

    /// <summary>
    /// The filter ends at the first space, digit or sign, because parameter names never start with those.
    /// </summary>
    private static void SplitText(string text, out string filter, out string valueText)
    {
        for (var index = 0; index < text.Length; index++)
        {
            var c = text[index];
            if (c == ' ')
            {
                filter = text[..index];
                valueText = text[(index + 1)..].Trim();
                return;
            }

            if (char.IsDigit(c) || ValueStartCharacters.Contains(c))
            {
                filter = text[..index];
                valueText = text[index..].Trim();
                return;
            }
        }

        filter = text;
        valueText = string.Empty;
    }

    private static void UpdateMatches(string filter)
    {
        _matches.Clear();
        _matchedIds.Clear();
        for (var rank = 0; rank <= LowestRank; rank++)
        {
            foreach (var entry in _entries)
            {
                if (GetMatchRank(entry, filter) != rank)
                    continue;

                _matches.Add(entry.InputId);
                _matchedIds.Add(entry.InputId);
            }
        }

        _focusIndex = 0;
        _scrollToFocusedRow = true;
    }

    /// <returns>0 name prefix, 1 word prefix, 2 initials, 3 substring, -1 no match</returns>
    private static int GetMatchRank(ParameterEntry entry, string filter)
    {
        if (filter.Length == 0 || entry.Name.StartsWith(filter, StringComparison.OrdinalIgnoreCase))
            return 0;

        var name = entry.Name;
        for (var index = 1; index < name.Length; index++)
        {
            if (IsWordStart(name, index)
                && string.Compare(name, index, filter, 0, filter.Length, StringComparison.OrdinalIgnoreCase) == 0)
                return 1;
        }

        if (entry.LowerInitials.StartsWith(filter, StringComparison.OrdinalIgnoreCase))
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

    private static void UpdatePreview(Instance instance)
    {
        RevertPreview();
        _isResetPending = false;

        if (!TryGetFocusedSlot(instance, out var slot) || _valueText.Length == 0 || instance.Parent == null)
        {
            UpdateStatus(instance);
            return;
        }

        if (_valueText == "r")
        {
            _isResetPending = true;
            UpdateStatus(instance, "reset to default");
            return;
        }

        if (slot.HasInputConnections)
        {
            UpdateStatus(instance, "is connected", isError: true);
            return;
        }

        if (!TryParseValue(slot.Input.Value, _valueText, out var newValue, out var error))
        {
            UpdateStatus(instance, error, isError: true);
            return;
        }

        // Status first, while the slot still holds the original value
        UpdateStatus(instance, "→ " + FormatValue(newValue));
        _previewCommand = new ChangeInputValueCommand(instance.Parent.Symbol, instance.SymbolChildId, slot.Input, null, instance);
        _previewCommand.AssignNewValue(newValue);
    }

    private static void RevertPreview()
    {
        _previewCommand?.Undo();
        _previewCommand = null;
    }

    private static void Apply(Instance instance)
    {
        if (_previewCommand != null)
        {
            UndoRedoStack.Add(_previewCommand);
            _previewCommand = null;
            return;
        }

        if (_isResetPending && instance.Parent != null && TryGetFocusedSlot(instance, out var slot))
            UndoRedoStack.AddAndExecute(new ResetInputToDefault(instance.Parent.Symbol, instance.SymbolChildId, slot.Input));
    }

    private static void Cancel()
    {
        RevertPreview();
        Close();
    }

    private static void Close()
    {
        _previewCommand = null;
        _childId = Guid.Empty;
        _text = string.Empty;
        _matches.Clear();
        _matchedIds.Clear();
    }

    private static bool TryParseValue(InputValue current, string text, [NotNullWhen(true)] out InputValue? result, out string error)
    {
        result = null;
        error = string.Empty;
        switch (current)
        {
            case InputValue<float> floatValue:
                if (!TryEvaluateNumber(floatValue.Value, text, out var floatResult))
                    break;

                result = new InputValue<float>((float)floatResult);
                return true;

            case InputValue<int> intValue:
                if (!TryEvaluateNumber(intValue.Value, text, out var intResult))
                    break;

                result = new InputValue<int>((int)Math.Round(intResult));
                return true;

            case InputValue<bool>:
                if (!TryParseBool(text, out var boolResult))
                    break;

                result = new InputValue<bool>(boolResult);
                return true;

            default:
                error = "typing a " + current.ValueType.Name + " isn't supported yet";
                return false;
        }

        error = "invalid value";
        return false;
    }

    /** Leading +, * or / apply relative to the current value; anything else (including -5) is absolute. */
    private static bool TryEvaluateNumber(double current, string text, out double result)
    {
        result = current;
        if (text.Length == 0)
            return false;

        var operation = text[0];
        var isRelative = operation is '+' or '*' or '/';
        var numberText = isRelative ? text[1..] : text;
        if (!double.TryParse(numberText, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
            return false;

        switch (operation)
        {
            case '+':
                result = current + number;
                return true;
            case '*':
                result = current * number;
                return true;
            case '/':
                if (number == 0)
                    return false;

                result = current / number;
                return true;
            default:
                result = number;
                return true;
        }
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

    private static string FormatValue(InputValue value)
    {
        return value switch
                   {
                       InputValue<float> f => f.Value.ToString("0.####", CultureInfo.InvariantCulture),
                       InputValue<int> i   => i.Value.ToString(CultureInfo.InvariantCulture),
                       InputValue<bool> b  => b.Value ? "true" : "false",
                       _                   => value.ValueType.Name,
                   };
    }

    /** The status line is rebuilt only on changes, not per frame. */
    private static void UpdateStatus(Instance instance, string? suffix = null, bool isError = false)
    {
        _statusColor = isError ? UiColors.StatusAttention : UiColors.Text;
        if (!TryGetFocusedSlot(instance, out var slot))
        {
            _statusLine = "no matching parameter";
            _statusColor = UiColors.StatusAttention;
            return;
        }

        var current = FormatValue(slot.Input.Value);
        _statusLine = suffix == null
                          ? $"{slot.Input.Name} = {current}"
                          : $"{slot.Input.Name} = {current}  {suffix}";
    }

    private const int MaxTextLength = 64;
    private const float InputWidth = 120;
    private const int LowestRank = 3;
    private const string ValueStartCharacters = "+-*/.";
    private const string HintsLine = "Up/Down  Parameter     Enter  Apply     Esc  Cancel     r  Reset";

    private static readonly List<ParameterEntry> _entries = new(32);
    private static readonly List<Guid> _matches = new(32);
    private static readonly HashSet<Guid> _matchedIds = [];

    private static Guid _childId;
    private static Guid _compositionSymbolId;
    private static int _selectionChangeCounterAtOpen;
    private static int _openFrame;
    private static string _text = string.Empty;
    private static string _lastText = string.Empty;
    private static string _lastFilter = string.Empty;
    private static string _valueText = string.Empty;
    private static int _focusIndex;
    private static bool _needsKeyboardFocus;
    private static bool _scrollToFocusedRow;
    private static bool _isResetPending;
    private static ChangeInputValueCommand? _previewCommand;
    private static string _statusLine = string.Empty;
    private static Color _statusColor = UiColors.Text;
}
