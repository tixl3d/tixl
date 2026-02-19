#nullable enable

using System;
using T3.Core.Animation;
using T3.Core.Operator;

namespace T3.Core.Audio;

/// <summary>
/// Converts sample data from BASS into a list of buffers that can then be used by Operators like [AudioWaveform].
/// 
/// <para><b>Note:</b> This static class delegates to <see cref="AudioAnalysisContext.Default"/>.
/// For multi-threaded analysis, create separate <see cref="AudioAnalysisContext"/> instances.</para>
/// </summary>
public static class WaveFormProcessing
{
    private static AudioAnalysisContext Context => AudioAnalysisContext.Default;

    #region Buffer Accessors (delegate to default context)

    /// <summary>
    /// Interleaved stereo sample buffer from BASS.
    /// Delegates to <see cref="AudioAnalysisContext.Default"/>.
    /// </summary>
    internal static float[] InterleavenSampleBuffer => Context.InterleavedSampleBuffer;

    /// <summary>
    /// Result code from last BASS waveform data fetch.
    /// </summary>
    internal static int LastFetchResultCode
    {
        get => Context.LastWaveformFetchResult;
        set => Context.LastWaveformFetchResult = value;
    }

    /// <summary>
    /// To avoid unnecessary processing we only fetch wave data from BASS when requested once from an Operator.
    /// </summary>
    internal static bool RequestedOnce
    {
        get => Context.WaveformRequested;
        set => Context.WaveformRequested = value;
    }

    /// <summary>
    /// Left channel waveform samples.
    /// Delegates to <see cref="AudioAnalysisContext.Default"/>.
    /// </summary>
    public static float[] WaveformLeftBuffer => Context.WaveformLeftBuffer;

    /// <summary>
    /// Right channel waveform samples.
    /// Delegates to <see cref="AudioAnalysisContext.Default"/>.
    /// </summary>
    public static float[] WaveformRightBuffer => Context.WaveformRightBuffer;

    /// <summary>
    /// Low frequency waveform (filtered).
    /// Delegates to <see cref="AudioAnalysisContext.Default"/>.
    /// </summary>
    public static float[] WaveformLowBuffer => Context.WaveformLowBuffer;

    /// <summary>
    /// Mid frequency waveform (filtered).
    /// Delegates to <see cref="AudioAnalysisContext.Default"/>.
    /// </summary>
    public static float[] WaveformMidBuffer => Context.WaveformMidBuffer;

    /// <summary>
    /// High frequency waveform (filtered).
    /// Delegates to <see cref="AudioAnalysisContext.Default"/>.
    /// </summary>
    public static float[] WaveformHighBuffer => Context.WaveformHighBuffer;

    #endregion

    /// <summary>
    /// Needs to be called from Operators that want to access Waveform Data.
    /// It will prevent multiple updates per frame.
    /// Uses the default context.
    /// </summary>
    public static void UpdateWaveformData()
    {
        UpdateWaveformData(Context);
    }

    /// <summary>
    /// Updates waveform data for a specific context.
    /// </summary>
    /// <param name="context">The analysis context to update</param>
    public static void UpdateWaveformData(AudioAnalysisContext context)
    {
        context.WaveformRequested = true;

        // Prevent multiple updates
        if (Playback.FrameCount == context.LastWaveformUpdateFrame)
            return;

        context.LastWaveformUpdateFrame = Playback.FrameCount;

        if (context.LastWaveformFetchResult <= 0)
            return;

        // Note: During export, waveform data is populated via PopulateFromExportBuffer()
        // which is called in AudioRendering.GetFullMixDownBuffer(). That buffer contains
        // operator audio regardless of audio source mode. So we don't need to check
        // for external audio mode here - the data is already valid.

        var idx = 0;
        for (var it = 0; it < context.InterleavedSampleBuffer.Length;)
        {
            context.WaveformLeftBuffer[idx] = context.InterleavedSampleBuffer[it++];
            context.WaveformRightBuffer[idx] = context.InterleavedSampleBuffer[it++];
            idx += 1;
        }

        // Apply improved filters to create frequency-separated waveforms
        ProcessFilteredWaveformsImproved(context);
    }

    private struct FilterCoefficients(float a, float b)
    {
        public readonly float A = a;
        public readonly float B = b;
    }

    private static FilterCoefficients CalculateLowPassCoeffs(float cutoffFreq)
    {
        float sampleRate = AudioConfig.MixerFrequency;
        var rc = 1.0f / (2.0f * MathF.PI * cutoffFreq);
        var dt = 1.0f / sampleRate;
        var alpha = dt / (rc + dt);
        return new FilterCoefficients(alpha, 1.0f - alpha);
    }

    private static FilterCoefficients CalculateHighPassCoeffs(float cutoffFreq)
    {
        float sampleRate = AudioConfig.MixerFrequency;
        float rc = 1.0f / (2.0f * MathF.PI * cutoffFreq);
        float dt = 1.0f / sampleRate;
        float alpha = rc / (rc + dt);
        return new FilterCoefficients(alpha, alpha);
    }

