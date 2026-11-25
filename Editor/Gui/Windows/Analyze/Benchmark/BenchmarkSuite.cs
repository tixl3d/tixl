#nullable enable

namespace T3.Editor.Gui.Windows.Analyze.Benchmark;

/// <summary>
/// One test of the benchmark suite: a composition with an int input named <c>Load</c> whose first output is
/// pulled every frame while the load is swept from <see cref="MinLoad"/> to <see cref="MaxLoad"/>.
/// </summary>
/// <param name="UnitLabel">The swept amount in a few words, shown after a number: "50 layers".</param>
/// <param name="Description">What the test does, with N for the swept amount.</param>
/// <param name="Reference">An authored, typical amount of the swept parameter. The graph shows the duration at it.</param>
/// <param name="PlotsCpuTime">CPU tests plot the main thread's time; GPU tests plot the time until the GPU is done.</param>
/// <param name="GradeBestMs">The duration at the Reference that earns an A+, about what a current high-end machine needs.</param>
/// <param name="GradeWorstMs">The duration at the Reference that earns an F, where the work no longer fits a realtime scene.</param>
internal sealed record BenchmarkTest(
    string Key,
    string Category,
    Guid SymbolId,
    string UnitLabel,
    string Description,
    int MinLoad,
    int MaxLoad,
    int Reference,
    string ReferenceDescription,
    bool PlotsCpuTime,
    double GradeBestMs,
    double GradeWorstMs)
{
    /// <summary>
    /// Rates the duration at the Reference for people who don't want to read milliseconds: A+ at or below
    /// <see cref="GradeBestMs"/>, F at or above <see cref="GradeWorstMs"/>, and A to D in equal factors between.
    /// The range is stretched by <see cref="GetGradeScale"/>, so a Debug build grades the machine, not the build.
    /// </summary>
    public BenchmarkGrade GetGrade(double referenceMs, bool isDebugBuild)
    {
        var scale = GetGradeScale(isDebugBuild);
        if (referenceMs <= GradeBestMs * scale)
            return BenchmarkGrade.APlus;

        if (referenceMs >= GradeWorstMs * scale)
            return BenchmarkGrade.F;

        var t = Math.Log(referenceMs / (GradeBestMs * scale)) / Math.Log(GradeWorstMs / GradeBestMs);
        return (BenchmarkGrade)(1 + Math.Min((int)(t * 4), 3));
    }

    /** The factor applied to the grade range: Debug builds only slow down the CPU test. */
    public double GetGradeScale(bool isDebugBuild) => PlotsCpuTime && isDebugBuild ? DebugCpuSlowdown : 1;

    /** Debug builds evaluate operators about this much slower than Release builds (measured 0.30 vs. 0.12 ms at the CPU Reference). */
    private const double DebugCpuSlowdown = 2.5;
}

internal enum BenchmarkGrade
{
    APlus,
    A,
    B,
    C,
    D,
    F,
}

/// <summary>
/// The tests of the suite. Changing a test's operators or its Reference breaks comparisons with older results, so bump
/// <see cref="Version"/>. Labels and grade ranges only change the display.
/// </summary>
internal static class BenchmarkSuite
{
    public const int Version = 1;

    public static readonly BenchmarkTest[] Tests =
        [
            new("GraphEvaluation", "CPU", new Guid("a4055ada-4668-5205-b5e1-e45b032e754d"),
                "loop iterations", "Updating N loop iterations of 6 operators each", 1, 100_000, 500,
                "500 iterations: about 3,000 operator updates, a large composition", PlotsCpuTime: true,
                GradeBestMs: 0.1, GradeWorstMs: 3),
            new("PointProcessing", "Compute", new Guid("22a202a0-7fde-5c9d-a6fb-813d8264c82a"),
                "points", "Processing N points with two compute operators", 10, 10_000_000, 500_000,
                "500K points through two compute operators, a dense particle or point scene", PlotsCpuTime: false,
                GradeBestMs: 0.15, GradeWorstMs: 8),
            new("Fillrate", "Draw", new Guid("061cffbf-ba0e-515e-8342-0f940cde0c53"),
                "layers", "Drawing N blended full-screen layers at 1920×1080", 1, 2_000, 20,
                "20 blended full-screen layers, a layered image effect stack", PlotsCpuTime: false,
                GradeBestMs: 0.5, GradeWorstMs: 25),
        ];

    /// <summary>The swept amounts, 1-2-5 per decade within the test's range.</summary>
    public static List<int> GetLoads(BenchmarkTest test)
    {
        var loads = new List<int>();
        for (long decade = 1; decade <= test.MaxLoad; decade *= 10)
        {
            foreach (var factor in _stepFactors)
            {
                var load = decade * factor;
                if (load >= test.MinLoad && load <= test.MaxLoad)
                    loads.Add((int)load);
            }
        }

        return loads;
    }

    public static bool TryGetTest(string key, out BenchmarkTest test)
    {
        foreach (var candidate in Tests)
        {
            if (candidate.Key != key)
                continue;

            test = candidate;
            return true;
        }

        test = null!;
        return false;
    }

    private static readonly int[] _stepFactors = [1, 2, 5];
}
