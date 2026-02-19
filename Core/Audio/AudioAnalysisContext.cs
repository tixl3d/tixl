#nullable enable
using System;
using System.Collections.Generic;

namespace T3.Core.Audio;

/// <summary>
/// Holds all buffers and state for audio analysis (FFT, waveform, frequency bands).
/// 
/// <para><b>Thread Safety:</b></para>
/// This class is NOT thread-safe. All access to a single instance must be synchronized
/// externally if used from multiple threads. The default <see cref="Default"/> instance
/// is designed for single-threaded use on the main update loop.
/// 
/// <para><b>MultiThreading Migration Path:</b></para>
/// To enable multithreaded audio analysis:
/// <list type="number">
///   <item>Create separate <see cref="AudioAnalysisContext"/> instances per thread/consumer</item>
///   <item>Pass the context explicitly to analysis methods instead of using <see cref="Default"/></item>
///   <item>Ensure BASS channel reads are synchronized (BASS itself may have thread constraints)</item>
///   <item>Use locks or concurrent collections if sharing results between threads</item>
/// </list>
/// 
/// <para><b>Example - Per-Thread Analysis:</b></para>
/// <code>
/// // Create a dedicated context for background analysis
/// var backgroundContext = new AudioAnalysisContext();
/// 
/// // On background thread:
/// lock (bassLock)
/// {
///     AudioEngine.UpdateFftBuffer(streamHandle, backgroundContext);
/// }
/// backgroundContext.ProcessFftUpdate();
/// 
/// // Access results from backgroundContext.FrequencyBands, etc.
/// </code>
/// </summary>
public sealed class AudioAnalysisContext
{
    /// <summary>
    /// The default context used by the main thread audio update loop.
    /// This is the instance used when no explicit context is provided.
    /// 
    /// <para><b>Warning:</b> Only access this from the main thread. For multithreaded
    /// analysis, create separate instances.</para>
    /// </summary>
    internal static AudioAnalysisContext Default { get; } = new();

    #region FFT Buffers

    /// <summary>
    /// Raw FFT gain values from BASS. Written by <see cref="AudioEngine.UpdateFftBufferFromSoundtrack"/>.
    /// </summary>
    internal readonly float[] FftGainBuffer = new float[AudioConfig.FftBufferSize];

    /// <summary>
    /// FFT values converted to dB and normalized to 0-1 range.
    /// </summary>
    internal readonly float[] FftNormalizedBuffer = new float[AudioConfig.FftBufferSize];

    #endregion

    #region Frequency Band Analysis

    /// <summary>
    /// Current frequency band levels (0-1 normalized).
    /// </summary>
    internal readonly float[] FrequencyBands = new float[AudioConfig.FrequencyBandCount];

    /// <summary>
    /// Peak-hold values for frequency bands with decay.
    /// </summary>
    internal readonly float[] FrequencyBandPeaks = new float[AudioConfig.FrequencyBandCount];

    /// <summary>
    /// Attack values for frequency bands (rate of increase).
    /// </summary>
    internal readonly float[] FrequencyBandAttacks = new float[AudioConfig.FrequencyBandCount];

    /// <summary>
    /// Peak attack values with slower decay.
    /// </summary>
    internal readonly float[] FrequencyBandAttackPeaks = new float[AudioConfig.FrequencyBandCount];

    /// <summary>
    /// Onset detection values for beat synchronization.
    /// </summary>
    internal readonly float[] FrequencyBandOnSets = new float[AudioConfig.FrequencyBandCount];

    // Internal state for frequency band processing
    private readonly float[] _frequencyBandsPrevious = new float[AudioConfig.FrequencyBandCount];
    private readonly float[] _frequencyBandAverages = new float[AudioConfig.FrequencyBandCount];
    private readonly float[] _bandStrengthSums = new float[AudioConfig.FrequencyBandCount];
    private readonly Queue<float>[] _frequencyBandHistories;

    #endregion

    #region Waveform Buffers

    /// <summary>
    /// Interleaved stereo sample buffer from BASS. Written by <see cref="AudioEngine.UpdateFftBufferFromSoundtrack"/>.
    /// </summary>
    internal readonly float[] InterleavedSampleBuffer = new float[AudioConfig.WaveformSampleCount * 2];

    /// <summary>
    /// Result code from last BASS waveform data fetch.
    /// </summary>
    internal int LastWaveformFetchResult;

    /// <summary>
    /// Left channel waveform samples.
    /// </summary>
    internal readonly float[] WaveformLeftBuffer = new float[AudioConfig.WaveformSampleCount];

    /// <summary>
    /// Right channel waveform samples.
    /// </summary>
    internal readonly float[] WaveformRightBuffer = new float[AudioConfig.WaveformSampleCount];

    /// <summary>
    /// Low-frequency waveform (filtered).
    /// </summary>
    internal readonly float[] WaveformLowBuffer = new float[AudioConfig.WaveformSampleCount];

    /// <summary>
    /// Mid-frequency waveform (filtered).
    /// </summary>
    internal readonly float[] WaveformMidBuffer = new float[AudioConfig.WaveformSampleCount];

