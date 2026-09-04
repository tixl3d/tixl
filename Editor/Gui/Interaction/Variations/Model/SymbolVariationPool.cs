#nullable enable
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using T3.Core.Model;
using T3.Core.Operator;
using T3.Core.Operator.Slots;
using T3.Core.Settings;
using T3.Core.Utils;
using T3.Editor.UiModel;
using T3.Editor.UiModel.Commands;
using T3.Editor.UiModel.Commands.Graph;
using T3.Editor.UiModel.Commands.Variations;
using T3.Serialization;

namespace T3.Editor.Gui.Interaction.Variations.Model;

/// <summary>
/// Collects all presets and variations for a symbol.
/// </summary>
internal sealed class SymbolVariationPool
{
    public readonly Guid SymbolId;
    public readonly IReadOnlyList<Variation> AllVariations;
    public readonly IReadOnlyList<Variation> UserVariations;
    public readonly IReadOnlyList<Variation> Defaults;

    private readonly List<Variation> _userVariations;
    private readonly List<Variation> _defaults;
    private readonly List<Variation> _allVariations;

    private Variation? _activeVariation;

    public Variation? ActiveVariation
    {
        get => _activeVariation;
        private set
        {
            _activeVariation = value;
            // Activating a single variation collapses any live cross-fade back to it.
            ResetBlendWeights(value?.Id ?? Guid.Empty);
        }
    }

    /// <summary>
    /// True while a [BlendSnapshots] operator is procedurally driving this pool's blend. Manual
    /// blend faders treat the pool as read-only while this is set so the two don't fight.
    /// </summary>
    public bool IsBlendDrivenByOperator { get; set; }

    // Live cross-fade weight vector (session-only): variationId → weight. Persists across picker
    // opens, resets to the single active variation on activation, drops entries on removal.
    private readonly Dictionary<Guid, float> _blendWeights = new();
    private readonly Dictionary<Guid, float> _dragStartWeights = new();
    private readonly List<Guid> _blendWeightKeyBuffer = new();
    private readonly List<Variation> _weightedBlendVariations = new();
    private readonly List<float> _weightedBlendWeights = new();

    public SymbolVariationPool(Guid symbolId)
    {
        SymbolId = symbolId;
        ResolveVariationFilePaths(symbolId, out var writableFilePath, out var readOnlyDefaultsFilePath);
        _userVariations = LoadVariationsFromFile(writableFilePath, symbolId);
        _defaults = readOnlyDefaultsFilePath != null ? LoadVariationsFromFile(readOnlyDefaultsFilePath, symbolId) : [];
        _allVariations = new List<Variation>(_userVariations.Count + _defaults.Count);
        _allVariations.AddRange(_userVariations);
        _allVariations.AddRange(_defaults);

        UserVariations = _userVariations;
        Defaults = _defaults;
        AllVariations = _allVariations;

        DeduplicateSnapshotActivationIndices();
    }

    /// <summary>
    /// The ActivationIndex is both the MIDI/controller slot and the snapshot sort order, so it must be
    /// unique per snapshot. Old files (or hand edits) can hold clashing or negative indices; reassign
    /// those to the lowest free slots on load so the list, grid and pads stay coherent. In-memory only —
    /// the fix is written out by the next save.
    /// </summary>
    private void DeduplicateSnapshotActivationIndices()
    {
        var used = new HashSet<int>();
        List<Variation>? clashing = null;

        foreach (var v in _allVariations)
        {
            if (!v.IsSnapshot)
                continue;

            if (v.ActivationIndex >= 0 && used.Add(v.ActivationIndex))
                continue;

            (clashing ??= new List<Variation>()).Add(v);
        }

        if (clashing == null)
            return;

        var nextFree = 0;
        foreach (var v in clashing)
        {
            while (!used.Add(nextFree))
                nextFree++;

            Log.Warning($"Snapshot '{v.Title ?? "(unnamed)"}' had a clashing controller index {v.ActivationIndex}; reassigned to {nextFree} to keep the snapshot order unique.");
            v.ActivationIndex = nextFree;
        }
    }

    #region serialization
    /// <summary>
    /// Variations live in the owning package's .meta/Variations folder. For read-only packages
    /// (e.g. Lib in standalone builds) that folder only provides the shipped defaults, and user
    /// created variations are kept in a per-user overlay folder in AppData instead.
    /// Paths are resolved on every load/save because symbols can move between packages.
    /// </summary>
    private static void ResolveVariationFilePaths(Guid symbolId, out string writableFilePath, out string? readOnlyDefaultsFilePath)
    {
        readOnlyDefaultsFilePath = null;
        if (SymbolUiRegistry.TryGetSymbolUi(symbolId, out var symbolUi))
        {
            var package = symbolUi.Symbol.SymbolPackage;
            var packageFilePath = GetVariationFilePathInPackage(package.Folder, symbolId);
            if (!package.IsReadOnly)
            {
                writableFilePath = packageFilePath;
                return;
            }

            readOnlyDefaultsFilePath = packageFilePath;
        }

        writableFilePath = GetVariationFilePathInUserOverlay(symbolId);
    }

    internal static string GetVariationFilePathInPackage(string packageFolder, Guid symbolId)
        => Path.Combine(packageFolder, FileLocations.MetaSubFolder, VariationsSubFolder, $"{symbolId}.var");

