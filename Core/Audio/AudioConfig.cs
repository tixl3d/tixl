
namespace T3.Core.Audio;

/// <summary>
/// Configuration settings for the audio system.
/// Compile-time constants are used for buffer sizes that require static initialization.
/// Runtime settings can be configured through UserSettings in the Editor.
/// </summary>
public static class AudioConfig
{

    #region Mixer Configuration
    // Other values are const because they are used for static array initialization.

    /// <summary>
    /// Sample rate of all mixing (Hz). Fixed rather than following the device, so that exports and analysis
    /// are the same on every machine; the device connection converts where its rate differs.
    /// </summary>
    public const int MixerFrequency = 48000;
    #endregion

    #region 3D Audio Configuration
    /// <summary>
    /// Rolloff factor for 3D audio distance attenuation.
    /// 0 = no rolloff, 1 = real-world rolloff, higher = faster rolloff.
    /// </summary>
    internal static float RolloffFactor { get; set; } = 1.0f;

    /// <summary>
    /// Doppler factor for 3D audio velocity effects.
    /// 0 = no Doppler, 1 = real-world Doppler, higher = exaggerated Doppler.
    /// </summary>
    internal static float DopplerFactor { get; set; } = 1.0f;
    #endregion

    #region FFT and Analysis Configuration (Compile-time Constants)
    // Note: These are const because AudioAnalysis uses them to allocate static arrays.

    /// <summary>
    /// FFT buffer size for frequency analysis.
    /// </summary>
    internal const int FftBufferSize = 1024;

    /// <summary>
    /// Number of frequency bands for audio analysis.
    /// </summary>
    internal const int FrequencyBandCount = 32;

    /// <summary>
    /// Waveform sample buffer size.
    /// </summary>
    internal const int WaveformSampleCount = 1024;

    /// <summary>
    /// Low-pass filter cutoff frequency (Hz) for low frequency separation.
    /// </summary>
    internal const float LowPassCutoffFrequency = 250f;

    /// <summary>
    /// High-pass filter cutoff frequency (Hz) for high frequency separation.
    /// </summary>
    internal const float HighPassCutoffFrequency = 2000f;
    #endregion
}
