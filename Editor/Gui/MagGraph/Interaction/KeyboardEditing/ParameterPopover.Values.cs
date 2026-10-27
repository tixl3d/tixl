#nullable enable
using System.Globalization;
using T3.Core.DataTypes.Vector;
using T3.Core.Operator;
using T3.Core.Operator.Slots;
using T3.Editor.Gui.UiHelpers;
using T3.Editor.UiModel;
using T3.Editor.UiModel.Commands.Graph;
using static T3.Editor.Gui.MagGraph.Interaction.KeyboardEditing.ParameterValueParsing;

namespace T3.Editor.Gui.MagGraph.Interaction.KeyboardEditing;

internal static partial class ParameterPopover
{
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

    /** Only keys that aren't self-evident: Up/Down and Esc are left out. */
    private static string GetKeysLine()
    {
        if (_isNudging)
            return "[←→] Adjust   [Shift] Fine   [Ctrl] Coarse   [Enter] Apply";

        if (_isValueMode)
        {
            return _valueKind == ValueKinds.Options
                       ? "[⇥] Apply and Next   [Enter] Apply"
                       : "[Enter] Apply   [⇧R] Reset   [⇧I] Input";
        }

        return _valueKind switch
                   {
                       ValueKinds.Number      => "Type to filter   [⇥] Edit   [←→] Adjust   [⇧R] Reset   [⇧I] Input",
                       ValueKinds.Color       => "Type to filter   [⇥] Edit   [←→] Brightness   [⇧R] Reset   [⇧I] Input",
                       ValueKinds.Options     => "Type to filter   [⇥] Options   [←→] Change   [⇧R] Reset   [⇧I] Input",
                       ValueKinds.Bool        => "Type to filter   [⇥] [←→] Toggle   [⇧R] Reset   [⇧I] Input",
                       ValueKinds.Unsupported => "Type to filter   [⇧I] Insert Connected   [⇧R] Reset",
                       _                      => "Type to filter   [⇥] Edit   [⇧R] Reset   [⇧I] Input",
                   };
    }

    private const int MaxStringLabelLength = 30;
}