    internal static string GetVariationFilePathInUserOverlay(Guid symbolId)
        => Path.Combine(FileLocations.SettingsDirectory, UserOverlaySubFolder, $"{symbolId}.var");

    internal static List<Variation> LoadVariationsFromFile(string filePath, Guid compositionId)
    {
        string fileContent;
        try
        {
            if (!File.Exists(filePath))
                return [];

            fileContent = File.ReadAllText(filePath);
        }
        catch (Exception e)
        {
            Log.Warning($"Failed to read variations file {filePath}: {e.Message}");
            return [];
        }

        using var sr = new StringReader(fileContent);
        using var jsonReader = new JsonTextReader(sr);

        var result = new List<Variation>();

        try
        {
            var jToken = JToken.ReadFrom(jsonReader, SymbolJson.LoadSettings);

            if (jToken["Variations"] is JArray jArray)
            {
                foreach (var sceneToken in jArray)
                {
                    if (!sceneToken.Any())
                    {
                        Log.Error("No variations?");
                        continue;
                    }

                    if (!Variation.TryLoadVariationFromJson(compositionId, sceneToken, out var newVariation))
                    {
                        Log.Warning("Failed to parse variation json:" + sceneToken);
                        continue;
                    }

                    //TODO: this needs to be implemented
                    //newVariation.IsPreset = true;
                    result.Add(newVariation);
                }
            }
        }
        catch (Exception e)
        {
            Log.Error($"Failed to load presets and variations for {compositionId}: {e.Message}");
            return [];
        }

        return result;
    }

    public void SaveVariationsToFile()
    {
        ResolveVariationFilePaths(SymbolId, out var writableFilePath, out _);
        if (!TrySaveVariationsToFile(writableFilePath, SymbolId, UserVariations))
            Log.Error($"Failed to save presets and variations for {SymbolId}");
    }

    internal static bool TrySaveVariationsToFile(string filePath, Guid symbolId, IReadOnlyList<Variation> variations)
    {
        using var sw = new StringWriter();
        using var writer = new JsonTextWriter(sw);

        try
        {
            writer.Formatting = Formatting.Indented;
            writer.WriteStartObject();

            writer.WriteValue("Id", symbolId);

            writer.WritePropertyName("Variations");
            writer.WriteStartArray();

            foreach (var variation in variations)
            {
                variation.ToJson(writer);
            }

            writer.WriteEndArray();

            writer.WriteEndObject();
        }
        catch (Exception e)
        {
            Log.Error($"Json variation serialization failed: {e.Message}");
            return false;
        }

        try
        {
            var directory = Path.GetDirectoryName(filePath);
            if (directory != null)
                Directory.CreateDirectory(directory);

            File.WriteAllText(filePath, sw.ToString());
            return true;
        }
        catch (Exception e)
        {
            Log.Error($"Failed to save variations file {filePath}: {e.Message}");
            return false;
        }
    }

    internal const string VariationsSubFolder = "Variations";

    /** Pre-4.2 location of all variation files; still the writable location for symbols of read-only packages. */
    internal const string UserOverlaySubFolder = "variations";
    #endregion

    public void Apply(Instance instance, Variation variation)
    {
        StopHover();
        ActiveVariation = variation;

        MacroCommand? newCommand;

        if (variation.IsPreset)
        {
            if (!TryCreateApplyPresetCommand(instance, variation, out newCommand))
                return;
        }
        else
        {
            if (!TryCreateApplyVariationCommand(instance, variation, out newCommand))
                return;
        }

        UpdateActiveStateForVariation(variation.ActivationIndex);
        UndoRedoStack.AddAndExecute(newCommand);
    }

    /// <summary>
    /// Applies a variation's values like <see cref="Apply"/> but without recording an undo step. Used by
    /// procedural drivers (e.g. the [ActivateSnapshot] operator): each activation is automation output,
    /// not a user edit that belongs on the undo stack. The values still persist as the current state.
    /// </summary>
    public void ApplyWithoutUndo(Instance instance, Variation variation)
    {
        StopHover();
        ActiveVariation = variation;

        MacroCommand? newCommand;

        if (variation.IsPreset)
        {
            if (!TryCreateApplyPresetCommand(instance, variation, out newCommand))
                return;
        }
        else
        {
            if (!TryCreateApplyVariationCommand(instance, variation, out newCommand))
                return;
        }

        UpdateActiveStateForVariation(variation.ActivationIndex);
        newCommand.Do();
    }

    /// <summary>
    /// Marks a variation as the active one without applying its values — used when a freshly
    /// created snapshot already equals the current state, so applying would be a no-op command.
    /// </summary>
    public void SetActiveVariationWithoutApply(Variation variation)
    {
        ActiveVariation = variation;
        UpdateActiveStateForVariation(variation.ActivationIndex);
    }

    public void UpdateActiveStateForVariation(int variationIndex)
    {
        foreach (var v in AllVariations)
        {
            v.State = v.ActivationIndex == variationIndex ? Variation.States.Active : Variation.States.InActive;
        }

        //variation.State = Variation.States.Active;
    }

    #region live blend weights
    /// <summary>Current live cross-fade weight for a variation (0 when not part of the mix).</summary>
    public float GetBlendWeight(Guid variationId) => _blendWeights.GetValueOrDefault(variationId, 0f);

    public bool HasBlendWeights => _blendWeights.Count > 0;

