using ImGuiNET;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using T3.Core.DataTypes;
using T3.Core.Operator;
using T3.Core.Operator.Slots;
using T3.Core.Utils;
using T3.Editor.Gui.Graph.Dialogs;
using T3.Editor.Gui.Input;
using T3.Editor.Gui.Interaction;
using T3.Editor.Gui.Styling;
using T3.Editor.Gui.UiHelpers;
using T3.Editor.Gui.UiHelpers.Enums;
using T3.Editor.UiModel.InputsAndTypes;
using T3.Editor.UiModel.Modification;

namespace T3.Editor.Gui.InputUi.VectorInputs;

internal sealed class IntInputUi : IntVectorInputValueUi<int>
{
    public override bool IsAnimatable => true;

    public IntInputUi() : base(1) { }
        
    public override IInputUi Clone()
    {
        var clone = (IntInputUi)CloneWithType<IntInputUi>();
        clone._displayMode = _displayMode;
        return clone;
    }
        
    protected override InputEditStateFlags DrawEditControl(string name, Symbol.Child.Input input, ref int value, bool readOnly)
    {
        InputEditStateFlags result;
            
        if (MappedType != null)
        {
            result = DrawEnumInputEdit(ref value, MappedType, _displayMode);
        }
        else
        {
            IntComponents[0] = value;
            result = VectorValueEdit.Draw(IntComponents, Min, Max, Scale, ClampMin, ClampMax);
            if (readOnly)
                return InputEditStateFlags.Nothing;
                
            value = IntComponents[0];
        }
            
        return result;            
    }

    public InputEditStateFlags DrawEditControl(ref int value)
    {
        return SingleValueEdit.Draw(ref value, -Vector2.UnitX, Min, Max, ClampMin, ClampMax, Scale);
    }

    public override void ApplyValueToAnimation(IInputSlot inputSlot, InputValue inputValue, Animator animator, double time)
    {
        if (inputValue is not InputValue<int> typedInputValue)
            return;

        if (!animator.TryGetCurvesForInputSlot(inputSlot, out var curves))
            return;
        
        IntComponents[0] = typedInputValue.Value;
        Curve.UpdateCurveValues(curves, time, IntComponents);
    }

    public override bool DrawSettings()
    {
        var modified = base.DrawSettings();
        var symbol = Parent?.Symbol;
        var isEditable = symbol != null && !symbol.SymbolPackage.IsReadOnly;
        var mappedEnum = symbol != null ? GetMappedEnumOfSymbol(symbol) : MappedType;
        var isFlagEnum = mappedEnum != null && EnumCache.Instance.GetEnumEntry(mappedEnum).IsFlagEnum;

        if (!isFlagEnum && (mappedEnum != null || isEditable))
            modified |= DrawDisplayAsSwitch(symbol, mappedEnum, isEditable);

        if (isEditable && mappedEnum != null)
            DrawEditEntriesButton(symbol!, mappedEnum);

        _editEnumEntriesDialog.Draw();
        _showAsValueDialog.Draw();
        return modified;
    }

    internal void SetDisplayMode(EnumDisplayMode displayMode)
    {
        _displayMode = displayMode;
    }

    /// <summary>
    /// Value, Dropdown or Switch. Leaving Value creates an enum in the operator and going back removes it, both after
    /// a dialog, so only Dropdown and Switch change directly. Read-only operators only switch between those two.
    /// </summary>
    private bool DrawDisplayAsSwitch(Symbol? symbol, Type? mappedEnum, bool isEditable)
    {
        FormInputs.AddVerticalSpace();
        FormInputs.DrawFieldSetHeader("Display as");
        FormInputs.SetCursorToParameterEdit();

        if (!isEditable)
        {
            var modeIndex = (int)_displayMode;
            if (!CustomComponents.SegmentedButton(_enumDisplayModeLabels, ref modeIndex, tooltips: _enumDisplayModeTooltips))
                return false;

            _displayMode = (EnumDisplayMode)modeIndex;
            return true;
        }

        var selectedIndex = mappedEnum == null ? 0 : 1 + (int)_displayMode;
        if (!CustomComponents.SegmentedButton(_displayAsLabels, ref selectedIndex, tooltips: _displayAsTooltips))
            return false;

        if (selectedIndex == 0)
        {
            if (mappedEnum != null)
                _showAsValueDialog.ShowNextFrame(symbol!, Id, InputDefinition.Name, mappedEnum);

            return false;
        }

        var displayMode = (EnumDisplayMode)(selectedIndex - 1);
        if (mappedEnum == null)
        {
            _editEnumEntriesDialog.ShowNextFrameForNewEnum(symbol!, Id, InputDefinition.Name, Min, Max, displayMode);
            return false;
        }

        _displayMode = displayMode;
        return true;
    }

