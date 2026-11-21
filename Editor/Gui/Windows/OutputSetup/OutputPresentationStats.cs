#nullable enable
using System.Diagnostics;
using T3.Core.Logging;
using T3.Core.Output;

namespace T3.Editor.Gui.Windows.OutputSetup;

/// <summary>
/// What the active setup presented last frame and what each output cost — the numbers behind the app bar's
/// outputs indicator. Measured around the compositing call, which pulls (and therefore evaluates) the content,
/// so this is the editor's own frame time; the GPU work it queues is not included.
/// </summary>
internal static class OutputPresentationStats
{
    internal readonly record struct Entry(Guid OutputId, string Name, float DurationMs);

    /// <summary>The outputs presented in the last completed frame, in the setup's own order.</summary>
    public static IReadOnlyList<Entry> Entries => _entries;

    public static float TotalMs { get; private set; }

    public static void BeginFrame()
    {
        _entries.Clear();
        TotalMs = 0;
    }

    /// <summary>Records what presenting one output took, smoothed so the readout stays legible.</summary>
    public static void NotePresented(OutputDefinition output, long startTimestamp)
    {
        var durationMs = (float)((Stopwatch.GetTimestamp() - startTimestamp) * 1000.0 / Stopwatch.Frequency);
        var smoothed = _smoothedMsByOutput.TryGetValue(output.Id, out var previous)
                           ? previous + (durationMs - previous) * SmoothingPerFrame
                           : durationMs;

        _smoothedMsByOutput[output.Id] = smoothed;
        _entries.Add(new Entry(output.Id, SetupLabels.OutputLabel(output), smoothed));
        TotalMs += smoothed;

        // A single output that keeps costing tens of milliseconds is the difference between 60 and 10 fps,
        // and the app bar number is easy to miss. Say it once in the log so a slow presentation can be
        // traced without watching the readout.
        if (smoothed >= SlowPresentationMs)
        {
            var now = Environment.TickCount64;
            if (!_lastSlowLogByOutput.TryGetValue(output.Id, out var last) || now - last > SlowLogIntervalMs)
            {
                _lastSlowLogByOutput[output.Id] = now;
                Log.Warning($"Presenting '{SetupLabels.OutputLabel(output)}' takes {smoothed:0} ms per frame " +
                            $"(threshold {SlowPresentationMs} ms); {output.ResolvedResolution.Width}x{output.ResolvedResolution.Height} canvas.");
            }
        }
    }

    /// <summary>Forgets an output that is gone (deleted, or the whole setup swapped).</summary>
    public static void Release(Guid outputId)
    {
        _smoothedMsByOutput.Remove(outputId);
    }

    public static void ReleaseAll()
    {
        _smoothedMsByOutput.Clear();
        _entries.Clear();
        TotalMs = 0;
    }

    /** Heavy smoothing: the readout is judged by eye, and per-frame jitter would make it unreadable. */
    private const float SmoothingPerFrame = 0.05f;

    /** About 40 ms leaves no room for a 30 fps canvas; anything this slow is worth a log entry. */
    private const float SlowPresentationMs = 40f;
    private const long SlowLogIntervalMs = 10_000;

    private static readonly Dictionary<Guid, long> _lastSlowLogByOutput = [];

    private static readonly List<Entry> _entries = [];
    private static readonly Dictionary<Guid, float> _smoothedMsByOutput = [];
}
