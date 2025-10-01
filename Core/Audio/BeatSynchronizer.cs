using System;
using System.Collections.Generic;
using T3.Core.DataTypes.DataSet;
using T3.Core.IO;

namespace T3.Core.Audio;

/// <summary>
/// Analyzes the external WASAPI system audio and attempts to "latch onto" the audio timing so that the BPM
/// rate and editor timing follow the tempo of the soundtrack — even if the initial BPM rate was imprecise or is changing, as in live performances.
/// 
/// The algorithm requires an initial tempo and downbeat timing from the user. This is provided by tapping a few times and then pressing Resync.
/// It then uses expected onset timings (e.g., bass on the 1st and 1/4 beats, snares on the 2nd and 4th, etc.) to calculate a phase error
/// and shift the beat timing and BPM to minimize it.
/// 
/// I tested it with all kinds of musical styles (all in 4/4, of course): The usual suspects like dubstep, techno, electro (Plaster, THNTS), but also
/// analog (Metallica, Led Zeppelin, Deep Purple), disco (Michael Jackson), and jazz. It appears to work well for electronic music, but heavy tempo shifts
/// — like in jazz — are prone to slippage.
/// 
/// The implementation is more complex than expected:
/// 
/// To get maximum precision, we use every audio buffer update from WASAPI and immediately calculate frequency bins, possible onsets, and timing.
/// This is normally out of sync with the display update rate of 60Hz.
/// 
/// To avoid jittering, the resulting timing is then smoothed in BeatTiming.
/// You can enable ProjectSettings.EnableBeatSyncProfiling to get a better understanding of how different musical styles are matched.
/// The algorithm uses too many magic numbers to list, but the most relevant ones are:
/// - proportionalBpmAdjustment
/// - phaseAdjustmentAmount
/// 
/// These can be nicely tweaked via hot code reloading. Also, the definitions of FrequencyBands and RhythmicTemplates are useful targets for tweaking.
/// </summary>
public static class BeatSynchronizer
{
    /// <summary>
    /// Gets the current estimated BPM.
    /// </summary>
    public static double CurrentBpm => _currentBpm;

    /// <summary>
    /// Gets the current progress within the bar, from 0.0 to  1.0.
    /// </summary>
    public static double BarProgress => _barTime;

    /// <summary>
    /// Initializes the beat synchronizer. Must be called once at application start.
    /// </summary>
    private static void Initialize()
    {
        if (_initialized)
            return;

        for (var i = 0; i < FrequencyBandCount; i++)
        {
            _recentTypeOnsetStrengths[i] = new Queue<float>();
            _totalTypeOnsetStrengths[i] = 0f;
        }

        _initialized = true;
    }

    /// <summary>
    /// Manually resynchronizes the beat tracker, setting the BPM and forcing the phase to the start of a bar.
    /// This should be called by the user (e.g., via a "Resync" button).
    /// </summary>
    /// <param name="initialBpm">The BPM to set the beat tracker to initially.</param>
    public static void Resync(double initialBpm)
    {
        Initialize();
        _currentBpm = Math.Clamp(initialBpm, MinBpm, MaxBpm);
        _barTime = (int)(_barTime / 4) * 4; // Reset to beginning of last measure
        _detectedOnsets.Clear();

        for (var index = 0; index < _lastAnyOnsetDetectionTimes.Length; index++)
        {
            _lastAnyOnsetDetectionTimes[index] = 0;
        }

        var numberOfTrackedTypes = (int)FrequencyRangeType.Hihat + 1;
        for (var i = 0; i < numberOfTrackedTypes; i++)
        {
            _recentTypeOnsetStrengths[i].Clear();
            _totalTypeOnsetStrengths[i] = 0f;
        }
    }

