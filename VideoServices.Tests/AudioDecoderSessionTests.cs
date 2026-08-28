using System.Runtime.InteropServices;
using Sdcb.FFmpeg.Raw;
using T3.Core.Video;
using Xunit;

namespace T3.VideoServices.Tests;

/// <summary>
/// Exercises the real FFmpeg audio decode + resample path by round-tripping a known tone: encode a video
/// with an AAC track carrying a 440 Hz sine, decode it back through <see cref="AudioDecoderSession"/>, and
/// assert the PCM that comes out is the same tone, at the requested rate, with correct timing.
/// </summary>
public class AudioDecoderSessionTests
{
    private const int ToneHz = 440;
    private const int EncodedSampleRate = 48000;
    private const double ToneSeconds = 2.0;

    [Fact]
    public void Open_FileWithoutAudioTrack_ReturnsNullWithoutError()
    {
        // The checked-in samples are video-only. "No audio" is a normal outcome, not a failure — the caller
        // must be able to tell it apart from a broken file.
        using var session = AudioDecoderSession.TryOpen(TestAssets.Video720p, 48000, out var error);

        Assert.Null(session);
        Assert.Null(error);
    }

    [Fact]
    public void Open_MissingFile_ReturnsError()
    {
        using var session = AudioDecoderSession.TryOpen("does-not-exist.mp4", 48000, out var error);

        Assert.Null(session);
        Assert.NotNull(error);
    }

    [Fact]
    public void Decode_RoundTripsTheEncodedTone()
    {
        var path = EncodeToneClip();
        try
        {
            using var session = AudioDecoderSession.TryOpen(path, EncodedSampleRate, out var error);
            Assert.Null(error);
            Assert.NotNull(session);
            Assert.Equal(EncodedSampleRate, session!.OutputSampleRate);
            Assert.InRange(session.DurationSeconds, ToneSeconds - 0.2, ToneSeconds + 0.2);

            var left = DecodeAll(session, out var firstChunkStart, out var totalSeconds);

            Assert.InRange(firstChunkStart, -0.05, 0.05);
            Assert.InRange(totalSeconds, ToneSeconds - 0.2, ToneSeconds + 0.2);
            Assert.InRange(left.Count / (double)EncodedSampleRate, ToneSeconds - 0.2, ToneSeconds + 0.2);
            Assert.InRange(EstimateFrequency(left, EncodedSampleRate), ToneHz - 5, ToneHz + 5);
        }
        finally
        {
            TryDelete(path);
        }
    }

    [Fact]
    public void Decode_ResamplesToTheRequestedRate()
    {
        const int targetRate = 44100;
        var path = EncodeToneClip();
        try
        {
            using var session = AudioDecoderSession.TryOpen(path, targetRate, out _);
            Assert.NotNull(session);

            var left = DecodeAll(session!, out _, out var totalSeconds);

            // Same tone and same wall-clock length, but fewer samples — that is the resampler working.
            Assert.InRange(totalSeconds, ToneSeconds - 0.2, ToneSeconds + 0.2);
            Assert.InRange(left.Count / (double)targetRate, ToneSeconds - 0.2, ToneSeconds + 0.2);
            Assert.InRange(EstimateFrequency(left, targetRate), ToneHz - 5, ToneHz + 5);
        }
        finally
        {
            TryDelete(path);
        }
    }

    [Fact]
    public void SeekTo_ReportsThePositionItLandedOn()
    {
        var path = EncodeToneClip();
        try
        {
            using var session = AudioDecoderSession.TryOpen(path, EncodedSampleRate, out _);
            Assert.NotNull(session);

            session!.SeekTo(1.0);
            Assert.True(session.TryDecodeChunk(out var samples, out var startSeconds));
            Assert.False(samples.IsEmpty);

            // A seek lands on a packet boundary at or before the request, so being slightly early is expected —
            // being late, or reporting 0 (an un-anchored timestamp), is not.
            Assert.InRange(startSeconds, 0.85, 1.02);
        }
        finally
        {
            TryDelete(path);
        }
    }

