#nullable enable
using ImGuiNET;
using T3.Core.Animation;
using T3.Core.DataTypes;
using T3.Core.Operator;
using T3.Editor.Gui.Interaction;
using T3.Editor.Gui.Interaction.WithCurves;
using T3.Editor.Gui.Styling;
using T3.Editor.UiModel;
using T3.Editor.UiModel.Commands;
using T3.Editor.UiModel.Commands.Animation;
using T3.Editor.UiModel.InputsAndTypes;

namespace T3.Editor.Gui.Windows.TimeLine;

/// <summary>
/// One line in the timeline toolbar summarizing the selected keyframes: count, parameter, value range and
/// interpolation. Dragging or typing a range end scales the values around the other end, the range bar between them
/// offsets all values. Hidden when nothing is selected or the toolbar is too narrow.
/// </summary>
internal static class SelectedKeyframesBar
{
    private enum EditedFields
    {
        None,
        LowerValue,
        UpperValue,
        Offset,
    }

    public static void Draw(TimeLineCanvas timeLineCanvas, Instance composition, float minX, float maxX, float y)
    {
        UpdateSelectionCache(timeLineCanvas);
        if (_keys.Count == 0)
        {
            CancelEdit();
            return;
        }

        _composition = composition;
        UpdateRangeAndInterpolation();

        var padding = ImGui.GetStyle().FramePadding.X;
        var spacing = 4 * T3Ui.UiScaleFactor;
        var minValueWidth = ImGui.CalcTextSize(_allKeysAreIntegers ? MinWidthIntegerSample : MinWidthFloatSample).X;
        var height = TimeControls.ControlSize.Y;
        var format = _allKeysAreIntegers ? "{0:0}" : "{0:0.00}";

        // A scale drag may collapse the range; keep the layout until it completes.
        var hasRange = _upperValue - _lowerValue > MinScalableRange
                       || _editedField is EditedFields.LowerValue or EditedFields.UpperValue
                       || _editedField == EditedFields.Offset && _editIsDelta;
        var lowerWidth = MathF.Round(MathF.Max(ImGui.CalcTextSize(GetLabel(ref _lowerLabel, _lowerValue, format)).X, minValueWidth) + padding * 2);
        // Equal values merge into one field spanning both value slots and the bar, so the layout doesn't jump
        // when the range collapses or reappears.
        var upperWidth = MathF.Round(MathF.Max(ImGui.CalcTextSize(GetLabel(ref _upperLabel, _upperValue, format)).X, minValueWidth) + padding * 2);

        // Values beyond the reserved width would still resize the fields mid-drag; only let them grow until the edit ends.
        if (_editedField != EditedFields.None)
        {
            lowerWidth = MathF.Max(lowerWidth, _lowerWidthDuringEdit);
            upperWidth = MathF.Max(upperWidth, _upperWidthDuringEdit);
        }

        _lowerWidthDuringEdit = lowerWidth;
        _upperWidthDuringEdit = upperWidth;
        var offsetWidth = MathF.Round(RangeBarWidth * T3Ui.UiScaleFactor);
        var iconWidth = height;
        var summaryWidth = ImGui.CalcTextSize(_summaryLabel).X;

        var componentsWidth = _componentCount > 0
                                  ? MathF.Round(CustomComponents.GetSegmentedButtonWidth(_componentLabels, isCompact: true)) + spacing
                                  : 0;

        // The interpolation icon leads, so "[icon] 18× Alpha" reads as 18 keyframes of that interpolation.
        var separatorGap = 8 * T3Ui.UiScaleFactor;
        var valuesWidth = iconWidth + componentsWidth + lowerWidth + offsetWidth + upperWidth + spacing + separatorGap + 1;
        var showSummary = valuesWidth + summaryWidth + spacing <= maxX - minX;
        if (!showSummary && valuesWidth > maxX - minX)
        {
            CancelEdit();
            return;
        }

        var totalWidth = showSummary ? valuesWidth + summaryWidth + spacing : valuesWidth;
        var x = MathF.Floor(maxX - totalWidth);

        ImGui.SetCursorScreenPos(new Vector2(x, y));
        DrawInterpolationButton(new Vector2(iconWidth, height));
        x += iconWidth;

        if (showSummary)
        {
            var textPos = new Vector2(x, y + MathF.Floor((height - ImGui.GetFontSize()) / 2));
            ImGui.GetWindowDrawList().AddText(textPos, UiColors.TextMuted, _summaryLabel);
            x += summaryWidth;
        }

        if (_componentCount > 0)
        {
            var trackMin = new Vector2(x + spacing, y + MathF.Floor((height - ImGui.GetFrameHeight()) / 2));
            var trackWidth = componentsWidth - spacing;

            // The segment track is tuned for darker panels; on the lighter toolbar strip it would vanish without a backing.
            ImGui.GetWindowDrawList().AddRectFilled(trackMin, trackMin + new Vector2(trackWidth, ImGui.GetFrameHeight()),
                                                    UiColors.BackgroundFull.Fade(0.4f), 4 * T3Ui.UiScaleFactor);

            ImGui.SetCursorScreenPos(trackMin);
            var componentIndex = _componentFilterIndex;
            if (CustomComponents.SegmentedButton(_componentLabels, ref componentIndex, disabledItems: _componentsWithoutKeys, isCompact: true)
                && componentIndex != _componentFilterIndex)
            {
                CancelEdit();
                _componentFilterIndex = componentIndex;
                ApplyComponentFilter();
                UpdateRangeAndInterpolation();
            }

            x += componentsWidth;
        }

        ImGui.SetCursorScreenPos(new Vector2(x + spacing, y));
        if (hasRange)
        {
            // Hovering the range bar lights up both ends, because offsetting moves both. The lower value is drawn
            // before the bar, so it uses last frame's hover state.
            var hoveredValueColor = ImGui.GetColorU32(ImGuiCol.ButtonHovered);
            PushHighlightIf(_isRangeBarHighlighted, hoveredValueColor);
            DrawValueField(EditedFields.LowerValue, _lowerValue, new Vector2(lowerWidth, height), format, horizontalAlign: 1, isDelta: false,
                           "Scale around the upper value", "Drag or click to type.");
            PopHighlightIf(_isRangeBarHighlighted);
            ImGui.SameLine();

            // Offset is edited as a delta that starts at zero for each interaction.
            DrawValueField(EditedFields.Offset, _editedField == EditedFields.Offset ? _offsetDelta : 0, new Vector2(offsetWidth, height), format,
                           horizontalAlign: 0.5f, isDelta: true,
                           "Offset all values", "Drag, or click to type the offset.");
            _isRangeBarHighlighted = ImGui.IsItemHovered() || _editedField == EditedFields.Offset && _editIsDelta;
            var isTypingOffset = ImGui.IsItemActive() && ImGui.GetIO().WantTextInput;
            if (!isTypingOffset)
                DrawRangeBar(ImGui.GetItemRectMin(), ImGui.GetItemRectMax(), _isRangeBarHighlighted);

            ImGui.SameLine();
            PushHighlightIf(_isRangeBarHighlighted, hoveredValueColor);
            DrawValueField(EditedFields.UpperValue, _upperValue, new Vector2(upperWidth, height), format, horizontalAlign: 0, isDelta: false,
                           "Scale around the lower value", "Drag or click to type.");
            PopHighlightIf(_isRangeBarHighlighted);
        }
        else
        {
            DrawValueField(EditedFields.Offset, _lowerValue, new Vector2(lowerWidth + offsetWidth + upperWidth, height), format, horizontalAlign: 0.5f,
                           isDelta: false,
                           "Value of the selected keyframes", "Drag or click to type.");
        }

        // Separates the bar from the view-mode toggle that follows.
        var separatorX = MathF.Floor(ImGui.GetItemRectMax().X + separatorGap);
        var separatorInset = MathF.Floor(height / 4);
        ImGui.GetWindowDrawList().AddRectFilled(new Vector2(separatorX, y + separatorInset),
                                                new Vector2(separatorX + 1, y + height - separatorInset),
                                                UiColors.ForegroundFull.Fade(0.1f));
    }

