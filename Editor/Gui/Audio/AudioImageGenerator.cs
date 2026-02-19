#nullable enable
using System.Diagnostics.CodeAnalysis;
using System.Drawing;
using System.IO;
using ManagedBass;
using T3.Core.Audio;
using T3.Core.Resource;
using T3.Core.Resource.Assets;
using T3.Core.Utils;
using T3.Editor.Gui.UiHelpers;

namespace T3.Editor.Gui.Audio;

internal static class AudioImageGenerator
{
    public static bool TryGenerateSoundSpectrumAndVolume(SoundtrackClipDefinition clip, IResourceConsumer? instance, [NotNullWhen(true)] out string? imagePathAbsolute)
    {
        var relativePath = clip.FilePath;
        if (relativePath == null)
        {
            Log.Error("Audio clip has no file path");
            imagePathAbsolute = null;
            return false;
        }
            
        if (!AssetRegistry.TryResolveAddress(relativePath, instance, out var soundFilePathAbsolute, out _))
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

        // Use the offline analysis stream from AudioMixerManager instead of calling Bass.Init()
        // This prevents interference with live playback and operator audio streams
        var stream = AudioMixerManager.CreateOfflineAnalysisStream(soundFilePathAbsolute);
        if (stream == 0)
        {
            Log.Error($"Failed to create offline analysis stream for: {soundFilePathAbsolute}");
            imagePathAbsolute = null;
            return false;
        }

        try
        {
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

            // Note: For decode-only streams, we don't call ChannelPlay - we just read data directly

            var spectrumImage = new Bitmap((int)numSamples, ImageHeight);

            var intensityPalette = IntensityPalette;

            var logarithms = PrecomputedLogs;
            var f = (float)(FftBufferSize / logarithms[ImageHeight + 1]);
            var f2 = (float)((PaletteSize - 1) / Math.Log(MaxIntensity + 1));

            var logarithmicExponent = UserSettings.Config.ExpandSpectrumVisualizerVertically ? 10d : Math.E;
            var precalculatedLogMultiplier = 1d / Math.Log(logarithmicExponent) * f;

            const int channelLength = (int)DataFlags.FFT2048;
            var fftBuffer = new float[FftBufferSize];

            int logCounter = 0;

            for (var sampleIndex = 0; sampleIndex < numSamples; ++sampleIndex)
            {
                // Seek to position and get FFT data (decode-only streams use SetPosition, not Play)
                Bass.ChannelSetPosition(stream, sampleIndex * sampleLength);
                Bass.ChannelGetData(stream, fftBuffer, channelLength);

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
                    spectrumImage.SetPixel(sampleIndex, rowIndex, intensityPalette[palettePos]);
                }

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
                spectrumImage.Save(imagePathAbsolute);
                success = true;
            }
            catch (Exception e)
            {
                success = false;
                Log.Error(e.Message);
            }

            return success;
        }
        finally
        {
            // Always free the offline analysis stream
            AudioMixerManager.FreeOfflineAnalysisStream(stream);
        }
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
                                          alpha: RoundToInt(Math.Min(1f, pos01 / upperThreshold) * maxColorValue),
                                          red: RoundToInt(posThreshold01Clamped * maxColorValue),
                                          green: RoundToInt(Math.Max(0f, pos01 - 1f) * maxColorValue),
                                          blue: RoundToInt(Math.Min(pos01 * lowerThresholdInv, 1f - posThreshold01Clamped) * maxColorValue)
                                         );
        }
            
        return palette;
            
        int RoundToInt(float value) => (int)Math.Round(value);
    }

    private sealed class PreComputedLogs
    {
        private double[] _logEvaluations = new double[ImageHeight + 2];
            
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

    private const int FftBufferSize = 1024;
    private const int ImageHeight = 256;
    private const float MaxIntensity = 500;
    private const int ColorSteps = 255;
    private const int PaletteSize = 3 * ColorSteps;
}