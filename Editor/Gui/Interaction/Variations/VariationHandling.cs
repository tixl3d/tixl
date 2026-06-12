#nullable enable

using T3.Core.Operator;
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

        if (!ActivePoolForSnapshots.TryCreateVariationForCompositionInstances(_affectedInstances, out var newVariation))
        {
            return null;
        }

        newVariation.PosOnCanvas = VariationBaseCanvas.FindFreePositionForNewThumbnail(ActivePoolForSnapshots.AllVariations);
        if (activationIndex != AutoIndex)
            newVariation.ActivationIndex = activationIndex;

        newVariation.State = Variation.States.Active;
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
}