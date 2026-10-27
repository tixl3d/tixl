#nullable enable
using ImGuiNET;
using T3.Core.DataTypes.Vector;
using T3.Core.Operator;
using T3.Core.Operator.Slots;
using T3.Editor.Gui.InputUi.VectorInputs;
using T3.Editor.Gui.Interaction;
using T3.Editor.UiModel;
using T3.Editor.UiModel.Commands.Graph;
using T3.Editor.UiModel.InputsAndTypes;
using static T3.Editor.Gui.MagGraph.Interaction.KeyboardEditing.ParameterValueParsing;

namespace T3.Editor.Gui.MagGraph.Interaction.KeyboardEditing;

internal static partial class ParameterPopover
{
    /** What Left/Right adjust with the slider */
    private enum NudgeTargets
    {
        Number,
        Component,
        Hue,
        Saturation,
        Brightness,
    }

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

    private const float SliderGapPx = 24;

    /** Slider scale for color channels and properties, which mostly live in 0..1 */
    private const float ColorNudgeScale = 0.002f;

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
}
