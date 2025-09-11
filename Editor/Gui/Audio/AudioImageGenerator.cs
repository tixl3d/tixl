#nullable enable
using ManagedBass;
using Newtonsoft.Json.Linq;
using System.Diagnostics.CodeAnalysis;
using System.Drawing;
using System.IO;
using T3.Core.Audio;
using T3.Core.Resource;
using T3.Core.Utils;
using T3.Editor.Gui.UiHelpers;

namespace T3.Editor.Gui.Audio;

internal static class AudioImageGenerator
{
    public static bool TryGenerateSoundSpectrumAndVolume(AudioClipDefinition clip, IResourceConsumer? instance, [NotNullWhen(true)] out string? imagePathAbsolute)
    {
        var relativePath = clip.FilePath;
        if (relativePath == null)
        {
            Log.Error("Audio clip has no file path");
            imagePathAbsolute = null;
            return false;
        }

        if (!ResourceManager.TryResolvePath(relativePath, instance, out var soundFilePathAbsolute, out _))
        {
            Log.Error($"Could not get absolute path for audio clip: {relativePath}");
            imagePathAbsolute = null;
            return false;
        }

        string imageExtension = UserSettings.Config.ExpandSpectrumVisualizerVertically ? ".10.waveform.png" : ".waveform.png";
        var imageFilePath = clip.FilePath! + imageExtension;
        imagePathAbsolute = soundFilePathAbsolute + imageExtension;

        if (File.Exists(imagePathAbsolute))
        {
            Log.Debug($"Reusing sound image file: {imageFilePath}");
            return true;
        }

        Log.Debug($"Generating {imageFilePath}...");

        Bass.Init(-1, 44100, 0, IntPtr.Zero);
        var stream = Bass.CreateStream(soundFilePathAbsolute, 0, 0, BassFlags.Decode | BassFlags.Prescan);

        var streamLength = Bass.ChannelGetLength(stream);

        const double samplingResolution = 1.0 / 100;

        var sampleLength = Bass.ChannelSeconds2Bytes(stream, samplingResolution);
        var numSamples = streamLength / sampleLength;

        const int maxSamples = 16384; // 4k texture size limit
        if (numSamples > maxSamples)
        {
            sampleLength = (long)(sampleLength * numSamples / (double)maxSamples) + 100;
            numSamples = streamLength / sampleLength;
            Log.Debug($"Limiting texture size to {numSamples} samples");
        }

        Bass.ChannelPlay(stream);

        // Double the height to accommodate both spectrum and waveform
        var combinedImage = new Bitmap((int)numSamples, ImageHeight * 2);

        var intensityPalette = IntensityPalette;
        var waveformPalette = WaveformPalette;

        var logarithms = PrecomputedLogs;
        var f = (float)(FftBufferSize / logarithms[ImageHeight + 1]);
        var f2 = (float)((PaletteSize - 1) / Math.Log(MaxIntensity + 1));

        var logarithmicExponent = UserSettings.Config.ExpandSpectrumVisualizerVertically ? 10d : Math.E;
        var precalculatedLogMultiplier = 1d / Math.Log(logarithmicExponent) * f;

        const int channelLength = (int)DataFlags.FFT2048;
        var fftBuffer = new float[FftBufferSize];

        // Buffer for raw audio samples for waveform
        var audioBuffer = new float[1024]; // Adjust size as needed

        int logCounter = 0;

        for (var sampleIndex = 0; sampleIndex < numSamples; ++sampleIndex)
        {
            Bass.ChannelSetPosition(stream, sampleIndex * sampleLength);

            // Get FFT data for spectrum
            Bass.ChannelGetData(stream, fftBuffer, channelLength);

            // Get raw audio data for waveform
            Bass.ChannelSetPosition(stream, sampleIndex * sampleLength);
            Bass.ChannelGetData(stream, audioBuffer, (int)DataFlags.Float);

            // Generate spectrum (top half)
            for (var rowIndex = 0; rowIndex < ImageHeight; ++rowIndex)
            {
                const int spectrumLengthMinusOne = FftBufferSize - 1;
                const int imageHeightMinusOne = ImageHeight - 1;

                var j = (int)(f * logarithms[rowIndex + 1]);

                bool rowIndexInBounds = rowIndex is > 0 and < imageHeightMinusOne;
                int pj, nj;

                if (rowIndexInBounds)
                {
                    pj = (int)(logarithms[rowIndex] * precalculatedLogMultiplier);
                    nj = (int)(logarithms[rowIndex + 2] * precalculatedLogMultiplier);
                }
                else
                {
                    pj = nj = j;
                }

                var intensity = 125.0f * fftBuffer[spectrumLengthMinusOne - pj] +
                                750.0f * fftBuffer[spectrumLengthMinusOne - j] +
                                125.0f * fftBuffer[spectrumLengthMinusOne - nj];

                intensity = Math.Clamp(intensity, 0f, MaxIntensity) + 1;

                var palettePos = (int)(f2 * Math.Log(intensity));
                combinedImage.SetPixel(sampleIndex, rowIndex, intensityPalette[palettePos]);
            }
            var samplesRead = Bass.ChannelGetData(stream, audioBuffer, audioBuffer.Length * sizeof(float) | (int)DataFlags.Float);
            var actualSamplesCount = samplesRead / sizeof(float);
            // Generate waveform (bottom half)
            GenerateWaveformColumn(combinedImage, sampleIndex, audioBuffer, actualSamplesCount);

            if (++logCounter > 1000)
            {
                logCounter = 0;
                var percentage = sampleIndex / (float)numSamples;
                Log.Debug($"   computing sound image {percentage:P1}% complete");
            }
        }

        bool success;
        try
        {
            combinedImage.Save(imagePathAbsolute);
            success = true;
        }
        catch (Exception e)
        {
            success = false;
            Log.Error(e.Message);
        }

        Bass.ChannelStop(stream);
        Bass.StreamFree(stream);

        return success;
    }

