#nullable enable
using ImGuiNET;
using System.Diagnostics.CodeAnalysis;
using T3.Core.DataTypes.Vector;
using T3.Core.Operator;
using T3.Core.Operator.Slots;
using T3.Core.SystemUi;
using T3.Editor.Gui.Graph.Dialogs;
using T3.Editor.Gui.Styling;
using T3.Editor.Gui.UiHelpers;
using T3.Editor.Gui.Windows.Layouts;
using T3.Editor.UiModel;
using T3.Editor.UiModel.Commands;
using T3.Editor.UiModel.Commands.Graph;
using T3.Editor.UiModel.InputsAndTypes;
using T3.Editor.UiModel.ProjectHandling;
using T3.Editor.UiModel.Selection;
using T3.SystemUi;

namespace T3.Editor.Gui.Windows;

internal sealed class ParameterWindow : Window
{
    public ParameterWindow()
    {
        _instanceCounter++;
        Config.Title = LayoutHandling.ParametersPrefix + _instanceCounter;
        Config.Visible = true;
        MenuTitle = "Parameters";
        WindowFlags = ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse;
        _parameterWindowInstances.Add(this);
    }

    internal override List<Window> GetInstances()
    {
        return _parameterWindowInstances;
    }

    protected override void Close()
    {
        _parameterWindowInstances.Remove(this);
    }

    protected override void AddAnotherInstance()
    {
        // ReSharper disable once ObjectCreationAsStatement
        new ParameterWindow(); // Required to call constructor
    }

    protected override void DrawContent()
    {
        // Insert invisible spill over input to catch accidental imgui focus attempts
        {
            ImGui.SetNextItemWidth(2);
            var tmpBuffer = "";
            ImGui.PushStyleColor(ImGuiCol.FrameBg, Color.Transparent.Rgba);
            ImGui.InputText("##imgui workaround", ref tmpBuffer, 1);
            ImGui.PopStyleColor();
            ImGui.SameLine();
        }

        var components = ProjectView.Focused;
        if (components == null)
            return;

        var nodeSelection = components.NodeSelection;
        if (DrawSettingsForSelectedAnnotations(nodeSelection))
            return;

        var instance = nodeSelection.GetSelectedInstanceWithoutComposition()
                       ?? nodeSelection.GetSelectedComposition();

        var id = instance?.SymbolChildId ?? Guid.Empty;

        if (id != _lastSelectedInstanceId)
        {
            _lastSelectedInstanceId = id;
            _viewMode = ViewModes.Parameters;
        }

        if (instance == null)
        {
            var selectedInputs = nodeSelection.GetSelectedNodes<IInputUi>().ToList();
            if (selectedInputs.Count > 0)
            {
                instance = components.CompositionInstance;
                var inputUi = selectedInputs.First();
                _viewMode = ViewModes.Settings;
                _parameterSettings.SelectInput(inputUi.Id);
            }
        }

        if (instance == null)
            return;

        var symbol = instance.Symbol;
        var parentSymbol = instance.Parent?.Symbol;
        var path = instance.InstancePath;
        // ReSharper disable once RedundantAssignment
        instance = null; //allow unload of instance type in case a recompilation occurs
        
        // Draw dialogs
        OperatorHelp.EditDescriptionDialog.Draw(symbol); // TODO: This is probably not required...
        RenameInputDialog.Draw();

        if (!symbol.TryGetOrCreateInstance(path, parentSymbol, out instance))
        {
            Log.Error($"Failed to get instance for {symbol.Name} with path {string.Join(",", path)}");
            return;
        }
        
        if (!TryGetUiDefinitions(instance, out var symbolUi, out var symbolChildUi))
            return;

        var modified = false;
        modified |= DrawSymbolHeader(instance, symbolUi);

        if (instance.Parent == null)
        {
            CustomComponents.EmptyWindowMessage("Home canvas.");
            return;
        }

        switch (_viewMode)
        {
            case ViewModes.Parameters:
                DrawChildNameAndFlags(instance);
                DrawParametersArea(instance, symbolChildUi, symbolUi);
                break;
            case ViewModes.Settings:
                modified |= _parameterSettings.DrawContent(symbolUi, nodeSelection);
                break;
            case ViewModes.Help:
                using (new ChildWindowScope("help", Vector2.Zero, ImGuiWindowFlags.None, Color.Transparent))
                {
                    OperatorHelp.DrawHelp(symbolUi);
                }

                break;
        }

        if (modified)
            symbolUi.FlagAsModified();
    }

