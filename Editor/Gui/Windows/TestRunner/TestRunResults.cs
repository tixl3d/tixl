#nullable enable
using System.IO;
using System.Linq;
using Newtonsoft.Json;

namespace T3.Editor.Gui.Windows.TestRunner;

/// <summary>
/// Derives the latest result per test set from the auto-saved <c>TestRuns/*.json</c> files (no separate
/// summary file). Scans newest-first, keeps the first occurrence of each set, and caches the map in
/// memory until <see cref="Invalidate"/> is called (after a new run is saved). Used to draw the
/// completion checkmark and the per-step status bar in the runner and the welcome window.
/// </summary>
internal static class TestRunResults
{
    internal sealed class StepInfo
    {
        public Outcome Outcome;
        public string Comment = string.Empty;
    }

    internal sealed class SetResult
    {
        public DateTime RunUtc;
        public IReadOnlyList<StepInfo> Steps = Array.Empty<StepInfo>();
        public bool HadIssues;
    }

    internal static bool TryGet(string setId, out SetResult result)
    {
        EnsureLoaded();
        return _bySet.TryGetValue(setId, out result!);
    }

    /// <summary>Forces a reload on the next access — call after a run is auto-saved.</summary>
    internal static void Invalidate() => _loaded = false;

    private static void EnsureLoaded()
    {
        if (_loaded)
            return;

        _bySet = Load();
        _loaded = true;
    }

    private static Dictionary<string, SetResult> Load()
    {
        var map = new Dictionary<string, SetResult>(StringComparer.OrdinalIgnoreCase);

        var dir = TestRunExport.RunsDirectory;
        if (!Directory.Exists(dir))
            return map;

        string[] files;
        try
        {
            files = Directory.GetFiles(dir, "*.json");
        }
        catch (Exception e)
        {
            Log.Debug($"Could not list test runs: {e.Message}");
            return map;
        }

        // Timestamp filenames (yyyyMMdd_HHmmss) sort chronologically, so descending = newest first.
        Array.Sort(files, StringComparer.Ordinal);
        Array.Reverse(files);

        var cap = Math.Min(files.Length, MaxFilesToScan);
        for (var i = 0; i < cap; i++)
        {
            TestRunExport.RunDto? run;
            try
            {
                run = JsonConvert.DeserializeObject<TestRunExport.RunDto>(File.ReadAllText(files[i]));
            }
            catch
            {
                continue;
            }

            if (run?.Sets == null)
                continue;

            foreach (var set in run.Sets)
            {
                if (string.IsNullOrEmpty(set.Id) || map.ContainsKey(set.Id))
                    continue; // already have a newer result for this set

                var steps = new List<StepInfo>(set.Steps.Count);
                var hadIssues = false;
                foreach (var step in set.Steps)
                {
                    var outcome = Enum.TryParse<Outcome>(step.Outcome, out var parsed) ? parsed : Outcome.Pending;
                    if (outcome is Outcome.Fail or Outcome.Other)
                        hadIssues = true;
                    steps.Add(new StepInfo { Outcome = outcome, Comment = step.Comment });
                }

                map[set.Id] = new SetResult { RunUtc = run.FinishedUtc, Steps = steps, HadIssues = hadIssues };
            }
        }

        return map;
    }

    private const int MaxFilesToScan = 200;
    private static Dictionary<string, SetResult> _bySet = new();
    private static bool _loaded;
}
