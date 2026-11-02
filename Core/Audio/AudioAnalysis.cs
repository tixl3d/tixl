using System;

namespace T3.Core.Audio;

/// <summary>
/// Analyze audio from the internal soundtrack or a selected input device.
/// Transforms the FFT buffer and generates frequency bands and peaks.
/// 
/// <para><b>Note:</b> This static class delegates to <see cref="AudioAnalysisContext.Default"/>.
/// For multithreaded analysis, create separate <see cref="AudioAnalysisContext"/> instances.</para>
/// </summary>
public static class AudioAnalysis
{
    private static AudioAnalysisContext Context => AudioAnalysisContext.Default;

    /// <summary>
    /// Processes FFT data to compute frequency bands, peaks, and attacks.
    /// Delegates to <see cref="AudioAnalysisContext.Default"/>.
    /// </summary>
    internal static void ProcessUpdate(float gainFactor = 1f, float decayFactor = 0.9f)
    {
        Context.ProcessFftUpdate(gainFactor, decayFactor);
    }

    #region Buffer Accessors (delegate to default context)

    /// <summary>
    /// Result of the FFT analysis in gain. Delegates to <see cref="AudioAnalysisContext.Default"/>.
    /// </summary>
    public static float[] FftGainBuffer => Context.FftGainBuffer;

    /// <summary>
    /// Result of the FFT analysis converted to dB and mapped to normalized range.
    /// Delegates to <see cref="AudioAnalysisContext.Default"/>.
    /// </summary>
    public static float[] FftNormalizedBuffer => Context.FftNormalizedBuffer;

    /// <summary>
    /// Current frequency band levels (0-1 normalized).
    /// Delegates to <see cref="AudioAnalysisContext.Default"/>.
    /// </summary>
    public static float[] FrequencyBands => Context.FrequencyBands;

    /// <summary>
    /// Peak-hold values for frequency bands with decay.
    /// Delegates to <see cref="AudioAnalysisContext.Default"/>.
    /// </summary>
    public static float[] FrequencyBandPeaks => Context.FrequencyBandPeaks;

    /// <summary>
    /// Attack values for frequency bands (rate of increase).
    /// Delegates to <see cref="AudioAnalysisContext.Default"/>.
    /// </summary>
    public static float[] FrequencyBandAttacks => Context.FrequencyBandAttacks;

    /// <summary>
    /// Peak attack values with slower decay.
    /// Delegates to <see cref="AudioAnalysisContext.Default"/>.
    /// </summary>
    public static float[] FrequencyBandAttackPeaks => Context.FrequencyBandAttackPeaks;

    /// <summary>
    /// Onset detection values for beat synchronization.
    /// Delegates to <see cref="AudioAnalysisContext.Default"/>.
    /// </summary>
    internal static float[] FrequencyBandOnSets => Context.FrequencyBandOnSets;

    #endregion

    internal const int FftBufferSize = 1024;
}
