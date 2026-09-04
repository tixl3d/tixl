#nullable enable

using T3.Core.IO;
using T3.Core.Operator;
using T3.Core.Operator.Interfaces;
using T3.Core.Operator.Slots;
using T3.Core.Utils;
using T3.Editor.Gui.Interaction.Variations.Model;
using T3.Editor.Gui.Windows.Variations;
using T3.Editor.UiModel;
using T3.Editor.UiModel.Commands;
using T3.Editor.UiModel.Commands.Graph;
using T3.Editor.UiModel.Commands.Variations;
using T3.Editor.UiModel.ProjectHandling;

namespace T3.Editor.Gui.Interaction.Variations;

/// <summary>
/// Applies actions on variations to the currently active pool.
/// </summary>
/// <remarks>
/// Variations are a sets of symbolChild.input-parameters combinations defined for an Symbol.
/// These input slots can also include the symbols out inputs which thus can be used for defining
/// and applying "presets" to instances of that symbol.
///
/// Most variations will modify(!) the parent symbol. This is great while working within a single symbol
/// and tweaking and blending parameters. However it's potentially unintended (or dangerous) if the
/// modified symbol has many instances. That's why applying symbol-variations is not allowed for Symbols
/// in the lib-namespace.  
/// </remarks>
internal static class VariationHandling
{
    public static SymbolVariationPool? ActivePoolForSnapshots { get; private set; }
    public static Instance? ActiveInstanceForSnapshots { get; private set; }

    public static SymbolVariationPool? ActivePoolForPresets { get; private set; }
    public static Instance? ActiveInstanceForPresets { get; private set; }

    /// <summary>
    /// Update variation handling
    /// </summary>
    public static void Update()
    {
        // Sync with composition selected in UI
        // var primaryGraphWindow = GraphWindow.Focused;
        // if (primaryGraphWindow == null)
        //     return;
        if (ProjectView.Focused == null)
            return;

        var nodeSelection = ProjectView.Focused.NodeSelection;
        var singleSelectedInstance = nodeSelection.GetSelectedInstanceWithoutComposition();

        if (singleSelectedInstance != null)
        {
            var selectedSymbolId = singleSelectedInstance.Symbol.Id;
            ActivePoolForPresets = GetOrLoadVariations(selectedSymbolId);
            if (singleSelectedInstance.Parent != null)
                ActivePoolForSnapshots = GetOrLoadVariations(singleSelectedInstance.Parent.Symbol.Id);

            ActiveInstanceForPresets = singleSelectedInstance;
            ActiveInstanceForSnapshots = singleSelectedInstance.Parent;
        }
        else
        {
            ActivePoolForPresets = null;

            var activeCompositionInstance = ProjectView.Focused.CompositionInstance;

            ActiveInstanceForSnapshots = activeCompositionInstance;

            // Prevent variations for library operators
            if (activeCompositionInstance != null)
            {
                if (activeCompositionInstance.Symbol.Namespace.StartsWith("Lib."))
                {
                    ActivePoolForSnapshots = null;
                }
                else
                {
                    ActivePoolForSnapshots = GetOrLoadVariations(activeCompositionInstance.Symbol.Id);
                }
            }

            if (!nodeSelection.IsAnythingSelected())
            {
                ActiveInstanceForPresets = ActiveInstanceForSnapshots;
            }
        }

        BlendActions.SmoothVariationBlending.UpdateBlend();

        ProcessSnapshotActivationRequests();
        ProcessSnapshotBlendRequests();

        // Frame-driven, window-independent: lets callers (e.g. the snapshot control view) render
        // thumbnails without the Variations window being open.
        VariationThumbnailRenderer.Update();
    }

    /// <summary>
    /// Applies one-shot snapshot activations forwarded by [ActivateSnapshot] operators and clears them
    /// (a pending entry means "activate now"). Activations only apply to the composition currently
    /// active for snapshots.
    /// </summary>
    private static void ProcessSnapshotActivationRequests()
    {
        var pending = SnapShotBlendingData.PendingActivationsByComposition;
        if (pending.Count == 0)
            return;

        foreach (var (compositionSymbolId, rawIndex) in pending)
        {
            if (ActiveInstanceForSnapshots != null && ActiveInstanceForSnapshots.Symbol.Id == compositionSymbolId)
                SnapshotActions.ActivateSnapshotByModuloIndex(rawIndex);
        }

        pending.Clear();
    }