    /// <summary>
    /// High-frequency waveform (filtered).
    /// </summary>
    internal readonly float[] WaveformHighBuffer = new float[AudioConfig.WaveformSampleCount];

    /// <summary>
    /// Whether waveform data has been requested by an operator this session.
    /// </summary>
    internal bool WaveformRequested;

    // Filter state for waveform processing (maintains continuity between frames)
    internal float LowFilterY1;
    internal float MidHighPassY1;
    internal float MidHighPassX1;
    internal float MidLowPassY1;
    internal float HighFilterY1;
    internal float HighFilterX1;

    // Temporary buffers for waveform filtering
    internal readonly float[] MidFilterBuffer = new float[AudioConfig.WaveformSampleCount];
    internal readonly float[] TempBuffer = new float[AudioConfig.WaveformSampleCount];

    // Export accumulation buffer
    internal readonly float[] ExportAccumulationBuffer = new float[AudioConfig.WaveformSampleCount * 2];

    #endregion

    #region Frame Tracking

    /// <summary>
    /// Frame number when waveform was last updated (prevents duplicate updates per frame).
    /// </summary>
    internal int LastWaveformUpdateFrame = -1;

    #endregion

    /// <summary>
    /// Creates a new audio analysis context with freshly allocated buffers.
    /// </summary>
    private AudioAnalysisContext()
    {
        _frequencyBandHistories = new Queue<float>[AudioConfig.FrequencyBandCount];
        for (var i = 0; i < AudioConfig.FrequencyBandCount; i++)
        {
            _frequencyBandHistories[i] = new Queue<float>(FrequencyBandHistoryLength);
        }
    }

    /// <summary>
    /// Resets all buffers and state to initial values.
    /// Useful when starting a new analysis session or switching audio sources.
    /// </summary>
    internal void Reset()
    {
        Array.Clear(FftGainBuffer, 0, FftGainBuffer.Length);
        Array.Clear(FftNormalizedBuffer, 0, FftNormalizedBuffer.Length);
        Array.Clear(FrequencyBands, 0, FrequencyBands.Length);
        Array.Clear(FrequencyBandPeaks, 0, FrequencyBandPeaks.Length);
        Array.Clear(FrequencyBandAttacks, 0, FrequencyBandAttacks.Length);
        Array.Clear(FrequencyBandAttackPeaks, 0, FrequencyBandAttackPeaks.Length);
        Array.Clear(FrequencyBandOnSets, 0, FrequencyBandOnSets.Length);
        Array.Clear(_frequencyBandsPrevious, 0, _frequencyBandsPrevious.Length);
        Array.Clear(_frequencyBandAverages, 0, _frequencyBandAverages.Length);
        Array.Clear(_bandStrengthSums, 0, _bandStrengthSums.Length);

        foreach (var queue in _frequencyBandHistories)
            queue.Clear();

        Array.Clear(InterleavedSampleBuffer, 0, InterleavedSampleBuffer.Length);
        Array.Clear(WaveformLeftBuffer, 0, WaveformLeftBuffer.Length);
        Array.Clear(WaveformRightBuffer, 0, WaveformRightBuffer.Length);
        Array.Clear(WaveformLowBuffer, 0, WaveformLowBuffer.Length);
        Array.Clear(WaveformMidBuffer, 0, WaveformMidBuffer.Length);
        Array.Clear(WaveformHighBuffer, 0, WaveformHighBuffer.Length);
        Array.Clear(MidFilterBuffer, 0, MidFilterBuffer.Length);
        Array.Clear(TempBuffer, 0, TempBuffer.Length);
        Array.Clear(ExportAccumulationBuffer, 0, ExportAccumulationBuffer.Length);

        LowFilterY1 = 0;
        MidHighPassY1 = 0;
        MidHighPassX1 = 0;
        MidLowPassY1 = 0;
        HighFilterY1 = 0;
        HighFilterX1 = 0;

        LastWaveformFetchResult = 0;
        LastWaveformUpdateFrame = -1;
        WaveformRequested = false;
    }

    #region FFT Processing

    private const float EstimatedAudioUpdatePeriod = 0.003f;
    private const int FrequencyBandHistoryLength = (int)(1 / EstimatedAudioUpdatePeriod);

