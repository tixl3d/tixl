using System.Runtime.InteropServices;
using Sdcb.FFmpeg.Formats;
using Sdcb.FFmpeg.Raw;
using T3.Core.Video;
using Xunit;

namespace T3.VideoServices.Tests;

/// <summary>
/// Exercises the real FFmpeg encode path: encode synthetic RGBA frames to a file, then decode it back with
/// <see cref="VideoDecoderSession"/> and assert the round-trip metadata + frame count. Uses MPEG-4 Part 2
/// (<c>mpeg4</c>) — a software, always-present LGPL encoder — so the test needs no GPU and proves the
/// licence-clean path (software H.264 is libx264 = GPL and absent from the shipped build).
/// </summary>
public class VideoFileEncoderTests
{
    [Fact]
    public void Encode_Mpeg4_RoundTripsThroughDecoder()
    {
        const int width = 320;
        const int height = 240;
        const int frameCount = 30;
        var path = Path.Combine(Path.GetTempPath(), $"tixl-encode-{Guid.NewGuid():N}.mp4");

        try
        {
            var settings = new VideoEncoderSettings
                               {
                                   FilePath = path,
                                   Width = width,
                                   Height = height,
                                   FrameRate = new AVRational(30, 1),
                                   BitRate = 2_000_000,
                                   VideoCodecId = AVCodecID.Mpeg4,
                                   SourceFormat = AVPixelFormat.Rgba,
                                   SourceBytesPerPixel = 4,
                               };

            var frame = new byte[width * height * 4];
            using (var encoder = new VideoFileEncoder(settings))
            {
                for (var i = 0; i < frameCount; i++)
                {
                    FillGradient(frame, width, height, i);
                    encoder.WriteVideoFrame(frame, width * 4);
                }
            } // Dispose → Finish: drains the encoder and writes the container trailer.

            Assert.True(File.Exists(path));
            Assert.True(new FileInfo(path).Length > 0, "encoded file is empty");

            using var session = VideoDecoderSession.TryOpen(path, VideoPlaybackOptimization.FastSeeking, out var error);
            Assert.Null(error);
            Assert.NotNull(session);
            Assert.Equal(width, session!.Width);
            Assert.Equal(height, session.Height);

            var decoded = 0;
            while (session.TryReadNextFrame(out _))
                decoded++;

            // All frames must survive the round-trip (allow a 1-frame slack for encoder/muxer flush quirks).
            Assert.True(decoded >= frameCount - 1, $"decoded {decoded} frames, expected ~{frameCount}");
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    [Fact]
    public void Encode_ProRes_RoundTripsThroughDecoder()
    {
        const int width = 320;
        const int height = 240;
        const int frameCount = 30;
        var path = Path.Combine(Path.GetTempPath(), $"tixl-encode-prores-{Guid.NewGuid():N}.mov");

        try
        {
            var settings = new VideoEncoderSettings
                               {
                                   FilePath = path,
                                   Width = width,
                                   Height = height,
                                   FrameRate = new AVRational(30, 1),
                                   BitRate = 0, // ProRes derives its own rate from the profile
                                   VideoCodecId = AVCodecID.Prores,
                                   EncoderPixelFormat = AVPixelFormat.Yuv422p10le, // ProRes rejects 4:2:0
                                   SourceFormat = AVPixelFormat.Rgba,
                                   SourceBytesPerPixel = 4,
                               };

            var frame = new byte[width * height * 4];
            using (var encoder = new VideoFileEncoder(settings))
            {
                for (var i = 0; i < frameCount; i++)
                {
                    FillGradient(frame, width, height, i);
                    encoder.WriteVideoFrame(frame, width * 4);
                }
            }

            Assert.True(File.Exists(path));
            Assert.True(new FileInfo(path).Length > 0, "encoded file is empty");

            using var session = VideoDecoderSession.TryOpen(path, VideoPlaybackOptimization.FastSeeking, out var error);
            Assert.Null(error);
            Assert.NotNull(session);
            Assert.Equal(width, session!.Width);
            Assert.Equal(height, session.Height);

            var decoded = 0;
            while (session.TryReadNextFrame(out _))
                decoded++;
            Assert.True(decoded >= frameCount - 1, $"decoded {decoded} ProRes frames, expected ~{frameCount}");
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    [Fact]
    public void HardwareEncoder_WhenAvailable_RoundTripsThroughDecoder()
    {
        var encoderName = HardwareEncoderProbe.H264HardwareEncoder;
        if (encoderName == null)
            return; // No usable hardware encoder on this machine (e.g. GPU-less CI) — nothing to verify here.

        const int width = 320;
        const int height = 240;
        const int frameCount = 30;
        var path = Path.Combine(Path.GetTempPath(), $"tixl-encode-hw-{Guid.NewGuid():N}.mp4");

        try
        {
            var settings = new VideoEncoderSettings
                               {
                                   FilePath = path,
                                   Width = width,
                                   Height = height,
                                   FrameRate = new AVRational(30, 1),
                                   BitRate = 4_000_000,
                                   VideoEncoderName = encoderName,
                                   SourceFormat = AVPixelFormat.Rgba,
                                   SourceBytesPerPixel = 4,
                               };

            var frame = new byte[width * height * 4];
            using (var encoder = new VideoFileEncoder(settings))
            {
                for (var i = 0; i < frameCount; i++)
                {
                    FillGradient(frame, width, height, i);
                    encoder.WriteVideoFrame(frame, width * 4);
                }
            }

            using var session = VideoDecoderSession.TryOpen(path, VideoPlaybackOptimization.FastSeeking, out var error);
            Assert.Null(error);
            Assert.NotNull(session);
            Assert.Equal(width, session!.Width);
            Assert.Equal(height, session.Height);

            var decoded = 0;
            while (session.TryReadNextFrame(out _))
                decoded++;
            Assert.True(decoded >= frameCount - 1, $"decoded {decoded} frames via {encoderName}, expected ~{frameCount}");
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    [Fact]
    public void Encode_WithAudio_MuxesVideoAndAacStreams()
    {
        const int width = 320;
        const int height = 240;
        const int frameCount = 30;
        const int fps = 30;
        const int sampleRate = 48000;
        const int channels = 2;
        var path = Path.Combine(Path.GetTempPath(), $"tixl-encode-av-{Guid.NewGuid():N}.mp4");

        try
        {
            var settings = new VideoEncoderSettings
                               {
                                   FilePath = path,
                                   Width = width,
                                   Height = height,
                                   FrameRate = new AVRational(fps, 1),
                                   BitRate = 2_000_000,
                                   VideoCodecId = AVCodecID.Mpeg4,
                                   SourceFormat = AVPixelFormat.Rgba,
                                   SourceBytesPerPixel = 4,
                                   EncodeAudio = true,
                                   AudioSampleRate = sampleRate,
                                   AudioChannels = channels,
                                   AudioBitRate = 192_000,
                               };

            var videoFrame = new byte[width * height * 4];
            var samplesPerVideoFrame = sampleRate / fps;
            var audioChunk = new byte[samplesPerVideoFrame * channels * sizeof(float)];

            using (var encoder = new VideoFileEncoder(settings))
            {
                for (var i = 0; i < frameCount; i++)
                {
                    FillGradient(videoFrame, width, height, i);
                    encoder.WriteVideoFrame(videoFrame, width * 4);

                    FillSine(audioChunk, samplesPerVideoFrame, channels, i, sampleRate);
                    encoder.WriteAudioSamples(audioChunk);
                }
            }

            using var fc = FormatContext.OpenInputUrl(path, null, null);
            fc.LoadStreamInfo();

            var video = fc.FindBestStreamOrNull(AVMediaType.Video);
            Assert.NotNull(video);

            var audio = fc.FindBestStreamOrNull(AVMediaType.Audio);
            Assert.NotNull(audio);
            var audioParams = audio!.Value.Codecpar;
            Assert.NotNull(audioParams);
            Assert.Equal(AVCodecID.Aac, audioParams!.CodecId);
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    // One video frame's worth of a 440 Hz stereo sine, as interleaved float32 bytes.
    private static void FillSine(byte[] buffer, int samplesPerChannel, int channels, int frameIndex, int sampleRate)
    {
        var samples = MemoryMarshal.Cast<byte, float>(buffer.AsSpan());
        var baseSample = (long)frameIndex * samplesPerChannel;
        for (var i = 0; i < samplesPerChannel; i++)
        {
            var t = (baseSample + i) / (double)sampleRate;
            var value = (float)(Math.Sin(2 * Math.PI * 440 * t) * 0.2);
            for (var ch = 0; ch < channels; ch++)
                samples[i * channels + ch] = value;
        }
    }

    // A moving gradient so successive frames differ (gives the encoder real motion to compress).
    private static void FillGradient(byte[] rgba, int width, int height, int frameIndex)
    {
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var o = (y * width + x) * 4;
                rgba[o + 0] = (byte)((x + frameIndex * 4) & 0xFF);
                rgba[o + 1] = (byte)((y + frameIndex * 2) & 0xFF);
                rgba[o + 2] = (byte)((frameIndex * 8) & 0xFF);
                rgba[o + 3] = 255;
            }
        }
    }
}
