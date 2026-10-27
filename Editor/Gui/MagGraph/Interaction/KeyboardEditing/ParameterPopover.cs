#nullable enable
using System.Diagnostics.CodeAnalysis;
using System.Text;
using ImGuiNET;
using T3.Core.DataTypes.Vector;
using T3.Core.Operator;
using T3.Core.Operator.Slots;
using T3.Editor.Gui.MagGraph.States;
using T3.Editor.Gui.UiHelpers;
using T3.Editor.UiModel;
using T3.Editor.UiModel.Commands;
using T3.Editor.UiModel.Commands.Graph;

namespace T3.Editor.Gui.MagGraph.Interaction.KeyboardEditing;

/// <summary>
/// Keyboard popover for editing the parameters of the selected operator, opened with S. It lists the parameters
/// with their values and shrinks to the matches of the filter. Up/Down move through the list like in a
/// spreadsheet: typing a value previews it live, and Down applies it and moves on.
/// Letters filter the parameters (name prefix, word prefix, initials) without changing their order. Tab (or Space) edits the value: numbers and
/// expressions ("10", "*2" which becomes "100*2", "=-5"), vectors ("0 1 0", "x 1", "*2" for all), colors
/// ("orange", "#f80", "a 0.5"), bools toggle, and on a dropdown the value filters its options. A leading "-" is a
/// negative number; the other operators continue from the current value. Shift+R resets the parameter to its
/// default, Shift+I inserts an operator connected to it; both keep letters free for values.
/// Left/Right change the value: numbers with the infinity slider (Shift fine, Ctrl coarse), "x" then Left/Right a
/// vector component, on colors brightness, or "h" hue, "s" saturation and "r g b a" channels; dropdowns step;
/// bools toggle. The input has no caret: typing always appends.
/// Enter applies and closes; Esc steps back: the typed value, then the filter, then the popover.
/// </summary>
internal static partial class ParameterPopover
{
    private readonly record struct ParameterEntry(Guid InputId, string Name, string LowerInitials);

    private readonly record struct EnumOption(string Name, int Value);

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
        _firstVisibleRow = 0;
        _maxHelpLineCount = 1;
        _dampedListHeight = 0;
        _rowAnimations.Clear();
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

    /// <summary>
    /// Keeps the matches in parameter order, so rows don't change places while filtering. The best ranked match
    /// gets the focus, the earliest one among equals.
    /// </summary>
    private static void UpdateMatches(string filter)
    {
        _matches.Clear();
        _matchedIds.Clear();
        _matchMasks.Clear();
        var bestRank = int.MaxValue;
        var bestMatchId = Guid.Empty;
        foreach (var entry in _entries)
        {
            var rank = GetMatchRank(entry.Name, entry.LowerInitials, filter);
            if (rank < 0)
                continue;

            _matches.Add(entry.InputId);
            _matchedIds.Add(entry.InputId);
            if (filter.Length > 0)
                _matchMasks[entry.InputId] = GetMatchMask(entry.Name, filter, rank);

            if (rank < bestRank)
            {
                bestRank = rank;
                bestMatchId = entry.InputId;
            }
        }

        // An empty filter keeps the cursor where it is
        if (filter.Length > 0 && bestMatchId != Guid.Empty)
            _focusedInputId = bestMatchId;
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
        if (_isNudging || _isValueMode)
        {
            // Keep the filter, so the focus stays on the same parameter
            RevertPreview();
            var filter = _lastFilter;
            SetInputText(filter);
            _lastText = filter;
            _isValueMode = false;
            _valueText = string.Empty;
            UpdatePreview(instance);
            return;
        }

        if (_text.Length > 0)
        {
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
        _inputLabel = "##parameterPopover" + _inputGeneration;
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
    private const int LowestRank = 3;
    private const string ValueStartCharacters = "+-*/.#=";
    private const string ExpressionOperators = "*/+-^%";

    /** Operators that continue from the current value when typed first; "-" starts a negative number instead */
    private const string ContinuingOperators = "*/+^%";

    private static readonly unsafe ImGuiInputTextCallback _inputCallback = OnInputCallback;
    private static readonly List<ParameterEntry> _entries = new(32);
    private static readonly List<Guid> _matches = new(32);
    private static readonly List<Guid> _allInputIds = new(32);
    private static readonly HashSet<Guid> _matchedIds = [];
    private static readonly Dictionary<Guid, ulong> _matchMasks = new(32);
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
    private static string _text = string.Empty;
    private static string _lastText = string.Empty;
    private static string _lastFilter = string.Empty;
    private static string _valueText = string.Empty;
    private static bool _isValueMode;
    private static int _inputGeneration;
    private static string _inputLabel = "##parameterPopover";
    private static bool _needsKeyboardFocus;
    private static int _focusRequestFrame;
    private static bool _isInputActive;
    private static int _optionFocus;
    private static string _valueCompletion = string.Empty;
    private static string _valueResult = string.Empty;
    private static string _valueError = string.Empty;
    private static ChangeInputValueCommand? _previewCommand;
    private static string _previewOriginalLabel = string.Empty;
    private static readonly List<KeyHintText.Segment> _valueGhostSegments = new(8);
    private static readonly List<KeyHintText.Segment> _keysSegments = new(8);
}