    private static void PushHighlightIf(bool isHighlighted, uint color)
    {
        if (isHighlighted)
            ImGui.PushStyleColor(ImGuiCol.Button, color);
    }

    private static void PopHighlightIf(bool isHighlighted)
    {
        if (isHighlighted)
            ImGui.PopStyleColor();
    }

    /** A horizontal line with end ticks, like a range slider, so the area between the values reads as the range itself. */
    private static void DrawRangeBar(Vector2 itemMin, Vector2 itemMax, bool isHighlighted)
    {
        var drawList = ImGui.GetWindowDrawList();
        var color = isHighlighted ? UiColors.ForegroundFull : UiColors.TextMuted;
        var inset = MathF.Round(5 * T3Ui.UiScaleFactor);
        var tickHalfHeight = MathF.Round(3 * T3Ui.UiScaleFactor);
        var thickness = MathF.Max(1, MathF.Round(T3Ui.UiScaleFactor));

        var left = MathF.Floor(itemMin.X + inset);
        var right = MathF.Floor(itemMax.X - inset);
        var centerY = MathF.Floor((itemMin.Y + itemMax.Y) / 2);

        drawList.AddRectFilled(new Vector2(left, centerY), new Vector2(right, centerY + thickness), color);
        drawList.AddRectFilled(new Vector2(left, centerY - tickHalfHeight), new Vector2(left + thickness, centerY + thickness + tickHalfHeight), color);
        drawList.AddRectFilled(new Vector2(right - thickness, centerY - tickHalfHeight), new Vector2(right, centerY + thickness + tickHalfHeight), color);
    }

