using ImGuiNET;
using T3.Core.Operator;
using T3.Editor.Gui.UiHelpers;
using T3.Editor.UiModel.InputsAndTypes;

namespace T3.Editor.Gui.InputUi.SimpleInputUis;

public sealed class EnumInputUi<T> : InputValueUi<T> where T : Enum
{
    public override IInputUi Clone()
    {
        return new EnumInputUi<T>
                   {
                       InputDefinition = InputDefinition,
                       Parent = Parent,
                       PosOnCanvas = PosOnCanvas,
                       Relevancy = Relevancy
                   };
    }

    protected override InputEditStateFlags DrawEditControl(string name, Symbol.Child.Input input, ref T value, bool readOnly)
    {
        return DrawEnumInputEdit(ref value);
    }

    public static InputEditStateFlags DrawEnumInputEdit(ref T value)
    {
        var enumInfo = EnumCache.Instance.GetTypedEnumEntry<T>();

        if (enumInfo.IsFlagEnum)
        {
            // show as checkboxes
            InputEditStateFlags editStateFlags = InputEditStateFlags.Nothing;
            if (ImGui.TreeNode("##enumParam124"))
            {
                bool[] checks = enumInfo.SetFlags;
                int intValue = (int)(object)value;
                for (int i = 0; i < enumInfo.ValueNames.Length; i++)
                {
                    int enumValueAsInt = enumInfo.ValuesAsInt[i];
                    checks[i] = (intValue & enumValueAsInt) > 0;
                    if (ImGui.Checkbox(enumInfo.ValueNames[i], ref checks[i]))
                    {
                        // value modified, store new flag
                        if (checks[i])
                        {
                            intValue |= enumValueAsInt;
                        }
                        else
                        {
                            intValue &= ~enumValueAsInt;
                        }

                        value = (T)(object)intValue;
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
            var index = Array.IndexOf(enumInfo.Values, value);
            var editStateFlags = InputEditStateFlags.Nothing;
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
                    _hasOriginalValue = true;
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

                // Preview the hovered entry live until the user commits or leaves the list.
                if (UserSettings.Config.ApplyDropdownValuesOnHover && hoveredIndex != _lastHoveredComboItem)
                {
                    _lastHoveredComboItem = hoveredIndex;

                    if (hoveredIndex >= 0)
                    {
                        value = enumInfo[hoveredIndex];
                        return InputEditStateFlags.Modified;
                    }

                    if (_hasOriginalValue)
                    {
                        value = _originalValue;
                        _lastHoveredComboItem = -1;
                        return InputEditStateFlags.Modified;
                    }
                }
            }

            if (modified)
            {
                value = enumInfo[index];
                editStateFlags |= InputEditStateFlags.ModifiedAndFinished;
            }

            if (ImGui.IsItemClicked())
            {
                editStateFlags |= InputEditStateFlags.Started;
            }

            return editStateFlags;
        }
    }

    protected override void DrawReadOnlyControl(string name, ref T value)
    {
        ImGui.TextUnformatted(value.ToString());
    }

    private static int _lastHoveredComboItem = -1;
    private static T _originalValue;
    private static bool _hasOriginalValue;
}