    /// <summary>
    /// True when the live weight vector is a genuine mix of 2+ variations — i.e. not the resting
    /// single-active state — so it's worth annotating (e.g. on the Variations thumbnails).
    /// </summary>
    public bool IsLiveBlendMix
    {
        get
        {
            var count = 0;
            foreach (var v in _blendWeights.Values)
            {
                if (v > 0.001f && ++count > 1)
                    return true;
            }

            return false;
        }
    }

    /// <summary>
    /// True when this variation holds the entire weight (the lone 100%) — its fader can't be reduced
    /// directly because there's nowhere to hand the weight to; another fader has to be raised first.
    /// </summary>
    public bool IsSoleFullWeight(Guid variationId)
    {
        if (GetBlendWeight(variationId) < 0.999f)
            return false;

        foreach (var (k, v) in _blendWeights)
        {
            if (k != variationId && v > 0.001f)
                return false;
        }

        return true;
    }

    /// <summary>Collapse the live weight vector to a single fully-active variation (empty clears it).</summary>
    public void ResetBlendWeights(Guid activeVariationId)
    {
        _blendWeights.Clear();
        if (activeVariationId != Guid.Empty)
            _blendWeights[activeVariationId] = 1f;
    }

    /// <summary>
    /// Snapshot the weight vector at the start of a fader drag. The dragged fader then cross-fades
    /// against these starting ratios, so it can be dragged to 100% and back without losing the
    /// sources it faded out.
    /// </summary>
    public void BeginBlendWeightDrag()
    {
        _dragStartWeights.Clear();
        foreach (var (k, v) in _blendWeights)
            _dragStartWeights[k] = v;
    }

    /// <summary>True if this variation was a source (non-zero) at the start of the current drag.</summary>
    public bool IsDragSource(Guid variationId)
        => _dragStartWeights.TryGetValue(variationId, out var w) && w > 0.001f;

    /// <summary>
    /// Sets one variation's cross-fade weight. Normalized (the rest rebalance proportionally so the
    /// vector sums to 1) unless <paramref name="free"/> drops the clamp, leaving weights independent
    /// and free to exceed 1. The sole non-zero weight can't be reduced — there's nowhere to send it.
    /// </summary>
    public void SetBlendWeight(Guid variationId, float newWeight, bool free)
    {
        if (free)
        {
            _blendWeights[variationId] = newWeight < 0 ? 0 : newWeight;
            return;
        }

        newWeight = newWeight < 0 ? 0 : (newWeight > 1 ? 1 : newWeight);

        // Cross-fade against the *other faders' ratio at the start of this drag* (captured by
        // BeginBlendWeightDrag), not their current values — so dragging to 100% and back refills the
        // sources instead of stranding them at 0.
        var sumStartOthers = 0f;
        foreach (var (k, v) in _dragStartWeights)
        {
            if (k != variationId)
                sumStartOthers += v;
        }

        if (sumStartOthers <= 0.0001f)
        {
            if (_dragStartWeights.GetValueOrDefault(variationId, 0f) < 0.999f)
            {
                // Nothing else in the mix and this fader wasn't full at drag start — the remainder
                // is the current (unsaved) parameter state, so the weight scales freely from it.
                _blendWeights.Clear();
                if (newWeight > 0.0001f)
                    _blendWeights[variationId] = newWeight;
                return;
            }

            // No source to fade back to — keep the lone fader full.
            _blendWeights.Clear();
            _blendWeights[variationId] = 1f;
            return;
        }

        var budget = 1f - newWeight;
        _blendWeights.Clear();
        foreach (var (k, v) in _dragStartWeights)
        {
            if (k == variationId || v <= 0.0001f)
                continue;

            var scaled = v / sumStartOthers * budget;
            if (scaled > 0.0001f)
                _blendWeights[k] = scaled;
        }

        if (newWeight > 0.0001f)
            _blendWeights[variationId] = newWeight;
    }

    /// <summary>The variation carrying the largest live weight, or null when the vector is empty.</summary>
    public Variation? GetDominantBlendVariation()
    {
        var bestId = Guid.Empty;
        var best = 0f;
        foreach (var (k, v) in _blendWeights)
        {
            if (v > best)
            {
                best = v;
                bestId = k;
            }
        }

        if (bestId == Guid.Empty)
            return null;

        foreach (var v in _allVariations)
        {
            if (v.Id == bestId)
                return v;
        }

        return null;
    }

    /// <summary>
    /// Applies the current weight vector as a live weighted blend (preview). Call
    /// <see cref="ApplyCurrentBlend"/> to bake it onto the undo stack.
    /// </summary>
    public void ApplyBlendWeights(Instance instance)
    {
        _weightedBlendVariations.Clear();
        _weightedBlendWeights.Clear();
        foreach (var v in _allVariations)
        {
            var w = GetBlendWeight(v.Id);
            if (w > 0.0001f)
            {
                _weightedBlendVariations.Add(v);
                _weightedBlendWeights.Add(w);
            }
        }

        if (_weightedBlendVariations.Count == 0)
        {
            // Dragged back to zero while blending from the current state — restore it.
            StopHover();
            return;
        }

        // A single partial weight means the remainder of the mix is the current (unsaved)
        // parameter state: blend from it toward the variation instead of applying it fully.
        if (_weightedBlendVariations.Count == 1 && _weightedBlendWeights[0] < 0.999f)
        {
            var variation = _weightedBlendVariations[0];
            if (variation.IsPreset)
            {
                BeginBlendToPresent(instance, variation, _weightedBlendWeights[0]);
            }
            else
            {
                BeginBlendTowardsSnapshot(instance, variation, _weightedBlendWeights[0]);
            }

            return;
        }

        BeginWeightedBlend(instance, _weightedBlendVariations, _weightedBlendWeights);
    }

