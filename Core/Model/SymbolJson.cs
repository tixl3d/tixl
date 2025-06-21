#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using T3.Core.Logging;
using T3.Core.Operator;
using T3.Core.Operator.Slots;
using T3.Serialization;

// ReSharper disable AssignNullToNotNullAttribute

namespace T3.Core.Model;

public static class SymbolJson
{
    #region writing
    public static void WriteSymbol(Symbol symbol, JsonTextWriter writer)
    {
        writer.WriteStartObject();

        writer.WriteValue(JsonKeys.Id, symbol.Id);
        writer.WriteComment(symbol.Name);

        WriteSymbolInputs(symbol.InputDefinitions, writer);
        WriteSymbolChildren(symbol.Children.Values.OrderBy(x => x.Id), writer);
        WriteConnections(symbol.Connections, writer);
        symbol.PlaybackSettings?.WriteToJson(writer);
        symbol.Animator.Write(writer);

        writer.WriteEndObject();
    }

    private static void WriteSymbolInputs(List<Symbol.InputDefinition> inputs, JsonTextWriter writer)
    {
        writer.WritePropertyName(JsonKeys.Inputs);
        writer.WriteStartArray();

        foreach (var input in inputs.OrderBy(x => x.Id))
        {
            writer.WriteStartObject();
            writer.WriteObject(JsonKeys.Id, input.Id);
            writer.WriteComment(input.Name);
            writer.WritePropertyName(JsonKeys.DefaultValue);
            input.DefaultValue.ToJson(writer);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
    }

    private static void WriteConnections(List<Symbol.Connection> connections, JsonTextWriter writer)
    {
        writer.WritePropertyName(JsonKeys.Connections);
        writer.WriteStartArray();
        foreach (var connection in connections.OrderBy(c => c.TargetParentOrChildId.ToString() + c.TargetSlotId))
        {
            writer.WriteStartObject();
            writer.WriteValue(JsonKeys.SourceParentOrChildId, connection.SourceParentOrChildId);
            writer.WriteValue(JsonKeys.SourceSlotId, connection.SourceSlotId);
            writer.WriteValue(JsonKeys.TargetParentOrChildId, connection.TargetParentOrChildId);
            writer.WriteValue(JsonKeys.TargetSlotId, connection.TargetSlotId);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
    }

    private static void WriteSymbolChildren(IEnumerable<Symbol.Child> children, JsonTextWriter writer)
    {
        writer.WritePropertyName(JsonKeys.Children);
        writer.WriteStartArray();
        foreach (var child in children)
        {
            writer.WriteStartObject();
            writer.WriteValue(JsonKeys.Id, child.Id);
            writer.WriteComment(child.ReadableName);
            if (child.IsBypassed)
            {
                writer.WriteValue(JsonKeys.IsBypassed, child.IsBypassed);
            }

            writer.WriteValue(JsonKeys.SymbolId, child.Symbol.Id);
            if (!string.IsNullOrEmpty(child.Name))
            {
                writer.WriteObject(JsonKeys.SymbolChildName, child.Name);
            }

            writer.WritePropertyName(JsonKeys.InputValues);
            writer.WriteStartArray();
            foreach (var (id, inputValue) in child.Inputs.OrderBy(x => x.Key))
            {
                if (inputValue.IsDefault)
                    continue;

                writer.WriteStartObject();
                writer.WriteValue(JsonKeys.Id, id);
                writer.WriteComment(inputValue.Name);
                writer.WriteObject(JsonKeys.Type, inputValue.Value.ValueType);
                writer.WritePropertyName(JsonKeys.Value);
                inputValue.Value.ToJson(writer);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();

            writer.WritePropertyName(JsonKeys.Outputs);
            writer.WriteStartArray();

            foreach (var (id, output) in child.Outputs)
            {
                if (output.DirtyFlagTrigger != output.OutputDefinition.DirtyFlagTrigger || output.OutputData != null || output.IsDisabled)
                {
                    writer.WriteStartObject();
                    writer.WriteValue(JsonKeys.Id, id);
                    writer.WriteComment(child.ReadableName);

                    if (output.OutputData != null)
                    {
                        writer.WritePropertyName(JsonKeys.OutputData);
                        writer.WriteStartObject();
                        writer.WriteObject(JsonKeys.Type, output.OutputData.DataType);
                        output.OutputData.ToJson(writer);
                        writer.WriteEndObject();
                    }

                    if (output.DirtyFlagTrigger != output.OutputDefinition.DirtyFlagTrigger)
                    {
                        writer.WriteObject(JsonKeys.DirtyFlagTrigger, output.DirtyFlagTrigger);
                    }

                    if (output.IsDisabled)
                    {
                        writer.WriteValue(JsonKeys.IsDisabled, output.IsDisabled);
                    }

                    writer.WriteEndObject();
                }
            }

            writer.WriteEndArray();

            writer.WriteEndObject(); // child
        }

        writer.WriteEndArray();
    }
    #endregion

    #region reading
    public static bool TryReadAndApplySymbolChildren(SymbolReadResult symbolReadResult)
    {
        var childrenJson = symbolReadResult.ChildrenJsonArray;

        // if (childrenJson.Length == 0)
        //     return true;

        var parent = symbolReadResult.Symbol;
        if (parent == null)
            return false;
        
        var result = true;

        foreach (var childJson in childrenJson)
        {
            result &= TryReadSymbolChild(in childJson, parent);
        }

        if (symbolReadResult.AnimatorJsonData != null)
            parent.Animator.Read(symbolReadResult.AnimatorJsonData, parent);

        return result;
    }

    private static bool TryReadSymbolChild(in JsonChildResult childJsonResult, Symbol parent)
    {
        // If the used symbol hasn't been loaded so far ensure it's loaded now
        if (!SymbolRegistry.TryGetSymbol(childJsonResult.SymbolId, out var symbol))
        {
            Log.Warning($"Error loading symbol child {childJsonResult.SymbolId}");
            return false;
        }

        var symbolChildJson = childJsonResult.Json;
        
        var name = symbolChildJson[JsonKeys.SymbolChildName]?.Value<string>();

        var isBypassed = false;
        if (symbolChildJson[JsonKeys.IsBypassed] is { } tt)
            isBypassed = tt.Value<bool>();

        var modifyAction = new Action<Symbol.Child>(child =>
                                                    {
                                                        var jInputValueArray = (JArray?)symbolChildJson[JsonKeys.InputValues];
                                                        if (jInputValueArray != null)
                                                        {
                                                            foreach (var inputValue in jInputValueArray)
                                                            {
                                                                ReadChildInputValue(child, inputValue);
                                                            }
                                                        }

                                                        var jOutputsArray = (JArray?)symbolChildJson[JsonKeys.Outputs];
                                                        if (jOutputsArray != null)
                                                        {
                                                            foreach (var outputJson in jOutputsArray)
                                                            {
                                                                JsonUtils.TryGetGuid(outputJson[JsonKeys.Id], out var outputId);
                                                                var outputDataJson = outputJson[JsonKeys.OutputData];
                                                                if (outputDataJson != null)
                                                                {
                                                                    ReadChildOutputData(child, outputId, outputDataJson);
                                                                }

                                                                var dirtyFlagJson = outputJson[JsonKeys.DirtyFlagTrigger];
                                                                if (dirtyFlagJson != null)
                                                                {
                                                                    if(Enum.TryParse<DirtyFlagTrigger>(dirtyFlagJson.Value<string>(), out var x))
                                                                        child.Outputs[outputId].DirtyFlagTrigger = x;
                                                                        
                                                                    //(DirtyFlagTrigger)Enum.Parse(typeof(DirtyFlagTrigger), readOnlySpan);
                                                                }

                                                                var isDisabledJson = outputJson[JsonKeys.IsDisabled];
                                                                if (isDisabledJson != null)
                                                                {
                                                                    if (child.Outputs.TryGetValue(outputId, out var output))
                                                                        output.IsDisabled = isDisabledJson.Value<bool>();
                                                                }
                                                            }
                                                        }
                                                    });


        parent.AddChild(symbol, childJsonResult.ChildId, name, isBypassed, modifyAction);
        return true;
    }

    private static (Guid, JToken?) ReadSymbolInputDefaults(JToken jsonInput)
    {
        JsonUtils.TryGetGuid(jsonInput[JsonKeys.Id], out var id);
        //var id = Guid.Parse(jsonInput[JsonKeys.Id].Value<string>());
        var jsonValue = jsonInput[JsonKeys.DefaultValue];
        return (id, jsonValue);
    }

    private static bool TryReadConnection(JToken jsonConnection, [NotNullWhen(true)] out Symbol.Connection? connection)
    {
        connection = null;

        if (!JsonUtils.TryGetGuid(jsonConnection[JsonKeys.SourceParentOrChildId], out var sourceInstanceId) ||
            !JsonUtils.TryGetGuid(jsonConnection[JsonKeys.TargetParentOrChildId], out var targetInstanceId) ||
            !JsonUtils.TryGetGuid(jsonConnection[JsonKeys.SourceSlotId], out var sourceSlotId) ||
            !JsonUtils.TryGetGuid(jsonConnection[JsonKeys.TargetSlotId], out var targetSlotId))
        {
            connection = null;
            return false;
        }
        
        connection = new Symbol.Connection(sourceInstanceId, sourceSlotId, targetInstanceId, targetSlotId);
        return true;
    }
    
    private static void ReadChildInputValue(Symbol.Child symbolChild, JToken inputJson)
    {
        JsonUtils.TryGetGuid(inputJson[JsonKeys.Id], out var id);
        var jsonValue = inputJson[JsonKeys.Value];
        var gotInput = symbolChild.Inputs.TryGetValue(id, out var input);
        if (!gotInput || input == null)
        {
            Log.Warning($"Skipping definition of obsolete input in [{symbolChild.Symbol.Name}]: " + id);
            return;
        }

        try
        {
            input.Value.SetValueFromJson(jsonValue);
            input.IsDefault = false;
        }
        catch
        {
            Log.Error($"Failed to read input value ({input.DefaultValue.ValueType}) for {symbolChild.Symbol.Id}: " + jsonValue);
        }
    }

    private static void ReadChildOutputData(Symbol.Child symbolChild, Guid outputId, JToken json)
    {
        if (json[JsonKeys.Type] == null)
            return;

        if (!symbolChild.Outputs.TryGetValue(outputId, out var output))
        {
            Log.Warning("Skipping definition of obsolete output " + outputId);
            return;
        }

        output.OutputData.ReadFromJson(json);
    }

    public static SymbolReadResult ReadSymbolRoot(in Guid id, JToken jToken, Type instanceType, SymbolPackage package)
    {
        // Read symbol with Id -> dictionary of Guid-JToken?
        var jChildrenJsonArray = (JArray?)jToken[JsonKeys.Children];

        JsonChildResult[] childrenJsons = [];
        
        if (jChildrenJsonArray != null)
        {
            childrenJsons = new JsonChildResult[jChildrenJsonArray.Count];
            for (var i = 0; i < jChildrenJsonArray.Count; i++)
            {
                childrenJsons[i] = new JsonChildResult(jChildrenJsonArray[i]);
            }
        }
        
        var connections = new List<Symbol.Connection>();
        var connectionsJson = (JArray?)jToken[JsonKeys.Connections];

        var hasConnections = false;
        if (connectionsJson != null)
        {
            hasConnections = connectionsJson.Count > 0;
            if (hasConnections)
            {
                ObtainConnections(connectionsJson, connections, instanceType);
            }
        }

        var inputJsonArray = (JArray?)jToken[JsonKeys.Inputs];
        var inputDefaults = inputJsonArray?
                           .Select(ReadSymbolInputDefaults)
                           .ToArray();

        var inputDefaultValueTokens = new Dictionary<Guid, JToken>();
        if (inputDefaults != null)
        {
            foreach (var (inputId,valueToken) in inputDefaults)
            {
                if(valueToken !=null)
                    inputDefaultValueTokens[inputId] = valueToken;
            }
        }

        var symbol = package.CreateSymbol(instanceType, id);

        if (hasConnections)
            symbol.Connections.AddRange(connections);

        foreach (var input in symbol.InputDefinitions)
        {
            // If no entry is present just the value default is used, happens for new inputs
            if (inputDefaultValueTokens.TryGetValue(input.Id, out var jsonDefaultValue))
            {
                input.DefaultValue.SetValueFromJson(jsonDefaultValue);
            }
        }

        symbol.PlaybackSettings = PlaybackSettings.ReadFromJson(jToken);

        var animatorJsonData = (JArray?)jToken[JsonKeys.Animator];
        return new SymbolReadResult(symbol, childrenJsons, animatorJsonData);
    }

    private static void ObtainConnections(JArray connectionsJson, List<Symbol.Connection> connections, Type type)
    {
        foreach (var c in connectionsJson)
        {
            if (!TryReadConnection(c, out var connection))
            {
                Log.Warning($"[{type}] Failed to read connection: " + c);
                continue;
            }
            
            connections.Add(connection);
        }
    }
    #endregion

    public readonly struct JsonKeys
    {
        internal const string Connections = "Connections";
        internal const string SourceParentOrChildId = "SourceParentOrChildId";
        internal const string SourceSlotId = "SourceSlotId";
        internal const string TargetParentOrChildId = "TargetParentOrChildId";
        internal const string TargetSlotId = "TargetSlotId";
        internal const string Children = "Children";
        public const string Id = "Id";
        internal const string SymbolChildName = "Name";
        internal const string SymbolId = "SymbolId";
        internal const string InputValues = "InputValues";
        internal const string OutputData = "OutputData";
        internal const string Type = "Type";
        internal const string IsBypassed = "IsBypassed";
        internal const string DirtyFlagTrigger = "DirtyFlagTrigger";
        internal const string IsDisabled = "IsDisabled";
        internal const string DefaultValue = "DefaultValue";
        internal const string Value = "Value";
        internal const string Inputs = "Inputs";
        internal const string Outputs = "Outputs";
        internal const string Animator = "Animator";
    }

    public readonly record struct SymbolReadResult(Symbol? Symbol, JsonChildResult[] ChildrenJsonArray, JArray? AnimatorJsonData);

    public readonly struct JsonChildResult
    {
        internal readonly Guid SymbolId;
        internal readonly Guid ChildId;
        internal readonly JToken Json;

        internal JsonChildResult(JToken json)
        {
            // todo: handle failure
            var idToken = json[JsonKeys.SymbolId];
            if (idToken != null)
            {
                var symbolIdString = idToken.Value<string>();
                _ = Guid.TryParse(symbolIdString, out SymbolId);
            }

            var childIdToken = json[JsonKeys.Id];
            if (childIdToken != null)
            {
                var childIdString = childIdToken.Value<string>();
                _ = Guid.TryParse(childIdString, out ChildId);
            }

            Json = json;
        }
    }

    public static JsonLoadSettings LoadSettings { get; } = new()
                                                               {
                                                                   CommentHandling = CommentHandling.Ignore,
                                                                   DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error,
                                                                   LineInfoHandling = LineInfoHandling.Ignore
                                                               };
}