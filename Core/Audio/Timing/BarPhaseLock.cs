#nullable enable
using System;
using T3.Core.DataTypes.DataSet;
using T3.Core.IO;

namespace T3.Core.Audio.Timing;

/// <summary>
/// A bar clock locked onto the bar rollovers of a wrapped phase signal, as produced by the DanceAi model.
/// Pure algorithm with no threading or IO, so it can be driven by synthetic phase sequences in tests.
/// </summary>
/// <remarks>
/// Only the wrap from 1 back to 0 is used as an event: the model's phase progresses unevenly within a bar,
/// so quarter crossings are not reliable beat times, but the bar start is. The intervals between events
/// give the tempo; the clock's distance to its bar start at the event gives the phase error. The error
/// is paid off by slewing the clock, never by stepping it, and a small trim on the tempo cancels bias.
/// </remarks>
internal sealed class BarPhaseLock
{
    /// <summary>Continuous bar count since the first bar start.</summary>
    public double BarTime { get; private set; }

    /// <summary>Current clock tempo, assuming four beats per bar.</summary>
    public double Bpm { get; private set; } = DefaultBpm;

    /// <summary>True once a first bar start has been seen.</summary>
    public bool IsLocked { get; private set; }

    /// <summary>Phase error in bars still to be slewed off. Exposed for the profiling traces.</summary>
    public double PendingPhaseError { get; private set; }

    public void Reset()
    {
        IsLocked = false;
        BarTime = 0;
        Bpm = DefaultBpm;
        _measuredBpm = DefaultBpm;
        _tempoTrim = 0;
        PendingPhaseError = 0;
        _previousPhase = double.NaN;
        _lastRolloverTimeSec = double.NaN;
        _intervalHead = 0;
        _intervalCount = 0;
    }

    /// <summary>
    /// Advance the clock by one frame of the phase signal and correct it if the frame contains a bar rollover.
    /// </summary>
    /// <param name="phase">Wrapped bar phase in [0, 1).</param>
    /// <param name="modelBarDurationSec">The model's own bar-duration prediction; only used for the initial tempo.</param>
    /// <param name="frameEndTimeSec">Timestamp of the end of this frame, on a jitter-free clock.</param>
    /// <param name="frameDurationSec">Frame length in seconds.</param>
    /// <param name="smoothing">0 follows the phase tightly, 1 trusts the running tempo.</param>
    public void Update(double phase, double modelBarDurationSec, double frameEndTimeSec, double frameDurationSec, float smoothing)
    {
        var isRollover = !double.IsNaN(_previousPhase) && phase < _previousPhase - 0.5;
        _previousPhase = phase;

        if (!IsLocked)
        {
            if (!isRollover)
                return;

            _measuredBpm = BpmMath.BpmFromBarDuration(Math.Max(modelBarDurationSec, MinBarDurationSec));
            Bpm = Math.Clamp(_measuredBpm, MinBpm, MaxBpm);
            BarTime = 0;
            _lastRolloverTimeSec = frameEndTimeSec;
            IsLocked = true;
            return;
        }

        BarTime += frameDurationSec * BpmMath.BarsPerSecond(Bpm);
        BleedPhaseError(frameDurationSec, smoothing);
        Bpm = Math.Clamp(_measuredBpm * (1 + _tempoTrim), MinBpm, MaxBpm);

        if (!isRollover)
            return;

        var barDurationSec = BpmMath.BarDurationSeconds(Bpm);
        var intervalSec = frameEndTimeSec - _lastRolloverTimeSec;

        if (intervalSec < barDurationSec * MinRolloverSpacingBars)
            return;

        _lastRolloverTimeSec = frameEndTimeSec;
        UpdateTempo(intervalSec, barDurationSec, smoothing);

        // Distance from the nearest bar start, in bars; positive means the clock is ahead.
        var barError = BarTime - Math.Round(BarTime);

        if (barError < -LagToLeadThreshold)
            barError += 1;

        PendingPhaseError = barError;
        _tempoTrim = Math.Clamp(_tempoTrim - barError * TempoTrimPerBarOfError, -MaxTempoTrim, MaxTempoTrim);

        if (CoreSettings.Config.EnableBeatSyncProfiling)
        {
            DebugDataRecording.KeepTraceData("BarPhase/barError", barError);
            DebugDataRecording.KeepTraceData("BarPhase/bpm", Bpm);
            DebugDataRecording.KeepTraceData("BarPhase/tempoTrim", _tempoTrim);
        }
    }

