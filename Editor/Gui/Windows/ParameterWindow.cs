#nullable enable
using ImGuiNET;
using System.Diagnostics.CodeAnalysis;
using T3.Editor.Gui.Help;
using T3.Core.DataTypes.Vector;
using T3.Core.Operator;
using T3.Core.Operator.Slots;
using T3.Core.SystemUi;
using T3.Editor.App;
using T3.Editor.Gui.Graph.Dialogs;
using T3.Editor.Gui.Input;
using T3.Editor.Gui.Interaction.Variations;
using T3.Editor.Gui.Interaction.Variations.Model;
using T3.Editor.Gui.Styling;
using T3.Editor.Skills.Training;
using T3.Editor.Gui.Windows.Layouts;
using T3.Editor.Gui.Windows.Output;
using T3.Editor.Gui.Windows.RenderExport;
using T3.Editor.UiModel;
using T3.Editor.UiModel.Commands;
using T3.Editor.UiModel.Commands.Graph;
using T3.Editor.UiModel.InputsAndTypes;
using T3.Editor.Gui.Windows.Variations;
using T3.Editor.UiModel.Modification;
using T3.Editor.UiModel.ProjectHandling;
using T3.Editor.UiModel.Selection;
using T3.SystemUi;

namespace T3.Editor.Gui.Windows;

