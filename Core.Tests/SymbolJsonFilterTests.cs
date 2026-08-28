using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json.Linq;
using T3.Core.Model;
using Xunit;

namespace Core.Tests;

public class SymbolJsonFilterTests
{
    private static readonly Guid _keptChild = new("11111111-1111-1111-1111-111111111111");
    private static readonly Guid _droppedChild = new("22222222-2222-2222-2222-222222222222");

    private const string SymbolJsonText = """
                                          {
                                            "FormatVersion": 3,
                                            "Id": "9b1a1ff1-2935-4d9a-880f-897a7f8885ad"/*Root*/,
                                            "Inputs": [],
                                            "Children": [
                                              { "Id": "11111111-1111-1111-1111-111111111111"/*Kept*/, "SymbolId": "aaaaaaaa-0000-0000-0000-000000000000" },
                                              { "Id": "22222222-2222-2222-2222-222222222222"/*Dropped*/, "SymbolId": "bbbbbbbb-0000-0000-0000-000000000000" }
                                            ],
                                            "Connections": [
                                              { "SourceParentOrChildId": "11111111-1111-1111-1111-111111111111", "SourceSlotId": "00000000-0000-0000-0000-000000000001",
                                                "TargetParentOrChildId": "00000000-0000-0000-0000-000000000000", "TargetSlotId": "00000000-0000-0000-0000-000000000002" },
                                              { "SourceParentOrChildId": "22222222-2222-2222-2222-222222222222", "SourceSlotId": "00000000-0000-0000-0000-000000000001",
                                                "TargetParentOrChildId": "11111111-1111-1111-1111-111111111111", "TargetSlotId": "00000000-0000-0000-0000-000000000003" }
                                            ],
                                            "Animator": [
                                              { "InstanceId": "11111111-1111-1111-1111-111111111111", "InputId": "00000000-0000-0000-0000-000000000004", "Curve": {} },
                                              { "InstanceId": "22222222-2222-2222-2222-222222222222", "InputId": "00000000-0000-0000-0000-000000000004", "Curve": {} }
                                            ]
                                          }
                                          """;

    [Fact]
    public void FilteredSymbolFile_KeepsOnlyReachableChildrenConnectionsAndAnimations()
    {
        var sourcePath = Path.GetTempFileName();
        var targetPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".t3");
        try
        {
            File.WriteAllText(sourcePath, SymbolJsonText);
            var kept = new HashSet<Guid> { _keptChild };

            var success = SymbolJson.TryWriteFilteredSymbolFile(sourcePath, targetPath, kept, out var removedCount);

            Assert.True(success);
            Assert.Equal(1, removedCount);

            var result = JObject.Parse(File.ReadAllText(targetPath));
            Assert.Equal("9b1a1ff1-2935-4d9a-880f-897a7f8885ad", result["Id"]!.Value<string>());

            var children = (JArray)result["Children"]!;
            Assert.Single(children);
            Assert.Equal(_keptChild.ToString(), children[0]["Id"]!.Value<string>());

            // The connection to the parent (Guid.Empty) stays, the one from the dropped child goes
            var connections = (JArray)result["Connections"]!;
            Assert.Single(connections);
            Assert.Equal(Guid.Empty.ToString(), connections[0]["TargetParentOrChildId"]!.Value<string>());

            var animations = (JArray)result["Animator"]!;
            Assert.Single(animations);
            Assert.Equal(_keptChild.ToString(), animations[0]["InstanceId"]!.Value<string>());
        }
        finally
        {
            File.Delete(sourcePath);
            if (File.Exists(targetPath))
                File.Delete(targetPath);
        }
    }

    [Fact]
    public void FilteredSymbolFile_WithAllChildrenKept_RemovesNothing()
    {
        var sourcePath = Path.GetTempFileName();
        var targetPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".t3");
        try
        {
            File.WriteAllText(sourcePath, SymbolJsonText);
            var kept = new HashSet<Guid> { _keptChild, _droppedChild };

            Assert.True(SymbolJson.TryWriteFilteredSymbolFile(sourcePath, targetPath, kept, out var removedCount));
            Assert.Equal(0, removedCount);

            var result = JObject.Parse(File.ReadAllText(targetPath));
            Assert.Equal(2, ((JArray)result["Children"]!).Count);
            Assert.Equal(2, ((JArray)result["Connections"]!).Count);
            Assert.Equal(2, ((JArray)result["Animator"]!).Count);
        }
        finally
        {
            File.Delete(sourcePath);
            if (File.Exists(targetPath))
                File.Delete(targetPath);
        }
    }
}