    /// <summary>
    /// Drives the live cross-fade from a [BlendSnapshots] operator: replaces the weight vector with the
    /// given normalized weights — so the picker faders and thumbnails reflect the procedural mix — and
    /// applies the blend.
    /// </summary>
    public void ApplyOperatorDrivenBlend(Instance instance, List<Variation> variations, List<float> normalizedWeights)
    {
        _blendWeights.Clear();
        for (var i = 0; i < variations.Count && i < normalizedWeights.Count; i++)
            _blendWeights[variations[i].Id] = normalizedWeights[i];

        ApplyBlendWeights(instance);
    }

    private void DropBlendWeight(Guid variationId)
    {
        if (!_blendWeights.Remove(variationId))
            return;

        // Renormalize the remainder so the cross-fade still sums to 1.
        var sum = 0f;
        foreach (var v in _blendWeights.Values)
            sum += v;

        if (sum <= 0.0001f)
            return;

        _blendWeightKeyBuffer.Clear();
        foreach (var k in _blendWeights.Keys)
            _blendWeightKeyBuffer.Add(k);
        foreach (var k in _blendWeightKeyBuffer)
            _blendWeights[k] /= sum;
    }
    #endregion

    public void BeginHover(Instance instance, Variation variation)
    {
        StopHover();

        MacroCommand? newCommand;

        if (variation.IsPreset)
        {
            if (!TryCreateApplyPresetCommand(instance, variation, out newCommand))
                return;
        }
        else
        {
            if (!TryCreateApplyVariationCommand(instance, variation, out newCommand))
                return;
        }

        RememberModificationFlagForPreview(instance, variation.IsPreset);
        _activeBlendCommand = newCommand;
        newCommand.Do();
    }

    public void BeginBlendToPresent(Instance instance, Variation variation, float blend)
    {
        StopHover();

        if (!TryCreateBlendToPresetCommand(instance, variation, blend, out var macroCommand))
            return;

        RememberModificationFlagForPreview(instance, isPreset: true);
        _activeBlendCommand = macroCommand;
        _activeBlendCommand.Do();
    }

    public void BeginBlendTowardsSnapshot(Instance instance, Variation variation, float blend)
    {
        StopHover();

        if (TryCreateBlendTowardsVariationCommand(instance, variation, blend, out var newMacroCommand))
        {
            RememberModificationFlagForPreview(instance, isPreset: false);
            _activeBlendCommand = newMacroCommand;
            _activeBlendCommand.Do();
        }

        // we dont need this
        //UpdateActiveStateForVariation(variation.ActivationIndex);
    }

    public void BeginWeightedBlend(Instance instance, List<Variation> variations, IEnumerable<float> weights)
    {
        StopHover();

        if (variations.Count == 0)
            return;

        var countPresets = 0;
        var countSnapshots = 0;
        foreach (var s in variations)
        {
            if (s.IsPreset)
            {
                countPresets++;
            }
            else
            {
                countSnapshots++;
            }
        }

        if (countSnapshots == variations.Count && countPresets == 0)
        {
            RememberModificationFlagForPreview(instance, isPreset: false);
            _activeBlendCommand = CreateWeightedBlendSnapshotCommand(instance, variations, weights);
            _activeBlendCommand?.Do();
        }
        else if (countPresets == variations.Count && countSnapshots == 0)
        {
            if (CreateWeightedBlendPresetCommand(instance, variations, weights, out var newMacroCommand))
            {
                RememberModificationFlagForPreview(instance, isPreset: true);
                _activeBlendCommand = newMacroCommand;
                newMacroCommand.Do();
            }
            else
            {
                _activeBlendCommand = null;
            }
        }
        else
        {
            Log.Error($"Can't mix {countPresets} presets and {countSnapshots} snapshots for weighted blending.");
        }
    }

    public void ApplyCurrentBlend()
    {
        if (_activeBlendCommand != null)
            UndoRedoStack.Add(_activeBlendCommand);

        _activeBlendCommand = null;
        _cleanSymbolIdBeforePreview = Guid.Empty;
    }

    public void StopHover()
    {
        if (_activeBlendCommand == null)
        {
            return;
        }

        _activeBlendCommand.Undo();
        _activeBlendCommand = null;
        RestoreModificationFlagAfterPreview();
    }