    private static void DrawValueField(EditedFields field, double value, Vector2 size, string format, float horizontalAlign, bool isDelta,
                                       string tooltip, string tooltipNote)
    {
        var startValue = value;

        ImGui.PushID((int)field);
        var result = isDelta
                         ? SingleValueEdit.Draw(ref value, size, scale: GetDragScale(), format: format, horizontalAlign: horizontalAlign,
                                                formatLabel: _formatOffsetLabel)
                         : SingleValueEdit.Draw(ref value, size, scale: GetDragScale(), format: format, horizontalAlign: horizontalAlign);
        ImGui.PopID();

        if (ImGui.IsItemHovered())
        {
            // InputText sets its own text cursor while typing.
            if (ImGui.GetMouseCursor() != ImGuiMouseCursor.TextInput)
                ImGui.SetMouseCursor(ImGuiMouseCursor.ResizeEW);

            CustomComponents.TooltipForLastItem(tooltip, tooltipNote);
        }

        if ((result & InputEditStateFlags.Modified) != 0 && (_editedField == EditedFields.None || _editedField == field))
        {
            if (_editedField == EditedFields.None)
                BeginEdit(field, isDelta ? 0 : startValue, isDelta);

            ApplyEdit(value);
        }

        // SingleValueEdit keeps reporting Finished for its last active field, so only the owner may complete.
        if ((result & InputEditStateFlags.Finished) != 0 && _editedField == field)
            CompleteEdit();
    }

    private static void BeginEdit(EditedFields field, double startValue, bool isDelta)
    {
        _editedField = field;
        _editIsDelta = isDelta;
        _editStartValue = startValue;
        _offsetDelta = 0;
        _snapshotLowerValue = _lowerValue;
        _snapshotUpperValue = _upperValue;

        _originalValues.Clear();
        foreach (var key in _keys)
        {
            _originalValues.Add(key.Value);
        }

        _changeKeyframesCommand = new ChangeKeyframesCommand(_keys, _curves);
    }

    private static void ApplyEdit(double newValue)
    {
        var lower = _snapshotLowerValue;
        var upper = _snapshotUpperValue;
        var range = upper - lower;

        if (_editedField == EditedFields.Offset)
            _offsetDelta = newValue;

        for (var keyIndex = 0; keyIndex < _keys.Count; keyIndex++)
        {
            var original = _originalValues[keyIndex];
            var result = _editedField switch
                             {
                                 EditedFields.LowerValue => upper + (original - upper) * (upper - newValue) / range,
                                 EditedFields.UpperValue => lower + (original - lower) * (newValue - lower) / range,
                                 _                       => original + newValue - _editStartValue,
                             };

            _keys[keyIndex].Value = _allKeysAreIntegers ? Math.Round(result) : result;
        }

        foreach (var curve in _curves)
        {
            curve.UpdateTangents();
        }
    }

    private static void CompleteEdit()
    {
        if (_changeKeyframesCommand != null && AnyValueChanged())
        {
            _changeKeyframesCommand.StoreCurrentValues();
            UndoRedoStack.Add(_changeKeyframesCommand);
            _composition?.GetSymbolUi().FlagAsModified();
        }

        _changeKeyframesCommand = null;
        _editedField = EditedFields.None;
        _offsetDelta = 0;
    }

