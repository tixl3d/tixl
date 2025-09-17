#nullable enable
using System.Diagnostics.CodeAnalysis;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using T3.Core.Model;
using T3.Core.Operator;
using T3.Core.Operator.Slots;
using T3.Editor.Gui.Windows.Variations;
using T3.Editor.UiModel;
using T3.Editor.UiModel.Selection;
using T3.Serialization;

namespace T3.Editor.Gui.Interaction.Variations.Model;

/// <summary>
/// Base class for serialization of presents, snapshots and ParameterCollection-Scenes
/// </summary>/
public sealed class Variation : ISelectableCanvasObject
{
    // Serialized fields...
    public Guid Id { get; init; }
    public string? Title;
    public int ActivationIndex;
    public bool IsPreset;
    
    public Vector2 PosOnCanvas { get; set; }
    public Vector2 Size { get; set; } = VariationThumbnail.ThumbnailSize;
    internal DateTime PublishedDate;
    
    // Other properties...
    internal bool IsSelected { get; set; }
    internal States State { get; set; } = States.InActive;
    internal bool IsSnapshot => !IsPreset;
    
    /// <summary>
    /// Changes by SymbolChildId
    /// </summary>
    public Dictionary<Guid, Dictionary<Guid, InputValue>> ParameterSetsForChildIds = [];
    
    public Variation Clone()
    {
        return new Variation()
                   {
                       Id = Id,
                       Title = Title,
                       ActivationIndex = ActivationIndex,
                       IsPreset = IsPreset,
                       PosOnCanvas = PosOnCanvas,
                       Size = Size,
                       PublishedDate = PublishedDate,
                       IsSelected = IsSelected,
                       State = State,
                       ParameterSetsForChildIds =
                           ParameterSetsForChildIds
                              .ToDictionary(kv => kv.Key,
                                            kv =>
                                                kv.Value.ToDictionary(kv2 => kv2.Key,
                                                                      kv2 => kv2.Value.Clone())),
                   };
    }

    internal static bool TryLoadVariationFromJson(Guid symbolId, JToken jToken, [NotNullWhen(true)] out Variation? variation)
    {
        variation = null;
        if (!SymbolUiRegistry.TryGetSymbolUi(symbolId, out var compositionSymbolUi))
            return false;
        
        var compositionSymbol = compositionSymbolUi.Symbol;

        if (!JsonUtils.TryGetGuid(jToken[nameof(Id)], out var variationId))
        {
            Log.Warning(" Can't find or parse variationId");
            return false;
        }
        
        variation = new Variation
                               {
                                   Id = variationId,
                                   Title = jToken[nameof(Title)]?.Value<string>() ?? String.Empty,
                                   ActivationIndex = jToken[nameof(ActivationIndex)]?.Value<int>() ?? -1,
                                   IsPreset = jToken[nameof(IsPreset)]?.Value<bool>() ?? false,
                                   ParameterSetsForChildIds = new Dictionary<Guid, Dictionary<Guid, InputValue>>(),
                               };
        
        var positionToken = jToken[nameof(PosOnCanvas)];
        if (positionToken != null)
        {
            variation.PosOnCanvas = new Vector2(positionToken["X"]?.Value<float>() ?? 0,
                                                   positionToken["Y"]?.Value<float>() ?? 0);
        }

        if(jToken[nameof(ParameterSetsForChildIds)] is not JObject changesToken)
            return false;
        
        foreach (var (symbolChildIdString, changes2) in changesToken)
        {
            var changeList = new Dictionary<Guid, InputValue>();
            if (changes2 is not JObject o)
                continue;

            if (!JsonUtils.TryGetGuid(symbolChildIdString, out var noneOrSymbolChildId))
            {
                Log.Warning($"Can't load presets: invalid symbol ID '{symbolChildIdString}'");
                continue;
            }
            
            Symbol symbolForChanges;
            
            if (noneOrSymbolChildId == _idWhenUsingCompositionSymbol)
            {
                symbolForChanges = compositionSymbol;
            }
            else if (compositionSymbol.Children.TryGetValue(noneOrSymbolChildId, out var symbolChild))
            {
                symbolForChanges = symbolChild.Symbol;
            }
            else
            {
                //Log.Warning($"Can't find symbol {symbolChildIdString} for preset changes");
                continue;
            }
            
            var symbolUi = symbolForChanges.GetSymbolUi();
            
            foreach (var (inputIdString, valueToken) in o)
            {
                if (!JsonUtils.TryGetGuid(inputIdString, out var inputId))
                {
                    Log.Warning($"Can't load presets: Invalid ID '{changeList}' in {symbolChildIdString}");
                    continue;
                }
                
                var input = symbolForChanges.InputDefinitions.SingleOrDefault(i => i.Id == inputId);
                
                if (input == null && inputId != Guid.Empty)
                {
                    //Log.Warning($"Can't load presets: input {symbolChildIdString}/{inputId} not found");
                    continue;
                }

                if (input == null)
                {
                    Log.Warning("Can't find input?");
                    continue;
                }

                if (symbolUi.InputUis.TryGetValue(inputId, out var inputUi))
                {
                    if (inputUi.ExcludedFromPresets)
                    {
                        //Log.Warning($"Skipping input {symbolUi}.{inputUi} excluded from presets.");
                        continue;
                    }
                }
                
                var inputValue = InputValueCreators.Entries[input.DefaultValue.ValueType]();
                inputValue.SetValueFromJson(valueToken);
                changeList[inputId] = inputValue;
            }
            
            if (changeList.Count > 0)
            {
                variation.ParameterSetsForChildIds[noneOrSymbolChildId] = changeList;
            }
        }
        
        return true;
    }

    private static readonly Guid _idWhenUsingCompositionSymbol = Guid.Empty; 

    internal void ToJson(JsonTextWriter writer)
    {
        var vec2Writer = TypeValueToJsonConverters.Entries[typeof(Vector2)];
        
        //writer.WritePropertyName(Id.ToString());
        writer.WriteStartObject();
        {
            writer.WriteValue(nameof(Id), Id);
            writer.WriteValue(nameof(IsPreset), IsPreset);
            writer.WriteValue(nameof(ActivationIndex), ActivationIndex);
            
            if(!string.IsNullOrEmpty(Title))
                writer.WriteObject(nameof(Title), Title);
            
            writer.WritePropertyName(nameof(PosOnCanvas));
            vec2Writer(writer, PosOnCanvas);
            
            writer.WritePropertyName(nameof(ParameterSetsForChildIds));
            writer.WriteStartObject();
            {
                foreach (var (id, values) in ParameterSetsForChildIds)
                {
                    if (values.Count <= 0) 
                        continue;
                    
                    writer.WritePropertyName(id.ToString());
                    writer.WriteStartObject();
                    foreach (var (inputId, value) in values)
                    {
                        writer.WritePropertyName(inputId.ToString());
                        value.ToJson(writer);
                    }
                    
                    writer.WriteEndObject();
                }
            }
            writer.WriteEndObject();
        }
        writer.WriteEndObject();
    }
    
    public override string ToString()
    {
        return $"{Title} #{ActivationIndex}";
    }

    internal enum States
    {
        Undefined,
        InActive,
        Active,
        Modified,
        IsBlended,
    }
}