    /// <summary>
    /// Save non-default parameters of single selected InstanceAccess as preset for its Symbol.  
    /// </summary>
    public Variation CreatePresetForInstanceSymbol(Instance instance)
    {
        var changes = new Dictionary<Guid, InputValue>();
        var symbolUi = instance.Symbol.GetSymbolUi();

        foreach (var input in instance.Inputs)
        {
            if (input.Input.IsDefault)
            {
                continue;
            }
            
            if(symbolUi.InputUis.TryGetValue(input.Id, out var inputUi))
            {
                if (inputUi.ExcludedFromPresets)
                {
                    Log.Debug($"Exclude {symbolUi}.{inputUi.InputDefinition.Name} for preset.");
                    continue;
                }
            }
            

            if (ValueUtils.BlendMethods.ContainsKey(input.Input.Value.ValueType))
            {
                changes[input.Id] = input.Input.Value.Clone();
            }
        }

        var newVariation = new Variation
                               {
                                   Id = Guid.NewGuid(),
                                   Title = "untitled",
                                   ActivationIndex = AllVariations.Count + 1, //TODO: First find the highest activation index
                                   IsPreset = true,
                                   PublishedDate = DateTime.Now,
                                   ParameterSetsForChildIds = new Dictionary<Guid, Dictionary<Guid, InputValue>>
                                                                  {
                                                                      [Guid.Empty] = changes
                                                                  },
                               };

        var command = new AddPresetOrVariationCommand(instance.Symbol, newVariation);
        UndoRedoStack.AddAndExecute(command);
        //SaveVariationsToFile();
        return newVariation;
    }

    public bool TryCreateVariationForCompositionInstances(List<Instance> instances, [NotNullWhen(true)] out Variation? newVariation)
        //public Variation CreateVariationForCompositionInstances(List<Instance> instances)
    {
        newVariation = null;

        var changeSets = new Dictionary<Guid, Dictionary<Guid, InputValue>>();
        if (instances == null! || instances.Count == 0)
        {
            Log.Warning("No instances to create variation for");
            return false;
        }

        Symbol? parentSymbol = null;

        foreach (var instance in instances)
        {
            if (instance.Parent == null || instance.Parent.Symbol.Id != SymbolId)
            {
                Log.Error($"InstanceAccess {instance.SymbolChildId} is not a child of VariationPool operator {SymbolId}");
                return false;
            }

            parentSymbol = instance.Parent.Symbol;

            var childUi = instance.GetChildUi();
            var changeSet = new Dictionary<Guid, InputValue>();
            var hasAnimatableParameters = false;

            foreach (var input in instance.Inputs)
            {
                if (!ValueUtils.BlendMethods.ContainsKey(input.Input.Value.ValueType))
                    continue;

                if (childUi != null && !childUi.IsInputIncludedForVariation(input.Id))
                    continue;

                hasAnimatableParameters = true;

                if (input.Input.IsDefault)
                {
                    continue;
                }

                changeSet[input.Id] = input.Input.Value.Clone();
            }

            if (!hasAnimatableParameters)
                continue;

            changeSets[instance.SymbolChildId] = changeSet;
        }

        newVariation = new Variation
                           {
                               Id = Guid.NewGuid(),
                               Title = "untitled",
                               ActivationIndex = AllVariations.Count + 1, //TODO: First find the highest activation index
                               IsPreset = false,
                               PublishedDate = DateTime.Now,
                               ParameterSetsForChildIds = changeSets,
                           };

        var command = new AddPresetOrVariationCommand(parentSymbol, newVariation);
        UndoRedoStack.AddAndExecute(command);
        SaveVariationsToFile();
        return true;
    }

    /// <summary>
    /// The lowest controller index above <paramref name="afterIndex"/> not used by a snapshot.
    /// <paramref name="ignore"/> skips one variation (e.g. the freshly-created one being placed).
    /// </summary>
    public int GetNextFreeActivationIndexAfter(int afterIndex, Variation? ignore = null)
    {
        var index = afterIndex + 1;
        while (IsActivationIndexUsed(index, ignore))
            index++;

        return index;
    }

    private bool IsActivationIndexUsed(int activationIndex, Variation? ignore)
    {
        foreach (var v in _allVariations)
        {
            if (v == ignore || v.IsPreset)
                continue;

            if (v.ActivationIndex == activationIndex)
                return true;
        }

        return false;
    }

    public void UpdateVariationPropertiesForInstances(Variation variation, List<Instance> instances, MacroCommand? collectInto = null)
    {
        if (instances == null! || instances.Count == 0)
        {
            Log.Warning("No instances to create variation for");
            return;
        }

        var newParameterSets = new Dictionary<Guid, Dictionary<Guid, InputValue>>(variation.ParameterSetsForChildIds);

        foreach (var instance in instances)
        {
            Debug.Assert(instance.Parent != null);

            if (instance.Parent.Symbol.Id != SymbolId)
            {
                Log.Error($"Instance {instance.SymbolChildId} is not a child of VariationPool operator {SymbolId}");
                return;
            }

            var childUi = instance.GetChildUi();
            var changeSet = new Dictionary<Guid, InputValue>();
            var hasAnimatableParameters = false;

            foreach (var input in instance.Inputs)
            {
                if (!ValueUtils.BlendMethods.ContainsKey(input.Input.Value.ValueType))
                    continue;

                if (childUi != null && !childUi.IsInputIncludedForVariation(input.Id))
                    continue;

                hasAnimatableParameters = true;

                if (input.Input.IsDefault)
                {
                    continue;
                }

                changeSet[input.Id] = input.Input.Value.Clone();
            }

            if (!hasAnimatableParameters)
                continue;

            newParameterSets[instance.SymbolChildId] = changeSet;
        }

        var command = new UpdateVariationParametersCommand(this, variation, newParameterSets);
        if (collectInto != null)
            collectInto.AddAndExecCommand(command);
        else
            UndoRedoStack.AddAndExecute(command);
    }

    public void DeleteVariation(Variation variation)
    {
        var command = new DeleteVariationCommand(this, variation);
        UndoRedoStack.AddAndExecute(command);
        SaveVariationsToFile();
    }

