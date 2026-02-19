#nullable enable
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using T3.Core.Model;
using T3.Core.Operator;
using T3.Core.Operator.Slots;
using T3.Core.UserData;
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

    public Variation? ActiveVariation { get; private set; }

    public SymbolVariationPool(Guid symbolId)
    {
        SymbolId = symbolId;
        _userVariations = LoadVariations(symbolId, UserData.UserDataLocation.User);
        _defaults = LoadVariations(symbolId, UserData.UserDataLocation.Defaults);
        _allVariations = new List<Variation>(_userVariations.Count + _defaults.Count);
        _allVariations.AddRange(_userVariations);
        _allVariations.AddRange(_defaults);

        UserVariations = _userVariations;
        Defaults = _defaults;
        AllVariations = _allVariations;
    }

    #region serialization
    private static List<Variation> LoadVariations(Guid compositionId, UserData.UserDataLocation location)
    {
        var relativePath = GetFilePathForVariationId(compositionId);
        var loaded = UserData.TryLoad(relativePath, location, out var fileContent, out _);
        if (!loaded)
            return [];

        //Log.Info($"Reading presets definition for : {compositionId}");

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
        // FIXME: Unclear after merge: verify if this is done implicitly by SaveVariationsToFile()
        // CreateFolderIfNotExists(UserData.UserDataLocation.User);

        SaveVariationsToFile(UserData.UserDataLocation.User);

        #if DEBUG
            SaveVariationsToFile(UserData.UserDataLocation.Defaults);
        #endif
    }

    private void SaveVariationsToFile(UserData.UserDataLocation location)
    {
        var relativePath = GetFilePathForVariationId(SymbolId);

        using var sw = new StringWriter();
        using var writer = new JsonTextWriter(sw);
        var variationCollection = location == UserData.UserDataLocation.User ? UserVariations : Defaults;

        try
        {
            writer.Formatting = Formatting.Indented;
            writer.WriteStartObject();

            writer.WriteValue("Id", SymbolId);

            writer.WritePropertyName("Variations");
            writer.WriteStartArray();

            foreach (var variation in variationCollection)
            {
                variation.ToJson(writer);
            }

            writer.WriteEndArray();

            writer.WriteEndObject();
        }
        catch (Exception e)
        {
            Log.Error($"Json variation serialization failed: {e.Message}");
        }

        if (!UserData.TrySave(relativePath, sw.ToString(), location))
            Log.Error($"Failed to save presets and variations for {SymbolId}");
    }

    private const string VariationsSubFolder = "variations";

    private static string GetFilePathForVariationId(Guid compositionId) => Path.Combine(VariationsSubFolder, $"{compositionId}.var");
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

    public void UpdateActiveStateForVariation(int variationIndex)
    {
        foreach (var v in AllVariations)
        {
            v.State = v.ActivationIndex == variationIndex ? Variation.States.Active : Variation.States.InActive;
        }

        //variation.State = Variation.States.Active;
    }

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

        _activeBlendCommand = newCommand;
        newCommand.Do();
    }

    public void BeginBlendToPresent(Instance instance, Variation variation, float blend)
    {
        StopHover();

        if (!TryCreateBlendToPresetCommand(instance, variation, blend, out var macroCommand))
            return;

        _activeBlendCommand = macroCommand;
        _activeBlendCommand.Do();
    }

    public void BeginBlendTowardsSnapshot(Instance instance, Variation variation, float blend)
    {
        StopHover();

        if (TryCreateBlendTowardsVariationCommand(instance, variation, blend, out var newMacroCommand))
        {
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
            _activeBlendCommand = CreateWeightedBlendSnapshotCommand(instance, variations, weights);
            _activeBlendCommand?.Do();
        }
        else if (countPresets == variations.Count && countSnapshots == 0)
        {
            if (CreateWeightedBlendPresetCommand(instance, variations, weights, out var newMacroCommand))
            {
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
    }

    public void StopHover()
    {
        if (_activeBlendCommand == null)
        {
            return;
        }

        _activeBlendCommand.Undo();
        _activeBlendCommand = null;
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

            var changeSet = new Dictionary<Guid, InputValue>();
            var hasAnimatableParameters = false;

            foreach (var input in instance.Inputs)
            {
                if (!ValueUtils.BlendMethods.ContainsKey(input.Input.Value.ValueType))
                    continue;

                hasAnimatableParameters = true;

                if (input.Input.IsDefault)
                {
                    continue;
                }

                if (ValueUtils.BlendMethods.ContainsKey(input.Input.Value.ValueType))
                {
                    changeSet[input.Id] = input.Input.Value.Clone();
                }
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

    public void UpdateVariationPropertiesForInstances(Variation variation, List<Instance> instances)
    {
        if (instances == null! || instances.Count == 0)
        {
            Log.Warning("No instances to create variation for");
            return;
        }

        foreach (var instance in instances)
        {
            Debug.Assert(instance.Parent != null);

            if (instance.Parent.Symbol.Id != SymbolId)
            {
                Log.Error($"Instance {instance.SymbolChildId} is not a child of VariationPool operator {SymbolId}");
                return;
            }

            var changeSet = new Dictionary<Guid, InputValue>();
            var hasAnimatableParameters = false;

            foreach (var input in instance.Inputs)
            {
                if (!ValueUtils.BlendMethods.ContainsKey(input.Input.Value.ValueType))
                    continue;

                hasAnimatableParameters = true;

                if (input.Input.IsDefault)
                {
                    continue;
                }

                if (ValueUtils.BlendMethods.ContainsKey(input.Input.Value.ValueType))
                {
                    changeSet[input.Id] = input.Input.Value.Clone();
                }
            }

            if (!hasAnimatableParameters)
                continue;

            // Write new changeset
            variation.ParameterSetsForChildIds[instance.SymbolChildId] = changeSet;
        }

        SaveVariationsToFile();
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
                else
                {
                    // Reset non-defaults
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
                else if (!inputSlot.Input.IsDefault)
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

    private static MatchTypes DoesPresetVariationMatch(Variation variation, Instance instance)
    {
        var setCorrectly = true;
        var foundOneMatch = false;
        var foundUnknownNonDefaults = false;

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
                    {
                    }
                    else
                    {
                        foundUnknownNonDefaults = true;
                    }
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

    public void AddDefaultVariation(Variation newVariation)
    {
        _defaults.Add(newVariation);
        _allVariations.Add(newVariation);
    }

    public void RemoveDefaultVariation(Variation newVariation)
    {
        _defaults.Remove(newVariation);
        _allVariations.Remove(newVariation);
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
    }

    private MacroCommand? _activeBlendCommand;
}