#nullable enable
using System.IO;
using T3.Core.Settings;
using T3.Serialization;

namespace T3.Editor.Gui.Windows.TestRunner;

/// <summary>
/// Remembers which guided test sets the user has completed, persisted to
/// <c>testRunHistory.json</c> in the settings folder. Used to mark completed sets with a checkmark in
/// the runner and the welcome window. Kept minimal (set id → last outcome) so it survives test edits;
/// unknown ids are simply ignored.
/// </summary>
internal static class TestRunHistoryStore
{
    internal enum SetOutcome
    {
        Passed,
        HadIssues,
    }

    internal sealed class Entry
    {
        public DateTime LastRunUtc { get; set; }
        public SetOutcome Outcome { get; set; }
    }

    internal static bool TryGet(string setId, out Entry entry) => Data.Sets.TryGetValue(setId, out entry!);

    internal static void MarkCompleted(string setId, SetOutcome outcome)
    {
        Data.Sets[setId] = new Entry { LastRunUtc = DateTime.UtcNow, Outcome = outcome };
        JsonUtils.TrySaveJson(Data, FilePath);
    }

    private static HistoryData Data => _data ??= JsonUtils.TryLoadingJson<HistoryData>(FilePath) ?? new HistoryData();
    private static HistoryData? _data;

    private static string FilePath => Path.Combine(FileLocations.SettingsDirectory, "testRunHistory.json");

    private sealed class HistoryData
    {
        public Dictionary<string, Entry> Sets { get; set; } = new();
    }
}