    public void DeleteVariations(List<Variation> variations)
    {
        var commands = new List<ICommand>();
        foreach (var variation in variations)
        {
            commands.Add(new DeleteVariationCommand(this, variation));
        }

        var newCommand = new MacroCommand("Delete variations", commands);
        UndoRedoStack.AddAndExecute(newCommand);
        SaveVariationsToFile();
    }

    private static bool TryCreateApplyVariationCommand(Instance compositionInstance, Variation variation, [NotNullWhen(true)] out MacroCommand? newMacroCommand)
    {
        newMacroCommand = null;

        var commands = new List<ICommand>();
        var compositionSymbol = compositionInstance.Symbol;

        foreach (var (childId, parameterSets) in variation.ParameterSetsForChildIds)
        {
            if (childId == Guid.Empty)
            {
                Log.Warning("Didn't expect parent-reference id in variation");
                continue;
            }

            if (!compositionInstance.Children.TryGetChildInstance(childId, out var instance))
                continue;

            var symbolChild = instance.SymbolChild;
            var childUi = instance.GetChildUi();

            // SymbolChild would only be null if the instance has no parent - this would only ever happen if the composition
            // erroneously has a non-child instance in its children list

            foreach (var input in symbolChild!.Inputs.Values)
            {
                if (!ValueUtils.BlendMethods.TryGetValue(input.Value.ValueType, out _))
                    continue;

                if (parameterSets.TryGetValue(input.Id, out var param))
                {
                    // if (param == null)
                    //     continue;

                    var newCommand = new ChangeInputValueCommand(compositionSymbol, childId, input, param);
                    commands.Add(newCommand);
                }
                else if (childUi == null || childUi.IsInputIncludedForVariation(input.Id))
                {
                    // Reset non-defaults, but leave parameters alone that are not snapshot-controlled
                    commands.Add(new ResetInputToDefault(compositionSymbol, childId, input));
                }
            }
        }

        newMacroCommand = new MacroCommand("Apply Variation Values", commands);
        return true;
    }

    private static MacroCommand CreateWeightedBlendSnapshotCommand(Instance compositionInstance, List<Variation> variations, IEnumerable<float> weights)
    {
        var commands = new List<ICommand>();
        var parentSymbol = compositionInstance.Symbol;
        var weightsArray = weights.ToArray();

        // Collect instances
        var affectedInstances = new HashSet<Guid>();
        foreach (var v in variations)
        {
            affectedInstances.UnionWith(v.ParameterSetsForChildIds.Keys);
        }

        foreach (var childId2 in affectedInstances)
        {
            if (!compositionInstance.Children.TryGetChildInstance(childId2, out var instance))
                continue;

            // Collect variation parameters
            var variationParameterSets = new List<Dictionary<Guid, InputValue>>();
            foreach (var variation in variations)
            {
                if (variation.ParameterSetsForChildIds.TryGetValue(childId2, out var parameterSet))
                {
                    variationParameterSets.Add(parameterSet);
                }
            }

            foreach (var inputSlot in instance.Inputs)
            {
                if (!ValueUtils.WeightedBlendMethods.TryGetValue(inputSlot.Input.DefaultValue.ValueType, out var blendFunction))
                    continue;

                var values = new List<InputValue>();
                var definedForSome = false;

                foreach (var parametersForInputs in variationParameterSets)
                {
                    if (parametersForInputs.TryGetValue(inputSlot.Id, out var paramValue))
                    {

                        values.Add(paramValue);
                        definedForSome = true;
                    }
                    else
                    {
                        values.Add(inputSlot.Input.DefaultValue);
                    }
                }

                if (definedForSome && weightsArray.Length == values.Count)
                {
                    var mixed2 = blendFunction(values.ToArray(), weightsArray);
                    var newCommand = new ChangeInputValueCommand(parentSymbol, instance.SymbolChildId, inputSlot.Input, mixed2);
                    commands.Add(newCommand);
                }
            }
        }

        var activeBlendCommand = new MacroCommand("Set Blended Snapshot Values", commands);
        return activeBlendCommand;
    }

    private static bool TryCreateBlendTowardsVariationCommand(Instance compositionInstance, Variation variation, float blend,
                                                              [NotNullWhen(true)] out MacroCommand? newMacroCommand)
    {
        newMacroCommand = null;

        var commands = new List<ICommand>();
        if (!variation.IsSnapshot)
            return false;

        foreach (var child in compositionInstance.Children.Values)
        {
            if (!variation.ParameterSetsForChildIds.TryGetValue(child.SymbolChildId, out var parametersForInputs))
                continue;

            var childUi = child.GetChildUi();

            foreach (var inputSlot in child.Inputs)
            {
                if (!ValueUtils.BlendMethods.TryGetValue(inputSlot.Input.DefaultValue.ValueType, out var blendFunction))
                    continue;

                if (parametersForInputs.TryGetValue(inputSlot.Id, out var parameter))
                {

                    var mixed = blendFunction(inputSlot.Input.Value, parameter, blend);
                    var newCommand = new ChangeInputValueCommand(compositionInstance.Symbol, child.SymbolChildId, inputSlot.Input, mixed);
                    commands.Add(newCommand);
                }
                else if (!inputSlot.Input.IsDefault
                         && (childUi == null || childUi.IsInputIncludedForVariation(inputSlot.Id)))
                {
                    var mixed = blendFunction(inputSlot.Input.Value, inputSlot.Input.DefaultValue, blend);
                    var newCommand = new ChangeInputValueCommand(compositionInstance.Symbol, child.SymbolChildId, inputSlot.Input, mixed);
                    commands.Add(newCommand);
                }
            }
        }

        newMacroCommand = new MacroCommand("Blend towards snapshot", commands);
        return true;
    }

