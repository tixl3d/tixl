#nullable enable
using ImGuiNET;
using T3.Core.DataTypes.Vector;
using T3.Core.Operator;
using T3.Core.Operator.Slots;
using T3.Core.Utils;
using T3.Editor.Gui.Input;
using T3.Editor.Gui.Interaction;
using T3.Editor.Gui.Interaction.Variations;
using T3.Editor.Gui.Interaction.Variations.Model;
using T3.Editor.Gui.Styling;
using T3.Editor.Gui.Windows.Variations;
using T3.Editor.Gui.UiHelpers;
using T3.Editor.UiModel;
using T3.Editor.UiModel.Commands;
using T3.Editor.UiModel.Commands.Graph;
using T3.Editor.UiModel.InputsAndTypes;
using T3.Editor.UiModel.ProjectHandling;
using T3.Editor.UiModel.Selection;

namespace T3.Editor.Gui.Windows;

/// <summary>
/// Shown in the parameter window when the composition itself is active: a precise per-parameter
/// control surface for snapshot-affected operators. Complements the Variations window, which
/// stays the visual/blending interface for the same <see cref="SymbolVariationPool"/>.
/// </summary>
[HelpUiID("ControlView")]
internal sealed class SnapshotControlView
{
    /// <summary>
    /// One row of the section-grouped op list: a group header when <see cref="ChildUi"/> is
    /// null (a null <see cref="Section"/> then means the "Ungrouped" bucket), an op otherwise.
    /// Nesting is rolled out into the header label as a path ("TEST01 / SUBSECTION"), so each
    /// group holds only the ops directly inside its section.
    /// </summary>
    private struct DisplayRow
    {
        public Section? Section;
        public SymbolUi.Child? ChildUi;

        /// <summary>Headers only: the all-caps nesting path shown as the label.</summary>
        public string? Label;

        /// <summary>Headers only: index of the first row after this group's ops.</summary>
        public int GroupEndIndex;
    }

    /// <summary>Set while the view draws its rows so the parameter menus can offer snapshot writes.</summary>
    private readonly record struct SnapshotMenuContext(SymbolVariationPool Pool, Variation ActiveSnapshot, Guid ChildKey);

    /// <summary>
    /// Caches whether current values differ from the selected snapshot. Comparing every input
    /// each frame would be too expensive, so results are recomputed only when the snapshot or
    /// the undo stack changes, with a low-frequency fallback for in-flight drag edits.
    /// </summary>
    private sealed class ModificationCheck
    {
        public void Invalidate()
        {
            _framesUntilRecheck = 0;
        }

        public bool IsModified(Instance composition, Variation variation)
        {
            var undoStackCount = UndoRedoStack.UndoStack.Count;
            if (variation.Id == _lastVariationId
                && undoStackCount == _lastUndoStackCount
                && --_framesUntilRecheck > 0)
            {
                return _isModified;
            }

            _lastVariationId = variation.Id;
            _lastUndoStackCount = undoStackCount;
            _framesUntilRecheck = CheckIntervalFrames;
            _modifiedInputs.Clear();
            _isModified = Compute(composition, variation, _modifiedInputs);
            return _isModified;
        }

        /// <summary>
        /// Per-input result of the last <see cref="IsModified"/> check. Composition inputs use
        /// Guid.Empty as child key, matching the variation's parameter sets.
        /// </summary>
        public bool IsInputModified(Guid childKey, Guid inputId)
        {
            return _modifiedInputs.Contains((childKey, inputId));
        }

        /// <summary>Whether any input of the given child differed in the last check.</summary>
        public bool IsAnyInputModifiedForChild(Guid childKey)
        {
            foreach (var entry in _modifiedInputs)
            {
                if (entry.Item1 == childKey)
                    return true;
            }

            return false;
        }

        internal static bool Compute(Instance composition, Variation variation)
        {
            return Compute(composition, variation, null);
        }

        /// <summary>
        /// Mirrors the apply semantics: inputs captured in the snapshot must match their stored
        /// value; captured ops' other blendable inputs must be at default (apply resets them).
        /// Without a result set the first mismatch returns early; with one, all are collected.
        /// </summary>
        private static bool Compute(Instance composition, Variation variation, HashSet<(Guid, Guid)>? modifiedInputs)
        {
            var anyModified = false;
            foreach (var (childId, parameterSet) in variation.ParameterSetsForChildIds)
            {
                Instance? instance;
                SymbolUi.Child? childUi = null;
                if (childId == Guid.Empty)
                {
                    instance = composition;
                }
                else if (composition.Children.TryGetChildInstance(childId, out instance))
                {
                    childUi = instance.GetChildUi();
                }
                else
                {
                    continue;
                }

                foreach (var inputSlot in instance.Inputs)
                {
                    var valueType = inputSlot.Input.Value.ValueType;
                    if (!ValueUtils.BlendMethods.ContainsKey(valueType))
                        continue;

                    if (!ValueUtils.CompareFunctions.TryGetValue(valueType, out var compare))
                        continue;

                    bool inputModified;
                    if (parameterSet.TryGetValue(inputSlot.Id, out var storedValue))
                    {
                        inputModified = !compare(storedValue, inputSlot.Input.Value);
                    }
                    else
                    {
                        inputModified = !inputSlot.Input.IsDefault
                                        && (childUi == null || childUi.IsInputIncludedForVariation(inputSlot.Id));
                    }

                    if (!inputModified)
                        continue;

                    if (modifiedInputs == null)
                        return true;

                    modifiedInputs.Add((childId, inputSlot.Id));
                    anyModified = true;
                }
            }

            return anyModified;
        }

        private const int CheckIntervalFrames = 30;
        private Guid _lastVariationId;
        private int _lastUndoStackCount = -1;
        private int _framesUntilRecheck;
        private bool _isModified;
        private readonly HashSet<(Guid, Guid)> _modifiedInputs = new();
    }

    public void Draw()
    {
        var pool = VariationHandling.ActivePoolForSnapshots;
        var composition = VariationHandling.ActiveInstanceForSnapshots;
        if (pool == null || composition == null)
            return;

        CollectSnapshots(pool);

        // Without any snapshot the view still lists the controlled parameters — they are useful
        // (and editable) on their own; only the snapshot-relative actions are unavailable.
        var selectedSnapshot = GetDisplayedSnapshot(pool, composition);
        var isModified = selectedSnapshot != null && _modificationCheck.IsModified(composition, selectedSnapshot);

        FormInputs.AddVerticalSpace(2);
        
        ImGui.PushStyleColor(ImGuiCol.ChildBg, UiColors.BackgroundFull.Fade(0.15f).Rgba);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(5, 5) * T3Ui.UiScaleFactor);
        ImGui.BeginChild("snapshotParameters", Vector2.Zero, ImGuiChildFlags.AlwaysUseWindowPadding);
        {
            //FormInputs.AddSectionSubHeader("Snapshot Parameters");
            DrawSelectorBar(pool, composition, selectedSnapshot, isModified);
            FormInputs.AddVerticalSpace(2);
            DrawOpList(composition, selectedSnapshot);
        }
        ImGui.EndChild();

        HandleSnapshotKeyboardNav(pool, composition, selectedSnapshot);

