#nullable enable
using System.Diagnostics;
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
        _entries.Add(new Entry(output.Id, output.Name, smoothed));
        TotalMs += smoothed;
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

    private static readonly List<Entry> _entries = [];
    private static readonly Dictionary<Guid, float> _smoothedMsByOutput = [];
}