    private static void ApplyLowPassFilterImproved(float[] input, float[] output, FilterCoefficients coeffs, ref float y1)
    {
        for (int i = 0; i < input.Length; i++)
        {
            output[i] = coeffs.A * input[i] + coeffs.B * y1;
            y1 = output[i];
        }
    }

    private static void ApplyHighPassFilterImproved(float[] input, float[] output, FilterCoefficients coeffs, ref float y1, ref float x1)
    {
        for (int i = 0; i < input.Length; i++)
        {
            output[i] = coeffs.A * (y1 + input[i] - x1);
            y1 = output[i];
            x1 = input[i];
        }
    }

    private static void ProcessFilteredWaveformsImproved(AudioAnalysisContext context)
    {
        // Create mono mix for filtering (reuse temp buffer)
        for (int i = 0; i < AudioConfig.WaveformSampleCount; i++)
        {
            context.TempBuffer[i] = (context.WaveformLeftBuffer[i] + context.WaveformRightBuffer[i]) * 0.5f;
        }

        // Apply filters with state preservation for better continuity
        // Low frequencies: Pure low-pass at 250Hz
        ApplyLowPassFilterImproved(context.TempBuffer, context.WaveformLowBuffer, _lowPassCoeffs, ref context.LowFilterY1);

        // High frequencies: Pure high-pass at 2000Hz
        ApplyHighPassFilterImproved(context.TempBuffer, context.WaveformHighBuffer, _highPassCoeffs, ref context.HighFilterY1, ref context.HighFilterX1);

        // Mid-frequencies: High-pass at 250Hz, then low-pass at 2000Hz (band-pass)
        ApplyHighPassFilterImproved(context.TempBuffer, context.MidFilterBuffer, _midHighPassCoeffs, ref context.MidHighPassY1, ref context.MidHighPassX1);
        ApplyLowPassFilterImproved(context.MidFilterBuffer, context.WaveformMidBuffer, _midLowPassCoeffs, ref context.MidLowPassY1);
    }

    // Improved filter coefficients (calculated once, shared across all contexts)
    private static readonly FilterCoefficients _lowPassCoeffs = CalculateLowPassCoeffs(AudioConfig.LowPassCutoffFrequency);
    private static readonly FilterCoefficients _midHighPassCoeffs = CalculateHighPassCoeffs(AudioConfig.LowPassCutoffFrequency);
    private static readonly FilterCoefficients _midLowPassCoeffs = CalculateLowPassCoeffs(AudioConfig.HighPassCutoffFrequency);
    private static readonly FilterCoefficients _highPassCoeffs = CalculateHighPassCoeffs(AudioConfig.HighPassCutoffFrequency);

    /// <summary>
    /// Populates the waveform buffers from an export mixdown buffer.
    /// Accumulates samples across frames to provide a rolling window of audio data.
    /// Called during export to provide waveform data to AudioWaveform operator.
    /// Uses the default context.
    /// </summary>
    /// <param name="mixBuffer">Interleaved stereo float buffer from export mixdown</param>
    public static void PopulateFromExportBuffer(float[] mixBuffer)
    {
        PopulateFromExportBuffer(mixBuffer, Context);
    }

    /// <summary>
    /// Populates the waveform buffers from an export mixdown buffer for a specific context.
    /// </summary>
    /// <param name="mixBuffer">Interleaved stereo float buffer from export mixdown</param>
    /// <param name="context">The analysis context to populate</param>
    public static void PopulateFromExportBuffer(float[] mixBuffer, AudioAnalysisContext context)
    {
        if (mixBuffer == null || mixBuffer.Length < 2)
            return;

        int interleavedSampleCount = AudioConfig.WaveformSampleCount * 2;

        // Shift existing data to make room for new samples
        int samplesToAdd = Math.Min(mixBuffer.Length, interleavedSampleCount);
        int samplesToShift = interleavedSampleCount - samplesToAdd;

        if (samplesToShift > 0)
        {
            // Shift old data left
            Array.Copy(context.ExportAccumulationBuffer, samplesToAdd, context.ExportAccumulationBuffer, 0, samplesToShift);
        }

        // Add new samples at the end
        int sourceStartIndex = Math.Max(0, mixBuffer.Length - samplesToAdd);
        Array.Copy(mixBuffer, sourceStartIndex, context.ExportAccumulationBuffer, samplesToShift, samplesToAdd);

        // Copy to the interleaved sample buffer
        Array.Copy(context.ExportAccumulationBuffer, 0, context.InterleavedSampleBuffer, 0, interleavedSampleCount);

        context.LastWaveformFetchResult = interleavedSampleCount;
    }

    /// <summary>
    /// Resets the export accumulation buffer. Should be called when starting a new export.
    /// Uses the default context.
    /// </summary>
    public static void ResetExportBuffer()
    {
        ResetExportBuffer(Context);
    }

    /// <summary>
    /// Resets the export accumulation buffer for a specific context.
    /// </summary>
    /// <param name="context">The analysis context to reset</param>
    public static void ResetExportBuffer(AudioAnalysisContext context)
    {
        Array.Clear(context.ExportAccumulationBuffer, 0, context.ExportAccumulationBuffer.Length);
        Array.Clear(context.InterleavedSampleBuffer, 0, context.InterleavedSampleBuffer.Length);
    }
}