    // Encodes a short MPEG-4 clip whose AAC track is a steady 440 Hz stereo sine.
    private static string EncodeToneClip()
    {
        const int width = 160;
        const int height = 120;
        const int fps = 30;
        const int channels = 2;
        var frameCount = (int)(ToneSeconds * fps);
        var path = Path.Combine(Path.GetTempPath(), $"tixl-audio-{Guid.NewGuid():N}.mp4");

        var settings = new VideoEncoderSettings
                           {
                               FilePath = path,
                               Width = width,
                               Height = height,
                               FrameRate = new AVRational(fps, 1),
                               BitRate = 1_000_000,
                               VideoCodecId = AVCodecID.Mpeg4,
                               SourceFormat = AVPixelFormat.Rgba,
                               SourceBytesPerPixel = 4,
                               EncodeAudio = true,
                               AudioSampleRate = EncodedSampleRate,
                               AudioChannels = channels,
                               AudioBitRate = 192_000,
                           };

        var videoFrame = new byte[width * height * 4];
        var samplesPerVideoFrame = EncodedSampleRate / fps;
        var audioChunk = new byte[samplesPerVideoFrame * channels * sizeof(float)];

        using (var encoder = new VideoFileEncoder(settings))
        {
            for (var i = 0; i < frameCount; i++)
            {
                encoder.WriteVideoFrame(videoFrame, width * 4);
                FillSine(audioChunk, samplesPerVideoFrame, channels, i, EncodedSampleRate);
                encoder.WriteAudioSamples(audioChunk);
            }
        }

        return path;
    }

    // Drains the session, returning the left channel and reporting the timeline the chunks describe.
    private static List<float> DecodeAll(AudioDecoderSession session, out double firstChunkStart, out double totalSeconds)
    {
        var left = new List<float>();
        firstChunkStart = double.NaN;
        var lastStart = 0.0;
        var lastLengthSeconds = 0.0;

        while (session.TryDecodeChunk(out var samples, out var startSeconds))
        {
            Assert.Equal(0, samples.Length % AudioDecoderSession.OutputChannels);
            if (double.IsNaN(firstChunkStart))
                firstChunkStart = startSeconds;
            else
                Assert.True(startSeconds >= lastStart, $"chunk start went backwards: {startSeconds} after {lastStart}");

            for (var i = 0; i < samples.Length; i += AudioDecoderSession.OutputChannels)
                left.Add(samples[i]);

            lastStart = startSeconds;
            lastLengthSeconds = samples.Length / (double)AudioDecoderSession.OutputChannels / session.OutputSampleRate;
        }

        if (double.IsNaN(firstChunkStart))
            firstChunkStart = 0;

        totalSeconds = lastStart + lastLengthSeconds - firstChunkStart;
        return left;
    }

    // Frequency from signed crossings, ignoring near-zero samples so the encoder's priming silence and
    // quantisation noise don't register as crossings.
    private static double EstimateFrequency(List<float> mono, int sampleRate)
    {
        const float threshold = 0.05f;
        var lastSign = 0;
        var crossings = 0;
        var firstCrossing = -1;
        var lastCrossing = -1;

        for (var i = 0; i < mono.Count; i++)
        {
            var sign = mono[i] > threshold ? 1 : mono[i] < -threshold ? -1 : 0;
            if (sign == 0)
                continue;

            if (lastSign != 0 && sign != lastSign)
            {
                crossings++;
                if (firstCrossing < 0)
                    firstCrossing = i;
                lastCrossing = i;
            }

            lastSign = sign;
        }

        if (crossings < 2)
            return 0;

        // The span between the first and last crossing holds (crossings - 1) half periods.
        var seconds = (lastCrossing - firstCrossing) / (double)sampleRate;
        return (crossings - 1) / 2.0 / seconds;
    }

    private static void FillSine(byte[] buffer, int samplesPerChannel, int channels, int frameIndex, int sampleRate)
    {
        var samples = MemoryMarshal.Cast<byte, float>(buffer.AsSpan());
        var baseSample = (long)frameIndex * samplesPerChannel;
        for (var i = 0; i < samplesPerChannel; i++)
        {
            var t = (baseSample + i) / (double)sampleRate;
            var value = (float)(Math.Sin(2 * Math.PI * ToneHz * t) * 0.2);
            for (var ch = 0; ch < channels; ch++)
                samples[i * channels + ch] = value;
        }
    }

    private static void TryDelete(string path)
    {
        if (File.Exists(path))
            File.Delete(path);
    }
}