    private static void GenerateWaveformColumn(Bitmap image, int sampleIndex, float[] audioBuffer, int sampleCount)
    {
        const int waveformHeight = ImageHeight;
        const int waveformStartY = ImageHeight; // Start at the bottom of the spectrum
        const int waveformCenterY = waveformStartY + waveformHeight / 2;

        if (sampleCount <= 0)
        {
            // Fill with background if no samples
            for (int y = waveformStartY; y < waveformStartY + waveformHeight; y++)
            {
                image.SetPixel(sampleIndex, y, Color.FromArgb(0, 16, 16, 16)); // Transparent dark background
            }
            return;
        }

        // Calculate RMS (Root Mean Square) and peak for this sample chunk
        float rms = 0f;
        float peak = 0f;

        for (int i = 0; i < sampleCount; i++)
        {
            float sample = Math.Abs(audioBuffer[i]);
            rms += sample * sample;
            peak = Math.Max(peak, sample);
        }
        rms = (float)Math.Sqrt(rms / sampleCount);

        // Use the maximum value found so far to normalize
        const float maxExpectedAmplitude = 1.1f;
        const float headroom = 1.3f; // 20% headroom to handle occasional peaks

        // Normalize to the expected range
        float normalizedPeak = Math.Clamp(peak / maxExpectedAmplitude, 0f, 1f) * headroom;
        float normalizedRms = Math.Clamp(rms / maxExpectedAmplitude, 0f, 1f) * headroom;

        // Scale to pixel height (use 95% of available space for headroom)
        var maxAmplitudePixels = (int)((waveformHeight / 2) * 0.70f);
        var peakAmplitudePixels = (int)(normalizedPeak * maxAmplitudePixels);
        var rmsAmplitudePixels = (int)(normalizedRms * maxAmplitudePixels);

        // Calculate color based on RMS volume
        Color rmsColor = GetVolumeColor(normalizedRms);
        Color peakColor = GetVolumeColor(normalizedPeak);

        // Clear the column first (solid dark background)
        for (int y = waveformStartY; y < waveformStartY + waveformHeight; y++)
        {
            image.SetPixel(sampleIndex, y, Color.FromArgb(0, 16, 16, 16)); // transparent dark background
        }
        // Draw RMS as a solid filled area with volume - based color
if (rmsAmplitudePixels > 0)
        {
            for (int offset = -rmsAmplitudePixels; offset <= rmsAmplitudePixels; offset++)
            {
                int y = waveformCenterY + offset;
                if (y >= waveformStartY && y < waveformStartY + waveformHeight)
                {
                    image.SetPixel(sampleIndex, y, rmsColor);
                }
            }
        }

        // Draw peak waveform additively blended with existing colors
        if (peakAmplitudePixels > 0)
        {
            // Draw positive peak
            for (int offset = 0; offset <= peakAmplitudePixels; offset++)
            {
                int y = waveformCenterY - offset;
                if (y >= waveformStartY && y < waveformStartY + waveformHeight)
                {
                    var existingColor = image.GetPixel(sampleIndex, y);
                    var blendedColor = BlendColors(existingColor, peakColor);
                    image.SetPixel(sampleIndex, y, blendedColor);
                }
            }

            // Draw negative peak
            for (int offset = 0; offset <= peakAmplitudePixels; offset++)
            {
                int y = waveformCenterY + offset;
                if (y >= waveformStartY && y < waveformStartY + waveformHeight)
                {
                    var existingColor = image.GetPixel(sampleIndex, y);
                    var blendedColor = BlendColors(existingColor, peakColor);
                    image.SetPixel(sampleIndex, y, blendedColor);
                }
            }
        }

        // Draw center line for reference
        if (waveformCenterY >= waveformStartY && waveformCenterY < waveformStartY + waveformHeight)
        {
            image.SetPixel(sampleIndex, waveformCenterY, Color.FromArgb(255, 64, 64, 64)); // Solid gray
        }

        // For very low amplitude signals, draw a minimal indicator
        if (peakAmplitudePixels == 0 && rmsAmplitudePixels == 0 && peak > 0)
        {
            Color quietColor = GetVolumeColor(normalizedRms);
            image.SetPixel(sampleIndex, waveformCenterY - 1, quietColor);
            image.SetPixel(sampleIndex, waveformCenterY + 1, quietColor);
        }
    }

