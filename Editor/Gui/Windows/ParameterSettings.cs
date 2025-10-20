using ImGuiNET;
using T3.Core.DataTypes.Vector;
using T3.Editor.Gui.Input;
using T3.Editor.Gui.Styling;
using T3.Editor.Gui.UiHelpers;
using T3.Editor.UiModel;
using T3.Editor.UiModel.InputsAndTypes;
using T3.Editor.UiModel.Modification;
using T3.Editor.UiModel.Selection;

namespace T3.Editor.Gui.Windows;

public sealed class ParameterSettings
{
    public bool DrawToggleIcon(SymbolUi symbolUi, ref bool isEnabled)
    {
        var modified = false;

        IsActive = isEnabled;
        ImGui.SameLine();
        var w = ImGui.GetFrameHeight();
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + 2);
        ImGui.PushStyleColor(ImGuiCol.Button, Color.Transparent.Rgba);
        if (CustomComponents.IconButton(
                                        Icon.List,
                                        new Vector2(w, w),
                                        IsActive
                                            ? CustomComponents.ButtonStates.Activated
                                            : CustomComponents.ButtonStates.Dimmed
                                       ))
        {
            IsActive = !IsActive;
            modified = true;
            isEnabled = IsActive;
        }

        ImGui.PopStyleColor();
        return modified;
    }

    internal bool DrawContent(SymbolUi symbolUi, NodeSelection nodeSelection)
    {
        var modified = false;

        using (new ChildWindowScope("wrapper",
                                    new Vector2(0, 0),
                                    ImGuiWindowFlags.None,
                                    UiColors.BackgroundInputField))
        {
            FormInputs.AddVerticalSpace(5);

            var selectedInputUi = DrawSelectionArea(symbolUi, nodeSelection);
            ImGui.SameLine(0, 0);
            modified = DrawSettingsForParameter(selectedInputUi);
        }

        return modified;
    }

    private bool _isDraggingParameterOrder;
    private static bool _wasDraggingParameterOrder;

    private List<IInputUi> _symbolUisWhileDragging = [];

    private IInputUi DrawSelectionArea(SymbolUi symbolUi, NodeSelection nodeSelection)
    {
        IInputUi selectedInputUi = null;

        using (new ChildWindowScope("selector",
                                    new Vector2(SelectionWidth * T3Ui.UiScaleFactor, 0),
                                    ImGuiWindowFlags.NoBackground,
                                    Color.Transparent,
                                    0))
        {
            var dl = ImGui.GetWindowDrawList();
            var symbol = symbolUi.Symbol;

            ImGui.PushStyleVar(ImGuiStyleVar.ButtonTextAlign, new Vector2(0, 0.5f));

            var inputUis = _isDraggingParameterOrder ? _symbolUisWhileDragging : symbolUi.InputUis.Values.ToList();
            var dragCompleted = false;

            for (var index = 0; index < inputUis.Count; index++)
            {
                var inputUi = inputUis[index];
                var isSelected = inputUi.InputDefinition.Id == SelectedInputId;
                if (isSelected)
                    selectedInputUi = inputUi;

                var padding = inputUi.AddPadding ? 4 : 0f;
                if (!string.IsNullOrEmpty(inputUi.GroupTitle))
                    padding = 13;

                var size = new Vector2(SelectionWidth * T3Ui.UiScaleFactor, padding + ImGui.GetFrameHeight());
                var clicked = ImGui.InvisibleButton(inputUi.InputDefinition.Name, size);

                var typeColor = TypeUiRegistry.GetPropertiesForType(inputUi.Type).Color;
                var textColor = isSelected ? ColorVariations.OperatorLabel.Apply(typeColor) : typeColor.Fade(0.9f);
                var backgroundColor = isSelected ? UiColors.WindowBackground : Color.Transparent;
                if ((ImGui.IsItemHovered() || ImGui.IsItemActive()) && !isSelected)
                {
                    backgroundColor = UiColors.WindowBackground.Fade(0.5f);
                }

                // Handle dragging
                var itemMin = ImGui.GetItemRectMin();
                var itemMax = ImGui.GetItemRectMax();

                if (!_isDraggingParameterOrder && ImGui.IsItemActive() && ImGui.IsMouseDragging(ImGuiMouseButton.Left))
                {
                    _isDraggingParameterOrder = true;
                    _symbolUisWhileDragging = symbolUi.InputUis.Values.ToList();
                }
                else if (_isDraggingParameterOrder)
                {
                    //if (ImGui.IsItemActive() && !ImGui.IsItemHovered())
                    if (ImGui.IsItemActive())
                    {
                        var mouseY = ImGui.GetMousePos().Y;
                        var halfHeight = ImGui.GetItemRectSize().Y / 2;
                        var indexDelta = 0;
                        if (mouseY < itemMin.Y - halfHeight && index > 0)
                        {
                            indexDelta = -1;
                        }
                        else if (mouseY > itemMax.Y + halfHeight && index < symbolUi.InputUis.Count - 1)
                        {
                            indexDelta = 1;
                        }

                        if (indexDelta != 0)
                        {
                            (_symbolUisWhileDragging[index + indexDelta], _symbolUisWhileDragging[index])
                                = (_symbolUisWhileDragging[index], _symbolUisWhileDragging[index + indexDelta]);

                            _wasDraggingParameterOrder = true;
                        }
                    }
                }

                dl.AddRectFilled(itemMin, ImGui.GetItemRectMax(), backgroundColor, 7, ImDrawFlags.RoundCornersLeft);

                // Draw group title
                if (!string.IsNullOrEmpty(inputUi.GroupTitle))
                {
                    dl.AddText(Fonts.FontSmall, Fonts.FontSmall.FontSize,
                               itemMin
                               + new Vector2(6, 0),
                               UiColors.TextMuted.Fade(0.5f),
                               inputUi.GroupTitle);
                }

                // Draw name
                var font = isSelected ? Fonts.FontBold : Fonts.FontNormal;
                dl.AddText(font, font.FontSize,
                           itemMin
                           + new Vector2(8, padding + 2),
                           textColor,
                           inputUi.InputDefinition.Name.AddSpacesForImGuiOutput());

                // Drag handle
                if (ImGui.IsItemActive() || (!ImGui.IsAnyItemActive() && ImGui.IsItemHovered()))
                {
                    for (int i = 0; i < 3; i++)
                    {
                        var pos = new Vector2(itemMin.X + 2, itemMin.Y + i * 5 + size.Y / 2 - 6);
                        dl.AddRectFilled(pos, pos + new Vector2(3, 2), UiColors.ForegroundFull.Fade(0.2f), 0);
                    }
                }

                // Handle click
                if (clicked && !_wasDraggingParameterOrder)
                {
                    SelectedInputId = inputUi.InputDefinition.Id;
                    selectedInputUi = inputUi;
                    var selectedInstance = nodeSelection.GetFirstSelectedInstance();
                    if (selectedInstance == null)
                        nodeSelection.SetSelection(inputUi);
                }

                if (ImGui.IsItemDeactivated())
                {
                    dragCompleted = true;
                }
            }

            ImGui.PopStyleVar();

            if (selectedInputUi == null && symbolUi.InputUis.Count > 0)
            {
                SelectedInputId = symbolUi.InputUis.Values.First().InputDefinition.Id;
            }

            if (dragCompleted && _wasDraggingParameterOrder)
            {
                _isDraggingParameterOrder = false;

                // Sort inputDef by order of inputUis...
                var originalList = symbol.InputDefinitions.ToList();
                symbol.InputDefinitions.Clear();
                foreach (var inputUi in _symbolUisWhileDragging)
                {
                    var inputDefinition = originalList.Find(def => def.Id == inputUi.InputDefinition.Id);
                    if (inputDefinition != null)
                    {
                        symbol.InputDefinitions.Add(inputDefinition);
                    }
                }

                _symbolUisWhileDragging.Clear();
                _wasDraggingParameterOrder = false;
                symbol.SortInputSlotsByDefinitionOrder();
                InputsAndOutputs.AdjustInputOrderOfSymbol(symbol);
                Log.Debug(" Applying new parameter order");
            }
        }

        return selectedInputUi;
    }

    private static bool DrawSettingsForParameter(IInputUi selectedInputUi)
    {
        var modified = false;
        using (new ChildWindowScope("settings",
                                    new Vector2(0, 0), ImGuiWindowFlags.None,
                                    UiColors.WindowBackground.Rgba,
                                    10,
                                    5))
        {
            if (selectedInputUi != null)
            {
                FormInputs.AddSectionHeaderParam(selectedInputUi.InputDefinition.Name);
                ImGui.PushStyleColor(ImGuiCol.Text, UiColors.TextMuted.Rgba);
                ImGui.TextUnformatted(selectedInputUi.Type.Name);
                ImGui.PopStyleColor();
                FormInputs.AddVerticalSpace(5);

                var style = ParameterListStyles.Default;
                if (selectedInputUi.AddPadding)
                {
                    style = ParameterListStyles.WithPadding;
                }
                else if (selectedInputUi.GroupTitle != null)
                {
                    style = ParameterListStyles.WithGroup;
                }

                FormInputs.DrawFieldSetHeader("Show parameter as");
                if (FormInputs.AddEnumDropdown(ref style, null))
                {
                    switch (style)
                    {
                        case ParameterListStyles.Default:
                            selectedInputUi.AddPadding = false;
                            selectedInputUi.GroupTitle = null;
                            break;
                        case ParameterListStyles.WithPadding:
                            selectedInputUi.AddPadding = true;
                            selectedInputUi.GroupTitle = null;
                            break;
                        case ParameterListStyles.WithGroup:
                            selectedInputUi.AddPadding = false;
                            selectedInputUi.GroupTitle = "";
                            break;
                    }

                    modified = true;
                }

                if (style == ParameterListStyles.WithGroup)
                {
                    var groupTitle = selectedInputUi.GroupTitle;
                    if (FormInputs.AddStringInput("##groupTitle", ref groupTitle, "Group Title", null,
                                                  "Group title shown above parameter\n\nGroup will be collapsed by default if name ends with '...' (three dots)."))
                    {
                        selectedInputUi.GroupTitle = groupTitle;
                        modified = true;
                    }
                }

                {
                    FormInputs.DrawFieldSetHeader("Relevancy in Graph");
                    var tmpForRef = selectedInputUi.Relevancy;
                    if (FormInputs.AddEnumDropdown(ref tmpForRef, null, defaultValue: Relevancy.Optional))
                    {
                        selectedInputUi.Relevancy = tmpForRef;
                        modified = true;
                    }
                }

                {
                    FormInputs.AddVerticalSpace();
                    FormInputs.SetIndentToLeft();
                    var tmp = selectedInputUi.ExcludedFromPresets;
                    if (FormInputs.AddCheckBox("Excluded from presets", 
                                               ref tmp,
                                               """
                                               Saving certain parameters in presets might not be useful or expected. E.g. for Text rendering, the actual string should not be part of the text style.
                                               This setting does also apply to Snapshots.
                                               """,
                                               false))
                    {
                        selectedInputUi.ExcludedFromPresets = tmp;
                        modified = true;
                    }
                    FormInputs.SetIndentToParameters();
                }

                // Draw additional settings for input types like Vector2, Vector3, etc.
                modified |= selectedInputUi.DrawSettings();

                FormInputs.AddVerticalSpace();

                FormInputs.DrawFieldSetHeader("Documentation");
                var width = ImGui.GetContentRegionAvail().X;
                var description = string.IsNullOrEmpty(selectedInputUi.Description) ? string.Empty : selectedInputUi.Description;

                ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 5);
                if (ImGui.InputTextMultiline("##parameterDescription", ref description, 16000, new Vector2(width, 0)))
                {
                    selectedInputUi.Description = string.IsNullOrEmpty(description) ? null : description;
                    modified = true;
                }

                ImGui.PopStyleVar();
            }
            else
            {
                CustomComponents.EmptyWindowMessage("no input selected...");
            }
        }

        return modified;
    }

    private const float SelectionWidth = 150;

    private enum ParameterListStyles
    {
        Default,
        WithPadding,
        WithGroup,
    }

    internal Guid SelectedInputId { get; set; }

    public bool IsActive { get; private set; } = false;

    public void SelectInput(Guid inputUiId)
    {
        SelectedInputId = inputUiId;
    }
}