    /// <summary>
    /// Applies any procedural snapshot-blend requests forwarded by [BlendSnapshots] operators. Each
    /// operator re-arms its request every evaluation; we consume the flag here so an operator that
    /// stops evaluating (deleted, disconnected, paused) releases its blend after one frame.
    /// </summary>
    private static void ProcessSnapshotBlendRequests()
    {
        foreach (var (compositionSymbolId, request) in SnapShotBlendingData.RequestsByComposition)
        {
            if (!request.Enabled)
            {
                ReleaseOpDrivenBlend(compositionSymbolId);
                continue;
            }

            request.Enabled = false;

            if (ActivePoolForSnapshots == null || ActiveInstanceForSnapshots == null
                || ActiveInstanceForSnapshots.Symbol.Id != compositionSymbolId)
            {
                request.ResolvedStatusLevel = IStatusProvider.StatusLevel.Notice;
                request.ResolvedStatusMessage = "This composition is not currently active for snapshots.";
                ReleaseOpDrivenBlend(compositionSymbolId);
                continue;
            }

            ApplyOpDrivenBlend(ActivePoolForSnapshots, ActiveInstanceForSnapshots, request);
        }
    }

    private static void ApplyOpDrivenBlend(SymbolVariationPool pool, Instance instance, SnapShotBlendingData.BlendRequest request)
    {
        _opOrderedSnapshots.Clear();
        foreach (var v in pool.AllVariations)
        {
            if (v.IsSnapshot)
                _opOrderedSnapshots.Add(v);
        }

        if (_opOrderedSnapshots.Count == 0)
        {
            request.ResolvedStatusLevel = IStatusProvider.StatusLevel.Warning;
            request.ResolvedStatusMessage = "This composition has no snapshots.";
            ReleaseOpDrivenBlend(instance.Symbol.Id);
            return;
        }

        if (request.Mode == SnapShotBlendingData.IndexMode.SnapshotIndices)
            VariationBaseCanvas.SortByActivationIndex(_opOrderedSnapshots);

        _opBlendVariations.Clear();
        _opBlendWeights.Clear();
        var notFoundCount = 0;

        for (var i = 0; i < request.Indices.Count; i++)
        {
            var weight = i < request.WeightFactors.Count ? request.WeightFactors[i] : 0f;
            if (weight <= 0.0001f)
                continue;

            if (!TryResolveSnapshot(request.Mode, request.Indices[i], out var variation))
            {
                notFoundCount++;
                continue;
            }

            _opBlendVariations.Add(variation);
            _opBlendWeights.Add(weight);
        }

        if (_opBlendVariations.Count == 0)
        {
            if (notFoundCount > 0)
            {
                request.ResolvedStatusLevel = IStatusProvider.StatusLevel.Warning;
                request.ResolvedStatusMessage = "None of the snapshot indices were found.";
            }
            else
            {
                request.ResolvedStatusLevel = IStatusProvider.StatusLevel.Notice;
                request.ResolvedStatusMessage = "All weight factors are zero.";
            }

            ReleaseOpDrivenBlend(instance.Symbol.Id);
            return;
        }

        // Normalize so the raw weighted-sum blend stays well-formed (weights must sum to 1).
        var sum = 0f;
        foreach (var w in _opBlendWeights)
            sum += w;

        if (sum > 0.0001f)
        {
            for (var i = 0; i < _opBlendWeights.Count; i++)
                _opBlendWeights[i] /= sum;
        }

        // Re-applying an unchanged blend would needlessly rebuild the macro command every frame; the
        // previous apply stays live until released, so a steady hold costs nothing.
        var alreadyDriving = pool.IsBlendDrivenByOperator && _opDrivenCompositionId == instance.Symbol.Id;
        if (!alreadyDriving || HasOpBlendChanged())
        {
            pool.ApplyOperatorDrivenBlend(instance, _opBlendVariations, _opBlendWeights);
            pool.IsBlendDrivenByOperator = true;
            _opDrivenCompositionId = instance.Symbol.Id;
            CacheAppliedOpBlend();
        }

        if (notFoundCount > 0)
        {
            request.ResolvedStatusLevel = IStatusProvider.StatusLevel.Notice;
            request.ResolvedStatusMessage = $"Blending {_opBlendVariations.Count} snapshot(s); {notFoundCount} index(es) not found.";
        }
        else
        {
            request.ResolvedStatusLevel = IStatusProvider.StatusLevel.Success;
            request.ResolvedStatusMessage = $"Blending {_opBlendVariations.Count} snapshot(s).";
        }
    }