    private bool DrawSymbolHeader(Instance op, SymbolUi symbolUi)
    {
        var modified = false;
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(5, 5));
        ImGui.BeginChild("header", new Vector2(0, ImGui.GetFrameHeight() + 5),
                         false,
                         ImGuiWindowFlags.AlwaysAutoResize
                         | ImGuiWindowFlags.NoScrollbar
                         | ImGuiWindowFlags.NoScrollWithMouse
                         | ImGuiWindowFlags.NoBackground
                         | ImGuiWindowFlags.AlwaysUseWindowPadding);
        {
            ImGui.AlignTextToFramePadding();
            // Namespace and symbol
            ImGui.PushFont(Fonts.FontBold);

            ImGui.TextUnformatted(op.Symbol.Name);
            ImGui.PopFont();

            ImGui.SameLine();
            ImGui.PushStyleColor(ImGuiCol.Text, UiColors.TextDisabled.Rgba);
            ImGui.TextUnformatted(" in ");
            ImGui.PopStyleColor();

            /*ImGui.SameLine();
            ImGui.TextUnformatted(op.Symbol.SymbolPackage.RootNamespace);*/ // This is redundant I keep it commented out for now just in case

            ImGui.SameLine();

            var namespaceForEdit = op.Symbol.Namespace ?? "";

            var iconSize = ImGui.GetFrameHeight(); 
            var totalIconSpace = (iconSize * 3) + (2 * ImGui.GetStyle().ItemSpacing.X); // 3 icons, 2 spaces between

            ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X - totalIconSpace);

            var symbol = op.Symbol;
            var package = symbol.SymbolPackage;
            if (!package.IsReadOnly)
            {
                var namespaceModified = InputWithTypeAheadSearch.Draw("##namespace", 
                                                                      EditorSymbolPackage.AllSymbols
                                                                                                .Select(i => i.Namespace)
                                                                                                .Distinct()
                                                                                                .OrderBy(i => i),
                                                                      false,
                                                                      ref namespaceForEdit, 
                                                                      out _, 
                                                                      out _);
                if (namespaceModified && !string.IsNullOrEmpty(namespaceForEdit) && ImGui.IsKeyPressed((ImGuiKey)Key.Return))
                {
                    if (!EditableSymbolProject.ChangeSymbolNamespace(symbol, namespaceForEdit, out var reason))
                    {
                        BlockingWindow.Instance.ShowMessageBox(reason, "Could not rename namespace");
                    }
                    else
                    {
                        modified = true;
                    }
                }
            }
            else
            {
                ImGui.Text(op.Symbol.Namespace);
            }

            ImGui.PopStyleColor();

            // Tags...
            {
                ImGui.SameLine();
                modified |= DrawSymbolTagsButton(symbolUi);
            }

            // Settings Modes
            {
                var isSettingsMode = _viewMode == ViewModes.Settings;
                if (_parameterSettings.DrawToggleIcon(symbolUi, ref isSettingsMode))
                {
                    _viewMode = isSettingsMode ? ViewModes.Settings : ViewModes.Parameters;
                }

                CustomComponents.TooltipForLastItem("Click to toggle parameter settings.");
            }