    /// <summary>
    /// Processes the FFT gain buffer to compute frequency bands, peaks, attacks, and onsets.
    /// Call this after <see cref="FftGainBuffer"/> has been populated with FFT data.
    /// </summary>
    /// <param name="gainFactor">Multiplier for FFT gain values.</param>
    /// <param name="decayFactor">Decay factor for peak values (0-1, higher = slower decay).</param>
    internal void ProcessFftUpdate(float gainFactor = 1f, float decayFactor = 0.9f)
    {
        var lastTargetIndex = -1;

        lock (FrequencyBands)
        {
            for (var binIndex = 0; binIndex < AudioConfig.FftBufferSize; binIndex++)
            {
                var gain = FftGainBuffer[binIndex] * gainFactor;
                var gainDb = gain <= 0.000001f ? float.NegativeInfinity : 20 * MathF.Log10(gain);

                var normalizedValue = RemapAndClamp(gainDb, -80, 0, 0, 1);
                FftNormalizedBuffer[binIndex] = normalizedValue;

                var bandIndex = _bandIndexForFftBin[binIndex];
                if (bandIndex == NoBandIndex)
                    continue;

                if (bandIndex != lastTargetIndex)
                {
                    FrequencyBands[bandIndex] = 0;
                    lastTargetIndex = bandIndex;
                }

                FrequencyBands[bandIndex] = MathF.Max(FrequencyBands[bandIndex], normalizedValue);
            }
        }

        UpdateSlidingWindowAverages();

        lock (FrequencyBandPeaks)
        {
            for (var bandIndex = 0; bandIndex < AudioConfig.FrequencyBandCount; bandIndex++)
            {
                // Compute attacks
                {
                    var lastPeak = FrequencyBandPeaks[bandIndex];
                    var decayed = lastPeak * decayFactor;
                    var currentValue = FrequencyBands[bandIndex];
                    var newPeak = MathF.Max(decayed, currentValue);
                    FrequencyBandPeaks[bandIndex] = newPeak;

                    const float attackAmplification = 4;
                    var newAttack = Clamp((newPeak - lastPeak) * attackAmplification, 0, 10000);
                    var lastAttackDecayed = FrequencyBandAttacks[bandIndex] * decayFactor;
                    FrequencyBandAttacks[bandIndex] = MathF.Max(newAttack, lastAttackDecayed);
                }

                FrequencyBandAttackPeaks[bandIndex] = MathF.Max(FrequencyBandAttackPeaks[bandIndex] * 0.995f, FrequencyBandAttacks[bandIndex]);

                // Compute onsets for beat synchronization
                {
                    var lastValue = _frequencyBandsPrevious[bandIndex];
                    var smoothed = _frequencyBandAverages[bandIndex];
                    var newValueAboveAverage = FrequencyBands[bandIndex] - smoothed;
                    _frequencyBandsPrevious[bandIndex] = newValueAboveAverage;

                    var delta = Clamp((newValueAboveAverage - lastValue) * 2, 0, 1000);
                    FrequencyBandOnSets[bandIndex] = delta;
                }
            }
        }
    }

    private void UpdateSlidingWindowAverages()
    {
        for (var i = 0; i < AudioConfig.FrequencyBandCount; i++)
        {
            var currentStrength = FrequencyBands[i];
            _frequencyBandHistories[i].Enqueue(currentStrength);
            _bandStrengthSums[i] += currentStrength;

            if (_frequencyBandHistories[i].Count > FrequencyBandHistoryLength)
            {
                _bandStrengthSums[i] -= _frequencyBandHistories[i].Dequeue();
            }

            var averageStrength = 0f;
            if (_frequencyBandHistories[i].Count > 0)
            {
                averageStrength = _bandStrengthSums[i] / _frequencyBandHistories[i].Count;
            }

            _frequencyBandAverages[i] = averageStrength;
        }
    }

    #endregion

    #region Static Lookup Table

    private const int NoBandIndex = -1;

    /// <summary>
    /// Lookup table mapping FFT bin indices to frequency band indices.
    /// Shared across all contexts since it's read-only configuration data.
    /// </summary>
    private static readonly int[] _bandIndexForFftBin = InitializeBandLookupTable();

    private static int[] InitializeBandLookupTable()
    {
        var r = new int[AudioConfig.FftBufferSize];
        const float lowestBandFrequency = 55;
        const float highestBandFrequency = 15000;

        var maxOctave = MathF.Log2(highestBandFrequency / lowestBandFrequency);
        for (var i = 0; i < AudioConfig.FftBufferSize; i++)
        {
            var bandIndex = NoBandIndex;
            var freq = (float)i / AudioConfig.FftBufferSize * (AudioConfig.MixerFrequency / 2f);

            switch (i)
            {
                case 0:
                    break;
                case < 6:
                    bandIndex = i - 1;
                    break;
                default:
                {
                    var octave = MathF.Log2(freq / lowestBandFrequency);
                    var octaveNormalized = octave / maxOctave;
                    bandIndex = (int)(octaveNormalized * AudioConfig.FrequencyBandCount);
                    if (bandIndex >= AudioConfig.FrequencyBandCount)
                        bandIndex = NoBandIndex;
                    break;
                }
            }

            r[i] = bandIndex;
        }

        return r;
    }

    #endregion

    #region Helper Methods

    private static float RemapAndClamp(float value, float inMin, float inMax, float outMin, float outMax)
    {
        var t = (value - inMin) / (inMax - inMin);
        t = MathF.Max(0, MathF.Min(1, t));
        return outMin + t * (outMax - outMin);
    }

    private static float Clamp(float value, float min, float max)
    {
        return MathF.Max(min, MathF.Min(max, value));
    }

    #endregion
}