    private static bool TryResolveSnapshot(SnapShotBlendingData.IndexMode mode, int index, out Variation variation)
    {
        if (mode == SnapShotBlendingData.IndexMode.ControllerIndices)
        {
            foreach (var v in _opOrderedSnapshots)
            {
                if (v.ActivationIndex == index)
                {
                    variation = v;
                    return true;
                }
            }

            variation = null!;
            return false;
        }

        // Ordinal position in reading order (the list is pre-sorted by the caller).
        if (index >= 0 && index < _opOrderedSnapshots.Count)
        {
            variation = _opOrderedSnapshots[index];
            return true;
        }

        variation = null!;
        return false;
    }

    private static void ReleaseOpDrivenBlend(Guid compositionSymbolId)
    {
        if (_opDrivenCompositionId != compositionSymbolId)
            return;

        var pool = GetOrLoadVariations(compositionSymbolId);
        if (pool.IsBlendDrivenByOperator)
        {
            pool.StopHover();
            pool.IsBlendDrivenByOperator = false;
            // Collapse the weight vector back to the active variation so the picker stops showing the mix.
            pool.ResetBlendWeights(pool.ActiveVariation?.Id ?? Guid.Empty);
        }

        _opDrivenCompositionId = Guid.Empty;
        _lastAppliedVariationIds.Clear();
        _lastAppliedWeights.Clear();
    }

    private static bool HasOpBlendChanged()
    {
        if (_lastAppliedVariationIds.Count != _opBlendVariations.Count)
            return true;

        for (var i = 0; i < _opBlendVariations.Count; i++)
        {
            if (_lastAppliedVariationIds[i] != _opBlendVariations[i].Id)
                return true;

            if (MathF.Abs(_lastAppliedWeights[i] - _opBlendWeights[i]) > 0.0005f)
                return true;
        }

        return false;
    }

    private static void CacheAppliedOpBlend()
    {
        _lastAppliedVariationIds.Clear();
        _lastAppliedWeights.Clear();
        foreach (var v in _opBlendVariations)
            _lastAppliedVariationIds.Add(v.Id);
        foreach (var w in _opBlendWeights)
            _lastAppliedWeights.Add(w);
    }

    public static SymbolVariationPool GetOrLoadVariations(Guid symbolId)
    {
        if (_variationPoolForOperators.TryGetValue(symbolId, out var variationForComposition))
        {
            return variationForComposition;
        }

        var newOpVariationPool = new SymbolVariationPool(symbolId);
        _variationPoolForOperators[newOpVariationPool.SymbolId] = newOpVariationPool;
        return newOpVariationPool;
    }

    private const int AutoIndex = -1;

    /// <summary>
    /// This tries to create at new variation and saves the variation file
    /// </summary>
    public static Variation? CreateOrUpdateSnapshotVariation(int activationIndex = AutoIndex)
    {
        // Only allow for snapshots.
        if (ActivePoolForSnapshots == null || ActiveInstanceForSnapshots == null)
        {
            return null;
        }

        // Delete previous snapshot for that index.
        if (activationIndex != AutoIndex && SymbolVariationPool.TryGetSnapshot(activationIndex, out var existingVariation))
        {
            ActivePoolForSnapshots.DeleteVariation(existingVariation);
        }

        _affectedInstances.Clear();

        AddSnapshotEnabledChildrenToList(ActiveInstanceForSnapshots, _affectedInstances);

        var activeBefore = ActivePoolForSnapshots.ActiveVariation;

        if (!ActivePoolForSnapshots.TryCreateVariationForCompositionInstances(_affectedInstances, out var newVariation))
        {
            return null;
        }

        // The list now sorts by ActivationIndex, so placement is purely a matter of which index the new
        // snapshot gets; its free canvas position is independent.
        newVariation.PosOnCanvas = VariationBaseCanvas.FindFreePositionForNewThumbnail(ActivePoolForSnapshots.AllVariations);

        if (activationIndex != AutoIndex)
        {
            // Explicit slot (e.g. a MIDI pad).
            newVariation.ActivationIndex = activationIndex;
        }
        else if (activeBefore is { IsSnapshot: true })
        {
            // Insert right behind the active snapshot: the next free controller index after it.
            newVariation.ActivationIndex = ActivePoolForSnapshots.GetNextFreeActivationIndexAfter(activeBefore.ActivationIndex, newVariation);
        }
        else
        {
            // No active snapshot (e.g. the first one): take the lowest free index so 0 (top-left) is used.
            newVariation.ActivationIndex = ActivePoolForSnapshots.GetNextFreeActivationIndexAfter(-1, newVariation);
        }

        // Make the new snapshot the active one so every view (Variations window + control view)
        // agrees. The values already equal the current state, so no apply command is needed.
        ActivePoolForSnapshots.SetActiveVariationWithoutApply(newVariation);
        ActivePoolForSnapshots.SaveVariationsToFile();
        return newVariation;
    }