    /** A click that only opens the text input shouldn't leave an empty undo step. */
    private static bool AnyValueChanged()
    {
        for (var keyIndex = 0; keyIndex < _keys.Count && keyIndex < _originalValues.Count; keyIndex++)
        {
            if (_keys[keyIndex].Value != _originalValues[keyIndex])
                return true;
        }

        return false;
    }

    /** Commits a running edit when the bar disappears, so the values already applied stay undoable. */
    private static void CancelEdit()
    {
        if (_editedField != EditedFields.None)
            CompleteEdit();
    }

    private static void DrawInterpolationButton(Vector2 size)
    {
        var icon = _commonInterpolationIndex >= 0 ? ToolbarIcons[_commonInterpolationIndex] : Icon.None;
        if (icon != Icon.None)
        {
            CustomComponents.IconButton(icon, size);
        }
        else
        {
            ImGui.PushStyleColor(ImGuiCol.Text, UiColors.TextMuted.Rgba);
            ImGui.Button("?##interpolation", size);
            ImGui.PopStyleColor();
        }

        if (ImGui.IsItemHovered())
        {
            CustomComponents.TooltipForLastItem(_commonInterpolationIndex >= 0
                                                    ? InterpolationLabels[_commonInterpolationIndex]
                                                    : "Mixed interpolation",
                                                "Click to change.");
        }

        CustomComponents.ContextMenuForItem(DrawInterpolationMenu, "Interpolation", "##interpolationMenu", ImGuiPopupFlags.MouseButtonLeft);
    }

    private static void DrawInterpolationMenu()
    {
        for (var index = 0; index < Interpolations.Length; index++)
        {
            var isUsed = (_usedInterpolationsMask & (1 << (int)Interpolations[index])) != 0;
            if (!CustomComponents.DrawMenuItem(_interpolationMenuId + index, MenuIcons[index], InterpolationLabels[index], isChecked: isUsed))
                continue;

            SetInterpolationOfSelection(Interpolations[index]);
        }
    }

    private static void SetInterpolationOfSelection(VDefinition.KeyInterpolation interpolation)
    {
        var command = new ChangeKeyframesCommand(_keys, _curves);
        foreach (var key in _keys)
        {
            CurveEditing.SetInterpolation(key, interpolation);
        }

        foreach (var curve in _curves)
        {
            curve.UpdateTangents();
        }

        command.StoreCurrentValues();
        UndoRedoStack.Add(command);
        _composition?.GetSymbolUi().FlagAsModified();
    }

    /** Resolving keys to their curves walks all visible curves, so it only runs when the selection changes. */
    private static void UpdateSelectionCache(TimeLineCanvas timeLineCanvas)
    {
        var selection = timeLineCanvas.SharedSelectedKeyframes;
        var parameters = timeLineCanvas.SelectedAnimationParameters;

        var parametersHash = parameters.Count;
        for (var index = 0; index < parameters.Count; index++)
        {
            parametersHash = parametersHash * 397 ^ parameters[index].Hash;
        }

        if (selection.ChangeCounter == _lastSelectionChangeCounter && parametersHash == _lastParametersHash)
            return;

        // An edit must not continue on a different set of keys.
        CancelEdit();

        _lastSelectionChangeCounter = selection.ChangeCounter;
        _lastParametersHash = parametersHash;
        _allKeys.Clear();
        _allKeyComponents.Clear();
        _allCurves.Clear();
        _allCurveComponents.Clear();

        TimeLineCanvas.AnimationParameter? firstParameter = null;
        var parameterCount = 0;
        var haveSameVectorType = true;
        _allKeysAreIntegers = true;

        if (selection.Count > 0)
        {
            foreach (var parameter in parameters)
            {
                var parameterHasKeys = false;
                for (var curveIndex = 0; curveIndex < parameter.Curves.Length; curveIndex++)
                {
                    var curve = parameter.Curves[curveIndex];
                    var curveHasKeys = false;
                    foreach (var key in curve.GetVDefinitions())
                    {
                        if (!selection.Contains(key))
                            continue;

                        _allKeys.Add(key);
                        _allKeyComponents.Add(curveIndex);
                        curveHasKeys = true;
                    }

                    if (!curveHasKeys)
                        continue;

                    _allCurves.Add(curve);
                    _allCurveComponents.Add(curveIndex);
                    parameterHasKeys = true;
                }

                if (!parameterHasKeys)
                    continue;

                firstParameter ??= parameter;
                parameterCount++;
                haveSameVectorType &= parameter.Input.ValueType == firstParameter.Input.ValueType;
                _allKeysAreIntegers &= parameter.Input.ValueType == typeof(int);
            }
        }

        _singleParameterName = parameterCount == 1 ? firstParameter?.Input.Input.Name : null;

        var componentCount = haveSameVectorType && firstParameter != null ? firstParameter.Curves.Length : 0;
        UpdateComponentLabels(componentCount, firstParameter?.Input.ValueType == typeof(Vector4));
        ApplyComponentFilter();
    }