    private static Color BlendColors(Color existing, Color newColor)
    {
        // Additive blending with clamping to prevent overflow
        int r = Math.Min(255, existing.R + newColor.R);
        int g = Math.Min(255, existing.G + newColor.G);
        int b = Math.Min(255, existing.B + newColor.B);
        int a = Math.Max(existing.A, newColor.A); // Use maximum alpha

        return Color.FromArgb(a, r, g, b);
    }

    private static Color GetVolumeColor(float normalizedVolume)
    {
        // Clamp volume to 0-1 range
        float volume = Math.Clamp(normalizedVolume, 0f, 1f);

        // Color gradient with red only in top 1%:
        // Quiet: 0-0.5 (50% of range) - blue to green
        // Medium: 0.5-0.99 (49% of range) - green to yellow to orange
        // Loud: 0.99-1.0 (1% of range) - orange to red

        if (volume < 0.5f) // Quiet - blue to green (50% of range)
        {
            float t = volume / 0.5f;
            return Color.FromArgb(128,
                (int)(0),                                    // R
                (int)(t * 128),                              // G
                (int)(128 * (1 - t))                         // B
            );
        }
        else if (volume < 0.99f) // Medium - green to yellow to orange (49% of range)
        {
            float t = (volume - 0.5f) / 0.49f;
            return Color.FromArgb(128,
                (int)(t * 255),                              // R
                (int)(128 + t * 127),                        // G
                (int)(0)                                     // B
            );
        }
        else // Loud - orange to red (1% of range)
        {
            float t = (volume - 0.99f) / 0.01f;
            return Color.FromArgb(128,
                (int)((128 + t * 127)),                                  // R
                (int)(0),                        // G
                (int)(128)                                     // B
            );
        }
    }

