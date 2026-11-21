#nullable enable
using System;

namespace T3.Core.DataTypes;

/// <summary>
/// The µs-based scanner-motion pass of the laser pipeline: the time-domain knobs the classic
/// laser software exposes (drawing speed, motion response, corner settle, transitions) expressed
/// in microseconds and converted to point counts at the output's scan rate. The dwell knobs ride
/// on the optimizer's own passes (blanking, corner anchors, shape entries); this class carries the
/// drawing-speed resampler, which must run BEFORE the optimizer - it inserts lit samples along the
/// drawn paths so the beam cannot move faster than the drawing-speed budget allows, and the
/// optimizer then tours, blanks and dwells the resampled frame unchanged.
/// </summary>
public static class LaserMotionTransforms
{
    /// <summary>Hard cap on generated points, mirroring <see cref="LaserOptimizerTransforms.MaxOutputPoints"/>:
    /// bounds damage from pathological input (a tight drawing speed on a full-field wireframe).</summary>
    public const int MaxOutputPoints = 1 << 20;

    /// <summary>Most samples one drawn interval may be stretched into: a tighter budget than this
    /// is a misconfiguration and would explode dense frames - it degrades to this cap.</summary>
    public const int MaxUpsample = 16;

    /// <summary>
    /// Resamples the frame's lit intervals so each takes at least the drawing-speed budget's time:
    /// an interval of length L (ILDA units) drawn at <see cref="Config.DrawingSpeedUs"/> µs per
    /// 1000 units needs ceil(L · µs · PPS / 10⁹) samples; fewer than that are stretched by
    /// inserting linearly interpolated points (positions interpolate, each inserted sample keeps
    /// the interval's start color). Blanked points and zero-length dwells pass through unchanged.
    /// Emits into <paramref name="buffer"/> (grown as needed, reused across frames by the caller's
    /// session) - zero steady-state allocation. Bounded writes: a full buffer drops the rest.
    /// </summary>
    public static LaserPoint[] Apply(LaserPoint[]? input, int count, in Config config, LaserPoint[] buffer, out int outputCount)
    {
        outputCount = 0;
        if (input == null || count <= 0 || config.DrawingSpeedUs <= 0f || config.PointsPerSecond <= 0)
        {
            // Disabled: zero-copy passthrough - the frame is returned untouched.
            outputCount = input != null ? Math.Max(0, Math.Min(count, input.Length)) : 0;
            return input ?? buffer;
        }

        count = Math.Min(count, input.Length);
        if (count <= 0)
            return buffer;

        if (buffer.Length < count)
            buffer = new LaserPoint[Math.Max(count, 4096)];

        // µs per 1000 ILDA units -> samples per unit: one point at the scan rate is
        // 1e6/PPS µs, so a length L needs L·(µs/1000)/(1e6/PPS) = L·µs·PPS/1e9 samples.
        var samplesPerUnit = (double)config.DrawingSpeedUs * config.PointsPerSecond / 1e9;
        if (samplesPerUnit <= 0)
            return buffer;

        for (var i = 0; i < count; i++)
        {
            var p = input[i];
            if (outputCount >= buffer.Length)
                break;

            buffer[outputCount++] = p;

            // Stretch only lit intervals: dark travel is the optimizer's blanking business.
            if (i + 1 >= count)
                break;

            var next = input[i + 1];
            if (p.I <= 0 || next.I <= 0)
                continue;

            var dx = (float)(next.X - p.X);
            var dy = (float)(next.Y - p.Y);
            var length = MathF.Sqrt(dx * dx + dy * dy);
            if (length < 0.5f)
                continue;

            var required = (int)Math.Ceiling(length * samplesPerUnit);
            if (required <= 1)
                continue;

            var inserted = Math.Min(required - 1, MaxUpsample - 1);
            for (var s = 1; s <= inserted && outputCount < buffer.Length; s++)
            {
                var t = s / (float)(inserted + 1);
                buffer[outputCount++] = new LaserPoint(
                    (int)MathF.Round(p.X + dx * t),
                    (int)MathF.Round(p.Y + dy * t),
                    p.R, p.G, p.B, p.I);
            }
        }

        return buffer;
    }

    public struct Config
    {
        /// <summary>Drawing speed in µs per 1000 ILDA units; 0 disables the pass (pure passthrough).</summary>
        public float DrawingSpeedUs;

        /// <summary>Scan rate (points per second) the µs budgets convert at; 0 disables the pass.</summary>
        public int PointsPerSecond;
    }
}