    /// <summary>
    /// Pay the phase error off over time by slewing the clock, never by stepping it.
    /// </summary>
    private void BleedPhaseError(double frameDurationSec, float smoothing)
    {
        if (PendingPhaseError == 0)
            return;

        var timeConstantSec = MinCorrectionTimeSec + smoothing * (MaxCorrectionTimeSec - MinCorrectionTimeSec);
        var gain = Math.Min(1, frameDurationSec / timeConstantSec);
        var maxCorrection = frameDurationSec * BpmMath.BarsPerSecond(Bpm) * MaxCorrectionTempoFraction;
        var correction = Math.Clamp(PendingPhaseError * gain, -maxCorrection, maxCorrection);
        BarTime -= correction;
        PendingPhaseError -= correction;
    }

    /// <summary>
    /// Tempo from the intervals between accepted bar rollovers, averaged over more bars the higher the smoothing.
    /// </summary>
    private void UpdateTempo(double intervalSec, double barDurationSec, float smoothing)
    {
        var ratio = intervalSec / barDurationSec;
        if (ratio < MinIntervalRatio || ratio > MaxIntervalRatio)
            return;

        _intervals[_intervalHead] = intervalSec;
        _intervalHead = (_intervalHead + 1) % MaxTempoAverageBars;
        if (_intervalCount < MaxTempoAverageBars)
            _intervalCount++;

        var window = Math.Min(_intervalCount, 1 + (int)(smoothing * (MaxTempoAverageBars - 1)));
        var sum = 0.0;
        for (var step = 1; step <= window; step++)
        {
            sum += _intervals[(_intervalHead + MaxTempoAverageBars - step) % MaxTempoAverageBars];
        }

        _measuredBpm = BpmMath.BpmFromBarDuration(sum / window);
    }

    internal const double MinBpm = 40.0;
    internal const double MaxBpm = 400.0;

    private const double DefaultBpm = 120.0;
    private const double MinBarDurationSec = 0.1;
    private const int MaxTempoAverageBars = 4;

    /** The raw phase jitters back and forth across the wrap point. */
    private const double MinRolloverSpacingBars = 0.6;

    /** Intervals below this ratio to the current bar are glitches, not tempo. */
    private const double MinIntervalRatio = 0.5;

    /** Intervals above this ratio to the current bar are stalls, not tempo. */
    private const double MaxIntervalRatio = 2.0;

    /** A clock further behind than this is treated as ahead, so corrections never flip direction. */
    private const double LagToLeadThreshold = 0.35;

    /** Correction time at smoothing 0. */
    private const double MinCorrectionTimeSec = 0.1;

    /** Correction time at smoothing 1. */
    private const double MaxCorrectionTimeSec = 1.5;

    /** The only value tuned by ear: slower drifted into the half-bar dead state, faster wobbled. */
    private const double MaxCorrectionTempoFraction = 0.25;

    /** Slow integral term against a systematic bias between measured tempo and phase. */
    private const double TempoTrimPerBarOfError = 0.3;

    /** Bound of the integral term; the interval tempo should already be within this. */
    private const double MaxTempoTrim = 0.03;

    private readonly double[] _intervals = new double[MaxTempoAverageBars];
    private int _intervalHead;
    private int _intervalCount;

    private double _measuredBpm = DefaultBpm;
    private double _tempoTrim;
    private double _previousPhase = double.NaN;
    private double _lastRolloverTimeSec = double.NaN;
}