    public static void RemoveInstancesFromVariations(IEnumerable<Guid> symbolChildIds, IReadOnlyList<Variation> variations, MacroCommand? collectInto = null)
    {
        if (ActivePoolForSnapshots == null || ActiveInstanceForSnapshots == null)
        {
            return;
        }

        var command = new RemoveInstancesFromVariationsCommand(ActivePoolForSnapshots, symbolChildIds, variations);
        if (collectInto != null)
            collectInto.AddAndExecCommand(command);
        else
            UndoRedoStack.AddAndExecute(command);
    }

    /// <summary>
    /// Toggles snapshot control for a single parameter as one undoable macro: updates the
    /// child's enabled set (enabling the first / disabling the last parameter also flips the
    /// per-op flag) and keeps all existing snapshots consistent — enabling captures the
    /// parameter's current value, disabling removes its stored values.
    /// </summary>
    internal static void ToggleParameterSnapshotControl(SymbolUi compositionUi, SymbolUi.Child childUi, Symbol.Child.Input input, bool enable)
    {
        // ParameterCollections (group index above 1) keep their own semantics
        if (childUi.SnapshotGroupIndex > 1)
            return;

        var inputId = input.InputDefinition.Id;
        HashSet<Guid>? newEnabledIds;
        int newGroupIndex;

        if (enable)
        {
            if (childUi.IsInputEnabledForSnapshots(inputId))
                return;

            newGroupIndex = 1;
            newEnabledIds = childUi.EnabledForSnapshots && childUi.SnapshotEnabledInputIds != null
                                ? [..childUi.SnapshotEnabledInputIds]
                                : [];
            newEnabledIds.Add(inputId);
        }
        else
        {
            if (!childUi.IsInputEnabledForSnapshots(inputId))
                return;

            // A null set means all parameters: materialize it before removing one
            newEnabledIds = childUi.SnapshotEnabledInputIds != null
                                ? [..childUi.SnapshotEnabledInputIds]
                                : CollectControlledInputIds(childUi);
            newEnabledIds.Remove(inputId);

            if (newEnabledIds.Count == 0)
            {
                newGroupIndex = 0;
                newEnabledIds = null;
            }
            else
            {
                newGroupIndex = 1;
            }
        }

        var macro = new MacroCommand("Toggle parameter snapshot control");
        macro.AddAndExecCommand(new ChangeSnapshotEnabledInputsCommand(compositionUi.Symbol.Id, childUi, newGroupIndex, newEnabledIds));

        var pool = GetOrLoadVariations(compositionUi.Symbol.Id);
        foreach (var variation in pool.AllVariations)
        {
            if (variation.IsPreset)
                continue;

            if (enable)
            {
                // Default values are not stored — apply resets non-stored controlled params anyway
                if (input.IsDefault)
                    continue;

                var newSets = CloneParameterSetsWithValue(variation.ParameterSetsForChildIds, childUi.Id, inputId, input.Value);
                macro.AddAndExecCommand(new UpdateVariationParametersCommand(pool, variation, newSets));
            }
            else
            {
                if (!variation.ParameterSetsForChildIds.TryGetValue(childUi.Id, out var storedSet))
                    continue;

                var removeWholeChild = newGroupIndex == 0;
                if (!removeWholeChild && !storedSet.ContainsKey(inputId))
                    continue;

                var newSets = CloneParameterSetsWithoutValue(variation.ParameterSetsForChildIds, childUi.Id, inputId, removeWholeChild);
                macro.AddAndExecCommand(new UpdateVariationParametersCommand(pool, variation, newSets));
            }
        }

        UndoRedoStack.Add(macro);
    }