        ImGui.PopStyleVar();
        ImGui.PopStyleColor();
    }

    /// <summary>
    /// Left/right cycle snapshots while the parameter window holds focus (and nothing is being
    /// edited). Draws the same focus frame as the Graph / Output windows so the binding is visible.
    /// </summary>
    private void HandleSnapshotKeyboardNav(SymbolVariationPool pool, Instance composition, Variation? selectedSnapshot)
    {
        if (!ImGui.IsWindowFocused(ImGuiFocusedFlags.RootAndChildWindows))
            return;

        var drawList = ImGui.GetWindowDrawList();
        var min = ImGui.GetWindowPos();
        drawList.AddRect(min, min + ImGui.GetWindowSize(), UiColors.ForegroundFull.Fade(0.2f));

        // Don't steal arrows from an active value drag or any text field (rename, search).
        if (ImGui.IsAnyItemActive() || ImGui.GetIO().WantTextInput)
            return;

        if (ImGui.IsKeyPressed(ImGuiKey.LeftArrow))
        {
            ApplySnapshot(pool, composition, GetNeighborSnapshot(selectedSnapshot, -1));
        }
        else if (ImGui.IsKeyPressed(ImGuiKey.RightArrow))
        {
            ApplySnapshot(pool, composition, GetNeighborSnapshot(selectedSnapshot, +1));
        }
        else if (selectedSnapshot != null && (ImGui.IsKeyPressed(ImGuiKey.Enter) || ImGui.IsKeyPressed(ImGuiKey.KeypadEnter)))
        {
            // Safe here: the control view only shows when nothing is selected in the graph, so this
            // can't collide with the graph's own Enter-to-rename.
            StartRenaming(selectedSnapshot);
        }
    }

    /// <summary>
    /// The snapshot shown in the view: the pool's active one when set; otherwise a display-only
    /// fallback — the snapshot matching the current values, or the first — so the view isn't
    /// empty before the user activates anything. The fallback is never applied.
    /// </summary>
    private Variation? GetDisplayedSnapshot(SymbolVariationPool pool, Instance composition)
    {
        // During a live cross-fade the single "active" snapshot is ambiguous, so follow the
        // dominant weight (for a plain activation the vector is {active:1}, so this is the active).
        if (pool.GetDominantBlendVariation() is {IsSnapshot: true} dominant && _snapshots.Contains(dominant))
        {
            _fallbackSnapshotId = Guid.Empty;
            return dominant;
        }

        if (pool.ActiveVariation is {IsSnapshot: true} activeVariation && _snapshots.Contains(activeVariation))
        {
            _fallbackSnapshotId = Guid.Empty;
            return activeVariation;
        }

        if (_snapshots.Count == 0)
            return null;

        foreach (var snapshot in _snapshots)
        {
            if (snapshot.Id == _fallbackSnapshotId)
                return snapshot;
        }

        // Stale or unset fallback (first view, composition switch, snapshot removed): re-pick
        Variation? fallback = null;
        foreach (var snapshot in _snapshots)
        {
            if (ModificationCheck.Compute(composition, snapshot))
                continue;

            fallback = snapshot;
            break;
        }

        fallback ??= _snapshots[0];
        _fallbackSnapshotId = fallback.Id;
        return fallback;
    }

    private void CollectSnapshots(SymbolVariationPool pool)
    {
        _snapshots.Clear();
        foreach (var variation in pool.AllVariations)
        {
            if (variation.IsSnapshot)
                _snapshots.Add(variation);
        }

        _snapshots.Sort(_byActivationIndex);
    }

    private void DrawSelectorBar(SymbolVariationPool pool, Instance composition, Variation? selectedSnapshot,
        bool isModified)
    {
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(5, 5) * T3Ui.UiScaleFactor);
        ImGui.BeginChild("snapshotSelector", new Vector2(0, ImGui.GetFrameHeight() + 10 * T3Ui.UiScaleFactor),
            ImGuiChildFlags.AlwaysUseWindowPadding,
            ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse | ImGuiWindowFlags.NoBackground);
        {
            UpdateCachedLabels(selectedSnapshot);

            var frameHeight = ImGui.GetFrameHeight();
            var spacing = ImGui.GetStyle().ItemSpacing.X;
            var addButtonGap = 4 * T3Ui.UiScaleFactor;
            // revert (drag) + create (drag) + actions menu
            var actionButtonsWidth = 3 * frameHeight + 2 * spacing + addButtonGap;
            var arrowsWidth = 2 * (frameHeight + spacing);

            // Index button — click to edit the MIDI controller indices
            ImGui.PushFont(Fonts.FontBold);
            var indexTextSize = ImGui.CalcTextSize(_indexLabel);
            ImGui.PopFont();
            var indexButtonWidth = MathF.Max(frameHeight, indexTextSize.X + 12 * T3Ui.UiScaleFactor);

            var indexClicked = ImGui.InvisibleButton("##indexButton", new Vector2(indexButtonWidth, frameHeight));
            var indexMin = ImGui.GetItemRectMin();
            var indexMax = ImGui.GetItemRectMax();
            var indexHovered = ImGui.IsItemHovered();
            var barDrawList = ImGui.GetWindowDrawList();
            barDrawList.AddRectFilled(indexMin, indexMax,
                                      indexHovered ? UiColors.BackgroundHover : UiColors.BackgroundButton,
                                      4 * T3Ui.UiScaleFactor);
            ImGui.PushFont(Fonts.FontBold);
            barDrawList.AddText(((indexMin + indexMax) / 2 - indexTextSize / 2).Floor(), UiColors.TextMuted, _indexLabel);
            ImGui.PopFont();

            if (indexClicked)
                _controllerGrid.Open(new Vector2(indexMin.X, indexMax.Y + 2 * T3Ui.UiScaleFactor));

            CustomComponents.TooltipForLastItem("Click to edit MIDI controller indices");
            ImGui.SameLine();

            var dropdownWidth = MathF.Max(frameHeight,
                ImGui.GetContentRegionAvail().X - arrowsWidth - actionButtonsWidth - 3 * spacing);

            var isRenaming = selectedSnapshot != null && _renamingSnapshotId == selectedSnapshot.Id;
            if (isRenaming)
            {
                ImGui.SetNextItemWidth(dropdownWidth);
                // Focus for two frames: when started by clicking the index, the triggering click
                // would otherwise steal focus back on the first frame.
                if (_renameFocusFramesLeft > 0)
                {
                    ImGui.SetKeyboardFocusHere();
                    _renameFocusFramesLeft--;
                }

                ImGui.InputText("##renameSnapshot", ref _renameBuffer, 256);
                if (ImGui.IsItemActive() || ImGui.IsItemFocused())
                {
                    ImGui.GetWindowDrawList().AddRect(ImGui.GetItemRectMin(), ImGui.GetItemRectMax(),
                        UiColors.StatusActivated, 4 * T3Ui.UiScaleFactor);
                }

                if (ImGui.IsItemDeactivated())
                {
                    selectedSnapshot!.Title = string.IsNullOrWhiteSpace(_renameBuffer) ? null : _renameBuffer;
                    pool.SaveVariationsToFile();
                    _renamingSnapshotId = Guid.Empty;
                }
            }
            else
            {
                var chosen = _picker.Draw(_snapshots, pool, composition, selectedSnapshot, _selectedSnapshotLabel, dropdownWidth,
                                          out var renameRequested, canvas: _snapshotCanvas);
                if (chosen != null)
                    ApplySnapshot(pool, composition, chosen);

                if (renameRequested && selectedSnapshot != null)
                    StartRenaming(selectedSnapshot);
            }

            // Prev / next arrows cycle by activation index order
            var hasSnapshots = _snapshots.Count > 0;
            ImGui.SameLine();
            if (DrawBarButton(Icon.ArrowLeft, hasSnapshots, "Previous snapshot  (Left arrow)", CustomComponents.ButtonStates.Default))
                ApplySnapshot(pool, composition, GetNeighborSnapshot(selectedSnapshot, -1));

            ImGui.SameLine();
            if (DrawBarButton(Icon.ArrowRight, hasSnapshots, "Next snapshot  (Right arrow)", CustomComponents.ButtonStates.Default))
                ApplySnapshot(pool, composition, GetNeighborSnapshot(selectedSnapshot, +1));

            // Right-aligned actions: write + create icons, then the actions menu (Revert lives there).
            CustomComponents.RightAlign(actionButtonsWidth);

            var canWrite = selectedSnapshot != null && isModified;
            if (DrawBarButton(Icon.Apply, canWrite, "Write the current values into the snapshot"))
                WriteSnapshot(pool, composition, selectedSnapshot!);

            // Always available; emphasized when the active snapshot has unsaved changes — and
            // before the first snapshot exists, where it's the only way forward.
            ImGui.SameLine(0, spacing + addButtonGap);
            var createState = isModified || _snapshots.Count == 0
                                  ? CustomComponents.ButtonStates.Emphasized
                                  : CustomComponents.ButtonStates.Default;
            if (DrawBarButton(Icon.Plus, true, "Create new snapshot from current values", createState))
                CreateAndRenameSnapshot(pool, composition);

            ImGui.SameLine();
            if (DrawBarButton(Icon.Settings2, selectedSnapshot != null, "Snapshot actions", CustomComponents.ButtonStates.Default))
                ImGui.OpenPopup(SnapshotActionsPopupId);

            DrawSnapshotActionsMenu(pool, composition, selectedSnapshot, isModified);

            var pickedFromGrid = _controllerGrid.Draw(pool, composition, selectedSnapshot, _snapshots);
            if (pickedFromGrid != null)
                ApplySnapshot(pool, composition, pickedFromGrid);
        }
        ImGui.EndChild();
        ImGui.PopStyleVar();
    }

    /// <summary>
    /// The "…" actions popup for the active snapshot. Revert / Rename / Update thumbnail, then
    /// Remove below a divider. (Write and create stay as bar icons.)
    /// </summary>
    private void DrawSnapshotActionsMenu(SymbolVariationPool pool, Instance composition, Variation? selectedSnapshot, bool isModified)
    {
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(6, 6) * T3Ui.UiScaleFactor);
        if (ImGui.BeginPopup(SnapshotActionsPopupId))
        {
            if (CustomComponents.DrawMenuItem(_revertActionId, Icon.Reset, "Revert", isEnabled: selectedSnapshot != null && isModified, reserveCheckmarkColumn: false))
                ApplySnapshot(pool, composition, selectedSnapshot!);

            if (CustomComponents.DrawMenuItem(_renameActionId, Icon.None, "Rename", isEnabled: selectedSnapshot != null, reserveCheckmarkColumn: false))
                StartRenaming(selectedSnapshot!);

            if (CustomComponents.DrawMenuItem(_updateThumbnailActionId, Icon.Refresh, "Update Thumbnail", isEnabled: selectedSnapshot != null, reserveCheckmarkColumn: false))
                VariationThumbnailRenderer.RequestRender(pool, composition, selectedSnapshot!.Id, toFile: true);

            CustomComponents.SeparatorLine();

            if (CustomComponents.DrawMenuItem(_removeActionId, Icon.Trash, "Remove", isEnabled: selectedSnapshot != null, reserveCheckmarkColumn: false))
            {
                pool.DeleteVariation(selectedSnapshot!);
                _modificationCheck.Invalidate();
            }

            ImGui.EndPopup();
        }

        ImGui.PopStyleVar();
    }

    /// <summary>
    /// Selector-bar tool icon: drawn in <paramref name="activeState"/> when actionable, disabled
    /// (no hover, tooltip kept) otherwise. Returns true only when active. With
    /// <paramref name="attentionOnHover"/> the glyph turns "needs attention" while hovered — for
    /// destructive actions like remove. Uses the shared tool-icon styling.
    /// </summary>
    private static bool DrawBarButton(Icon icon, bool isActive, string tooltip,
        CustomComponents.ButtonStates activeState = CustomComponents.ButtonStates.Emphasized,
        bool attentionOnHover = false)
    {
        var state = isActive ? activeState : CustomComponents.ButtonStates.Disabled;
        var clicked = CustomComponents.IconButton(icon, Vector2.Zero, state);

        if (isActive && attentionOnHover && ImGui.IsItemHovered())
            Icons.DrawIconOnLastItem(icon, UiColors.StatusAttention);

        CustomComponents.TooltipForLastItem(tooltip);
        return clicked && isActive;
    }

    private void StartRenaming(Variation variation)
    {
        _renamingSnapshotId = variation.Id;
        _renameBuffer = string.IsNullOrEmpty(variation.Title) || variation.Title == "untitled" ? "" : variation.Title;
        _renameFocusFramesLeft = 2;
    }

    private void CreateAndRenameSnapshot(SymbolVariationPool pool, Instance composition)
    {
        // CreateOrUpdateSnapshotVariation already makes the new snapshot active.
        var newVariation = VariationHandling.CreateOrUpdateSnapshotVariation();
        if (newVariation != null)
        {
            StartRenaming(newVariation);
            VariationThumbnailRenderer.RequestRender(pool, composition, newVariation.Id, toFile: true);
        }

        _modificationCheck.Invalidate();
    }

    private void DrawOpList(Instance composition, Variation? selectedSnapshot)
    {
        if (selectedSnapshot != null)
            CollectStaleChildIds(composition, selectedSnapshot);
        else
            _staleChildIds.Clear();

        UpdateDisplayRows(composition);

        var compositionUi = composition.GetSymbolUi();

        // Composition inputs captured under the Guid.Empty key
        var compositionInputsShown = false;
        if (selectedSnapshot != null
            && selectedSnapshot.ParameterSetsForChildIds.ContainsKey(Guid.Empty)
            && composition.Parent != null)
        {
            var parentUi = composition.Parent.GetSymbolUi();
            if (parentUi.ChildUis.TryGetValue(composition.SymbolChildId, out var compositionChildUi))
            {
                BeginBlock();
                DrawGroupHeader(composition.SymbolChildId, "Inputs", UiColors.TextMuted, out _);
                DrawControlledParameters(composition, compositionUi, compositionChildUi, parentUi,
                    selectedSnapshot, childKey: Guid.Empty);
                EndBlock(Guid.Empty);
                FormInputs.AddVerticalSpace(5);
                compositionInputsShown = true;
            }
        }

        if (_displayRows.Count == 0 && _staleChildIds.Count == 0 && !compositionInputsShown)
        {
            CustomComponents.EmptyWindowMessage("No operators are controlled by snapshots.\nSelect operators in the graph and use\n\"Control with Snapshots\" from their context menu.");
            return;
        }

        // Ops enabled for snapshots, grouped by their enclosing section frames
        var rowIndex = 0;
        while (rowIndex < _displayRows.Count)
        {
            var row = _displayRows[rowIndex];
            if (row.ChildUi == null)
            {
                DrawSectionGroupHeader(composition, selectedSnapshot, row, rowIndex);
                var isCollapsed = _collapsedGroupIds.Contains(row.Section?.Id ?? Guid.Empty);
                rowIndex = isCollapsed ? row.GroupEndIndex : rowIndex + 1;
            }
            else
            {
                if (composition.Children.TryGetChildInstance(row.ChildUi.Id, out var instance))
                    DrawOpBlock(composition, compositionUi, selectedSnapshot, row.ChildUi, instance);

                rowIndex++;
            }
        }

        if (selectedSnapshot != null)
            DrawStaleEntries(composition, selectedSnapshot);
    }

    private void DrawOpBlock(Instance composition, SymbolUi compositionUi, Variation? selectedSnapshot,
        SymbolUi.Child childUi, Instance instance)
    {
        var typeColor = instance.Outputs.Count > 0
            ? TypeUiRegistry.GetPropertiesForType(instance.Outputs[0].ValueType).Color
            : UiColors.Text;
        var labelColor = ColorVariations.OperatorLabel.Apply(typeColor);

        BeginBlock();
        DrawGroupHeader(childUi.Id, GetOpGroupLabel(childUi.SymbolChild), labelColor, out var nameClicked,
            bypassChild: childUi);

        if (nameClicked)
        {
            var projectView = ProjectView.Focused;
            if (projectView != null)
            {
                // Ops hidden inside a collapsed frame have no visible node to center
                // on - jump to the collapsed frame's header instead
                if (childUi.HiddenInCollapsedSectionId != Guid.Empty)
                {
                    projectView.GraphView.OpenAndFocusSection(composition.InstancePath, childUi.HiddenInCollapsedSectionId);
                }
                else
                {
                    projectView.NodeSelection.SetSelection(childUi, instance);
                    FitViewToSelectionHandling.FitViewToSelection();
                }
            }
        }

        DrawControlledParameters(instance, instance.GetSymbolUi(), childUi, compositionUi,
            selectedSnapshot, childKey: childUi.Id, onlyEnabledInputs: true);
        EndBlock(childUi.Id);

        FormInputs.AddVerticalSpace(5);
    }

    /// <summary>
    /// Collapsible group header for a section (a null section is the "Ungrouped" bucket).
    /// The label shows the section's nesting path. Clicking the row toggles collapse - per
    /// window instance, not serialized, independent of the frame's collapse state in the
    /// graph. Right-side tools: center the frame in the graph and revert the group's ops
    /// to the snapshot.
    /// </summary>
    private void DrawSectionGroupHeader(Instance composition, Variation? selectedSnapshot, DisplayRow row, int headerIndex)
    {
        var section = row.Section;
        var groupId = section?.Id ?? Guid.Empty;
        var isCollapsed = _collapsedGroupIds.Contains(groupId);

        ImGui.PushID(groupId.GetHashCode());

        var scale = T3Ui.UiScaleFactor;
        var frameHeight = ImGui.GetFrameHeight();
        var spacing = ImGui.GetStyle().ItemSpacing.X;

        var labelColor = UiColors.TextMuted;
        var label = row.Label ?? "UNGROUPED";

        var toolIconCount = section != null ? 2 : 1;
        var toolsWidth = toolIconCount * frameHeight + (toolIconCount - 1) * spacing;
        var nameWidth = MathF.Max(frameHeight, ImGui.GetContentRegionAvail().X - toolsWidth - spacing);

        var toggleClicked = ImGui.InvisibleButton("##groupHeader", new Vector2(nameWidth, frameHeight));
        var headerMin = ImGui.GetItemRectMin();
        CustomComponents.DrawHoverHighlightOnLastItem();
        CustomComponents.TooltipForLastItem(isCollapsed ? "Click to expand" : "Click to collapse");

        var drawList = ImGui.GetWindowDrawList();
        var iconSize = 13 * scale;
        Icons.DrawIconAtScreenPosition(isCollapsed ? Icon.ChevronRight : Icon.ChevronDown,
                                       headerMin + new Vector2(2 * scale, (frameHeight - iconSize) / 2),
                                       new Vector2(iconSize, iconSize), drawList, labelColor);

        ImGui.PushFont(Fonts.FontSmall);
        drawList.AddText(new Vector2(headerMin.X + iconSize + 6 * scale,
                                     headerMin.Y + (frameHeight - ImGui.GetTextLineHeight()) / 2),
                         labelColor, label);
        ImGui.PopFont();

        CustomComponents.RightAlign(toolsWidth);

        if (section != null)
        {
            if (DrawBarButton(Icon.Aim, true, "Center this frame in the graph", CustomComponents.ButtonStates.Default))
            {
                ProjectView.Focused?.GraphView.OpenAndFocusSection(composition.InstancePath, section.Id);
            }

            ImGui.SameLine();
        }

        var groupModified = selectedSnapshot != null && IsGroupModified(headerIndex, row.GroupEndIndex);
        if (DrawBarButton(Icon.Reset, groupModified, "Revert this group to the snapshot", CustomComponents.ButtonStates.Default))
        {
            RevertGroupToSnapshot(composition, selectedSnapshot!, headerIndex, row.GroupEndIndex);
        }

        if (toggleClicked && !_collapsedGroupIds.Add(groupId))
        {
            _collapsedGroupIds.Remove(groupId);
        }

        ImGui.PopID();
        FormInputs.AddVerticalSpace(1);
    }

    private bool IsGroupModified(int headerIndex, int groupEndIndex)
    {
        for (var i = headerIndex + 1; i < groupEndIndex; i++)
        {
            var childUi = _displayRows[i].ChildUi;
            if (childUi != null && _modificationCheck.IsAnyInputModifiedForChild(childUi.Id))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Re-applies the snapshot for the ops of one group only, mirroring the apply semantics:
    /// captured inputs return to their stored value, other controlled non-default inputs
    /// reset to default. One undoable macro; unchanged values add no commands.
    /// </summary>
    private void RevertGroupToSnapshot(Instance composition, Variation snapshot, int headerIndex, int groupEndIndex)
    {
        var macro = new MacroCommand("Revert group to snapshot");
        var anyCommands = false;

        for (var i = headerIndex + 1; i < groupEndIndex; i++)
        {
            var childUi = _displayRows[i].ChildUi;
            if (childUi == null)
                continue;

            if (!composition.Children.TryGetChildInstance(childUi.Id, out var instance))
                continue;

            snapshot.ParameterSetsForChildIds.TryGetValue(childUi.Id, out var parameterSet);

            foreach (var inputSlot in instance.Inputs)
            {
                var valueType = inputSlot.Input.Value.ValueType;
                if (!ValueUtils.BlendMethods.ContainsKey(valueType))
                    continue;

                if (!ValueUtils.CompareFunctions.TryGetValue(valueType, out var compare))
                    continue;

                if (parameterSet != null && parameterSet.TryGetValue(inputSlot.Id, out var storedValue))
                {
                    if (!compare(storedValue, inputSlot.Input.Value))
                    {
                        macro.AddAndExecCommand(new ChangeInputValueCommand(composition.Symbol, childUi.Id,
                            inputSlot.Input, storedValue));
                        anyCommands = true;
                    }
                }
                else if (!inputSlot.Input.IsDefault && childUi.IsInputIncludedForVariation(inputSlot.Id))
                {
                    macro.AddAndExecCommand(new ResetInputToDefault(composition.Symbol, childUi.Id, inputSlot.Input));
                    anyCommands = true;
                }
            }
        }

        if (anyCommands)
            UndoRedoStack.Add(macro);

        _modificationCheck.Invalidate();
    }

    /// <summary>
    /// Wraps a header+params block so a rounded background can be drawn behind it. Uses a
    /// draw-list channel split: content goes on the front channel, the box on the back one.
    /// </summary>
    private void BeginBlock()
    {
        var pos = ImGui.GetCursorScreenPos();
        _blockLeftX = pos.X;
        _blockRightX = pos.X + ImGui.GetContentRegionAvail().X;

        var drawList = ImGui.GetWindowDrawList();
        drawList.ChannelsSplit(2);
        drawList.ChannelsSetCurrent(1);
        ImGui.BeginGroup();
    }

    private void EndBlock(Guid highlightId)
    {
        ImGui.EndGroup();

        var drawList = ImGui.GetWindowDrawList();
        // 1px around the content; with the 5px between blocks below this leaves a ~3px gap.
        var pad = new Vector2(0, 1) * T3Ui.UiScaleFactor;
        var min = new Vector2(_blockLeftX, ImGui.GetItemRectMin().Y - pad.Y);
        var max = new Vector2(_blockRightX, ImGui.GetItemRectMax().Y + pad.Y);

        // Hovering the block highlights the matching operator in the graph; the same shared
        // hover set drives the reverse (hovering the op in the graph brightens the block).
        // IsMouseHoveringRect is a pure geometric test, so gate it on the window actually being
        // hovered — otherwise an open popup (controller grid, picker, actions menu) drawn on top
        // still bleeds hover onto the blocks behind it.
        if (highlightId != Guid.Empty
            && ImGui.IsWindowHovered(ImGuiHoveredFlags.ChildWindows)
            && ImGui.IsMouseHoveringRect(min, max))
            FrameStats.AddHoveredId(highlightId);

        var isOpHovered = highlightId != Guid.Empty && FrameStats.IsIdHovered(highlightId);
        var fade = isOpHovered ? 0.6f : 0.3f;

        drawList.ChannelsSetCurrent(0);
        drawList.AddRectFilled(min, max, UiColors.BackgroundButton.Fade(fade), 6 * T3Ui.UiScaleFactor);
        drawList.ChannelsMerge();
    }

    /// <summary>
    /// Section group headers render all-caps; the uppercased label is cached per section
    /// so the path concatenation on rebuild reuses stable strings.
    /// </summary>
    private string GetGroupLabel(Section section)
    {
        var source = section.Label;
        if (_groupLabelCache.TryGetValue(section.Id, out var cached) && ReferenceEquals(cached.Source, source))
            return cached.Upper;

        var upper = string.IsNullOrEmpty(source) ? "UNTITLED" : source.ToUpperInvariant();
        _groupLabelCache[section.Id] = (source, upper);
        return upper;
    }

    /// <summary>
    /// Renamed ops show as 'Blob "MyBlob"' — type first, then the custom name. Cached per
    /// child because the view draws every frame.
    /// </summary>
    private string GetOpGroupLabel(Symbol.Child symbolChild)
    {
        var customName = symbolChild.Name;
        if (_opLabelCache.TryGetValue(symbolChild.Id, out var cached)
            && ReferenceEquals(cached.CustomName, customName))
        {
            return cached.Label;
        }

        var label = string.IsNullOrEmpty(customName)
            ? symbolChild.Symbol.Name
            : $"{symbolChild.Symbol.Name} \"{customName}\"";
        _opLabelCache[symbolChild.Id] = (customName, label);
        return label;
    }

    /// <summary>
    /// Header row with the clickable op name. Operator groups are not collapsible —
    /// collapsing happens at the section-group level. A bypassable op gets a small
    /// bypass toggle on the right (like the parameter popup).
    /// </summary>
    private static void DrawGroupHeader(Guid groupId, string label, Color labelColor, out bool nameClicked,
        SymbolUi.Child? bypassChild = null)
    {
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(0));
        ImGui.PushID(groupId.GetHashCode());

        var bypassable = bypassChild != null && bypassChild.SymbolChild.IsBypassable();
        var nameWidth = bypassable
            ? ImGui.GetContentRegionAvail().X - ImGui.GetFrameHeight() - ImGui.GetStyle().ItemSpacing.X
            : 0f;

        ImGui.PushFont(Fonts.FontSmall);
        ImGui.PushStyleColor(ImGuiCol.Text, labelColor.Rgba);
        ImGui.PushStyleColor(ImGuiCol.HeaderHovered, Color.Transparent.Rgba);
        ImGui.PushStyleColor(ImGuiCol.HeaderActive, Color.Transparent.Rgba);
        ImGui.AlignTextToFramePadding();
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + 2 * T3Ui.UiScaleFactor);
        nameClicked = ImGui.Selectable(label, false, ImGuiSelectableFlags.None, new Vector2(nameWidth, 0));
        ImGui.PopStyleColor(3);
        ImGui.PopFont();

        // Subtle rounded highlight instead of the full-width Selectable header bar
        CustomComponents.DrawHoverHighlightOnLastItem();

        if (bypassable)
        {
            ImGui.SameLine();
            var isBypassed = bypassChild!.SymbolChild.IsBypassed;
            if (CustomComponents.ToggleTwoIconsButton(ref isBypassed, Icon.OperatorBypassOff, Icon.OperatorBypassOn,
                    CustomComponents.ButtonStates.NeedsAttention,
                    CustomComponents.ButtonStates.Default))
            {
                UndoRedoStack.AddAndExecute(new ChangeInstanceBypassedCommand(bypassChild.SymbolChild, isBypassed));
            }

            CustomComponents.TooltipForLastItem(isBypassed ? "Bypassed — click to enable" : "Bypass this operator");
        }

        ImGui.PopID();
        ImGui.PopStyleVar();

        // Small gap between the header label and the parameter rows
        FormInputs.AddVerticalSpace(1);
    }

    /// <summary>
    /// Draws editable parameter rows like the regular parameter view, but only for the
    /// controlled inputs — the blendable ones a snapshot write would capture. Rows are
    /// highlighted when they no longer match the snapshot; clicking the parameter name
    /// reverts to the snapshot value (see <see cref="TryResetParameterToSnapshot"/>).
    /// Without a snapshot the rows stay editable and fall back to the default-based
    /// highlighting and reset.
    /// </summary>
    private void DrawControlledParameters(Instance instance,
        SymbolUi symbolUi,
        SymbolUi.Child symbolChildUi,
        SymbolUi compositionSymbolUi,
        Variation? snapshot,
        Guid childKey,
        bool onlyEnabledInputs = false)
    {
        ImGui.PushStyleColor(ImGuiCol.FrameBg, UiColors.BackgroundButton.Rgba);
        ImGui.PushStyleColor(ImGuiCol.FrameBgHovered, UiColors.BackgroundHover.Rgba);
        ImGui.PushID(instance.GetHashCode());

        // Keep room right of the value edits for the per-row actions menu
        InputArea.ValueEditRightMargin = ImGui.GetFrameHeight() + 2 * T3Ui.UiScaleFactor;

        // Context for the parameter menus (right-click + per-row) so they can offer snapshot writes.
        var menuPool = VariationHandling.ActivePoolForSnapshots;
        _activeSnapshotMenuContext = menuPool != null && snapshot != null
            ? new SnapshotMenuContext(menuPool, snapshot, childKey)
            : null;

        foreach (var inputSlot in instance.Inputs)
        {
            if (!ValueUtils.BlendMethods.ContainsKey(inputSlot.Input.Value.ValueType))
                continue;

            if (!symbolUi.InputUis.TryGetValue(inputSlot.Id, out var inputUi))
                continue;

            if (inputUi.ExcludedFromPresets)
                continue;

            if (onlyEnabledInputs && !symbolChildUi.IsInputEnabledForSnapshots(inputSlot.Id))
                continue;

            ImGui.PushID(inputSlot.Id.GetHashCode());

            // In this view highlighting means "differs from the snapshot", not "non-default" —
            // without a snapshot there is no reference, so the regular highlighting applies.
            if (snapshot != null)
                InputArea.DimHighlightOverride = !_modificationCheck.IsInputModified(childKey, inputSlot.Id);

            var editState = inputUi.DrawParameterEdit(inputSlot, compositionSymbolUi, symbolChildUi,
                hideNonEssentials: false, skipIfDefault: false);
            InputArea.DimHighlightOverride = null;

            var valueMin = ImGui.GetItemRectMin();
            var valueMax = ImGui.GetItemRectMax();

            ParameterWindow.HandleInputEditState(instance, inputSlot, editState);

            DrawParameterActionMenu(instance, symbolChildUi, compositionSymbolUi, childKey, inputSlot, valueMin, valueMax);

            ImGui.PopID();
        }

        InputArea.ValueEditRightMargin = 0;
        _activeSnapshotMenuContext = null;

        ImGui.PopID();
        ImGui.PopStyleColor(2);
    }

    /// <summary>
    /// Per-parameter actions popup (opened by the "…" icon in the row's right gutter): write the
    /// parameter into the active snapshot / all snapshots, reset it, or drop it from snapshot control.
    /// </summary>
    private void DrawParameterActionMenu(Instance instance, SymbolUi.Child symbolChildUi, SymbolUi compositionSymbolUi,
        Guid childKey, IInputSlot inputSlot, Vector2 valueMin, Vector2 valueMax)
    {
        var scale = T3Ui.UiScaleFactor;
        var buttonSize = ImGui.GetFrameHeight();
        var cursorToRestore = ImGui.GetCursorScreenPos();

        ImGui.SetCursorScreenPos(new Vector2(valueMax.X + 2 * scale, valueMin.Y));

        if (CustomComponents.IconButton(Icon.Settings2, new Vector2(buttonSize, buttonSize), CustomComponents.ButtonStates.Default))
            ImGui.OpenPopup(ParameterActionPopupId);

        CustomComponents.TooltipForLastItem("Parameter actions");

        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(6, 6) * scale);
        if (ImGui.BeginPopup(ParameterActionPopupId))
        {
            // Write to snapshot / all + Reset to Snapshot — shared with the parameter right-click menu.
            if (instance.Parent != null
                && DrawSnapshotActionMenuItems(instance.Parent.Symbol, symbolChildUi, inputSlot.Input, reserveCheckmarkColumn: false))
            {
                _modificationCheck.Invalidate();
            }

            if (CustomComponents.DrawMenuItem(_paramResetId, Icon.Reset, "Reset to Default",
                    isEnabled: instance.Parent != null && !inputSlot.Input.IsDefault, reserveCheckmarkColumn: false)
                && instance.Parent != null)
            {
                UndoRedoStack.AddAndExecute(new ResetInputToDefault(instance.Parent.Symbol, instance.SymbolChildId, inputSlot.Input));
                _modificationCheck.Invalidate();
            }

            CustomComponents.SeparatorLine();

            // Composition inputs (childKey == Guid.Empty) live in a different owner; their toggle
            // isn't reachable through this child-ui, so the action is offered for ops only.
            if (CustomComponents.DrawMenuItem(_paramDisableId, Icon.None, "Disable Snapshot Control", isEnabled: childKey != Guid.Empty, reserveCheckmarkColumn: false))
            {
                VariationHandling.ToggleParameterSnapshotControl(compositionSymbolUi, symbolChildUi, inputSlot.Input, enable: false);
                _modificationCheck.Invalidate();
            }

            ImGui.EndPopup();
        }

        ImGui.PopStyleVar();

        // Validate the extent so the surrounding EndGroup (block box) doesn't assert on a pending set-pos.
        ImGui.SetCursorScreenPos(cursorToRestore);
        ImGui.Dummy(Vector2.Zero);
    }

    /// <summary>
    /// Snapshot-specific parameter actions for the parameter menus, active only while this view is
    /// drawing (see <see cref="_activeSnapshotMenuContext"/>): write the parameter into the active
    /// snapshot or all snapshots, and reset it to the active snapshot's stored value. Shared by the
    /// per-row gear menu and the parameter right-click menu. Returns true when one was triggered.
    /// </summary>
    internal static bool DrawSnapshotActionMenuItems(Symbol compositionSymbol, SymbolUi.Child symbolChildUi, Symbol.Child.Input input,
        bool reserveCheckmarkColumn)
    {
        if (_activeSnapshotMenuContext is not { } ctx)
            return false;

        var inputId = input.InputDefinition.Id;
        var value = input.Value;
        var differsFromActive = SnapshotValueDiffers(ctx.ActiveSnapshot, ctx.ChildKey, inputId, value, input.DefaultValue);
        var acted = false;

        if (CustomComponents.DrawMenuItem(_writeToSnapshotItemId, Icon.Apply, "Write to Snapshot", null, isEnabled: differsFromActive,
                reserveCheckmarkColumn: reserveCheckmarkColumn))
        {
            VariationHandling.ApplyParameterToVariations(ctx.Pool, new[] { ctx.ActiveSnapshot }, ctx.ChildKey, inputId, value,
                "Write parameter to snapshot");
            acted = true;
        }

        var differsFromAny = false;
        foreach (var v in ctx.Pool.AllVariations)
        {
            if (v.IsPreset)
                continue;

            if (SnapshotValueDiffers(v, ctx.ChildKey, inputId, value, input.DefaultValue))
            {
                differsFromAny = true;
                break;
            }
        }

        if (CustomComponents.DrawMenuItem(_writeToAllSnapshotsItemId, Icon.None, "Write to All Snapshots", null, isEnabled: differsFromAny,
                reserveCheckmarkColumn: reserveCheckmarkColumn))
        {
            VariationHandling.ApplyParameterToVariations(ctx.Pool, ctx.Pool.AllVariations, ctx.ChildKey, inputId, value,
                "Write parameter to all snapshots");
            acted = true;
        }

        if (CustomComponents.DrawMenuItem(_resetToSnapshotItemId, Icon.Reset, "Reset to Snapshot", null, isEnabled: differsFromActive,
                reserveCheckmarkColumn: reserveCheckmarkColumn))
        {
            if (ctx.ActiveSnapshot.ParameterSetsForChildIds.TryGetValue(ctx.ChildKey, out var set) && set.TryGetValue(inputId, out var stored))
                UndoRedoStack.AddAndExecute(new ChangeInputValueCommand(compositionSymbol, symbolChildUi.Id, input, stored));
            else
                UndoRedoStack.AddAndExecute(new ResetInputToDefault(compositionSymbol, symbolChildUi.Id, input));
            acted = true;
        }

        return acted;
    }

    /// <summary>
    /// While the control view draws its rows, clicking a parameter name reverts to the active
    /// snapshot's value — the reference the row highlight is measured against — instead of the
    /// default. Returns false when the view isn't drawing so the caller falls back to the
    /// regular reset-to-default. Values already matching the snapshot are left untouched.
    /// </summary>
    internal static bool TryResetParameterToSnapshot(Symbol compositionSymbol, SymbolUi.Child symbolChildUi, Symbol.Child.Input input)
    {
        if (_activeSnapshotMenuContext is not { } ctx)
            return false;

        var inputId = input.InputDefinition.Id;
        if (ctx.ActiveSnapshot.ParameterSetsForChildIds.TryGetValue(ctx.ChildKey, out var set)
            && set.TryGetValue(inputId, out var stored))
        {
            if (!ValueUtils.CompareFunctions.TryGetValue(input.Value.ValueType, out var compare) || !compare(stored, input.Value))
            {
                UndoRedoStack.AddAndExecute(new ChangeInputValueCommand(compositionSymbol, symbolChildUi.Id, input, stored));
            }
        }
        else if (!input.IsDefault)
        {
            // Not captured in the snapshot: applying it would reset the parameter to default
            UndoRedoStack.AddAndExecute(new ResetInputToDefault(compositionSymbol, symbolChildUi.Id, input));
        }

        return true;
    }

    /// <summary>True while the control view draws its rows — name-click and its hover hint then mean "reset to snapshot".</summary>
    internal static bool IsResetToSnapshotActive => _activeSnapshotMenuContext != null;

    private static bool SnapshotValueDiffers(Variation variation, Guid childKey, Guid inputId, InputValue current, InputValue defaultValue)
    {
        if (!ValueUtils.CompareFunctions.TryGetValue(current.ValueType, out var compare))
            return false;

        var stored = variation.ParameterSetsForChildIds.TryGetValue(childKey, out var set) && set.TryGetValue(inputId, out var v)
                         ? v
                         : defaultValue;
        return !compare(stored, current);
    }

    /// <summary>
    /// Ops still referenced by the snapshot but deleted or no longer enabled for snapshots.
    /// </summary>
    private void DrawStaleEntries(Instance composition, Variation selectedSnapshot)
    {
        if (_staleChildIds.Count == 0)
            return;

        var compositionUi = composition.GetSymbolUi();
        FormInputs.AddVerticalSpace(5);

        foreach (var childId in _staleChildIds)
        {
            ImGui.PushID(childId.GetHashCode());

            var label = compositionUi.ChildUis.TryGetValue(childId, out var childUi)
                ? childUi.SymbolChild.ReadableName
                : "Missing operator";

            ImGui.AlignTextToFramePadding();
            CustomComponents.StylizedText(label, Fonts.FontNormal, UiColors.TextMuted.Fade(0.5f));
            ImGui.SameLine();
            CustomComponents.StylizedText("no longer enabled", Fonts.FontSmall, UiColors.TextMuted.Fade(0.4f));

            ImGui.SameLine();
            if (CustomComponents.TransparentIconButton(Icon.Trash, Vector2.Zero, CustomComponents.ButtonStates.Default))
            {
                _pendingStaleRemovalId = childId;
            }

            CustomComponents.TooltipForLastItem("Remove the stored values for this operator from the snapshot");
            ImGui.PopID();
        }

        // Deferred to avoid mutating the snapshot while iterating its entries
        if (_pendingStaleRemovalId != Guid.Empty)
        {
            _singleStaleId[0] = _pendingStaleRemovalId;
            _singleVariation[0] = selectedSnapshot;
            VariationHandling.RemoveInstancesFromVariations(_singleStaleId, _singleVariation);
            _pendingStaleRemovalId = Guid.Empty;
            _modificationCheck.Invalidate();
        }
    }

    private void CollectStaleChildIds(Instance composition, Variation snapshot)
    {
        _staleChildIds.Clear();

        var compositionUi = composition.GetSymbolUi();
        foreach (var childId in snapshot.ParameterSetsForChildIds.Keys)
        {
            if (childId == Guid.Empty)
                continue;

            var isListed = compositionUi.ChildUis.TryGetValue(childId, out var childUi)
                           && childUi.EnabledForSnapshots
                           && composition.Children.TryGetChildInstance(childId, out _);
            if (!isListed)
                _staleChildIds.Add(childId);
        }
    }

    /// <summary>
    /// Rebuilds the section-grouped display rows from <see cref="SectionTree"/>. Structural
    /// changes arrive via commands, so the undo-stack count plus section/op counts catch
    /// nearly all of them; a low-frequency fallback covers the rest (in-flight drags in the
    /// graph), mirroring the <see cref="ModificationCheck"/> throttling.
    /// </summary>
    private void UpdateDisplayRows(Instance composition)
    {
        var compositionUi = composition.GetSymbolUi();
        var undoStackCount = UndoRedoStack.UndoStack.Count;
        if (composition.Symbol.Id == _rowsSymbolId
            && undoStackCount == _rowsUndoStackCount
            && compositionUi.Sections.Count == _rowsSectionCount
            && compositionUi.ChildUis.Count == _rowsChildCount
            && --_rowsFramesUntilRefresh > 0)
        {
            return;
        }

        _rowsSymbolId = composition.Symbol.Id;
        _rowsUndoStackCount = undoStackCount;
        _rowsSectionCount = compositionUi.Sections.Count;
        _rowsChildCount = compositionUi.ChildUis.Count;
        _rowsFramesUntilRefresh = RowRefreshIntervalFrames;

        _displayRows.Clear();
        _unsectionedOps.Clear();

        var roots = SectionTree.Build(compositionUi, _unsectionedOps);
        roots.Sort(_byNodeCanvasPosition);

        var hasGroups = false;
        foreach (var node in roots)
        {
            hasGroups |= EmitSectionRows(node, parentPath: null);
        }

        var anyUngrouped = false;
        foreach (var childUi in _unsectionedOps)
        {
            if (!childUi.EnabledForSnapshots)
                continue;

            anyUngrouped = true;
            break;
        }

        if (!anyUngrouped)
            return;

        // Without any section groups the list stays flat, like before sections existed
        var ungroupedHeaderIndex = -1;
        if (hasGroups)
        {
            ungroupedHeaderIndex = _displayRows.Count;
            _displayRows.Add(new DisplayRow { Label = "UNGROUPED" });
        }

        _unsectionedOps.Sort(_byChildCanvasPosition);
        foreach (var childUi in _unsectionedOps)
        {
            if (!childUi.EnabledForSnapshots)
                continue;

            _displayRows.Add(new DisplayRow { ChildUi = childUi });
        }

        if (ungroupedHeaderIndex >= 0)
        {
            var header = _displayRows[ungroupedHeaderIndex];
            header.GroupEndIndex = _displayRows.Count;
            _displayRows[ungroupedHeaderIndex] = header;
        }
    }

    /// <summary>
    /// Emits one group per section that directly contains snapshot-enabled ops, labeled with
    /// the section's nesting path ("TEST01 / SUBSECTION"), then recurses into nested sections.
    /// Sections without direct ops leave no header of their own - their name only shows up
    /// in their descendants' paths. Path strings are built here rather than per frame; the
    /// rebuild is throttled, so the concatenation stays off the hot path.
    /// </summary>
    private bool EmitSectionRows(SectionTree.Node node, string? parentPath)
    {
        var sectionLabel = GetGroupLabel(node.Section);
        var path = parentPath == null ? sectionLabel : parentPath + " / " + sectionLabel;

        var hasDirectMembers = false;
        foreach (var childUi in node.Members)
        {
            if (!childUi.EnabledForSnapshots)
                continue;

            hasDirectMembers = true;
            break;
        }

        var emittedAny = false;
        if (hasDirectMembers)
        {
            var headerIndex = _displayRows.Count;
            _displayRows.Add(new DisplayRow { Section = node.Section, Label = path });

            foreach (var childUi in node.Members)
            {
                if (!childUi.EnabledForSnapshots)
                    continue;

                _displayRows.Add(new DisplayRow { ChildUi = childUi });
            }

            var header = _displayRows[headerIndex];
            header.GroupEndIndex = _displayRows.Count;
            _displayRows[headerIndex] = header;
            emittedAny = true;
        }

        node.Children.Sort(_byNodeCanvasPosition);
        foreach (var child in node.Children)
        {
            emittedAny |= EmitSectionRows(child, path);
        }

        return emittedAny;
    }

    private void ApplySnapshot(SymbolVariationPool pool, Instance composition, Variation? snapshot)
    {
        if (snapshot == null)
            return;

        pool.Apply(composition, snapshot);
        _modificationCheck.Invalidate();
    }

    private void WriteSnapshot(SymbolVariationPool pool, Instance composition, Variation snapshot)
    {
        _affectedInstances.Clear();
        VariationHandling.AddSnapshotEnabledChildrenToList(composition, _affectedInstances);
        pool.UpdateVariationPropertiesForInstances(snapshot, _affectedInstances);
        _modificationCheck.Invalidate();
    }

    private Variation? GetNeighborSnapshot(Variation? selectedSnapshot, int direction)
    {
        if (_snapshots.Count == 0)
            return null;

        if (selectedSnapshot == null)
            return direction > 0 ? _snapshots[0] : _snapshots[^1];

        var index = _snapshots.IndexOf(selectedSnapshot);
        if (index == -1)
            return _snapshots[0];

        var nextIndex = (index + direction + _snapshots.Count) % _snapshots.Count;
        return _snapshots[nextIndex];
    }

    private void UpdateCachedLabels(Variation? selectedSnapshot)
    {
        var selectedId = selectedSnapshot?.Id ?? Guid.Empty;
        var title = selectedSnapshot?.Title;
        var activationIndex = selectedSnapshot?.ActivationIndex ?? int.MinValue;
        if (selectedId == _lastLabelVariationId && title == _lastLabelTitle && activationIndex == _lastLabelActivationIndex
            && _snapshots.Count == _lastLabelSnapshotCount)
        {
            return;
        }

        _lastLabelVariationId = selectedId;
        _lastLabelTitle = title;
        _lastLabelActivationIndex = activationIndex;
        _lastLabelSnapshotCount = _snapshots.Count;

        if (selectedSnapshot == null)
        {
            _indexLabel = "-";
            _selectedSnapshotLabel = _snapshots.Count == 0 ? "No Snapshots" : "Select snapshot...";
        }
        else
        {
            _indexLabel = selectedSnapshot.ActivationIndex.ToString("00");
            _selectedSnapshotLabel = string.IsNullOrEmpty(title) || title == "untitled"
                ? "Untitled"
                : title;
        }
    }

    private static readonly Comparison<Variation> _byActivationIndex
        = (a, b) => a.ActivationIndex.CompareTo(b.ActivationIndex);

    // Reading order: top-to-bottom, then left-to-right - matches the section tree's member order
    private static readonly Comparison<SymbolUi.Child> _byChildCanvasPosition
        = (a, b) =>
        {
            var byY = a.PosOnCanvas.Y.CompareTo(b.PosOnCanvas.Y);
            return byY != 0 ? byY : a.PosOnCanvas.X.CompareTo(b.PosOnCanvas.X);
        };

    private static readonly Comparison<SectionTree.Node> _byNodeCanvasPosition
        = (a, b) =>
        {
            var byY = a.Section.PosOnCanvas.Y.CompareTo(b.Section.PosOnCanvas.Y);
            return byY != 0 ? byY : a.Section.PosOnCanvas.X.CompareTo(b.Section.PosOnCanvas.X);
        };

    private const string SnapshotActionsPopupId = "##snapshotActions";
    private const string ParameterActionPopupId = "##parameterActions";
    private static readonly int _revertActionId = "snapshotRevert".GetHashCode();
    private static readonly int _paramResetId = "paramReset".GetHashCode();
    private static readonly int _paramDisableId = "paramDisable".GetHashCode();
    private static readonly int _writeToSnapshotItemId = "writeToSnapshot".GetHashCode();
    private static readonly int _writeToAllSnapshotsItemId = "writeToAllSnapshots".GetHashCode();
    private static readonly int _resetToSnapshotItemId = "resetToSnapshot".GetHashCode();
    private static SnapshotMenuContext? _activeSnapshotMenuContext;
    private static readonly int _renameActionId = "snapshotRename".GetHashCode();
    private static readonly int _updateThumbnailActionId = "snapshotUpdateThumbnail".GetHashCode();
    private static readonly int _removeActionId = "snapshotRemove".GetHashCode();

    private readonly ModificationCheck _modificationCheck = new();
    private readonly VariationPicker _picker = new();
    private readonly SnapshotCanvas _snapshotCanvas = new();
    private readonly SnapshotControllerGrid _controllerGrid = new();
    private const int RowRefreshIntervalFrames = 30;

    private readonly List<Variation> _snapshots = new();
    private readonly List<DisplayRow> _displayRows = new();
    private readonly List<SymbolUi.Child> _unsectionedOps = new();
    private readonly HashSet<Guid> _collapsedGroupIds = new();
    private Guid _rowsSymbolId;
    private int _rowsUndoStackCount = -1;
    private int _rowsSectionCount = -1;
    private int _rowsChildCount = -1;
    private int _rowsFramesUntilRefresh;
    private readonly List<Guid> _staleChildIds = new();
    private readonly List<Instance> _affectedInstances = new();
    private readonly Guid[] _singleStaleId = new Guid[1];
    private readonly Variation[] _singleVariation = new Variation[1];
    private Guid _pendingStaleRemovalId;
    private Guid _fallbackSnapshotId;
    private float _blockLeftX;
    private float _blockRightX;
    private readonly Dictionary<Guid, (string CustomName, string Label)> _opLabelCache = new();
    private readonly Dictionary<Guid, (string Source, string Upper)> _groupLabelCache = new();

    private Guid _lastLabelVariationId = Guid.NewGuid(); // force initial label update
    private string? _lastLabelTitle;
    private int _lastLabelActivationIndex = int.MinValue;
    private int _lastLabelSnapshotCount = -1;
    private string _indexLabel = "-";
    private string _selectedSnapshotLabel = "Select snapshot...";

    // Inline snapshot rename (selector bar)
    private Guid _renamingSnapshotId;
    private int _renameFocusFramesLeft;
    private string _renameBuffer = "";
}