[HelpUiID("ParameterWindow")]
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
        

        if (!NodeSelection.TryGetSelectedInstanceOrInput(out var instance, out var inputUi, out _selectionChanged))
        {
            var nodeSelection = ProjectView.Focused?.NodeSelection;
            var sectionSettingsShown = nodeSelection != null && DrawSettingsForSelectedSections(nodeSelection);
            if (!sectionSettingsShown
                && VariationHandling.ActivePoolForSnapshots != null
                && !SkillTraining.IsInPlayMode)
            {
                // Show the active composition's header (name/namespace) too, so entering an
                // operator doesn't require a background click to reveal it.
                var composition = ProjectView.Focused?.CompositionInstance;
                if (composition != null)
                {
                    if (DrawSymbolHeader(composition, composition.GetSymbolUi()))
                        composition.GetSymbolUi().FlagAsModified();
                }

                _snapshotControlView.Draw();
            }

            _lastSelectedInstanceId = Guid.Empty;
            return;
        }
        
        if (inputUi != null)
        {
            _viewMode = ViewModes.Settings;
            _parameterSettings.SelectInput(inputUi.Id);
        }
        else if (_selectionChanged)
        {
            _viewMode = ViewModes.Parameters; // Switch back from documentation
        }
        
        var symbol = instance.Symbol;
        var parentSymbol = instance.Parent?.Symbol;
        var path = instance.InstancePath;
        
        // ReSharper disable once RedundantAssignment
        instance = null; //allow to unload of instance type in case a recompilation occurs
        
        // Draw dialogs
        RenameInputDialog.Draw();

        if (!symbol.TryGetOrCreateInstance(path, parentSymbol, out instance))
        {
            Log.Error($"Failed to get instance for {symbol.Name} with path {string.Join(",", path)}");
            return;
        }

        // Clicking the graph background selects the composition itself. If it has no inputs
        // to show, the snapshot control view is more useful than an empty parameter list.
        // Handled before the ui-definition lookup, which requires a parent and would bail
        // out for compositions at the home level.
        if (_viewMode == ViewModes.Parameters
            && IsFocusedComposition(instance)
            && instance.Inputs.Count == 0
            && VariationHandling.ActivePoolForSnapshots != null
            && !SkillTraining.IsInPlayMode)
        {
            if (DrawSymbolHeader(instance, instance.GetSymbolUi()))
                instance.GetSymbolUi().FlagAsModified();

            _snapshotControlView.Draw();
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
                modified |= _parameterSettings.DrawContent(symbolUi, ProjectView.Focused!.NodeSelection);
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
                         ImGuiChildFlags.AlwaysUseWindowPadding,
                         ImGuiWindowFlags.NoScrollbar
                         | ImGuiWindowFlags.NoScrollWithMouse
                         | ImGuiWindowFlags.NoBackground);
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
            // 3 icons; the settings and help buttons each nudge themselves +2px right
            var iconBlockWidth = 3 * iconSize + 2 * (ImGui.GetStyle().ItemSpacing.X + 2);

            ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X - iconBlockWidth - ImGui.GetStyle().ItemSpacing.X);

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
                if (namespaceModified && !string.IsNullOrEmpty(namespaceForEdit) && ImGui.IsKeyPressed(Key.Return.ToImGuiKey()))
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

            // Tags...
            {
                ImGui.SameLine();
                // Hard right-align: read-only packages render the namespace as plain text,
                // which ignores the item width set above.
                ImGui.SetCursorPosX(ImGui.GetWindowWidth() - ImGui.GetStyle().WindowPadding.X - iconBlockWidth);
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

            // Help icon — shows the doc in the Help window (tooltip while that window is closed).
            OperatorHelp.DrawHelpIcon(symbolUi);
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
        var state = (int)symbolUiTags == 0 ? CustomComponents.ButtonStates.Disabled : CustomComponents.ButtonStates.Emphasized;

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
                       { SymbolUi.SymbolTags.Essential, UiColors.StatusActivated },
                       { SymbolUi.SymbolTags.Example, UiColors.StatusActivated },
                       { SymbolUi.SymbolTags.Project, UiColors.StatusActivated },
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
        ImGui.BeginChild("parameters", Vector2.Zero, ImGuiChildFlags.None | ImGuiChildFlags.AlwaysUseWindowPadding, ImGuiWindowFlags.NoBackground);

        // Scroll back up on operator change
        if (_selectionChanged)
            ImGui.SetScrollY(0);

        var selectedChildSymbolUi = instance.GetSymbolUi();
        
        // Draw parameters
        DrawParameters(instance, selectedChildSymbolUi, symbolChildUi, compositionSymbolUi, false, this);
        FormInputs.AddVerticalSpace(15);

        
        // With the Help window open the summary would only duplicate the doc shown there.
        if (!HelpWindow.IsOpen && OperatorHelp.DrawHelpSummary(symbolUi))
            HelpWindow.ShowTopic(HelpTopic.ForOperator(symbolUi.Symbol.Id), showWindow: true);

        OperatorHelp.DocumentationRenderer.DrawLinksAndExamples(symbolUi);

        CustomComponents.HandleDragScrolling(this);
        ImGui.EndChild();
        ImGui.PopStyleVar();
    }

    private void DrawChildNameAndFlags(Instance op)
    {
        var hideParameters = _parameterSettings.IsActive;
        if (hideParameters)
            return;

        var symbolChildUi = op.GetChildUi();
        if (symbolChildUi == null)
            return;

        // Add spacing before
        FormInputs.AddVerticalSpace(3);
        ImGui.Indent(5);

        var scale = T3Ui.UiScaleFactor;
        var frameHeight = ImGui.GetFrameHeight();
        var spacing = ImGui.GetStyle().ItemSpacing.X;
        var groupGap = 6 * scale;

        // Icon group: bypass + disable + pin-to-output (like the parameter popup)
        var iconGroupWidth = 3 * frameHeight + 2 * spacing;

        // Preset selector only when the preset pool tracks this instance
        var presetPool = VariationHandling.ActivePoolForPresets;
        var showPresets = presetPool != null
                          && VariationHandling.ActiveInstanceForPresets is { } presetInstance
                          && presetInstance.SymbolChildId == op.SymbolChildId
                          && presetInstance.Symbol.Id == op.Symbol.Id;

        // Reserve exact widths so the icon group ends flush right, aligned with the
        // symbol header above (whose child has 5px window padding).
        var availWidth = ImGui.GetContentRegionAvail().X - 5 * scale;
        float nameWidth;
        var dropdownWidth = 0f;
        if (showPresets)
        {
            // name | dropdown | new-preset | gap | icons
            var flexibleWidth = availWidth - (frameHeight + iconGroupWidth + 3 * spacing + groupGap);
            nameWidth = MathF.Max(60 * scale, flexibleWidth * 0.5f);
            dropdownWidth = MathF.Max(60 * scale, flexibleWidth - nameWidth);
        }
        else
        {
            nameWidth = MathF.Max(60 * scale, availWidth - (iconGroupWidth + spacing + groupGap));
        }

        // SymbolChild Name
        {
            ImGui.SetNextItemWidth(nameWidth);

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
        }

        // Preset dropdown + new-preset button
        if (showPresets)
        {
            CollectPresets(presetPool!);

            // Which preset fully describes the current values? Scanning all presets (instead of only
            // the last-activated one) keeps the indication correct after loading a project or when
            // values were changed from elsewhere.
            var matchingPreset = FindMatchingPreset(presetPool!, op);
            var activePreset = presetPool!.ActiveVariation is { IsPreset: true } activeVariation
                               && _presets.Contains(activeVariation)
                ? activeVariation
                : null;

            string label;
            Color labelColor;
            if (matchingPreset != null)
            {
                label = VariationPicker.GetTitle(matchingPreset);
                labelColor = UiColors.ForegroundFull;
            }
            else if (activePreset != null)
            {
                // Based on a preset but modified — the emphasized new-preset button carries the hint
                label = VariationPicker.GetTitle(activePreset);
                labelColor = UiColors.TextMuted;
            }
            else if (_presets.Count == 0)
            {
                label = "No Presets";
                labelColor = UiColors.TextMuted;
            }
            else
            {
                if (_presets.Count != _lastPresetCountForLabel)
                {
                    _lastPresetCountForLabel = _presets.Count;
                    _presetCountLabel = _presets.Count == 1 ? "1 Preset" : $"{_presets.Count} Presets";
                }

                label = _presetCountLabel;
                labelColor = UiColors.Text;
            }

            ImGui.SameLine();

            var renamingPreset = TryGetPresetById(_renamingPresetId);
            if (renamingPreset != null)
            {
                // Naming a fresh preset replaces the dropdown; Enter confirms, Escape keeps "untitled".
                ImGui.SetNextItemWidth(dropdownWidth);
                if (_focusPresetRename)
                {
                    ImGui.SetKeyboardFocusHere();
                    _focusPresetRename = false;
                }

                ImGui.InputText("##presetName", ref renamingPreset.Title, 256, ImGuiInputTextFlags.AutoSelectAll);
                if (ImGui.IsItemDeactivated())
                {
                    _renamingPresetId = Guid.Empty;
                    presetPool.SaveVariationsToFile();
                }
            }
            else
            {
                _renamingPresetId = Guid.Empty; // The named preset may vanish mid-edit (e.g. undo)
                var selectedPreset = matchingPreset ?? activePreset;
                var chosen = _presetPicker.Draw(_presets, presetPool, op, selectedPreset, label, dropdownWidth,
                                                out var renameRequested,
                                                labelColor: labelColor);
                if (chosen != null)
                {
                    presetPool.Apply(op, chosen);
                }
                else if (renameRequested && selectedPreset != null)
                {
                    _renamingPresetId = selectedPreset.Id;
                    _focusPresetRename = true;
                }
            }

            // Emphasize the new-preset button while there is something worth saving: non-default
            // values that no existing preset describes. Dimmed (but still clickable) otherwise.
            var hasNonDefaults = false;
            foreach (var input in op.Inputs)
            {
                if (input.Input.IsDefault)
                    continue;

                hasNonDefaults = true;
                break;
            }

            var suggestSaving = hasNonDefaults && matchingPreset == null;

            ImGui.SameLine();
            if (CustomComponents.IconButton(Icon.Presets, Vector2.Zero,
                                            suggestSaving
                                                ? CustomComponents.ButtonStates.Emphasized
                                                : CustomComponents.ButtonStates.Default))
            {
                var newPreset = PresetCanvas.CreatePresetForActivePool();
                if (newPreset != null)
                {
                    VariationThumbnailRenderer.RequestRender(presetPool, op, newPreset.Id, toFile: true);
                    _renamingPresetId = newPreset.Id;
                    _focusPresetRename = true;
                }
            }

            CustomComponents.TooltipForLastItem(suggestSaving
                                                    ? "Parameters don't match any preset — save as new preset"
                                                    : "Create new preset from current values");
        }

        // Bypass / disable / pin-to-output icons
        ImGui.SameLine(0, spacing + groupGap);
        {
            var bypassable = symbolChildUi.SymbolChild.IsBypassable();
            var isBypassed = symbolChildUi.SymbolChild.IsBypassed;
            if (CustomComponents.ToggleTwoIconsButton(ref isBypassed,
                                                      Icon.OperatorBypassOff,
                                                      Icon.OperatorBypassOn,
                                                      CustomComponents.ButtonStates.NeedsAttention,
                                                      CustomComponents.ButtonStates.Default,
                                                      isEnabled: bypassable)
                && bypassable)
            {
                UndoRedoStack.AddAndExecute(new ChangeInstanceBypassedCommand(symbolChildUi.SymbolChild, isBypassed));
            }

            CustomComponents.TooltipForLastItem(!bypassable
                                                    ? "This operator cannot be bypassed"
                                                    : isBypassed
                                                        ? "Bypassed — click to enable"
                                                        : "Bypass this operator");

            ImGui.SameLine();
            var isDisabled = symbolChildUi.SymbolChild.IsDisabled;
            if (CustomComponents.ToggleIconButton(ref isDisabled,
                                                  Icon.OperatorDisabled,
                                                  Vector2.Zero,
                                                  CustomComponents.ButtonStates.NeedsAttention))
            {
                UndoRedoStack.AddAndExecute(new ChangeInstanceIsDisabledCommand(symbolChildUi, isDisabled));
            }

            CustomComponents.TooltipForLastItem(isDisabled ? "Disabled — click to enable" : "Disable this operator");
            // Always drawn (disabled without an output window) so the icon row keeps its layout —
            // skipping it would leave the preceding SameLine dangling and pull the parameter
            // area up onto this line.
            ImGui.SameLine();
            var projectView = ProjectView.Focused;
            var canPin = projectView != null && RenderProcess.OutputWindow != null && projectView.CompositionInstance != null;
            if (!canPin)
            {
                CustomComponents.IconButton(Icon.PlayOutput, Vector2.Zero, CustomComponents.ButtonStates.Disabled);
                CustomComponents.TooltipForLastItem("Pin to output — needs an output window");
            }
            else
            {
                var isPinned = RenderProcess.OutputWindow!.Pinning.TryGetPinnedEvaluationInstance(projectView!.Structure,
                                   out var pinnedInstance)
                               && pinnedInstance == op;

                if (CustomComponents.ToggleIconButton(ref isPinned, Icon.PlayOutput, Vector2.Zero))
                {
                    if (LayoutHandling.FocusMode)
                    {
                        var selectedImage = projectView.NodeSelection.GetFirstSelectedInstance();
                        if (selectedImage != null)
                        {
                            projectView.SetBackgroundOutput(selectedImage);
                        }
                    }
                    else
                    {
                        NodeActions.PinSelectedToOutputWindow(projectView,
                            projectView.NodeSelection,
                            projectView.CompositionInstance!,
                            true);
                    }
                }

                CustomComponents.TooltipForLastItem(isPinned
                                                        ? "Shown as graph background — click to clear"
                                                        : "Show output as graph background");
            }
        }

        ImGui.Unindent(5);
    }

    private void CollectPresets(SymbolVariationPool pool)
    {
        _presets.Clear();
        foreach (var variation in pool.AllVariations)
        {
            if (variation.IsPreset)
                _presets.Add(variation);
        }
    }

    /// <summary>
    /// Finds the preset that exactly describes the instance's current values, with an early-out on
    /// the previous frame's match so the full scan only runs while values are changing.
    /// </summary>
    private Variation? FindMatchingPreset(SymbolVariationPool pool, Instance op)
    {
        var cached = TryGetPresetById(_matchedPresetId);
        if (cached != null && SymbolVariationPool.DoesInstanceMatchPresetExactly(op, cached))
            return cached;

        _matchedPresetId = Guid.Empty;

        // Prefer the last-activated preset when several store identical values
        if (pool.ActiveVariation is { IsPreset: true } active
            && _presets.Contains(active)
            && SymbolVariationPool.DoesInstanceMatchPresetExactly(op, active))
        {
            _matchedPresetId = active.Id;
            return active;
        }

        foreach (var preset in _presets)
        {
            if (!SymbolVariationPool.DoesInstanceMatchPresetExactly(op, preset))
                continue;

            _matchedPresetId = preset.Id;
            return preset;
        }

        return null;
    }

    private Variation? TryGetPresetById(Guid id)
    {
        if (id == Guid.Empty)
            return null;

        foreach (var preset in _presets)
        {
            if (preset.Id == id)
                return preset;
        }

        return null;
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
        ImGui.PushID(instance.GetHashCode());
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

            HandleInputEditState(instance, inputSlot, editState);

            if (editState == InputEditStateFlags.ShowOptions && parameterWindow != null)
            {
                parameterWindow._viewMode = ViewModes.Settings;
                parameterWindow._parameterSettings.SelectedInputId = inputUi.Id;
                //NodeSelection.SetSelection(inputUi);
            }

            ImGui.PopID();
        }
        ImGui.PopID(); // Instance

        ImGui.PopStyleColor(2);

        if (groupState == GroupState.InsideOpened)
            FormInputs.EndGroup();
    }

    /// <summary>
    /// Translates the edit state of a parameter row into in-flight value commands so edits
    /// are undoable. Shared by the regular parameter view and the snapshot control view.
    /// </summary>
    internal static void HandleInputEditState(Instance instance, IInputSlot inputSlot, InputEditStateFlags editState)
    {
        if (instance.Parent == null)
            return;

        if ((editState & InputEditStateFlags.Started) != 0)
        {
            _inputSlotForActiveCommand = inputSlot;
            _inputValueCommandInFlight =
                new ChangeInputValueCommand(instance.Parent.Symbol, instance.SymbolChildId, inputSlot.Input, inputSlot.Input.Value, instance);
        }

        if ((editState & InputEditStateFlags.Modified) != 0)
        {
            if (_inputValueCommandInFlight == null || _inputSlotForActiveCommand != inputSlot)
            {
                _inputValueCommandInFlight =
                    new ChangeInputValueCommand(instance.Parent.Symbol, instance.SymbolChildId, inputSlot.Input, inputSlot.Input.Value, instance);
                _inputSlotForActiveCommand = inputSlot;
            }

            _inputValueCommandInFlight.AssignNewValue(inputSlot.Input.Value);
            inputSlot.DirtyFlag.Invalidate();
        }

        if ((editState & InputEditStateFlags.Finished) != 0)
        {
            if (_inputValueCommandInFlight != null && _inputSlotForActiveCommand == inputSlot)
            {
                UndoRedoStack.Add(_inputValueCommandInFlight);
            }

            _inputValueCommandInFlight = null;
        }
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

    /// <summary>
    /// The selected instance is re-resolved from its path each frame, so compare ids
    /// rather than relying on reference identity with the focused composition.
    /// </summary>
    private static bool IsFocusedComposition(Instance instance)
    {
        var composition = ProjectView.Focused?.CompositionInstance;
        if (composition == null)
            return false;

        return composition == instance
               || (composition.Symbol.Id == instance.Symbol.Id && composition.SymbolChildId == instance.SymbolChildId);
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

    private static bool DrawSettingsForSelectedSections(NodeSelection nodeSelection)
    {
        var somethingVisible = false;
        // Draw Section settings
        foreach (var section in nodeSelection.GetSelectedNodes<Section>())
        {   
            ImGui.PushID(section.Id.GetHashCode());
            ImGui.Indent(10f * T3Ui.UiScaleFactor);
            //ImGui.SetCursorPosX(10f*T3Ui.UiScaleFactor);
            ImGui.PushFont(Fonts.FontLarge);
            ImGui.TextUnformatted("Section settings");
            ImGui.PopFont();

            FormInputs.AddVerticalSpace();
            ImGui.Separator();
            FormInputs.AddVerticalSpace();

            ImGui.ColorEdit4("color", ref section.Color.Rgba);

            FormInputs.AddVerticalSpace();
            CustomComponents.StylizedText("Label:", Fonts.FontBold, UiColors.TextMuted, true);
            ImGui.TextWrapped(section.Label);

            FormInputs.AddVerticalSpace();
            CustomComponents.StylizedText("Description:", Fonts.FontBold, UiColors.TextMuted.Rgba, true);
            ImGui.TextWrapped(section.Title);

            FormInputs.AddVerticalSpace();
            ImGui.Separator();
            FormInputs.AddVerticalSpace();
            ImGui.PushStyleColor(ImGuiCol.Text, UiColors.TextMuted.Rgba);
            CustomComponents.IconButton(Icon.Tip,new Vector2(20,20)* T3Ui.UiScaleFactor);
            ImGui.SameLine();
            ImGui.TextWrapped("How to create an Section:\n" +
                "1.Select the operators you want to include in the section.\n" +
                "2.Shift + A to add the section." );

            FormInputs.AddVerticalSpace();
            CustomComponents.IconButton(Icon.Tip, new Vector2(20, 20) * T3Ui.UiScaleFactor);
            ImGui.SameLine();
            ImGui.TextWrapped("How to edit an Section:\n" +
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
    private bool _selectionChanged;

    private readonly ParameterSettings _parameterSettings = new();
    private readonly SnapshotControlView _snapshotControlView = new();
    private readonly VariationPicker _presetPicker = new();
    private readonly List<Variation> _presets = new();
    private int _lastPresetCountForLabel = -1;
    private string _presetCountLabel = "";
    private Guid _matchedPresetId;
    private Guid _renamingPresetId;
    private bool _focusPresetRename;
    public static readonly RenameInputDialog RenameInputDialog = new();
}