    private static Color GetVolumeColorFire(float normalizedVolume)
    {
        float volume = Math.Clamp(normalizedVolume, 0f, 1f);
        if (volume < 0.5f)
        {
            float t = volume / 0.5f;
            return Color.FromArgb(255, (int)(255 * t), (int)(128 * t), 0);
        }
        else
        {
            float t = (volume - 0.5f) / 0.5f;
            return Color.FromArgb(255, 255, (int)(128 * (1 - t)), 0);
        }
    }

    private static Color GetVolumeColorIce(float normalizedVolume)
    {
        float volume = Math.Clamp(normalizedVolume, 0f, 1f);
        return Color.FromArgb(128,
            (int)(volume * 128),
            (int)(64 + volume * 127),
            (int)(128 - volume * 128)
        );
    }

    private static Color[] GeneratePalette()
    {
        var palette = new Color[PaletteSize];

        const float upperThreshold = 2 / 3f;
        const float lowerThreshold = 1 / 3f;
        const float lowerThresholdInv = 1 / lowerThreshold;

        const int maxColorValue = 255;

        for (var pos = 0; pos < PaletteSize; ++pos)
        {
            var pos01 = MathUtils.Remap(pos, 0, PaletteSize, 0f, 1f);
            var posThreshold01Clamped = Math.Clamp(
                                                   value: MathUtils.Remap(pos01, lowerThreshold, upperThreshold, 0f, 1f),
                                                   min: 0f,
                                                   max: 1f);

            palette[pos] = Color.FromArgb(
                                          // fraction of the upper threshold
                                          alpha: RoundToInt(Math.Min(1f, pos01 / upperThreshold) * maxColorValue),

                                          // normalized between lower and upper thresholds
                                          red: RoundToInt(posThreshold01Clamped * maxColorValue),

                                          // distance above upperThreshold
                                          green: RoundToInt(Math.Max(0f, pos01 - 1f) * maxColorValue),

                                          // distance from threshold it is below
                                          blue: RoundToInt(Math.Min(pos01 * lowerThresholdInv, 1f - posThreshold01Clamped) * maxColorValue)
                                         );
        }

        return palette;

        int RoundToInt(float value) => (int)Math.Round(value);
    }

    private static Color[] GenerateWaveformPalette()
    {
        var palette = new Color[256];

        for (int i = 0; i < 256; i++)
        {
            var intensity = i / 255f;
            palette[i] = Color.FromArgb(
                (int)(intensity * 255),     // Alpha
                (int)(intensity * 100),     // Red
                (int)(intensity * 180),     // Green
                (int)(intensity * 255)      // Blue
            );
        }

        return palette;
    }

    private sealed class PreComputedLogs
    {
        // ReSharper disable once FieldCanBeMadeReadOnly.Local
        private double[] _logEvaluations = new double[ImageHeight + 2];

        // index accessor
        public double this[int index] => _logEvaluations[index];

        public PreComputedLogs()
        {
            for (var i = 0; i < _logEvaluations.Length; ++i)
            {
                _logEvaluations[i] = Math.Log(i + 1);
            }
        }
    }

    private static readonly PreComputedLogs PrecomputedLogs = new();
    private static readonly Color[] IntensityPalette = GeneratePalette();
    private static readonly Color[] WaveformPalette = GenerateWaveformPalette();

    private const int FftBufferSize = 1024;
    private const int ImageHeight = 256;
    private const float MaxIntensity = 500;
    private const int ColorSteps = 255;
    private const int PaletteSize = 3 * ColorSteps;
}