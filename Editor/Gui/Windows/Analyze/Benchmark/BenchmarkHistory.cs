#nullable enable

namespace T3.Editor.Gui.Windows.Analyze.Benchmark;

/// <summary>
/// Compares runs of one machine and build configuration over time, so a change can be checked for improvements and
/// regressions. Runs are matched by label (OS, backend, configuration) and GPU.
/// </summary>
internal static class BenchmarkHistory
{
    /** Changes smaller than this are run-to-run noise (observed: 2–5 % on the GPU tests). */
    public const double MinRelativeChange = 0.10;

    /** At a few microseconds, 10 % is timer noise. */
    public const double MinAbsoluteChangeMs = 0.05;

    public enum Verdict
    {
        Unchanged,
        Improved,
        Regressed,
        NotComparable,
    }

    public readonly record struct TestComparison(
        string Key,
        double PreviousMs,
        double CurrentMs,
        Verdict Verdict,
        bool TestChanged)
    {
        public double ChangePercent => PreviousMs > 0 ? (CurrentMs - PreviousMs) / PreviousMs * 100 : 0;
    }

    /// <summary>The runs of the same machine and configuration as <paramref name="run"/>, newest first, itself included.</summary>
    public static List<BenchmarkRun> GetSameSetupRuns(BenchmarkRun run, IReadOnlyList<BenchmarkRun> allRuns)
    {
        var runs = new List<BenchmarkRun>();
        foreach (var candidate in allRuns)
        {
            // The run just measured is also in the list as loaded from its file.
            var isSameFile = !string.IsNullOrEmpty(run.FilePath) && candidate.FilePath == run.FilePath;
            if (candidate != run && !isSameFile && IsSameSetup(run, candidate))
                runs.Add(candidate);
        }

        runs.Add(run);

        runs.Sort((a, b) => b.StartedUtc.CompareTo(a.StartedUtc));
        return runs;
    }

    /// <summary>The newest run of the same setup that started before <paramref name="run"/>.</summary>
    public static BenchmarkRun? FindPrevious(BenchmarkRun run, IReadOnlyList<BenchmarkRun> allRuns)
    {
        BenchmarkRun? previous = null;
        foreach (var candidate in allRuns)
        {
            if (candidate == run || candidate.StartedUtc >= run.StartedUtc || !IsSameSetup(run, candidate))
                continue;

            if (previous == null || candidate.StartedUtc > previous.StartedUtc)
                previous = candidate;
        }

        return previous;
    }

    public static List<TestComparison> Compare(BenchmarkRun previous, BenchmarkRun current)
    {
        var comparisons = new List<TestComparison>();
        foreach (var test in BenchmarkSuite.Tests)
        {
            var hasPrevious = TryGetReferenceMs(previous, test, out var previousMs, out var previousFingerprint);
            var hasCurrent = TryGetReferenceMs(current, test, out var currentMs, out var currentFingerprint);
            var testChanged = hasPrevious && hasCurrent && previousFingerprint != currentFingerprint;

            var verdict = Verdict.NotComparable;
            if (hasPrevious && hasCurrent)
            {
                var difference = currentMs - previousMs;
                var isSignificant = Math.Abs(difference) > Math.Max(MinRelativeChange * previousMs, MinAbsoluteChangeMs);
                verdict = !isSignificant ? Verdict.Unchanged : difference > 0 ? Verdict.Regressed : Verdict.Improved;
            }

            comparisons.Add(new TestComparison(test.Key, previousMs, currentMs, verdict, testChanged));
        }

        return comparisons;
    }

    public static bool TryGetReferenceMs(BenchmarkRun run, BenchmarkTest test, out double ms, out string fingerprint)
    {
        ms = double.NaN;
        fingerprint = string.Empty;
        if (!run.TryGetTest(test.Key, out var result))
            return false;

        fingerprint = result.Fingerprint;
        result.GetCurve(test.PlotsCpuTime, _curveScratch);
        return BenchmarkTestResult.TryInterpolate(_curveScratch, test.Reference, out ms);
    }

    private static bool IsSameSetup(BenchmarkRun a, BenchmarkRun b)
    {
        return a.Label == b.Label && a.System.Adapter == b.System.Adapter && a.SuiteVersion == b.SuiteVersion;
    }

    private static readonly List<CurvePoint> _curveScratch = [];
}