            // Help-Mode
            {
                var isHelpMode = _viewMode == ViewModes.Help;
                if (OperatorHelp.DrawHelpIcon(symbolUi, ref isHelpMode))
                {
                    _viewMode = isHelpMode ? ViewModes.Help : ViewModes.Parameters;
                }
            }
        }

        ImGui.EndChild();
        ImGui.PopStyleVar();
        return modified;
    }

    public static bool DrawSymbolTagsButton(SymbolUi symbolUi)
    {
        var modified = false;
        var tagsPopupId = "##Tags" + symbolUi.Symbol.Id;
        var symbolUiTags = symbolUi.Tags;
        var state = GetButtonStatesForSymbolTags(symbolUiTags);

        if (CustomComponents.IconButton(Icon.Bookmark, Vector2.Zero, state))
        {
            ImGui.OpenPopup(tagsPopupId);
            UpdateTagColors();
        }

        if (ImGui.IsItemHovered())
        {
            ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(7,7));
            ImGui.SetNextWindowContentSize(new Vector2(250 * T3Ui.UiScaleFactor, 0));
            if (ImGui.BeginTooltip())
            {
                CustomComponents.HelpText("Symbol Tags affect listing in the Symbol Library and Search widgets.");
                FormInputs.AddVerticalSpace();
                var hadTags = false;
                        
                foreach (SymbolUi.SymbolTags tagValue in Enum.GetValues(typeof(SymbolUi.SymbolTags)))
                {
                    if (!symbolUiTags.HasFlag(tagValue) || tagValue == SymbolUi.SymbolTags.None)
                        continue;

                    hadTags = true;
                    DrawSymbolTag(symbolUiTags, tagValue);
                    ImGui.SameLine(0, 4);
                            
                    if (ImGui.GetContentRegionAvail().X < 100)
                        ImGui.NewLine();
                }

                if (hadTags)
                {
                    ImGui.NewLine();
                    FormInputs.AddVerticalSpace();
                }
                
                CustomComponents.HelpText("Click to Edit...");
                ImGui.EndTooltip();
            }
            ImGui.PopStyleVar();
        }

        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(7,7));
        if (ImGui.BeginPopup(tagsPopupId))
        {
            ImGui.Text("Symbol tags");
            FormInputs.AddVerticalSpace(4 * T3Ui.UiScaleFactor);
            
            foreach (SymbolUi.SymbolTags tagValue in Enum.GetValues(typeof(SymbolUi.SymbolTags)))
            {
                if (tagValue == SymbolUi.SymbolTags.None)
                    continue;
                
                if (DrawSymbolTag(symbolUiTags, tagValue))
                {
                    modified = true;
                    symbolUi.Tags ^= tagValue;
                }
            }
            
            ImGui.EndPopup();
        }
        ImGui.PopStyleVar();

        return modified;
    }

    public static CustomComponents.ButtonStates GetButtonStatesForSymbolTags(SymbolUi.SymbolTags symbolUiTags)
    {
        var state = (int)symbolUiTags == 0 ? CustomComponents.ButtonStates.Disabled : CustomComponents.ButtonStates.Normal;

        if ((symbolUiTags & (SymbolUi.SymbolTags.Essential | SymbolUi.SymbolTags.Example | SymbolUi.SymbolTags.Project)) != 0)
            state = CustomComponents.ButtonStates.Activated;

        if ((symbolUiTags & (SymbolUi.SymbolTags.Obsolete | SymbolUi.SymbolTags.NeedsFix | SymbolUi.SymbolTags.HasUpdate)) != 0)
            state = CustomComponents.ButtonStates.NeedsAttention;
        return state;
    }

    private static bool DrawSymbolTag(SymbolUi.SymbolTags symbolUiTags, SymbolUi.SymbolTags tagValue)
    {
        ImGui.PushFont(Fonts.FontSmall);
        var isClicked = false;
        var drawList = ImGui.GetWindowDrawList();
        var isActive = symbolUiTags.HasFlag(tagValue);
        var tagName = tagValue.ToString().ToUpperInvariant();
        var size = ImGui.CalcTextSize(tagName);
        var padding = new Vector2(10, 4) * T3Ui.UiScaleFactor;
        if (ImGui.InvisibleButton(tagName, size + padding * 2))
        {
            isClicked = true;
        }

        if (!_tagColors.TryGetValue(tagValue, out var color))
            color = UiColors.TextMuted;

        var textColor = isActive ? UiColors.ForegroundFull : ColorVariations.OperatorLabel.Apply(color).Fade(0.4f);
        var bgColor = isActive ? color : ColorVariations.OperatorBackground.Apply(color).Fade(0.2f);

        var isHovered = ImGui.IsItemHovered();
        var hoverFade = isHovered ? 1 : 0.8f;

        var pMin = ImGui.GetItemRectMin();
        var pMax = ImGui.GetItemRectMax();
        drawList.AddRectFilled(pMin, pMax, bgColor.Fade(hoverFade), 5);
        drawList.AddText(pMin + padding, textColor.Fade(hoverFade), tagName);
        ImGui.PopFont();
        return isClicked;
    }

    /// <summary>
    /// This is kind of annoying because the definition of the colors might change on theme switching.
    /// So this list would need to be updated on popup open... :-/
    /// </summary>
    private static Dictionary<SymbolUi.SymbolTags, Color> UpdateTagColors()
    {
        return new Dictionary<SymbolUi.SymbolTags, Color>
                   {
                       { SymbolUi.SymbolTags.Essential, UiColors.StatusAutomated },
                       { SymbolUi.SymbolTags.Example, UiColors.StatusAutomated },
                       { SymbolUi.SymbolTags.Project, UiColors.StatusAutomated },
                       { SymbolUi.SymbolTags.Obsolete, UiColors.StatusAttention },
                       { SymbolUi.SymbolTags.NeedsFix, UiColors.StatusAttention },
                       { SymbolUi.SymbolTags.HasUpdate, UiColors.StatusAttention },
                   };
    }

    private static Dictionary<SymbolUi.SymbolTags, Color> _tagColors = UpdateTagColors();

    private void DrawParametersArea(Instance instance, SymbolUi.Child symbolChildUi, SymbolUi symbolUi)
    {
        var compositionSymbolUi = ProjectView.Focused?.InstView?.SymbolUi;
        if (compositionSymbolUi == null)
            return;

        if (compositionSymbolUi == symbolUi)
        {
            compositionSymbolUi = instance.Parent?.GetSymbolUi();
            if (compositionSymbolUi == null)
                return;
        }
        
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(5, 5));
        ImGui.BeginChild("parameters", Vector2.Zero, false, ImGuiWindowFlags.AlwaysUseWindowPadding);

        

        var selectedChildSymbolUi = instance.GetSymbolUi();
        
        // Draw parameters
        DrawParameters(instance, selectedChildSymbolUi, symbolChildUi, compositionSymbolUi, false, this);
        FormInputs.AddVerticalSpace(15);

        
        if (OperatorHelp.DrawHelpSummary(symbolUi))
            _viewMode = ViewModes.Help;

        OperatorHelp.DocumentationRenderer.DrawLinksAndExamples(symbolUi);
        ImGui.EndChild();
        ImGui.PopStyleVar();
    }

    private void DrawChildNameAndFlags(Instance op)
    {
        var hideParameters = _viewMode == ViewModes.Help || _parameterSettings.IsActive;
        if (hideParameters)
            return;

        var symbolChildUi = op.GetChildUi();

        // Add spacing before
        FormInputs.AddVerticalSpace(3);
        ImGui.Indent(5);

        // SymbolChild Name
        if (symbolChildUi != null)
        {
            // Calculate button widths using the longer text variants for consistent spacing
            ImGui.PushFont(Fonts.FontBold);
            var disabledWidth = ImGui.CalcTextSize("DISABLED").X + (ImGui.GetStyle().FramePadding.X * 2);
            var bypassedWidth = ImGui.CalcTextSize("BYPASSED").X + (ImGui.GetStyle().FramePadding.X * 2);
            ImGui.PopFont();

            var totalButtonSpace = disabledWidth + bypassedWidth + ImGui.GetStyle().ItemSpacing.X + 5; // +5 for some extra spacing on the right

            ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X - totalButtonSpace);

            var nameForEdit = symbolChildUi.SymbolChild.Name;

            ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 5);
            if (ImGui.InputText("##symbolChildName", ref nameForEdit, 128))
            {
                if (_symbolChildNameCommand != null)
                    _symbolChildNameCommand.NewName = nameForEdit;
                symbolChildUi.SymbolChild.Name = nameForEdit;
            }

            ImGui.PopStyleVar();

            if (ImGui.IsItemActivated() && op.Parent != null)
            {
                _symbolChildNameCommand = new ChangeSymbolChildNameCommand(symbolChildUi, op.Parent.Symbol);
            }

            if (ImGui.IsItemDeactivatedAfterEdit())
            {
                if (_symbolChildNameCommand != null)
                    UndoRedoStack.Add(_symbolChildNameCommand);

                _symbolChildNameCommand = null;
            }

            // Fake placeholder text
            if (string.IsNullOrEmpty(nameForEdit))
                ImGui.GetWindowDrawList().AddText(ImGui.GetItemRectMin() + new Vector2(5, 4),
                                                  UiColors.TextMuted.Fade(0.5f),
                                                  "Untitled instance");

            ImGui.PushStyleColor(ImGuiCol.Button, UiColors.BackgroundInputField.Rgba);
            // Disabled toggle
            {
                ImGui.SameLine();
                ImGui.PushFont(Fonts.FontBold);
                if (symbolChildUi.SymbolChild.IsDisabled)
                {
                    ImGui.PushStyleColor(ImGuiCol.Button, UiColors.StatusAttention.Rgba);
                    ImGui.PushStyleColor(ImGuiCol.Text, UiColors.Text.Rgba);
                    if (ImGui.Button("DISABLED", new Vector2(disabledWidth, ImGui.GetFrameHeight())))
                    {
                        UndoRedoStack.AddAndExecute(new ChangeInstanceIsDisabledCommand(symbolChildUi, false));
                    }

                    ImGui.PopStyleColor(2);
                }
                else
                {
                    ImGui.PushStyleColor(ImGuiCol.Text, UiColors.TextMuted.Rgba);
                    if (ImGui.Button("ENABLED", new Vector2(disabledWidth, ImGui.GetFrameHeight())))
                    {
                        UndoRedoStack.AddAndExecute(new ChangeInstanceIsDisabledCommand(symbolChildUi, true));
                    }

                    ImGui.PopStyleColor();
                }

                ImGui.PopFont();
            }

            // Bypass
            {
                ImGui.SameLine();
                ImGui.PushFont(Fonts.FontBold);
                if (symbolChildUi.SymbolChild.IsBypassed)
                {
                    ImGui.PushStyleColor(ImGuiCol.Button, UiColors.StatusAttention.Rgba);
                    ImGui.PushStyleColor(ImGuiCol.Text, UiColors.Text.Rgba);

                    // TODO: check if bypassable
                    if (ImGui.Button("BYPASSED", new Vector2(bypassedWidth, ImGui.GetFrameHeight())))
                    {
                        UndoRedoStack.AddAndExecute(new ChangeInstanceBypassedCommand(symbolChildUi.SymbolChild, false));
                    }

                    ImGui.PopStyleColor(2);
                }
                else
                {
                    var bypassable = symbolChildUi.SymbolChild.IsBypassable();

                    ImGui.PushStyleColor(ImGuiCol.Text, UiColors.TextMuted.Rgba);

                    if (!bypassable)
                    {
                        ImGui.BeginDisabled();
                    }

                    if (ImGui.Button("BYPASS", new Vector2(bypassedWidth, ImGui.GetFrameHeight())))
                    {
                        UndoRedoStack.AddAndExecute(new ChangeInstanceBypassedCommand(symbolChildUi.SymbolChild, true));
                    }

                    if (!bypassable)
                    {
                        CustomComponents.TooltipForLastItem("This operator cannot be bypassed");
                        ImGui.EndDisabled();
                    }

                    ImGui.PopStyleColor();
                }

                ImGui.PopFont();
            }
            ImGui.PopStyleColor();
        }

        ImGui.Unindent(5);
      
    }

    /// <summary>
    /// Draw all parameters of the selected instance.
    /// The actual implementation is done in <see cref="InputValueUi{T}.DrawParameterEdit"/>  
    /// </summary>
    public static void DrawParameters(Instance instance, SymbolUi symbolUi, SymbolUi.Child symbolChildUi,
                                      SymbolUi compositionSymbolUi, bool hideNonEssentials, ParameterWindow? parameterWindow = null)
    {
        var groupState = GroupState.None;

        if (instance.Parent == null)
        {
            Log.Warning("can't draw parameters for root instance");
            return;
        }

        ImGui.PushStyleColor(ImGuiCol.FrameBg, UiColors.BackgroundButton.Rgba);
        ImGui.PushStyleColor(ImGuiCol.FrameBgHovered, UiColors.BackgroundHover.Rgba);
        foreach (var inputSlot in instance.Inputs)
        {
            if (!symbolUi.InputUis.TryGetValue(inputSlot.Id, out var inputUi))
            {
                Log.Warning("Trying to access an non existing input, probably the op instance is not the actual one.");
                continue;
            }

            InsertGroupsAndPadding(inputUi, ref groupState);

            ImGui.PushID(inputSlot.Id.GetHashCode());
            var skipIfDefault = groupState == GroupState.InsideClosed;

            // Draw the actual parameter line implemented
            // in the generic InputValueUi<T>.DrawParameterEdit() method

            ImGui.PushID(inputSlot.Id.GetHashCode());
            var editState = inputUi.DrawParameterEdit(inputSlot, compositionSymbolUi, symbolChildUi, hideNonEssentials: hideNonEssentials, skipIfDefault);
            ImGui.PopID();

            // ... and handle the edit state
            if ((editState&InputEditStateFlags.Started) != 0)
            {
                _inputSlotForActiveCommand = inputSlot;
                _inputValueCommandInFlight =
                    new ChangeInputValueCommand(instance.Parent.Symbol, instance.SymbolChildId, inputSlot.Input, inputSlot.Input.Value);
            }

            if ((editState&InputEditStateFlags.Modified) != 0)
            {
                if (_inputValueCommandInFlight == null || _inputSlotForActiveCommand != inputSlot)
                {
                    _inputValueCommandInFlight =
                        new ChangeInputValueCommand(instance.Parent.Symbol, instance.SymbolChildId, inputSlot.Input, inputSlot.Input.Value);
                    _inputSlotForActiveCommand = inputSlot;
                }

                _inputValueCommandInFlight.AssignNewValue(inputSlot.Input.Value);
                inputSlot.DirtyFlag.Invalidate();
            }

            if ((editState&InputEditStateFlags.Finished) != 0)
            {
                if (_inputValueCommandInFlight != null && _inputSlotForActiveCommand == inputSlot)
                {
                    UndoRedoStack.Add(_inputValueCommandInFlight);
                }

                _inputValueCommandInFlight = null;
            }

            if (editState == InputEditStateFlags.ShowOptions && parameterWindow != null)
            {
                parameterWindow._viewMode = ViewModes.Settings;
                parameterWindow._parameterSettings.SelectedInputId = inputUi.Id;
                //NodeSelection.SetSelection(inputUi);
            }

            ImGui.PopID();
        }

        ImGui.PopStyleColor(2);

        if (groupState == GroupState.InsideOpened)
            FormInputs.EndGroup();
    }

    private static void InsertGroupsAndPadding(IInputUi inputUi, ref GroupState groupState)
    {
        // Layouts padding and groups
        if (inputUi.AddPadding)
            FormInputs.AddVerticalSpace(2);

        if (string.IsNullOrEmpty(inputUi.GroupTitle))
            return;

        if (groupState == GroupState.InsideOpened)
            FormInputs.EndGroup();

        if (inputUi.GroupTitle.EndsWith("..."))
        {
            var isOpen = FormInputs.BeginGroup(inputUi.GroupTitle);
            groupState = isOpen ? GroupState.InsideOpened : GroupState.InsideClosed;
        }
        else
        {
            groupState = GroupState.None;
            FormInputs.AddVerticalSpace(5);
            ImGui.PushFont(Fonts.FontSmall);
            ImGui.PushStyleColor(ImGuiCol.Text, UiColors.TextMuted.Rgba);
            ImGui.SetCursorPosX(4);
            ImGui.TextUnformatted(inputUi.GroupTitle.ToUpperInvariant());
            ImGui.PopStyleColor();
            ImGui.PopFont();
            FormInputs.AddVerticalSpace(2);
        }
    }

    private static bool TryGetUiDefinitions(Instance instance, 
                                            [NotNullWhen(true)] out SymbolUi? symbolUi, 
                                            [NotNullWhen(true)] out SymbolUi.Child? symbolChildUi)
    {
        symbolUi = instance.GetSymbolUi();
        symbolChildUi = null;
        if (instance.Parent == null)
            return false;

        var parentUi = instance.Parent.GetSymbolUi();
        symbolChildUi = parentUi.ChildUis[instance.SymbolChildId];
            return true;
    }

    // TODO: Refactor this into a separate class
    private static bool DrawSettingsForSelectedAnnotations(NodeSelection nodeSelection)
    {
        var somethingVisible = false;
        // Draw Annotation settings
        foreach (var annotation in nodeSelection.GetSelectedNodes<Annotation>())
        {   
            ImGui.PushID(annotation.Id.GetHashCode());
            ImGui.Indent(10f * T3Ui.UiScaleFactor);
            //ImGui.SetCursorPosX(10f*T3Ui.UiScaleFactor);
            ImGui.PushFont(Fonts.FontLarge);
            ImGui.TextUnformatted("Annotation settings");
            ImGui.PopFont();

            FormInputs.AddVerticalSpace();
            ImGui.Separator();
            FormInputs.AddVerticalSpace();

            ImGui.ColorEdit4("color", ref annotation.Color.Rgba);

            FormInputs.AddVerticalSpace();
            CustomComponents.StylizedText("Label:", Fonts.FontBold, UiColors.TextMuted, true);
            ImGui.TextWrapped(annotation.Label);

            FormInputs.AddVerticalSpace();
            CustomComponents.StylizedText("Description:", Fonts.FontBold, UiColors.TextMuted.Rgba, true);
            ImGui.TextWrapped(annotation.Title);

            FormInputs.AddVerticalSpace();
            ImGui.Separator();
            FormInputs.AddVerticalSpace();
            ImGui.PushStyleColor(ImGuiCol.Text, UiColors.TextMuted.Rgba);
            CustomComponents.IconButton(Icon.Hint,new Vector2(20,20)* T3Ui.UiScaleFactor);
            ImGui.SameLine();
            ImGui.TextWrapped("How to create an Annotation:\n" +
                "1.Select the operators you want to include in the annotation.\n" +
                "2.Shift + A to add the annotation." );

            FormInputs.AddVerticalSpace();
            CustomComponents.IconButton(Icon.Hint, new Vector2(20, 20) * T3Ui.UiScaleFactor);
            ImGui.SameLine();
            ImGui.TextWrapped("How to edit an Annotation:\n" +
                "1.Double click on the header.\n" +
                "2.Type in label and/or description");
            ImGui.PopStyleColor();
            ImGui.Unindent();
            ImGui.PopID();
            somethingVisible = true;
        }

        return somethingVisible;
    }

    private enum GroupState
    {
        None,
        InsideClosed,
        InsideOpened,
    }

    private enum ViewModes
    {
        Parameters,
        Settings,
        Help,
    }

    private ViewModes _viewMode = ViewModes.Parameters;

    public static bool IsAnyInstanceVisible()
    {
        return WindowManager.IsAnyInstanceVisible<ParameterWindow>();
    }

    private static readonly List<Window> _parameterWindowInstances = new();
    private ChangeSymbolChildNameCommand? _symbolChildNameCommand;
    private static ChangeInputValueCommand? _inputValueCommandInFlight;
    private static IInputSlot? _inputSlotForActiveCommand;
    private static int _instanceCounter;
    private Guid _lastSelectedInstanceId;

    private readonly ParameterSettings _parameterSettings = new();
    public static readonly RenameInputDialog RenameInputDialog = new();
}