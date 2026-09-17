using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using T3.Core.Model;
using T3.Core.Operator;
using Xunit;

namespace Core.Tests;

/// <summary>
/// Children whose symbol is missing must survive a load/save cycle together with their
/// connections and animations.
/// </summary>
public class SymbolJsonUnresolvedChildrenTests
{
    private static readonly Guid _symbolId = new("9b1a1ff1-2935-4d9a-880f-897a7f8885ad");
    private static readonly Guid _firstChild = new("11111111-1111-1111-1111-111111111111");
    private static readonly Guid _secondChild = new("22222222-2222-2222-2222-222222222222");

    private const string SymbolJsonText = """
                                          {
                                            "FormatVersion": 3,
                                            "Id": "9b1a1ff1-2935-4d9a-880f-897a7f8885ad"/*Root*/,
                                            "Inputs": [],
                                            "Children": [
                                              { "Id": "11111111-1111-1111-1111-111111111111"/*MidiInput*/, "SymbolId": "aaaaaaaa-0000-0000-0000-000000000000",
                                                "SymbolName": "Lib.io.midi.MidiInput", "InputValues": [ { "Id": "00000000-0000-0000-0000-000000000009"/*Channel*/, "Type": "System.Int32", "Value": 7 } ], "Outputs": [] },
                                              { "Id": "22222222-2222-2222-2222-222222222222"/*MyRenamedOp*/, "SymbolId": "bbbbbbbb-0000-0000-0000-000000000000",
                                                "Name": "MyRenamedOp", "InputValues": [], "Outputs": [] }
                                            ],
                                            "Connections": [
                                              { "SourceParentOrChildId": "22222222-2222-2222-2222-222222222222", "SourceSlotId": "00000000-0000-0000-0000-000000000001",
                                                "TargetParentOrChildId": "00000000-0000-0000-0000-000000000000", "TargetSlotId": "00000000-0000-0000-0000-000000000002" },
                                              { "SourceParentOrChildId": "11111111-1111-1111-1111-111111111111", "SourceSlotId": "00000000-0000-0000-0000-000000000001",
                                                "TargetParentOrChildId": "00000000-0000-0000-0000-000000000000", "TargetSlotId": "00000000-0000-0000-0000-000000000002" },
                                              { "SourceParentOrChildId": "11111111-1111-1111-1111-111111111111", "SourceSlotId": "00000000-0000-0000-0000-000000000001",
                                                "TargetParentOrChildId": "22222222-2222-2222-2222-222222222222", "TargetSlotId": "00000000-0000-0000-0000-000000000003" }
                                            ],
                                            "Animator": [
                                              { "InstanceId": "11111111-1111-1111-1111-111111111111", "InputId": "00000000-0000-0000-0000-000000000004", "Curve": { "Keys": [] } }
                                            ]
                                          }
                                          """;

    [Fact]
    public void UnresolvedChildren_SurviveLoadAndSave()
    {
        var symbol = Load(SymbolJsonText);

        Assert.Equal(2, symbol.UnresolvedChildren.Count);
        Assert.Empty(symbol.Children);
        Assert.Empty(symbol.Connections);

        var original = JObject.Parse(SymbolJsonText);
        var saved = JObject.Parse(Save(symbol));

        Assert.True(JToken.DeepEquals(original["Children"], saved["Children"]));
        Assert.True(JToken.DeepEquals(original["Connections"], saved["Connections"]));
        Assert.True(JToken.DeepEquals(original["Animator"], saved["Animator"]));
    }

    [Fact]
    public void SymbolName_IsReadWhenPresent()
    {
        var symbol = Load(SymbolJsonText);

        var first = symbol.UnresolvedChildren.Single(c => c.Id == _firstChild);
        Assert.Equal("Lib.io.midi.MidiInput", first.SymbolName);
        Assert.Null(first.Name);

        // Saved before symbol names were recorded
        var second = symbol.UnresolvedChildren.Single(c => c.Id == _secondChild);
        Assert.Null(second.SymbolName);
        Assert.Equal("MyRenamedOp", second.Name);
    }

    [Fact]
    public void RemovedUnresolvedChild_IsNotWrittenBackUntilRestored()
    {
        var symbol = Load(SymbolJsonText);

        Assert.True(symbol.TryRemoveUnresolvedChild(_firstChild, out var removed));

        var saved = JObject.Parse(Save(symbol));
        var children = (JArray)saved["Children"]!;
        Assert.Single(children);
        Assert.Equal(_secondChild.ToString(), children[0]["Id"]!.Value<string>());

        // Only the connection from the remaining child to the symbol output is left
        var connections = (JArray)saved["Connections"]!;
        Assert.Single(connections);
        Assert.Equal(_secondChild.ToString(), connections[0]["SourceParentOrChildId"]!.Value<string>());

        Assert.Null(saved["Animator"]);

        // Restoring brings back the exact original file content
        symbol.RestoreUnresolvedChild(removed);
        var original = JObject.Parse(SymbolJsonText);
        var restored = JObject.Parse(Save(symbol));
        Assert.True(JToken.DeepEquals(original["Children"], restored["Children"]));
        Assert.True(JToken.DeepEquals(original["Connections"], restored["Connections"]));
        Assert.True(JToken.DeepEquals(original["Animator"], restored["Animator"]));
    }

    private static Symbol Load(string json)
    {
        var root = JObject.Parse(json, SymbolJson.LoadSettings);
        var symbol = new Symbol(typeof(object), _symbolId, null);

        foreach (var connectionJson in (JArray)root["Connections"]!)
        {
            symbol.Connections.Add(new Symbol.Connection(Guid.Parse(connectionJson["SourceParentOrChildId"]!.Value<string>()!),
                                                         Guid.Parse(connectionJson["SourceSlotId"]!.Value<string>()!),
                                                         Guid.Parse(connectionJson["TargetParentOrChildId"]!.Value<string>()!),
                                                         Guid.Parse(connectionJson["TargetSlotId"]!.Value<string>()!)));
        }

        var childResults = ((JArray)root["Children"]!).Select(childJson => new SymbolJson.JsonChildResult(childJson)).ToArray();
        var readResult = new SymbolJson.SymbolReadResult(symbol, childResults, (JArray?)root["Animator"]);

        Assert.True(SymbolJson.TryReadAndApplySymbolChildren(readResult));
        return symbol;
    }

    private static string Save(Symbol symbol)
    {
        using var stringWriter = new StringWriter();
        using (var jsonWriter = new JsonTextWriter(stringWriter) { Formatting = Formatting.Indented })
        {
            SymbolJson.WriteSymbol(symbol, jsonWriter);
        }

        return stringWriter.ToString();
    }
}
