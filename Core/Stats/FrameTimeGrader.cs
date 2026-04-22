using System;

namespace T3.Core.Stats;

/// <summary>
/// Derives a single 0..1 score and letter grade (A+ .. F) from the distribution
/// of frame times in a <see cref="RollingMetric"/>.
///
/// Approach:
///   1. Detect the target frame time by snapping the window median to the nearest
///      common refresh period (1/144, 1/120, 1/75, 1/60). Adapts to high-refresh displays.
///   2. Use P99 vs. target to measure tail latency.
///   3. Penalise the fraction of samples &gt; 2x target (drops &gt; ~33 ms @ 60 Hz).
/// </summary>
public static class FrameTimeGrader
{
    public readonly struct Result
    {
        public readonly float Score;       // 0..1
        public readonly string Letter;     // "A+", "A", ... "F"
        public readonly float TargetMs;    // detected target frame time
        public readonly float P50Ms;
        public readonly float P95Ms;
        public readonly float P99Ms;
        public readonly float FractionOver2x;

        public Result(float score, string letter, float targetMs, float p50, float p95, float p99, float fracOver2x)
        {
            Score = score; Letter = letter; TargetMs = targetMs;
            P50Ms = p50; P95Ms = p95; P99Ms = p99; FractionOver2x = fracOver2x;
        }
    }

    private static readonly float[] _commonTargetsMs =
        {
            1000f / 144f, 1000f / 120f, 1000f / 75f, 1000f / 60f, 1000f / 30f,
        };

    public static Result Grade(RollingMetric frameTimes)
    {
        if (frameTimes.Count == 0)
            return new Result(0f, "-", 1000f / 60f, 0f, 0f, 0f, 0f);

        var p50 = Percentile(frameTimes, 0.50f);
        var p95 = Percentile(frameTimes, 0.95f);
        var p99 = Percentile(frameTimes, 0.99f);
        var targetMs = SnapToCommonTarget(p50);
        var fracOver2x = FractionOver(frameTimes, 2f * targetMs);

        // Tail-latency component: P99 within [target, 4*target] → 1..0.
        var tail = 1f - (p99 - targetMs) / (targetMs * 3f);
        if (tail < 0f) tail = 0f; else if (tail > 1f) tail = 1f;

        // Drop penalty: every 1% of samples > 2x target removes 2 points.
        var score = tail - fracOver2x * 2f;
        if (score < 0f) score = 0f; else if (score > 1f) score = 1f;

        return new Result(score, ScoreToLetter(score, targetMs, p50), targetMs, p50, p95, p99, fracOver2x);
    }

    /// <summary>
    /// Percentile computed directly from the histogram bucket counts.
    /// Returns the lower edge of the containing bucket — precision is bucket-bound, which is fine for grading.
    /// </summary>
    private static float Percentile(RollingMetric h, float p)
    {
        var target = (int)MathF.Ceiling(p * h.Count);
        if (target < 1) target = 1;

        var running = 0;
        var slots = h.Slots;
        for (var i = 0; i < slots.Length; i++)
        {
            running += slots[i].CountRecent;
            if (running >= target)
                return slots[i].ValueRangeMin;
        }
        return slots[^1].ValueRangeMin;
    }

    private static float FractionOver(RollingMetric h, float thresholdMs)
    {
        var slots = h.Slots;
        var over = 0;
        for (var i = 0; i < slots.Length; i++)
        {
            if (slots[i].ValueRangeMin >= thresholdMs)
                over += slots[i].CountRecent;
        }
        return (float)over / h.Count;
    }

    private static float SnapToCommonTarget(float medianMs)
    {
        // Snap upward: we prefer to credit high-refresh displays rather than grade them against 60 Hz.
        var best = _commonTargetsMs[^1];
        var bestDist = float.PositiveInfinity;
        for (var i = 0; i < _commonTargetsMs.Length; i++)
        {
            var t = _commonTargetsMs[i];
            var d = MathF.Abs(medianMs - t);
            if (d < bestDist) { bestDist = d; best = t; }
        }
        return best;
    }

    private static string ScoreToLetter(float score, float targetMs, float p50Ms)
    {
        // Bonus A+ only when essentially everything hits the target and the target is 60 Hz or faster.
        if (score >= 0.97f && targetMs <= 1000f / 60f + 0.5f && p50Ms <= targetMs + 0.5f)
            return "A+";
        if (score >= 0.85f)
            return "A";
        if (score >= 0.70f)
            return "B";
        if (score >= 0.50f)
            return "C";
        if (score >= 0.30f)
            return "D";
        return "F";
    }
}