    /** Only an enum declared inside the operator's own class can be edited. */
    private void DrawEditEntriesButton(Symbol symbol, Type mappedEnum)
    {
        FormInputs.SetCursorToParameterEdit();
        if (ImGui.Button("Edit Entries..."))
        {
            if (mappedEnum.DeclaringType != symbol.InstanceType)
            {
                Log.Warning($"Can't edit enum {mappedEnum.FullName}: only enums declared inside {symbol.Name} can be edited.");
            }
            else
            {
                _editEnumEntriesDialog.ShowNextFrame(symbol, mappedEnum);
            }
        }

        CustomComponents.TooltipForLastItem($"Edit the entries of {mappedEnum.Name}, the enum this parameter is shown with.");
    }

    /** The slot's mapped type is only known once the parameter was drawn, so it's read from the operator type. */
    private Type? GetMappedEnumOfSymbol(Symbol symbol)
    {
        if (!ReferenceEquals(symbol.InstanceType, _mappedEnumLookupType))
        {
            _mappedEnumLookupType = symbol.InstanceType;
            InputsAndOutputs.TryGetMappedEnumType(symbol, Id, out _mappedEnumOfSymbol);
        }

        return _mappedEnumOfSymbol;
    }

    public override void Write(JsonTextWriter writer)
    {
        base.Write(writer);
        EnumDisplayModeUi.Write(writer, _displayMode);
    }

    public override void Read(JToken? inputToken)
    {
        base.Read(inputToken);
        _displayMode = EnumDisplayModeUi.Read(inputToken);
    }

