#nullable enable
using ImGuiNET;
using T3.Core.DataTypes.Vector;
using T3.Core.Operator;
using T3.Core.Operator.Slots;
using T3.Core.Utils;
using T3.Editor.Gui.Input;
using T3.Editor.Gui.Interaction.Variations;
using T3.Editor.Gui.Interaction.Variations.Model;
using T3.Editor.Gui.Styling;
using T3.Editor.Gui.UiHelpers;
using T3.Editor.UiModel;
using T3.Editor.UiModel.Commands;
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
            _isModified = Compute(composition, variation);
            return _isModified;
        }

        /// <summary>
        /// Mirrors the apply semantics: inputs captured in the snapshot must match their stored
        /// value; captured ops' other blendable inputs must be at default (apply resets them).
        /// </summary>
        private static bool Compute(Instance composition, Variation variation)
        {
            foreach (var (childId, parameterSet) in variation.ParameterSetsForChildIds)
            {
                Instance? instance;
                if (childId == Guid.Empty)
                {
                    instance = composition;
                }
                else if (!composition.Children.TryGetChildInstance(childId, out instance))
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

                    if (parameterSet.TryGetValue(inputSlot.Id, out var storedValue))
                    {
                        if (!compare(storedValue, inputSlot.Input.Value))
                            return true;
                    }
                    else if (!inputSlot.Input.IsDefault)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private const int CheckIntervalFrames = 30;
        private Guid _lastVariationId;
        private int _lastUndoStackCount = -1;
        private int _framesUntilRecheck;
        private bool _isModified;
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
                VariationHandling.CreateOrUpdateSnapshotVariation();
                _modificationCheck.Invalidate();
            }

            return;
        }

        Variation? selectedSnapshot = null;
        if (pool.ActiveVariation is { IsSnapshot: true } activeVariation && _snapshots.Contains(activeVariation))
            selectedSnapshot = activeVariation;

        var isModified = selectedSnapshot != null && _modificationCheck.IsModified(composition, selectedSnapshot);

        DrawSelectorBar(pool, composition, selectedSnapshot, isModified);
        DrawOpList(composition, selectedSnapshot);
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

            // Index indicator
            ImGui.AlignTextToFramePadding();
            CustomComponents.StylizedText(_indexLabel, Fonts.FontBold, UiColors.TextMuted);
            ImGui.SameLine();

            var frameHeight = ImGui.GetFrameHeight();
            var spacing = ImGui.GetStyle().ItemSpacing.X;
            var actionButtonsWidth = 3 * frameHeight + 2 * spacing;
            var arrowsWidth = 2 * (frameHeight + spacing);

            // Snapshot dropdown
            ImGui.SetNextItemWidth(MathF.Max(frameHeight,
                                             ImGui.GetContentRegionAvail().X - arrowsWidth - actionButtonsWidth - 2 * spacing));
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

            // Prev / next arrows cycle by activation index order
            ImGui.SameLine();
            if (CustomComponents.IconButton(Icon.ChevronLeft, Vector2.Zero) && _snapshots.Count > 0)
            {
                ApplySnapshot(pool, composition, GetNeighborSnapshot(selectedSnapshot, -1));
            }

            ImGui.SameLine();
            if (CustomComponents.IconButton(Icon.ChevronRight, Vector2.Zero) && _snapshots.Count > 0)
            {
                ApplySnapshot(pool, composition, GetNeighborSnapshot(selectedSnapshot, +1));
            }

            // Right-aligned actions
            CustomComponents.RightAlign(actionButtonsWidth);

            var canWrite = selectedSnapshot != null && isModified;
            if (CustomComponents.IconButton(Icon.Snapshot, Vector2.Zero,
                                            canWrite ? CustomComponents.ButtonStates.Normal : CustomComponents.ButtonStates.Disabled)
                && canWrite)
            {
                WriteSnapshot(pool, composition, selectedSnapshot!);
            }

            CustomComponents.TooltipForLastItem("Update snapshot from current values");

            ImGui.SameLine();
            var canRevert = selectedSnapshot != null && isModified;
            if (CustomComponents.IconButton(Icon.Revert, Vector2.Zero,
                                            canRevert ? CustomComponents.ButtonStates.Normal : CustomComponents.ButtonStates.Disabled)
                && canRevert)
            {
                ApplySnapshot(pool, composition, selectedSnapshot!);
            }

            CustomComponents.TooltipForLastItem("Revert to snapshot values");

            ImGui.SameLine();
            if (CustomComponents.IconButton(Icon.Trash, Vector2.Zero,
                                            selectedSnapshot != null ? CustomComponents.ButtonStates.Normal : CustomComponents.ButtonStates.Disabled)
                && selectedSnapshot != null)
            {
                pool.DeleteVariation(selectedSnapshot);
                _modificationCheck.Invalidate();
            }

            CustomComponents.TooltipForLastItem("Remove snapshot");
        }
        ImGui.EndChild();
        ImGui.PopStyleVar();
    }

    private void DrawOpList(Instance composition, Variation? selectedSnapshot)
    {
        if (selectedSnapshot == null)
        {
            CustomComponents.EmptyWindowMessage("Select a snapshot\nto view and edit its parameters.");
            return;
        }

        ImGui.PushStyleColor(ImGuiCol.ChildBg, UiColors.WindowBackground.Fade(0.5f).Rgba);
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
                    if (DrawGroupHeader(composition.SymbolChildId, "Inputs", UiColors.TextMuted, out _))
                    {
                        DrawControlledParameters(composition, compositionUi, compositionChildUi, parentUi);
                    }

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
                var isExpanded = DrawGroupHeader(entry.ChildUi.Id, symbolChild.ReadableName, labelColor, out var nameClicked);

                if (nameClicked)
                {
                    var projectView = ProjectView.Focused;
                    if (projectView != null)
                    {
                        projectView.NodeSelection.SetSelection(entry.ChildUi, entry.Instance);
                        FitViewToSelectionHandling.FitViewToSelection();
                    }
                }

                if (isExpanded)
                {
                    DrawControlledParameters(entry.Instance, entry.Instance.GetSymbolUi(), entry.ChildUi, compositionUi);
                }

                FormInputs.AddVerticalSpace(5);
            }

            DrawStaleEntries(composition, selectedSnapshot);
        }
        ImGui.EndChild();
        ImGui.PopStyleVar();
        ImGui.PopStyleColor();
    }

    /// <summary>
    /// Header row: a collapse chevron followed by the clickable op name.
    /// Returns true when the group content should be drawn.
    /// </summary>
    private bool DrawGroupHeader(Guid groupId, string label, Color labelColor, out bool nameClicked)
    {
        nameClicked = false;
        ImGui.PushID(groupId.GetHashCode());

        var isCollapsed = _collapsedGroupIds.Contains(groupId);
        if (CustomComponents.TransparentIconButton(isCollapsed ? Icon.ChevronRight : Icon.ChevronDown,
                                                   Vector2.Zero,
                                                   CustomComponents.ButtonStates.Dimmed))
        {
            if (isCollapsed)
                _collapsedGroupIds.Remove(groupId);
            else
                _collapsedGroupIds.Add(groupId);
        }

        ImGui.SameLine();
        ImGui.PushFont(Fonts.FontSmall);
        ImGui.PushStyleColor(ImGuiCol.Text, labelColor.Rgba);
        ImGui.AlignTextToFramePadding();
        nameClicked = ImGui.Selectable(label);
        ImGui.PopStyleColor();
        ImGui.PopFont();

        ImGui.PopID();
        return !isCollapsed;
    }

    /// <summary>
    /// Draws editable parameter rows like the regular parameter view, but only for the
    /// controlled inputs — the blendable ones a snapshot write would capture.
    /// </summary>
    private static void DrawControlledParameters(Instance instance,
                                                 SymbolUi symbolUi,
                                                 SymbolUi.Child symbolChildUi,
                                                 SymbolUi compositionSymbolUi)
    {
        ImGui.PushStyleColor(ImGuiCol.FrameBg, UiColors.BackgroundButton.Rgba);
        ImGui.PushStyleColor(ImGuiCol.FrameBgHovered, UiColors.BackgroundHover.Rgba);
        ImGui.PushID(instance.GetHashCode());

        foreach (var inputSlot in instance.Inputs)
        {
            if (!ValueUtils.BlendMethods.ContainsKey(inputSlot.Input.Value.ValueType))
                continue;

            if (!symbolUi.InputUis.TryGetValue(inputSlot.Id, out var inputUi))
                continue;

            if (inputUi.ExcludedFromPresets)
                continue;

            ImGui.PushID(inputSlot.Id.GetHashCode());
            var editState = inputUi.DrawParameterEdit(inputSlot, compositionSymbolUi, symbolChildUi, hideNonEssentials: false, skipIfDefault: false);
            ParameterWindow.HandleInputEditState(instance, inputSlot, editState);
            ImGui.PopID();
        }

        ImGui.PopID();
        ImGui.PopStyleColor(2);
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
            if (CustomComponents.TransparentIconButton(Icon.Trash, Vector2.Zero, CustomComponents.ButtonStates.Dimmed))
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
    private readonly HashSet<Guid> _collapsedGroupIds = new();
    private readonly Guid[] _singleStaleId = new Guid[1];
    private readonly Variation[] _singleVariation = new Variation[1];
    private Guid _pendingStaleRemovalId;

    private Guid _lastLabelVariationId = Guid.NewGuid(); // force initial label update
    private string? _lastLabelTitle;
    private string _indexLabel = "-";
    private string _selectedSnapshotLabel = "Select snapshot...";
}
