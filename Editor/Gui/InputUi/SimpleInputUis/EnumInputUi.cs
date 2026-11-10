using ImGuiNET;
using T3.Core.Operator;
using T3.Editor.Gui.Styling;
using T3.Editor.Gui.UiHelpers;
using T3.Editor.Gui.UiHelpers.Enums;
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

            var hasValue = index >= 0 && index < enumInfo.ValueNames.Length;
            var preview = hasValue ? enumInfo.DisplayNames[index] : string.Empty;
            var isValueUnavailable = hasValue && enumInfo.Hints[index].Relevance == OptionRelevance.Unavailable;

            if (enumInfo.HasHints)
            {
                var maxPopupHeight = ImGui.GetMainViewport().WorkSize.Y * MaxPopupHeightRelativeToViewport;
                ImGui.SetNextWindowSizeConstraints(new Vector2(ImGui.CalcItemWidth(), 0), new Vector2(float.MaxValue, maxPopupHeight));
            }

            // A project can hold a value this machine can't use, e.g. a texture format saved on another backend.
            if (isValueUnavailable)
                ImGui.PushStyleColor(ImGuiCol.Text, UiColors.StatusAttention.Rgba);

            var isOpen = ImGui.BeginCombo("##dropDownParam", preview);

            if (isValueUnavailable)
            {
                ImGui.PopStyleColor();
                if (!isOpen)
                    CustomComponents.TooltipForLastItem(UiColors.StatusAttention, enumInfo.Hints[index].Tooltip, enumInfo.ValueNames[index]);
            }

            if (isOpen)
            {
                var hoveredIndex = -1;
                if (ImGui.IsWindowAppearing())
                {
                    _originalValue = value;
                    _hasOriginalValue = true;
                }

                for (var orderIndex = 0; orderIndex < enumInfo.DisplayOrder.Length; orderIndex++)
                {
                    if (orderIndex == 0 && enumInfo.ImportantCount > 0)
                        CustomComponents.DrawMenuGroupLabel(enumInfo.ImportantGroupTitle, alignWithIconColumn: false);

                    if (orderIndex == enumInfo.ImportantCount && orderIndex > 0)
                        ImGui.Separator();

                    var i = enumInfo.DisplayOrder[orderIndex];
                    var hint = enumInfo.Hints[i];
                    var isSelected = i == index;
                    var isUnavailable = hint.Relevance == OptionRelevance.Unavailable;

                    if (isUnavailable)
                        ImGui.PushStyleColor(ImGuiCol.Text, UiColors.TextMuted.Rgba);

                    var flags = isUnavailable ? ImGuiSelectableFlags.Disabled : ImGuiSelectableFlags.None;
                    if (ImGui.Selectable(enumInfo.SelectableIds[i], isSelected, flags))
                    {
                        index = i;
                        modified = true;
                    }

                    if (isUnavailable)
                        ImGui.PopStyleColor();

                    // Unavailable values are never previewed on hover: applying one is exactly what must not happen.
                    if (!isUnavailable && ImGui.IsItemHovered())
                    {
                        hoveredIndex = i;
                    }

                    if (hint.Label != null)
                    {
                        CustomComponents.TooltipForLastItem(UiColors.Text, enumInfo.ValueNames[i], hint.Tooltip);
                    }
                    else if (hint.Tooltip != null)
                    {
                        CustomComponents.TooltipForLastItem(isUnavailable ? UiColors.StatusAttention : UiColors.Text, hint.Tooltip);
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
        var enumInfo = EnumCache.Instance.GetTypedEnumEntry<T>();
        var index = Array.IndexOf(enumInfo.Values, value);
        ImGui.TextUnformatted(index >= 0 && !enumInfo.IsFlagEnum ? enumInfo.DisplayNames[index] : value.ToString());
    }

    /** Long lists like texture formats would otherwise run off the screen. */
    private const float MaxPopupHeightRelativeToViewport = 0.5f;

    private static int _lastHoveredComboItem = -1;
    private static T _originalValue;
    private static bool _hasOriginalValue;
}