    private static InputEditStateFlags DrawEnumInputEdit(ref int value, Type enumType, EnumDisplayMode displayMode)
    {
        var enumInfo = EnumCache.Instance.GetEnumEntry(enumType);

        if (enumInfo.IsFlagEnum)
        {
            // show as checkboxes
            InputEditStateFlags editStateFlags = InputEditStateFlags.Nothing;
            if (ImGui.TreeNode("##enumParamInt124"))
            {
                bool[] checks = enumInfo.SetFlags;
                for (int i = 0; i < enumInfo.ValueNames.Length; i++)
                {
                    int enumValueAsInt = enumInfo.ValuesAsInt[i];
                    checks[i] = (value & enumValueAsInt) > 0;
                    if (ImGui.Checkbox(enumInfo.ValueNames[i], ref checks[i]))
                    {
                        // value modified, store new flag
                        if (checks[i])
                        {
                            value |= enumValueAsInt;
                        }
                        else
                        {
                            value &= ~enumValueAsInt;
                        }

                        editStateFlags |= InputEditStateFlags.Modified;
                    }

                    if (ImGui.IsItemClicked())
                    {
                        editStateFlags |= InputEditStateFlags.Started;
                    }

                    if (ImGui.IsItemDeactivatedAfterEdit())
                    {
                        editStateFlags |= InputEditStateFlags.Finished;
                    }
                }

                ImGui.TreePop();
            }

            return editStateFlags;
        }
        else
        {
            int index = Array.IndexOf(enumInfo.ValuesAsInt, value);
            if (EnumDisplayModeUi.TryDrawSegmented(displayMode, enumInfo, ref index, out var segmentedFlags))
            {
                if (segmentedFlags != InputEditStateFlags.Nothing)
                    value = enumInfo.ValuesAsInt[index];

                return segmentedFlags;
            }

            InputEditStateFlags editStateFlags = InputEditStateFlags.Nothing;
            
            // Draw combo box to modify options on hover
            var modified = false;

            var preview = index >= 0 && index < enumInfo.ValueNames.Length
                              ? enumInfo.ValueNames[index]
                              : string.Empty;

            var isOpen = ImGui.BeginCombo("##dropDownParam", preview);
            if (isOpen)
            {
                var hoveredIndex = -1;
                if (ImGui.IsWindowAppearing())
                {
                    _originalValue = value;
                }
                for (var i = 0; i < enumInfo.ValueNames.Length; i++)
                {
                    var isSelected = i == index;

                    if (ImGui.Selectable(enumInfo.ValueNames[i], isSelected))
                    {
                        index = i;
                        modified = true;
                    }

                    if (ImGui.IsItemHovered())
                    {
                        hoveredIndex = i;
                    }

                    if (isSelected)
                    {
                        ImGui.SetItemDefaultFocus();
                    }
                }

                ImGui.EndCombo();

                if (UserSettings.Config.ApplyDropdownValuesOnHover && hoveredIndex != _lastHoveredComboItem)
                {
                    _lastHoveredComboItem = hoveredIndex;

                    if (hoveredIndex >= 0)
                    {
                        value = enumInfo.ValuesAsInt[hoveredIndex];
                        return InputEditStateFlags.Modified;
                    }
                    else
                    {
                        if (_originalValue >= 0)
                        {
                            value = _originalValue;
                            _lastHoveredComboItem = -1;
                            return InputEditStateFlags.Modified;
                        }

                    }
                }
            }

            if (modified)
            {
                value = enumInfo.ValuesAsInt[index];
                editStateFlags |= InputEditStateFlags.ModifiedAndFinished;
            }

            if (!ImGui.IsItemActive())
            {
                var io = ImGui.GetIO();
                if (ImGui.IsItemHovered() && io.KeyCtrl)
                {
                    T3Ui.MouseWheelFieldHovered = true;
                    ImGui.SetMouseCursor(ImGuiMouseCursor.ResizeEW);
                    var dl = ImGui.GetForegroundDrawList();
                    dl.AddRect(ImGui.GetItemRectMin(), ImGui.GetItemRectMax(), UiColors.StatusActivated);

                    var wheel = io.MouseWheel;
                    if (wheel == 0)
                        return InputEditStateFlags.Nothing;

                    // Step through the entries, whose values need not be consecutive.
                    var delta = wheel > 0 ? -1 : 1;
                    var steppedIndex = (Array.IndexOf(enumInfo.ValuesAsInt, value) + delta).Clamp(0, enumInfo.ValuesAsInt.Length - 1);
                    value = enumInfo.ValuesAsInt[steppedIndex];
                    
                    return InputEditStateFlags.Modified;
                }
            }
            else if (ImGui.IsItemClicked())
            {
                editStateFlags |= InputEditStateFlags.Started;
            }

            return editStateFlags;
        }
    }
    
    private EnumDisplayMode _displayMode;

    private static int _lastHoveredComboItem = -1;
    private static int _originalValue = -1;
    private Type? _mappedEnumLookupType;
    private Type? _mappedEnumOfSymbol;
    private static readonly EditEnumEntriesDialog _editEnumEntriesDialog = new();
    private static readonly ShowAsValueDialog _showAsValueDialog = new();
    private static readonly string[] _displayAsLabels = ["Value", "Dropdown", "Switch"];
    private static readonly string?[] _displayAsTooltips =
        [
            "A plain number. Leaving a dropdown removes its enum from the operator.",
            "The entries of an enum declared in the operator, in a dropdown.",
            "The entries side by side. Falls back to a dropdown if they don't fit.",
        ];
    private static readonly string[] _enumDisplayModeLabels = ["Dropdown", "Switch"];
    private static readonly string?[] _enumDisplayModeTooltips = [_displayAsTooltips[1], _displayAsTooltips[2]];
}