    private static bool TryCreateApplyPresetCommand(Instance instance, Variation variation, [NotNullWhen(true)] out MacroCommand? newMacroCommand)
    {
        newMacroCommand = null;
        if (instance.Parent == null)
            return false;

        const string commandName = "Apply Preset Values";
        if (!instance.Parent.Symbol.Children.ContainsKey(instance.SymbolChildId))
        {
            newMacroCommand = new MacroCommand(commandName, Array.Empty<ICommand>());
            return true;
        }

        var commands = new List<ICommand>();

        foreach (var (childId, parametersForInputs) in variation.ParameterSetsForChildIds)
        {
            if (childId != Guid.Empty)
            {
                Log.Warning("Didn't expect childId in preset");
                continue;
            }

            foreach (var inputSlot in instance.Inputs)
            {
                if (!ValueUtils.BlendMethods.ContainsKey(inputSlot.ValueType))
                    continue;

                if (parametersForInputs.TryGetValue(inputSlot.Id, out var paramValue))
                {

                    var newCommand = new ChangeInputValueCommand(instance.Parent.Symbol, instance.SymbolChildId, inputSlot.Input, paramValue);
                    commands.Add(newCommand);
                }
                else
                {
                    // ResetOtherNonDefaults
                    commands.Add(new ResetInputToDefault(instance.Parent.Symbol, instance.SymbolChildId, inputSlot.Input));
                }
            }
        }

        newMacroCommand = new MacroCommand(commandName, commands);
        return true;
    }

    private static bool TryCreateBlendToPresetCommand(Instance instance, Variation variation, float blend,
                                                      [NotNullWhen(true)] out MacroCommand? newMacroCommand)
    {
        newMacroCommand = null;
        var commands = new List<ICommand>();
        if (instance.Parent == null)
            return false;

        var parentSymbol = instance.Parent.Symbol;

        if (parentSymbol.Children.ContainsKey(instance.SymbolChildId))
        {
            foreach (var (childId, parametersForInputs) in variation.ParameterSetsForChildIds)
            {
                if (childId != Guid.Empty)
                {
                    Log.Warning("Didn't expect childId in preset");
                    continue;
                }

                foreach (var inputSlot in instance.Inputs)
                {
                    if (!ValueUtils.BlendMethods.TryGetValue(inputSlot.Input.DefaultValue.ValueType, out var blendFunction))
                        continue;

                    if (parametersForInputs.TryGetValue(inputSlot.Id, out var parameter))
                    {
                        // TODO:
                        if (parameter == null)
                            continue;

                        var mixed = blendFunction(inputSlot.Input.Value, parameter, blend);
                        var newCommand = new ChangeInputValueCommand(parentSymbol, instance.SymbolChildId, inputSlot.Input, mixed);
                        commands.Add(newCommand);
                    }
                    else if (!inputSlot.Input.IsDefault)
                    {
                        var mixed = blendFunction(inputSlot.Input.Value, inputSlot.Input.DefaultValue, blend);
                        var newCommand = new ChangeInputValueCommand(parentSymbol, instance.SymbolChildId, inputSlot.Input, mixed);
                        commands.Add(newCommand);
                    }
                }
            }
        }

        newMacroCommand = new MacroCommand("Set Preset Values", commands);
        return true;
    }

    private static bool CreateWeightedBlendPresetCommand(Instance instance, List<Variation> variations, IEnumerable<float> weights,
                                                         [NotNullWhen(true)] out MacroCommand? newMacroCommand)
    {
        newMacroCommand = null;

        var commands = new List<ICommand>();
        if (instance.Parent == null)
            return false;

        var parentSymbol = instance.Parent.Symbol;
        var weightsArray = weights.ToArray();

        if (parentSymbol.Children.ContainsKey(instance.SymbolChildId))
        {
            // collect variation parameters
            var variationParameterSets = new List<Dictionary<Guid, InputValue>>();
            foreach (var variation in variations)
            {
                foreach (var (childId, parametersForInputs) in variation.ParameterSetsForChildIds)
                {
                    if (childId != Guid.Empty)
                    {
                        Log.Warning("Didn't expect childId in preset");
                        continue;
                    }

                    variationParameterSets.Add(parametersForInputs);
                }
            }

            foreach (var inputSlot in instance.Inputs)
            {
                if (!ValueUtils.WeightedBlendMethods.TryGetValue(inputSlot.Input.DefaultValue.ValueType, out var blendFunction))
                    continue;

                var values = new List<InputValue>();
                var definedForSome = false;

                foreach (var parametersForInputs in variationParameterSets)
                {
                    if (parametersForInputs.TryGetValue(inputSlot.Id, out var parameterValue))
                    {
                        // if (parameterValue == null)
                        //     continue;

                        values.Add(parameterValue);
                        definedForSome = true;
                    }
                    else
                    {
                        values.Add(inputSlot.Input.DefaultValue);
                    }
                }

                if (definedForSome && weightsArray.Length == values.Count)
                {
                    var mixed2 = blendFunction(values.ToArray(), weightsArray);
                    var newCommand = new ChangeInputValueCommand(parentSymbol, instance.SymbolChildId, inputSlot.Input, mixed2);
                    commands.Add(newCommand);
                }
            }
        }

        newMacroCommand = new MacroCommand("Set Blended Preset Values", commands);
        return true;
    }