    /** Rebuilt only when the vector type changes, which also resets the filter to all components. */
    private static void UpdateComponentLabels(int componentCount, bool isColor)
    {
        var names = isColor ? DopeSheetArea.ColorCurveNames : DopeSheetArea.CurveNames;
        if (componentCount < 2 || componentCount > names.Length)
            componentCount = 0;

        if (componentCount == _componentCount && isColor == _componentsAreColor)
            return;

        _componentCount = componentCount;
        _componentsAreColor = isColor;
        _componentFilterIndex = componentCount;
        if (componentCount == 0)
        {
            _componentLabels = [];
            _componentsWithoutKeys = [];
            return;
        }

        _componentLabels = new string[componentCount + 1];
        var allLabel = string.Empty;
        for (var componentIndex = 0; componentIndex < componentCount; componentIndex++)
        {
            _componentLabels[componentIndex] = names[componentIndex];
            allLabel += names[componentIndex];
        }

        _componentLabels[componentCount] = allLabel;
        _componentsWithoutKeys = new bool[componentCount + 1];
    }

    /** Narrows the edited keys to the chosen component. The last segment, the default, keeps all of them. */
    private static void ApplyComponentFilter()
    {
        if (_componentCount > 0)
        {
            for (var componentIndex = 0; componentIndex < _componentCount; componentIndex++)
            {
                _componentsWithoutKeys[componentIndex] = true;
            }

            foreach (var componentIndex in _allKeyComponents)
            {
                _componentsWithoutKeys[componentIndex] = false;
            }

            if (_componentFilterIndex < _componentCount && _componentsWithoutKeys[_componentFilterIndex])
                _componentFilterIndex = _componentCount;
        }

        var includesAll = _componentFilterIndex >= _componentCount;

        _keys.Clear();
        for (var keyIndex = 0; keyIndex < _allKeys.Count; keyIndex++)
        {
            if (includesAll || _allKeyComponents[keyIndex] == _componentFilterIndex)
                _keys.Add(_allKeys[keyIndex]);
        }

        _curves.Clear();
        for (var curveIndex = 0; curveIndex < _allCurves.Count; curveIndex++)
        {
            if (includesAll || _allCurveComponents[curveIndex] == _componentFilterIndex)
                _curves.Add(_allCurves[curveIndex]);
        }

        // Formatted only here, when the selection or the filter changes.
        var countPrefix = _keys.Count > 1 ? $"{_keys.Count}\u00D7" : string.Empty;
        _summaryLabel = _singleParameterName != null
                            ? $"{countPrefix} {_singleParameterName}".TrimStart()
                            : countPrefix;
    }

    private static void UpdateRangeAndInterpolation()
    {
        _lowerValue = double.PositiveInfinity;
        _upperValue = double.NegativeInfinity;
        _usedInterpolationsMask = 0;
        var commonInterpolation = (VDefinition.KeyInterpolation?)null;
        var isMixed = false;

        foreach (var key in _keys)
        {
            _lowerValue = Math.Min(_lowerValue, key.Value);
            _upperValue = Math.Max(_upperValue, key.Value);
            _usedInterpolationsMask |= 1 << (int)key.InInterpolation | 1 << (int)key.OutInterpolation;

            // A constant key keeps its in-interpolation, so the out side decides how it reads.
            var keyInterpolation = key.OutInterpolation == VDefinition.KeyInterpolation.Constant || key.InInterpolation == key.OutInterpolation
                                       ? key.OutInterpolation
                                       : VDefinition.KeyInterpolation.Tangent;

            if (commonInterpolation == null)
            {
                commonInterpolation = keyInterpolation;
            }
            else if (commonInterpolation != keyInterpolation)
            {
                isMixed = true;
            }
        }

        _commonInterpolationIndex = -1;
        if (isMixed || commonInterpolation == null)
            return;

        for (var index = 0; index < Interpolations.Length; index++)
        {
            if (Interpolations[index] == commonInterpolation)
                _commonInterpolationIndex = index;
        }
    }