    /// <summary>
    /// Writes <paramref name="value"/> as the stored value of one input (<paramref name="childId"/> /
    /// <paramref name="inputId"/>) into each of the given snapshots, as a single undoable macro.
    /// Backs the per-parameter "Apply to snapshot" / "Apply to all snapshots" actions in the
    /// snapshot control view.
    /// </summary>
    internal static void ApplyParameterToVariations(SymbolVariationPool pool, IEnumerable<Variation> targets,
                                                    Guid childId, Guid inputId, InputValue value, string commandName)
    {
        var macro = new MacroCommand(commandName);
        var any = false;
        foreach (var variation in targets)
        {
            if (variation.IsPreset)
                continue;

            var newSets = CloneParameterSetsWithValue(variation.ParameterSetsForChildIds, childId, inputId, value);
            macro.AddAndExecCommand(new UpdateVariationParametersCommand(pool, variation, newSets));
            any = true;
        }

        if (any)
            UndoRedoStack.Add(macro);
    }

    /// <summary>
    /// All blendable, non-excluded input ids of the child's symbol — the parameters the
    /// snapshot system can control. Used to materialize the legacy "all enabled" state.
    /// </summary>
    private static HashSet<Guid> CollectControlledInputIds(SymbolUi.Child childUi)
    {
        var result = new HashSet<Guid>();
        var symbol = childUi.SymbolChild.Symbol;
        var symbolUi = symbol.GetSymbolUi();

        foreach (var inputDefinition in symbol.InputDefinitions)
        {
            if (!ValueUtils.BlendMethods.ContainsKey(inputDefinition.DefaultValue.ValueType))
                continue;

            if (symbolUi.InputUis.TryGetValue(inputDefinition.Id, out var inputUi) && inputUi.ExcludedFromPresets)
                continue;

            result.Add(inputDefinition.Id);
        }

        return result;
    }

    private static Dictionary<Guid, Dictionary<Guid, InputValue>> CloneParameterSetsWithValue(Dictionary<Guid, Dictionary<Guid, InputValue>> source,
                                                                                              Guid childId, Guid inputId, InputValue value)
    {
        var result = new Dictionary<Guid, Dictionary<Guid, InputValue>>(source);
        result[childId] = result.TryGetValue(childId, out var childSet)
                              ? new Dictionary<Guid, InputValue>(childSet)
                              : new Dictionary<Guid, InputValue>();
        result[childId][inputId] = value.Clone();
        return result;
    }

    private static Dictionary<Guid, Dictionary<Guid, InputValue>> CloneParameterSetsWithoutValue(Dictionary<Guid, Dictionary<Guid, InputValue>> source,
                                                                                                 Guid childId, Guid inputId, bool removeWholeChild)
    {
        var result = new Dictionary<Guid, Dictionary<Guid, InputValue>>(source);
        if (removeWholeChild)
        {
            result.Remove(childId);
            return result;
        }

        if (result.TryGetValue(childId, out var childSet))
        {
            var newChildSet = new Dictionary<Guid, InputValue>(childSet);
            newChildSet.Remove(inputId);
            result[childId] = newChildSet;
        }

        return result;
    }

    internal static void AddSnapshotEnabledChildrenToList(Instance instance, List<Instance> list)
    {
        var compositionUi = instance.GetSymbolUi();
        foreach (var childInstance in instance.Children.Values)
        {
            var symbolChildUi = compositionUi.ChildUis[childInstance.SymbolChildId]; // Debug.Assert(symbolChildUi != null);

            if (!symbolChildUi.EnabledForSnapshots)
                continue;

            list.Add(childInstance);
        }
    }

    // private static IEnumerable<Instance> GetSnapshotEnabledChildren(Instance instance)
    // {
    //     var compositionUi = SymbolUiRegistry.Entries[instance.Symbol.Id];
    //     foreach (var childInstance in instance.Children)
    //     {
    //         var symbolChildUi = compositionUi.ChildUis.SingleOrDefault(cui => cui.Id == childInstance.SymbolChildId);
    //         Debug.Assert(symbolChildUi != null);
    //
    //         if (symbolChildUi.SnapshotGroupIndex == 0)
    //             continue;
    //
    //         yield return childInstance;
    //     }
    // }

    private static readonly Dictionary<Guid, SymbolVariationPool> _variationPoolForOperators = new();
    private static readonly List<Instance> _affectedInstances = new(100);

    // Reused buffers for procedural snapshot blending (per-frame, allocation-free).
    private static readonly List<Variation> _opOrderedSnapshots = new();
    private static readonly List<Variation> _opBlendVariations = new();
    private static readonly List<float> _opBlendWeights = new();
    private static readonly List<Guid> _lastAppliedVariationIds = new();
    private static readonly List<float> _lastAppliedWeights = new();
    private static Guid _opDrivenCompositionId = Guid.Empty;
}