    /// <summary>True when the instance's current parameter values still match the preset's stored values.</summary>
    public static bool DoesInstanceMatchPreset(Instance instance, Variation preset)
        => DoesPresetVariationMatch(preset, instance) != MatchTypes.NoMatch;

    /// <summary>
    /// True when the preset fully describes the current state: its stored values match AND every other
    /// preset-eligible parameter is at default. Stricter than <see cref="DoesInstanceMatchPreset"/>,
    /// which tolerates additional non-default parameters.
    /// </summary>
    public static bool DoesInstanceMatchPresetExactly(Instance instance, Variation preset)
        => DoesPresetVariationMatch(preset, instance) == MatchTypes.PresetAndDefaultParamsMatch;

    private static MatchTypes DoesPresetVariationMatch(Variation variation, Instance instance)
    {
        var setCorrectly = true;
        var foundOneMatch = false;
        var foundUnknownNonDefaults = false;
        var symbolUi = instance.Symbol.GetSymbolUi();

        foreach (var (symbolChildId, values) in variation.ParameterSetsForChildIds)
        {
            if (symbolChildId != Guid.Empty)
                continue;

            foreach (var input in instance.Inputs)
            {
                var inputIsDefault = input.Input.IsDefault;
                var variationIncludesInput = values.ContainsKey(input.Id);

                if (!ValueUtils.CompareFunctions.TryGetValue(input.ValueType, out var function))
                    continue;

                if (variationIncludesInput)
                {
                    foundOneMatch = true;

                    if (inputIsDefault)
                    {
                        setCorrectly = false;
                    }
                    else
                    {
                        var inputValueMatches = function(values[input.Id], input.Input.Value);
                        setCorrectly &= inputValueMatches;
                    }
                }
                else
                {
                    if (inputIsDefault)
                        continue;

                    // Mirror the eligibility filter of CreatePresetForInstanceSymbol: inputs that can
                    // never be stored in a preset must not count as drift.
                    if (!ValueUtils.BlendMethods.ContainsKey(input.ValueType))
                        continue;

                    if (symbolUi.InputUis.TryGetValue(input.Id, out var inputUi) && inputUi.ExcludedFromPresets)
                        continue;

                    foundUnknownNonDefaults = true;
                }
            }
        }

        if (!foundOneMatch || !setCorrectly)
        {
            return MatchTypes.NoMatch;
        }

        return foundUnknownNonDefaults ? MatchTypes.PresetParamsMatch : MatchTypes.PresetAndDefaultParamsMatch;
    }

    private enum MatchTypes
    {
        NoMatch,
        PresetParamsMatch,
        PresetAndDefaultParamsMatch,
    }

    public static bool TryGetSnapshot(int activationIndex, [NotNullWhen(true)] out Variation? variation)
    {
        variation = null;
        if (VariationHandling.ActivePoolForSnapshots == null)
            return false;

        foreach (var v in VariationHandling.ActivePoolForSnapshots.AllVariations)
        {
            if (v.ActivationIndex != activationIndex)
                continue;

            variation = v;
            return true;
        }

        return false;
    }

    public void AddUserVariation(Variation newVariation)
    {
        _userVariations.Add(newVariation);
        _allVariations.Add(newVariation);
    }

    public void RemoveUserVariation(Variation newVariation)
    {
        _userVariations.Remove(newVariation);
        _allVariations.Remove(newVariation);
        DropBlendWeight(newVariation.Id);

        // Keep the active reference from dangling (e.g. undoing a preset creation or deleting the active one)
        if (_activeVariation == newVariation)
            ActiveVariation = null;
    }

    /// <summary>
    /// Preview commands (hover, thumbnail rendering, live blends) flag the composition's SymbolUi as
    /// modified on both Do and Undo. Since <see cref="StopHover"/> fully reverts the values, restore a
    /// previously clean flag — otherwise merely previewing marks read-only symbols as modified and
    /// triggers the save-as-copy dialog when leaving them.
    /// </summary>
    private void RememberModificationFlagForPreview(Instance instance, bool isPreset)
    {
        _cleanSymbolIdBeforePreview = Guid.Empty;

        var compositionSymbol = isPreset ? instance.Parent?.Symbol : instance.Symbol;
        if (compositionSymbol == null)
            return;

        if (!SymbolUiRegistry.TryGetSymbolUi(compositionSymbol.Id, out var symbolUi))
            return;

        if (!symbolUi.HasBeenModified)
            _cleanSymbolIdBeforePreview = compositionSymbol.Id;
    }

    private void RestoreModificationFlagAfterPreview()
    {
        if (_cleanSymbolIdBeforePreview == Guid.Empty)
            return;

        if (SymbolUiRegistry.TryGetSymbolUi(_cleanSymbolIdBeforePreview, out var symbolUi))
            symbolUi.ClearModifiedFlag();

        _cleanSymbolIdBeforePreview = Guid.Empty;
    }

    private MacroCommand? _activeBlendCommand;

    /// <summary>Id of the composition symbol that was unmodified when the current preview began.</summary>
    private Guid _cleanSymbolIdBeforePreview;
}