    /// <summary>
    /// Updates the beat timer, detects onsets, and adjusts BPM via PID controller.
    /// This is called by BeatTiming after user tapped and resynced...
    /// </summary>
    internal static void UpdateBeatTimer()
    {
        Initialize();

        var currentTimeMs = WasapiAudioInput.LastUpdateTime * 1000;
        var deltaTimeMs = WasapiAudioInput.TimeSinceLastUpdate * 1000;

        // Advance time...
        _barTime += _currentBpm / 60.0 / 1000.0 / 4.0 * deltaTimeMs;

        var maxAge = (60000.0 / MinBpm * 2);

        _detectedOnsets.RemoveAll(o => currentTimeMs - o.TimeMs > maxAge);

        // Find onsets
        for (var index = 0; index < _bands.Length; index++)
        {
            var band = _bands[index];

            var currentBassOnsetStrength = SumBandAttacks(band.StartBand, band.EndBand);
            if (!TryDetectAndQueueOnsetStrength(band, currentBassOnsetStrength, currentTimeMs, out var onset))
                continue;

            if (ProjectSettings.Config.EnableBeatSyncProfiling)
            {
                DebugDataRecording.KeepTraceData("BPM/OnSet/" + band.Type, onset.Amplitude);
            }

            _lastAnyOnsetDetectionTimes[index] = currentTimeMs;
            _detectedOnsets.Add(onset);
        }

        // Adjust bpmRate through phase offset 
        var totalWeightedPhaseError = 0.0;
        var totalWeight = 0.0;

        if (_detectedOnsets.Count > 0)
        {
            for (var index = 0; index < _detectedOnsets.Count; index++)
            {
                var onset = _detectedOnsets[index];
                var conceptualBarStartTimeMs = currentTimeMs - _barTime % 1 * BarDurationMs;

                var offsetFromNearestConceptualBarStartMs = (onset.TimeMs - conceptualBarStartTimeMs) % BarDurationMs;

                if (offsetFromNearestConceptualBarStartMs < 0)
                    offsetFromNearestConceptualBarStartMs += BarDurationMs;

                var onsetNormalizedBarPosition = offsetFromNearestConceptualBarStartMs / BarDurationMs;

                foreach (var template in _onsetRhythmicTemplates[(int)onset.Type])
                {
                    var rawErrorToTemplateNormalized = onsetNormalizedBarPosition - template.NormalizedBarPosition;
                    if (rawErrorToTemplateNormalized > 0.5) rawErrorToTemplateNormalized -= 1.0;
                    if (rawErrorToTemplateNormalized < -0.5) rawErrorToTemplateNormalized += 1.0;

                    var errorToTemplateMs = rawErrorToTemplateNormalized * BarDurationMs;

                    if (!(Math.Abs(errorToTemplateMs) <= template.ToleranceMs * 2)) // HACK
                        continue;

                    totalWeightedPhaseError += rawErrorToTemplateNormalized * template.ImpactWeight;
                    totalWeight += template.ImpactWeight * onset.Amplitude;
                }
            }
        }

        var currentPhaseErrorNormalized = 0.0;
        var hasRelevantOnsets = totalWeight > 0.0;

        if (hasRelevantOnsets)
            currentPhaseErrorNormalized = totalWeightedPhaseError / totalWeight;

        if (!hasRelevantOnsets)
            return;

        // This is basically the P in a PID controller and controls how fast the BPM should be adjusted
        // -0.1    ***  loses track with Led Zeppelin 
        // -0.3   ****  faster but jittery
        // -0.7     **  too jumpy
        // -1.0      *  erratic
        double proportionalBpmAdjustment = -0.4f;

        // High values result in a "pumping" effect. 
        double phaseAdjustmentAmount = 0.01f;

        var bpmCorrection = (proportionalBpmAdjustment * currentPhaseErrorNormalized);

        if (ProjectSettings.Config.EnableBeatSyncProfiling)
        {
            DebugDataRecording.KeepTraceData("BPM/barProgress", _barTime % 1);
            DebugDataRecording.KeepTraceData("BPM/current", _currentBpm);
            DebugDataRecording.KeepTraceData("BPM/phaseError", currentPhaseErrorNormalized);
            DebugDataRecording.KeepTraceData("BPM/bpmCorrection", bpmCorrection);
        }

        var phaseCorrection = currentPhaseErrorNormalized * phaseAdjustmentAmount;

        _currentBpm += bpmCorrection;
        _currentBpm = Math.Clamp(_currentBpm, MinBpm, MaxBpm);
        _barTime -= phaseCorrection;
    }

    /// <summary>
    /// Sums the attack values within a specified band range.
    /// </summary>
    private static float SumBandAttacks(int startBand, int endBand)
    {
        var sum = 0f;
        // Ensure bounds checking for robustness, although usually constant
        var actualEndBand = Math.Min(endBand, AudioAnalysis.FrequencyBandCount - 1);
        for (var i = startBand; i <= actualEndBand; i++)
        {
            sum += AudioAnalysis.FrequencyBandOnSets[i];
        }

        return sum / (actualEndBand - startBand);
    }

    /// <summary>
    /// Updates the sliding window average for a given onset type and checks for an onset.
    /// </summary>
    /// <returns>A tuple indicating if an onset was detected for this type, and its current strength.</returns>
    private static bool TryDetectAndQueueOnsetStrength(FrequencyBand band, float currentStrength, double currentTimeMs, out Onset onset)
    {
        var typeIndex = (int)band.Type; // Cast enum to int for array indexing
        onset = new Onset(currentTimeMs, currentStrength, band.Type);

        // Update sliding window average
        _recentTypeOnsetStrengths[typeIndex].Enqueue(currentStrength);
        _totalTypeOnsetStrengths[typeIndex] += currentStrength;

        if (_recentTypeOnsetStrengths[typeIndex].Count > OnsetHistoryWindowSizeFrames)
        {
            _totalTypeOnsetStrengths[typeIndex] -= _recentTypeOnsetStrengths[typeIndex].Dequeue();
        }

        var averageStrength = 0f;
        if (_recentTypeOnsetStrengths[typeIndex].Count > 0)
        {
            averageStrength = _totalTypeOnsetStrengths[typeIndex] / _recentTypeOnsetStrengths[typeIndex].Count;
        }

        // Check for onset specific to this type
        var hasEnoughPause = (currentTimeMs - _lastAnyOnsetDetectionTimes[typeIndex]) > MinOnsetIntervalMs;
        var hasEnoughStrength = currentStrength > averageStrength * band.OnSetThresholdFactor * 1.4f;

        return hasEnoughPause && hasEnoughStrength;
    }

