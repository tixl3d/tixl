#nullable enable
using System.Diagnostics.CodeAnalysis;
using System.Drawing;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using ManagedBass;
using T3.Core.Audio;
using T3.Core.Resource;
using T3.Core.Resource.Assets;
using T3.Core.Settings;
using T3.Core.Utils;
using T3.Editor.Gui.UiHelpers;

namespace T3.Editor.Gui.Audio;

internal static class AudioImageGenerator
{
    public static bool TryGenerateClipImage(TimelineAudioClip clip, IResourceConsumer? instance, AudioClipStyle style, [NotNullWhen(true)] out string? imagePathAbsolute)
    {
        var relativePath = clip.AssetPath;
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

        // Cache waveform images in a per-user temp folder instead of next to the audio file -
        // they are fully derived/temporary and shouldn't pollute the user's project assets.
        // (The ".waveform" suffix for the spectrum style is historical — it keeps existing caches valid.)
        var imageVariantSuffix = style switch
                                     {
                                         AudioClipStyle.Waveform => ".peaks.png",
                                         AudioClipStyle.VolumeLevel => ".loudness.png",
                                         _ => UserSettings.Config.ExpandSpectrumVisualizerVertically ? ".10.waveform.png" : ".waveform.png",
                                     };
        imagePathAbsolute = GetWaveformImageCachePath(soundFilePathAbsolute, imageVariantSuffix);

        if (File.Exists(imagePathAbsolute))
        {
            Log.Debug($"Reusing sound image file: {imagePathAbsolute}");
            return true;
        }

        Log.Debug($"Generating {imagePathAbsolute}...");

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(imagePathAbsolute)!);
        }
        catch (Exception e)
        {
            Log.Error($"Failed to create soundtrack image cache directory: {e.Message}");
            imagePathAbsolute = null;
            return false;
        }

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
            if (style != AudioClipStyle.Spectrum)
                return GenerateAmplitudeImage(stream, style, ref imagePathAbsolute);
            var streamLength = Bass.ChannelGetLength(stream);

            const double samplingResolution = 1.0 / 100;

            var sampleLength = Bass.ChannelSeconds2Bytes(stream, samplingResolution);
            // Short samples (e.g. a one-shot drum hit) can yield <1 column at 0.01s resolution;
            // a zero/negative width makes `new Bitmap(...)` throw. Use at least one column so the
            // generator works for short clips, not just long soundtracks.
            var numSamples = sampleLength > 0 ? streamLength / sampleLength : 0;
            if (numSamples < 1)
            {
                if (streamLength <= 0)
                {
                    Log.Warning($"Audio file is empty or unreadable for waveform image: {soundFilePathAbsolute}");
                    imagePathAbsolute = null;
                    return false;
                }
                numSamples = 1;
            }

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

    /// <summary>
    /// Renders an amplitude-based image (peak waveform or RMS volume envelope) by sequentially decoding
    /// the stream in ~10 ms columns. White-on-transparent so renderers can tint it to the theme.
    /// </summary>
    private static bool GenerateAmplitudeImage(int stream, AudioClipStyle style, ref string? imagePathAbsolute)
    {
        var streamLength = Bass.ChannelGetLength(stream);

        const double samplingResolution = 1.0 / 100;
        var sampleLength = Bass.ChannelSeconds2Bytes(stream, samplingResolution);
        var numSamples = sampleLength > 0 ? streamLength / sampleLength : 0;
        if (numSamples < 1)
        {
            if (streamLength <= 0)
            {
                Log.Warning("Audio file is empty or unreadable for amplitude image");
                imagePathAbsolute = null;
                return false;
            }

            numSamples = 1;
        }

        const int maxSamples = 16384; // 4k texture size limit
        if (numSamples > maxSamples)
        {
            sampleLength = (long)(sampleLength * numSamples / (double)maxSamples) + 100;
            numSamples = streamLength / sampleLength;
        }

        var columns = (int)numSamples;
        var peaks = new float[columns];
        var rms = new float[columns];
        var sampleBuffer = new float[sampleLength / sizeof(float) + 1];

        Bass.ChannelSetPosition(stream, 0);
        for (var column = 0; column < columns; column++)
        {
            // The offline stream decodes as 32-bit float (BassFlags.Float), so a plain byte count reads floats.
            var bytesRead = Bass.ChannelGetData(stream, sampleBuffer, (int)sampleLength);
            if (bytesRead <= 0)
                break;

            var floatCount = Math.Min(bytesRead / sizeof(float), sampleBuffer.Length);
            var peak = 0f;
            var sumOfSquares = 0.0;
            for (var i = 0; i < floatCount; i++)
            {
                var sample = Math.Abs(sampleBuffer[i]);
                if (sample > peak)
                    peak = sample;

                sumOfSquares += sample * sample;
            }

            peaks[column] = Math.Min(peak, 1f);
            rms[column] = floatCount > 0 ? Math.Min((float)Math.Sqrt(sumOfSquares / floatCount), 1f) : 0f;
        }

        var image = new Bitmap(columns, ImageHeight);
        var solid = Color.FromArgb(230, 255, 255, 255);
        var soft = Color.FromArgb(90, 255, 255, 255);

        if (style == AudioClipStyle.Waveform)
        {
            // Single symmetric peak silhouette around the vertical center — one clean shape that stays
            // readable at small clip heights (a separate RMS band read as visual noise there).
            const int center = ImageHeight / 2;
            const int halfHeight = ImageHeight / 2 - 1;
            for (var column = 0; column < columns; column++)
            {
                var peakRows = (int)(peaks[column] * halfHeight);
                for (var row = -peakRows; row <= peakRows; row++)
                    image.SetPixel(column, center + row, solid);
            }
        }
        else
        {
            // Volume-level envelope: a bottom-filled RMS curve, lightly smoothed across columns and
            // normalized to the file's loudest moment — raw RMS of even normalized audio only reaches
            // ~10-30% of full scale, which read as a barely-visible sliver.
            var maxRms = 0f;
            for (var column = 0; column < columns; column++)
            {
                if (rms[column] > maxRms)
                    maxRms = rms[column];
            }

            var normalize = maxRms > 0.001f ? 1f / maxRms : 1f;
            for (var column = 0; column < columns; column++)
            {
                var left = rms[Math.Max(column - 1, 0)];
                var right = rms[Math.Min(column + 1, columns - 1)];
                var level = Math.Min((left + rms[column] * 2 + right) * 0.25f * normalize, 1f);

                var rows = (int)(level * (ImageHeight - 2));
                for (var row = 0; row < rows; row++)
                    image.SetPixel(column, ImageHeight - 1 - row, row >= rows - 2 ? solid : soft);
            }
        }

        try
        {
            image.Save(imagePathAbsolute!);
            return true;
        }
        catch (Exception e)
        {
            Log.Error(e.Message);
            imagePathAbsolute = null;
            return false;
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

    /// <summary>
    /// Builds a stable cache path for the waveform image of the given audio file inside
    /// %AppData%\TiXL\Tmp\SoundtrackImages. The filename hashes the absolute audio path so two
    /// soundtracks with the same name in different folders never collide, and the original
    /// filename is kept as a prefix to make the temp folder readable.
    /// </summary>
    private static string GetWaveformImageCachePath(string soundFilePathAbsolute, string variantSuffix)
    {
        var normalized = soundFilePathAbsolute.Replace('\\', '/');
        var hashBytes = SHA1.HashData(Encoding.UTF8.GetBytes(normalized.ToLowerInvariant()));
        var hash = Convert.ToHexString(hashBytes).AsSpan(0, 16).ToString();

        var safePrefix = Path.GetFileNameWithoutExtension(soundFilePathAbsolute);
        foreach (var invalid in Path.GetInvalidFileNameChars())
            safePrefix = safePrefix.Replace(invalid, '_');

        return Path.Combine(SoundtrackImageCacheFolder, $"{safePrefix}.{hash}{variantSuffix}");
    }

    private static readonly string SoundtrackImageCacheFolder = Path.Combine(FileLocations.TempFolder, "SoundtrackImages");

    private static readonly PreComputedLogs PrecomputedLogs = new();
    private static readonly Color[] IntensityPalette = GeneratePalette();

    private const int FftBufferSize = 1024;
    private const int ImageHeight = 256;
    private const float MaxIntensity = 500;
    private const int ColorSteps = 255;
    private const int PaletteSize = 3 * ColorSteps;
}