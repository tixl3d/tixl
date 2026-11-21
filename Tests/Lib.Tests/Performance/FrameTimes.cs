using System.Collections.Concurrent;

namespace Lib.Tests;

/// <summary>
/// Collects per-frame CPU times of the example scenes. Set TIXL_FRAME_REPORT to a file path to get one line per scene
/// (frames, average, 95th percentile, worst) when the test run ends. Times are recorded, never asserted: a loaded
/// test machine must not fail a test.
/// </summary>
internal static class FrameTimes
{
    public static void Record(string scene, double milliseconds, long allocatedBytes = -1)
    {
        Samples.GetOrAdd(scene, _ => new ConcurrentBag<double>()).Add(milliseconds);
        if (allocatedBytes >= 0)
            Allocations.GetOrAdd(scene, _ => new ConcurrentBag<long>()).Add(allocatedBytes);
    }

    static FrameTimes()
    {
        AppDomain.CurrentDomain.ProcessExit += (_, _) => WriteReport();
    }

    private static void WriteReport()
    {
        var path = Environment.GetEnvironmentVariable("TIXL_FRAME_REPORT");
        if (string.IsNullOrEmpty(path))
            return;

        var lines = new List<string>();
        foreach (var (scene, bag) in Samples.OrderBy(entry => entry.Key))
        {
            var times = bag.ToArray();
            Array.Sort(times);
            // The first frames build shapes and warm the JIT; the steady state is what playback sees.
            var steady = times.Length > 20 ? times[..^3] : times;
            var line = $"{scene,-55} frames {times.Length,5}  avg {steady.Average(),7:0.00} ms  p95 {times[(int)(times.Length * 0.95)],7:0.00} ms  max {times[^1],7:0.00} ms";
            if (Allocations.TryGetValue(scene, out var allocations))
            {
                // The median frame: the first frames build shapes and allocate on purpose.
                var sorted = allocations.ToArray();
                Array.Sort(sorted);
                line += $"  alloc/frame (median) {sorted[sorted.Length / 2],8} B";
            }

            lines.Add(line);
        }

        File.WriteAllLines(path, lines);
    }

    private static readonly ConcurrentDictionary<string, ConcurrentBag<double>> Samples = new();
    private static readonly ConcurrentDictionary<string, ConcurrentBag<long>> Allocations = new();
}
