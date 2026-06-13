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
internal sealed class SnapshotControlView
{
    private struct OpEntry
    {
        public Instance Instance;
        public SymbolUi.Child ChildUi;
    }

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

        if (_snapshots.Count == 0)
        {
            if (CustomComponents.EmptyWindowMessage("No snapshots yet.\nSnapshots capture the parameters of all\noperators enabled for snapshots.",
                                                    "Create snapshot"))
            {
                CreateAndRenameSnapshot(pool);
            }

            return;
        }

        var selectedSnapshot = GetDisplayedSnapshot(pool, composition);
        var isModified = selectedSnapshot != null && _modificationCheck.IsModified(composition, selectedSnapshot);

        DrawSelectorBar(pool, composition, selectedSnapshot, isModified);
        DrawOpList(composition, selectedSnapshot);
    }

    /// <summary>
    /// The snapshot shown in the view: the pool's active one when set; otherwise a display-only
    /// fallback — the snapshot matching the current values, or the first — so the view isn't
    /// empty before the user activates anything. The fallback is never applied.
    /// </summary>
    private Variation? GetDisplayedSnapshot(SymbolVariationPool pool, Instance composition)
    {
        if (pool.ActiveVariation is { IsSnapshot: true } activeVariation && _snapshots.Contains(activeVariation))
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

    private void DrawSelectorBar(SymbolVariationPool pool, Instance composition, Variation? selectedSnapshot, bool isModified)
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
            var actionButtonsWidth = 4 * frameHeight + 3 * spacing + addButtonGap;
            var arrowsWidth = 2 * (frameHeight + spacing);

            // Index indicator — click to rename the active snapshot
            ImGui.AlignTextToFramePadding();
            CustomComponents.StylizedText(_indexLabel, Fonts.FontBold, UiColors.TextMuted);
            if (ImGui.IsItemClicked() && selectedSnapshot != null)
                StartRenaming(selectedSnapshot);

            CustomComponents.TooltipForLastItem("Click to rename snapshot");
            ImGui.SameLine();

            var dropdownWidth = MathF.Max(frameHeight,
                                          ImGui.GetContentRegionAvail().X - arrowsWidth - actionButtonsWidth - 3 * spacing);

            var isRenaming = selectedSnapshot != null && _renamingSnapshotId == selectedSnapshot.Id;
            if (isRenaming)
            {
                ImGui.SetNextItemWidth(dropdownWidth);
                if (_renameJustStarted)
                {
                    ImGui.SetKeyboardFocusHere();
                    _renameJustStarted = false;
                }

                ImGui.InputText("##renameSnapshot", ref _renameBuffer, 256);
                if (ImGui.IsItemDeactivated())
                {
                    selectedSnapshot!.Title = string.IsNullOrWhiteSpace(_renameBuffer) ? null : _renameBuffer;
                    pool.SaveVariationsToFile();
                    _renamingSnapshotId = Guid.Empty;
                }
            }
            else
            {
                // Snapshot dropdown
                ImGui.SetNextItemWidth(dropdownWidth);
                if (ImGui.BeginCombo("##snapshotDropdown", _selectedSnapshotLabel))
                {
                    foreach (var snapshot in _snapshots)
                    {
                        var isSelected = snapshot == selectedSnapshot;
                        var label = string.IsNullOrEmpty(snapshot.Title) || snapshot.Title == "untitled"
                                        ? $"Snapshot #{snapshot.ActivationIndex}"
                                        : $"{snapshot.Title} #{snapshot.ActivationIndex}";
                        if (ImGui.Selectable(label, isSelected))
                        {
                            ApplySnapshot(pool, composition, snapshot);
                        }
                    }

                    ImGui.EndCombo();
                }
            }

            // Prev / next arrows cycle by activation index order
            var hasSnapshots = _snapshots.Count > 0;
            ImGui.SameLine();
            if (DrawBarButton(Icon.ArrowLeft, hasSnapshots, "Previous snapshot", CustomComponents.ButtonStates.Default))
                ApplySnapshot(pool, composition, GetNeighborSnapshot(selectedSnapshot, -1));

            ImGui.SameLine();
            if (DrawBarButton(Icon.ArrowRight, hasSnapshots, "Next snapshot", CustomComponents.ButtonStates.Default))
                ApplySnapshot(pool, composition, GetNeighborSnapshot(selectedSnapshot, +1));

            // Right-aligned actions
            CustomComponents.RightAlign(actionButtonsWidth);

            var canWrite = selectedSnapshot != null && isModified;
            if (DrawBarButton(Icon.Apply, canWrite, "Update snapshot from current values"))
                WriteSnapshot(pool, composition, selectedSnapshot!);

            ImGui.SameLine();
            if (DrawBarButton(Icon.Reset, canWrite, "Revert to snapshot values"))
                ApplySnapshot(pool, composition, selectedSnapshot!);

            ImGui.SameLine();
            if (DrawBarButton(Icon.Trash, selectedSnapshot != null, "Remove snapshot",
                              CustomComponents.ButtonStates.Default, attentionOnHover: true))
            {
                pool.DeleteVariation(selectedSnapshot!);
                _modificationCheck.Invalidate();
            }

            // Creating a new snapshot only makes sense when the current values differ
            ImGui.SameLine(0, spacing + addButtonGap);
            var canCreate = selectedSnapshot == null || isModified;
            if (DrawBarButton(Icon.Plus, canCreate, "Create new snapshot from current values"))
                CreateAndRenameSnapshot(pool);
        }
        ImGui.EndChild();
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
        _renameJustStarted = true;
    }

    private void CreateAndRenameSnapshot(SymbolVariationPool pool)
    {
        var newVariation = VariationHandling.CreateOrUpdateSnapshotVariation();
        if (newVariation != null)
        {
            pool.SetActiveVariationWithoutApply(newVariation);
            StartRenaming(newVariation);
        }

        _modificationCheck.Invalidate();
    }

    private void DrawOpList(Instance composition, Variation? selectedSnapshot)
    {
        if (selectedSnapshot == null)
        {
            CustomComponents.EmptyWindowMessage("Select a snapshot\nto view and edit its parameters.");
            return;
        }

        ImGui.PushStyleColor(ImGuiCol.ChildBg, UiColors.BackgroundFull.Fade(0.25f).Rgba);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(5, 5) * T3Ui.UiScaleFactor);
        ImGui.BeginChild("snapshotParameters", Vector2.Zero, ImGuiChildFlags.AlwaysUseWindowPadding);
        {
            CollectOpEntries(composition, selectedSnapshot);

            var compositionUi = composition.GetSymbolUi();

            // Composition inputs captured under the Guid.Empty key
            if (selectedSnapshot.ParameterSetsForChildIds.ContainsKey(Guid.Empty)
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
                }
            }

            // Ops enabled for snapshots, sorted by canvas position
            foreach (var entry in _opEntries)
            {
                var typeColor = entry.Instance.Outputs.Count > 0
                                    ? TypeUiRegistry.GetPropertiesForType(entry.Instance.Outputs[0].ValueType).Color
                                    : UiColors.Text;
                var labelColor = ColorVariations.OperatorLabel.Apply(typeColor);

                var symbolChild = entry.ChildUi.SymbolChild;
                BeginBlock();
                DrawGroupHeader(entry.ChildUi.Id, GetOpGroupLabel(symbolChild), labelColor, out var nameClicked);

                if (nameClicked)
                {
                    var projectView = ProjectView.Focused;
                    if (projectView != null)
                    {
                        projectView.NodeSelection.SetSelection(entry.ChildUi, entry.Instance);
                        FitViewToSelectionHandling.FitViewToSelection();
                    }
                }

                DrawControlledParameters(entry.Instance, entry.Instance.GetSymbolUi(), entry.ChildUi, compositionUi,
                                         selectedSnapshot, childKey: entry.ChildUi.Id, onlyEnabledInputs: true);
                EndBlock(entry.ChildUi.Id);

                FormInputs.AddVerticalSpace(5);
            }

            DrawStaleEntries(composition, selectedSnapshot);
        }
        ImGui.EndChild();
        ImGui.PopStyleVar();
        ImGui.PopStyleColor();
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
        var pad = new Vector2(0, 3) * T3Ui.UiScaleFactor;
        var min = new Vector2(_blockLeftX, ImGui.GetItemRectMin().Y - pad.Y);
        var max = new Vector2(_blockRightX, ImGui.GetItemRectMax().Y + pad.Y);

        // Hovering the block highlights the matching operator in the graph; the same shared
        // hover set drives the reverse (hovering the op in the graph brightens the block).
        if (highlightId != Guid.Empty && ImGui.IsMouseHoveringRect(min, max))
            FrameStats.AddHoveredId(highlightId);

        var isOpHovered = highlightId != Guid.Empty && FrameStats.IsIdHovered(highlightId);
        var fade = isOpHovered ? 0.6f : 0.3f;

        drawList.ChannelsSetCurrent(0);
        drawList.AddRectFilled(min, max, UiColors.BackgroundButton.Fade(fade), 6 * T3Ui.UiScaleFactor);
        drawList.ChannelsMerge();
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
    /// collapsing is reserved for the annotation/section groups coming with the section tree.
    /// </summary>
    private static void DrawGroupHeader(Guid groupId, string label, Color labelColor, out bool nameClicked)
    {
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(0));
        ImGui.PushID(groupId.GetHashCode());

        ImGui.PushFont(Fonts.FontSmall);
        ImGui.PushStyleColor(ImGuiCol.Text, labelColor.Rgba);
        ImGui.PushStyleColor(ImGuiCol.HeaderHovered, Color.Transparent.Rgba);
        ImGui.PushStyleColor(ImGuiCol.HeaderActive, Color.Transparent.Rgba);
        ImGui.AlignTextToFramePadding();
        nameClicked = ImGui.Selectable(label);
        ImGui.PopStyleColor(3);
        ImGui.PopFont();

        // Subtle rounded highlight instead of the full-width Selectable header bar
        CustomComponents.DrawHoverHighlightOnLastItem();

        ImGui.PopID();
        ImGui.PopStyleVar();
    }

    /// <summary>
    /// Draws editable parameter rows like the regular parameter view, but only for the
    /// controlled inputs — the blendable ones a snapshot write would capture. Rows are
    /// highlighted when they no longer match the snapshot and get a revert button.
    /// </summary>
    private void DrawControlledParameters(Instance instance,
                                          SymbolUi symbolUi,
                                          SymbolUi.Child symbolChildUi,
                                          SymbolUi compositionSymbolUi,
                                          Variation snapshot,
                                          Guid childKey,
                                          bool onlyEnabledInputs = false)
    {
        ImGui.PushStyleColor(ImGuiCol.FrameBg, UiColors.BackgroundButton.Rgba);
        ImGui.PushStyleColor(ImGuiCol.FrameBgHovered, UiColors.BackgroundHover.Rgba);
        ImGui.PushID(instance.GetHashCode());

        // Keep room right of the value edits for the per-row revert buttons
        InputArea.ValueEditRightMargin = ImGui.GetFrameHeight() + 2 * T3Ui.UiScaleFactor;

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

            var isMismatch = _modificationCheck.IsInputModified(childKey, inputSlot.Id);
            var isDraggingThis = _revertDragInputId == inputSlot.Id && _revertDragChildKey == childKey;
            var highlight = isMismatch || isDraggingThis;

            ImGui.PushID(inputSlot.Id.GetHashCode());

            // In this view highlighting means "differs from the snapshot", not "non-default"
            InputArea.DimHighlightOverride = !highlight;
            var editState = inputUi.DrawParameterEdit(inputSlot, compositionSymbolUi, symbolChildUi, hideNonEssentials: false, skipIfDefault: false);
            InputArea.DimHighlightOverride = null;

            ParameterWindow.HandleInputEditState(instance, inputSlot, editState);

            if (highlight)
                HandleRevertHandle(instance, snapshot, childKey, inputSlot);

            ImGui.PopID();
        }

        InputArea.ValueEditRightMargin = 0;

        ImGui.PopID();
        ImGui.PopStyleColor(2);
    }

    /// <summary>
    /// Revert handle in the space reserved right of the value edit. A plain click reverts the
    /// parameter to the snapshot value; dragging opens an infinity slider whose factor scales
    /// the delta (1 = current, 0 = full revert, &gt;1 amplifies away from the snapshot).
    /// </summary>
    private void HandleRevertHandle(Instance instance, Variation snapshot, Guid childKey, IInputSlot inputSlot)
    {
        if (instance.Parent == null)
            return;

        var itemMin = ImGui.GetItemRectMin();
        var itemMax = ImGui.GetItemRectMax();
        var buttonSize = ImGui.GetFrameHeight();

        var cursorToRestore = ImGui.GetCursorScreenPos();
        ImGui.SetCursorScreenPos(new Vector2(itemMax.X + 2 * T3Ui.UiScaleFactor, itemMin.Y));

        CustomComponents.IconButton(Icon.Reset, new Vector2(buttonSize, buttonSize), UiColors.ForegroundFull);
        CustomComponents.TooltipForLastItem("Click to revert, drag to scale the change");

        if (ImGui.IsItemActivated())
        {
            _revertDragInputId = inputSlot.Id;
            _revertDragChildKey = childKey;
            _revertStartValue = inputSlot.Input.Value.Clone();
            _revertSnapshotValue = snapshot.ParameterSetsForChildIds.TryGetValue(childKey, out var set)
                                   && set.TryGetValue(inputSlot.Id, out var stored)
                                       ? stored.Clone()
                                       : inputSlot.Input.DefaultValue.Clone();
            _revertFactor = 1;
            _revertModified = false;
            _revertDragCommand = null;
            _revertSliderStarted = false;
        }

        var isThis = _revertDragInputId == inputSlot.Id && _revertDragChildKey == childKey;

        if (isThis && ImGui.IsItemActive())
        {
            var draggedFar = ImGui.GetIO().MouseDragMaxDistanceSqr[0] > UserSettings.Config.ClickThreshold;
            if (draggedFar
                && _revertStartValue != null && _revertSnapshotValue != null
                && ValueUtils.BlendMethods.TryGetValue(inputSlot.Input.Value.ValueType, out var blend))
            {
                // Restart on the first slider frame so the factor initializes to 1 rather than
                // keeping the overlay's stale value from a previous (unrelated) edit.
                var restarted = !_revertSliderStarted;
                _revertSliderStarted = true;
                InfinitySliderOverlay.Draw(ref _revertFactor, restarted, ImGui.GetMousePos(), min: 0, max: 1, scale: 0.05f);

                var blended = blend(_revertSnapshotValue, _revertStartValue, (float)_revertFactor);
                if (blended != null)
                {
                    _revertDragCommand ??= new ChangeInputValueCommand(instance.Parent.Symbol, instance.SymbolChildId,
                                                                       inputSlot.Input, inputSlot.Input.Value);
                    _revertDragCommand.AssignNewValue(blended);
                    inputSlot.DirtyFlag.Invalidate();
                    _revertModified = true;
                }
            }
        }

        if (isThis && ImGui.IsItemDeactivated())
        {
            if (_revertModified && _revertDragCommand != null)
            {
                // Dragging back to factor 1 leaves the value unchanged — nothing to record
                if (Math.Abs(_revertFactor - 1) > 0.0001)
                    UndoRedoStack.Add(_revertDragCommand);
            }
            else
            {
                RevertParameterToSnapshot(instance, snapshot, childKey, inputSlot);
            }

            _revertDragInputId = Guid.Empty;
            _revertDragChildKey = Guid.Empty;
            _revertStartValue = null;
            _revertSnapshotValue = null;
            _revertDragCommand = null;
            _revertModified = false;
            _modificationCheck.Invalidate();
        }

        // Restoring the cursor with SetCursorScreenPos leaves ImGui's "set-pos" flag pending;
        // a zero Dummy validates the extent so a following EndGroup (block box) doesn't assert.
        ImGui.SetCursorScreenPos(cursorToRestore);
        ImGui.Dummy(Vector2.Zero);
    }

    private void RevertParameterToSnapshot(Instance instance, Variation snapshot, Guid childKey, IInputSlot inputSlot)
    {
        if (instance.Parent == null)
            return;

        if (snapshot.ParameterSetsForChildIds.TryGetValue(childKey, out var parameterSet)
            && parameterSet.TryGetValue(inputSlot.Id, out var storedValue))
        {
            UndoRedoStack.AddAndExecute(new ChangeInputValueCommand(instance.Parent.Symbol, instance.SymbolChildId, inputSlot.Input, storedValue));
        }
        else
        {
            // Not captured in the snapshot: applying it would reset the parameter to default
            UndoRedoStack.AddAndExecute(new ResetInputToDefault(instance.Parent.Symbol, instance.SymbolChildId, inputSlot.Input));
        }

        _modificationCheck.Invalidate();
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

    private void CollectOpEntries(Instance composition, Variation snapshot)
    {
        _opEntries.Clear();
        _staleChildIds.Clear();

        var compositionUi = composition.GetSymbolUi();

        foreach (var childInstance in composition.Children.Values)
        {
            if (!compositionUi.ChildUis.TryGetValue(childInstance.SymbolChildId, out var childUi))
                continue;

            if (!childUi.EnabledForSnapshots)
                continue;

            _opEntries.Add(new OpEntry
                               {
                                   Instance = childInstance,
                                   ChildUi = childUi,
                               });
        }

        _opEntries.Sort(_byCanvasPosition);

        foreach (var childId in snapshot.ParameterSetsForChildIds.Keys)
        {
            if (childId == Guid.Empty)
                continue;

            var isListed = false;
            foreach (var entry in _opEntries)
            {
                if (entry.ChildUi.Id != childId)
                    continue;

                isListed = true;
                break;
            }

            if (!isListed)
                _staleChildIds.Add(childId);
        }
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
        if (selectedId == _lastLabelVariationId && title == _lastLabelTitle)
            return;

        _lastLabelVariationId = selectedId;
        _lastLabelTitle = title;

        if (selectedSnapshot == null)
        {
            _indexLabel = "-";
            _selectedSnapshotLabel = "Select snapshot...";
        }
        else
        {
            _indexLabel = selectedSnapshot.ActivationIndex.ToString();
            _selectedSnapshotLabel = string.IsNullOrEmpty(title) || title == "untitled"
                                         ? $"Snapshot #{selectedSnapshot.ActivationIndex}"
                                         : $"{title} #{selectedSnapshot.ActivationIndex}";
        }
    }

    private static readonly Comparison<Variation> _byActivationIndex
        = (a, b) => a.ActivationIndex.CompareTo(b.ActivationIndex);

    private static readonly Comparison<OpEntry> _byCanvasPosition
        = (a, b) =>
          {
              var byY = a.ChildUi.PosOnCanvas.Y.CompareTo(b.ChildUi.PosOnCanvas.Y);
              return byY != 0 ? byY : a.ChildUi.PosOnCanvas.X.CompareTo(b.ChildUi.PosOnCanvas.X);
          };

    private readonly ModificationCheck _modificationCheck = new();
    private readonly List<Variation> _snapshots = new();
    private readonly List<OpEntry> _opEntries = new();
    private readonly List<Guid> _staleChildIds = new();
    private readonly List<Instance> _affectedInstances = new();
    private readonly Guid[] _singleStaleId = new Guid[1];
    private readonly Variation[] _singleVariation = new Variation[1];
    private Guid _pendingStaleRemovalId;
    private Guid _fallbackSnapshotId;
    private float _blockLeftX;
    private float _blockRightX;
    private readonly Dictionary<Guid, (string CustomName, string Label)> _opLabelCache = new();

    private Guid _lastLabelVariationId = Guid.NewGuid(); // force initial label update
    private string? _lastLabelTitle;
    private string _indexLabel = "-";
    private string _selectedSnapshotLabel = "Select snapshot...";

    // Inline snapshot rename (selector bar)
    private Guid _renamingSnapshotId;
    private bool _renameJustStarted;
    private string _renameBuffer = "";

    // Per-row revert drag (infinity slider scaling the delta to the snapshot)
    private Guid _revertDragInputId;
    private Guid _revertDragChildKey;
    private InputValue? _revertStartValue;
    private InputValue? _revertSnapshotValue;
    private ChangeInputValueCommand? _revertDragCommand;
    private double _revertFactor;
    private bool _revertSliderStarted;
    private bool _revertModified;
}