    /** Larger ranges drag faster, so a selection spanning thousands doesn't take forever to scale. */
    private static float GetDragScale()
    {
        var magnitude = Math.Max(Math.Abs(_lowerValue), Math.Abs(_upperValue));
        var scale = (float)Math.Max(magnitude, Math.Max(_upperValue - _lowerValue, 1)) * 0.01f;
        return _allKeysAreIntegers ? MathF.Max(scale, 0.1f) : scale;
    }

    /** Formats only when the value changed: the toolbar redraws every frame. */
    private static string GetLabel(ref CachedLabel label, double value, string format)
    {
        if (label.Text == null || label.Value != value || !ReferenceEquals(label.Format, format))
        {
            label.Text = string.Format(format, value);
            label.Value = value;
            label.Format = format;
        }

        return label.Text;
    }

    private struct CachedLabel
    {
        public double Value;
        public string? Format;
        public string? Text;
    }

    private const float RangeBarWidth = 28;
    private const double MinScalableRange = 1e-6;
    private static readonly Func<double, string> _formatOffsetLabel = static _ => string.Empty;

    private static readonly VDefinition.KeyInterpolation[] Interpolations =
        [
            VDefinition.KeyInterpolation.Linear,
            VDefinition.KeyInterpolation.Smooth,
            VDefinition.KeyInterpolation.Cubic,
            VDefinition.KeyInterpolation.Horizontal,
            VDefinition.KeyInterpolation.Constant,
        ];

    private static readonly string[] InterpolationLabels = ["Linear", "Smooth (Clamped)", "Smooth", "Horizontal", "Constant"];

    // There is no horizontal interpolation icon yet: the menu leaves its slot empty and the toolbar
    // borrows the dope sheet's horizontal keyframe glyph.
    private static readonly Icon[] MenuIcons =
        [Icon.InterpolateLinear, Icon.InterpolateSmoothSafe, Icon.InterpolateSmooth, Icon.None, Icon.InterpolateConstant];

    private static readonly Icon[] ToolbarIcons =
        [Icon.InterpolateLinear, Icon.InterpolateSmoothSafe, Icon.InterpolateSmooth, Icon.DopeSheetKeyframeHorizontal, Icon.InterpolateConstant];

    private static readonly int _interpolationMenuId = nameof(_interpolationMenuId).GetHashCode();

    private const string MinWidthFloatSample = "-000.000";
    private const string MinWidthIntegerSample = "-0000";

    private static readonly List<VDefinition> _allKeys = new(64);
    private static readonly List<int> _allKeyComponents = new(64);
    private static readonly List<Curve> _allCurves = new(16);
    private static readonly List<int> _allCurveComponents = new(16);
    private static string? _singleParameterName;

    private static int _componentCount;
    private static bool _componentsAreColor;
    private static int _componentFilterIndex;
    private static string[] _componentLabels = [];
    private static bool[] _componentsWithoutKeys = [];

    /** The keys and curves the bar edits: the selection, narrowed by the component filter. */
    private static readonly List<VDefinition> _keys = new(64);
    private static readonly List<Curve> _curves = new(16);
    private static readonly List<double> _originalValues = new(64);
    private static int _lastSelectionChangeCounter = -1;
    private static int _lastParametersHash;
    private static string _summaryLabel = string.Empty;
    private static bool _allKeysAreIntegers;

    private static double _lowerValue;
    private static double _upperValue;
    private static int _usedInterpolationsMask;
    private static int _commonInterpolationIndex = -1;
    private static CachedLabel _lowerLabel;
    private static CachedLabel _upperLabel;
    private static float _lowerWidthDuringEdit;
    private static float _upperWidthDuringEdit;

    private static EditedFields _editedField;
    private static double _editStartValue;
    private static bool _editIsDelta;
    private static bool _isRangeBarHighlighted;
    private static double _offsetDelta;
    private static double _snapshotLowerValue;
    private static double _snapshotUpperValue;
    private static ChangeKeyframesCommand? _changeKeyframesCommand;
    private static Instance? _composition;
}
