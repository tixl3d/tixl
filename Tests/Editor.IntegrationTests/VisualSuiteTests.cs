using Newtonsoft.Json.Linq;
using Xunit;
using Xunit.Abstractions;

namespace Editor.IntegrationTests;

/// <summary>
/// Drives the full visual reference test suite ([AllTests] in the examples package)
/// over the debug protocol. Slow (~15-60s) and mutates the examples package's saved
/// trigger state - exclude from quick runs with: dotnet test --filter Category!=VisualSuite
/// </summary>
[Collection("Editor")]
[Trait("Category", "VisualSuite")]
public sealed class VisualSuiteTests(EditorFixture editor, ITestOutputHelper output)
{
    private const string AllTestsSymbol = "b4a9f19a-bbb0-4d65-8d4a-560460e2505c";
    private const string ExecuteTestsSymbol = "83cb923e-a387-4be2-b391-4111c7bd90fe";
    private const string EmptyGuid = "00000000-0000-0000-0000-000000000000";

    /// <summary>Known-flaky tests (timing precision - motion blur / particle nondeterminism).</summary>
    private static readonly string[] _knownFlakyPrefixes =
        [
            "DemoProjectTests / DemoWorksForEverybody / 35",
            "DemoProjectTests / DemoThere / 05",
        ];

    [Fact]
    public void FullSuite_PassesExceptKnownFlaky()
    {
        var client = editor.Client;
        client.Call("openProject", new { symbolId = AllTestsSymbol }).Require("openProject AllTests");
        client.PumpFrames(30);

        // The composition contains one ExecuteTests per category plus the aggregating
        // "Test all" - the one whose Result feeds the composition's own output.
        var graph = client.GetGraphState();
        var rootConnection = graph["connections"]!.First(c => c["targetParentOrChildId"]!.Value<string>() == EmptyGuid);
        var testAllId = Guid.Parse(rootConnection["sourceParentOrChildId"]!.Value<string>()!);
        var testAllChild = graph["children"]!.First(c => c["childId"]!.Value<string>() == testAllId.ToString());
        Assert.Equal(ExecuteTestsSymbol, testAllChild["symbolId"]!.Value<string>());

        // Selecting makes the UI preview pull the op every frame - that's what evaluates it.
        client.Select(testAllId);
        client.PumpFrames(10);

        var report = TriggerAndAwaitReport(testAllId);
        output.WriteLine(report);

        var realFailures = report.Split('\n')
                                 .Where(line => line.Contains("FAILED")
                                                && !line.StartsWith("SUCCESS") && !line.StartsWith("FAILED:")
                                                && !_knownFlakyPrefixes.Any(line.StartsWith))
                                 .ToList();
        Assert.True(realFailures.Count == 0, "real failures:\n" + string.Join("\n", realFailures));
    }

    /// <summary>Sets a clean false->true flank (the trigger may be saved as true) and polls for a fresh report.</summary>
    private string TriggerAndAwaitReport(Guid testAllId)
    {
        var client = editor.Client;
        var previous = client.GetOutputValue(testAllId) ?? "";

        client.SetInput(testAllId, "TriggerTest", JToken.FromObject(false));
        client.PumpFrames(5);
        client.SetInput(testAllId, "TriggerTest", JToken.FromObject(true));

        var deadline = DateTime.UtcNow + TimeSpan.FromMinutes(10);
        while (DateTime.UtcNow < deadline)
        {
            client.PumpFrames(10);
            var value = client.GetOutputValue(testAllId) ?? "";
            if (value != previous && (value.StartsWith("SUCCESS") || value.StartsWith("FAILED")))
                return value;
        }

        throw new TimeoutException("Visual suite did not produce a report within 10 minutes");
    }
}