    private static double BarDurationMs => (60000.0 / _currentBpm) * 4.0;

    private sealed record FrequencyBand(FrequencyRangeType Type, int StartBand, int EndBand, float OnSetThresholdFactor);

    private record struct RhythmicTemplate(float NormalizedBarPosition, float ImpactWeight, float ToleranceMs);

    private sealed record Onset(double TimeMs, float Amplitude, FrequencyRangeType Type);

    private enum FrequencyRangeType
    {
        Bass = 0,
        Snare = 1,
        Hihat = 2,
        Undefined = 3 // Keep Undefined last, as it's not for direct indexing
    }

    // Timing
    private static double _currentBpm = 120.0;
    private static double _barTime;

    private static readonly List<Onset> _detectedOnsets = [];

    private static readonly Queue<float>[] _recentTypeOnsetStrengths = new Queue<float>[FrequencyBandCount];
    private static readonly float[] _totalTypeOnsetStrengths = new float[FrequencyBandCount];
    private static readonly double[] _lastAnyOnsetDetectionTimes = new double[FrequencyBandCount];

    private const int FrequencyBandCount = (int)FrequencyRangeType.Undefined;

    private const int OnsetHistoryWindowSizeFrames = 200;
    private static bool _initialized;

    // Configuration Constants & PID Gains
    private const double MinBpm = 50.0;
    private const double MaxBpm = 190.0;
    private const double MinOnsetIntervalMs = 50.0;

    private static readonly FrequencyBand[] _bands =
        [
            new(FrequencyRangeType.Bass, 0, 7, 3.5f),
            new(FrequencyRangeType.Snare, 4, 26, 3.0f),
            new(FrequencyRangeType.Hihat, 20, 31, 3.0f),
        ];

    // Rhythmic Template Definition (now an array)
    private static readonly List<RhythmicTemplate>[] _onsetRhythmicTemplates =
        [
            // Bass
                [
                    new RhythmicTemplate { NormalizedBarPosition = 0.00f, ImpactWeight = 1.3f, ToleranceMs = 50 },
                    new RhythmicTemplate { NormalizedBarPosition = 0.25f, ImpactWeight = 0.5f, ToleranceMs = 50 },
                    new RhythmicTemplate { NormalizedBarPosition = 0.50f, ImpactWeight = 0.8f, ToleranceMs = 50 },
                    new RhythmicTemplate { NormalizedBarPosition = 0.75f, ImpactWeight = 0.5f, ToleranceMs = 50 },
                    // new RhythmicTemplate { NormalizedBarPosition = 0.125f, ImpactWeight = 0.3f, ToleranceMs = 30 },
                    // new RhythmicTemplate { NormalizedBarPosition = 0.375f, ImpactWeight = 0.3f, ToleranceMs = 30 },
                    // new RhythmicTemplate { NormalizedBarPosition = 0.625f, ImpactWeight = 0.3f, ToleranceMs = 30 },
                    // new RhythmicTemplate { NormalizedBarPosition = 0.875f, ImpactWeight = 0.3f, ToleranceMs = 30 }
                ],
            // Snare
                [
                    new RhythmicTemplate { NormalizedBarPosition = 0.25f, ImpactWeight = 1.0f, ToleranceMs = 50 },
                    new RhythmicTemplate { NormalizedBarPosition = 0.75f, ImpactWeight = 1.0f, ToleranceMs = 50 },
                    new RhythmicTemplate { NormalizedBarPosition = 0.00f, ImpactWeight = 0.3f, ToleranceMs = 30 },
                    new RhythmicTemplate { NormalizedBarPosition = 0.50f, ImpactWeight = 0.3f, ToleranceMs = 30 }
                ],
            // Hihat
                [
                    new RhythmicTemplate { NormalizedBarPosition = 0.000f, ImpactWeight = 1.0f, ToleranceMs = 60 },
                    new RhythmicTemplate { NormalizedBarPosition = 0.125f, ImpactWeight = 0.7f, ToleranceMs = 60 },
                    new RhythmicTemplate { NormalizedBarPosition = 0.250f, ImpactWeight = 0.5f, ToleranceMs = 60 },
                    new RhythmicTemplate { NormalizedBarPosition = 0.375f, ImpactWeight = 0.7f, ToleranceMs = 60 },
                    new RhythmicTemplate { NormalizedBarPosition = 0.500f, ImpactWeight = 0.5f, ToleranceMs = 60 },
                    new RhythmicTemplate { NormalizedBarPosition = 0.625f, ImpactWeight = 0.7f, ToleranceMs = 60 },
                    new RhythmicTemplate { NormalizedBarPosition = 0.750f, ImpactWeight = 0.5f, ToleranceMs = 60 },
                    new RhythmicTemplate { NormalizedBarPosition = 0.875f, ImpactWeight = 0.7f, ToleranceMs = 60 }
                ]
        ];
}