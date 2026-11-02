using System;
using System.IO;
using T3.Core.Audio.Native;
using Xunit;

namespace Core.Tests;

/// <summary>Runs the native library offline, so no sound device is needed.</summary>
public class NativeAudioEngineTests
{
    private const int SampleRate = 48000;

    /** Sounds with pitch enabled pass through a linear resampler, which holds one frame back even at pitch 1. */
    private const int ResamplerLatencyFrames = 1;

    [Fact]
    public void RenderedAudioMatchesTheFileAfterSeeking()
    {
        var path = WriteRampWav(seconds: 2);
        try
        {
            using var engine = NativeAudioEngine.TryCreate(NativeAudio.Mode.Offline, NativeAudio.Backend.Null, SampleRate);
            Assert.NotNull(engine);

            var source = engine.LoadFile(path);
            Assert.True(source.IsValid);
            Assert.Equal(2.0, engine.GetLengthSecs(source), 3);

            Assert.True(engine.Play(source));
            Assert.True(engine.Seek(source, 1.0));

            var rendered = new float[256 * 2];
            Assert.True(engine.Render(rendered, 256));
            for (var frame = ResamplerLatencyFrames; frame < 256; frame++)
            {
                Assert.Equal(RampValueAt(SampleRate + frame - ResamplerLatencyFrames), rendered[frame * 2], 3);
            }
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void HandleOfUnloadedSoundFailsAfterItsSlotIsReused()
    {
        var path = WriteRampWav(seconds: 1);
        try
        {
            using var engine = NativeAudioEngine.TryCreate(NativeAudio.Mode.Offline, NativeAudio.Backend.Null, SampleRate);
            Assert.NotNull(engine);

            var stale = engine.LoadFile(path);
            engine.Unload(stale);
            var current = engine.LoadFile(path);

            Assert.NotEqual(stale, current);
            Assert.False(engine.Play(stale));
            Assert.True(engine.Play(current));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void CursorFollowsSeekAndRenderedFrames()
    {
        var path = WriteRampWav(seconds: 2);
        try
        {
            using var engine = NativeAudioEngine.TryCreate(NativeAudio.Mode.Offline, NativeAudio.Backend.Null, SampleRate);
            Assert.NotNull(engine);

            var source = engine.LoadFile(path);
            Assert.True(engine.TryGetFormat(source, out var channels, out var sampleRate));
            Assert.Equal(2, channels);
            Assert.Equal(SampleRate, sampleRate);

            Assert.False(engine.IsPlaying(source));
            engine.Play(source);
            Assert.True(engine.IsPlaying(source));

            engine.Seek(source, 0.5);
            Assert.Equal(0.5, engine.GetCursorSecs(source), 6); // a seek not yet applied still reports its target

            var rendered = new float[4800 * 2];
            engine.Render(rendered, 4800);
            Assert.InRange(engine.GetCursorSecs(source), 0.59, 0.61);

            engine.Pause(source);
            Assert.False(engine.IsPlaying(source));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void OutputHistoryHoldsTheRenderedStereoAndMono()
    {
        var path = WriteRampWav(seconds: 1);
        try
        {
            using var engine = NativeAudioEngine.TryCreate(NativeAudio.Mode.Offline, NativeAudio.Backend.Null, SampleRate);
            Assert.NotNull(engine);
            engine.Play(engine.LoadFile(path));

            var rendered = new float[1024 * 2];
            engine.Render(rendered, 1024);

            var stereo = new float[256 * 2];
            var mono = new float[256];
            engine.ReadOutputStereo(stereo);
            engine.ReadOutputMono(mono);

            for (var frame = 0; frame < 256; frame++)
            {
                var renderedFrame = 1024 - 256 + frame;
                Assert.Equal(rendered[renderedFrame * 2], stereo[frame * 2]);
                Assert.Equal(rendered[renderedFrame * 2 + 1], stereo[frame * 2 + 1]);
                Assert.Equal(rendered[renderedFrame * 2], mono[frame], 5);
            }

            Assert.True(engine.TakeOutputPeak() > 0);
            Assert.Equal(0f, engine.TakeOutputPeak());
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void OfflineDecoderReportsFormatAndSeeks()
    {
        var path = WriteRampWav(seconds: 2);
        try
        {
            Assert.True(OfflineAudioDecoder.TryOpen(path, out var decoder));
            using (decoder)
            {
                Assert.Equal(2, decoder.Channels);
                Assert.Equal(SampleRate, decoder.SampleRate);
                Assert.Equal(2.0, decoder.DurationSecs, 6);

                var samples = new float[8];
                Assert.True(decoder.Seek(12345));
                Assert.Equal(4, decoder.Read(samples));
                Assert.Equal(RampValueAt(12345), samples[0], 5);

                decoder.Seek(2 * SampleRate - 2);
                Assert.Equal(2, decoder.Read(samples));
            }

            Assert.False(OfflineAudioDecoder.TryOpen("/does/not/exist.wav", out _));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void FftOfTheOutputHistoryFindsAPlayingSine()
    {
        const int fftSize = 2048;
        const int bin = 100;
        const float amplitude = 0.5f;
        var path = WriteWav(seconds: 1, frame => amplitude * MathF.Sin(2 * MathF.PI * bin * frame / fftSize));
        try
        {
            using var engine = NativeAudioEngine.TryCreate(NativeAudio.Mode.Offline, NativeAudio.Backend.Null, SampleRate);
            Assert.NotNull(engine);
            engine.Play(engine.LoadFile(path));
            engine.Render(new float[4096 * 2], 4096);

            var samples = new float[fftSize];
            var magnitudes = new float[fftSize / 2];
            engine.ReadOutputMono(samples);
            new Fft(fftSize).ComputeMagnitudes(samples, magnitudes);

            Assert.InRange(magnitudes[bin], amplitude * 0.99f, amplitude * 1.01f);
            Assert.True(magnitudes[bin + 10] < amplitude * 0.01f);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void FftOfASineMatchesTheBassScale()
    {
        const int size = 2048;
        const int bin = 100;
        const float amplitude = 0.5f;
        var samples = new float[size];
        for (var index = 0; index < size; index++)
            samples[index] = amplitude * MathF.Sin(2 * MathF.PI * bin * index / size);

        var magnitudes = new float[size / 2];
        new Fft(size).ComputeMagnitudes(samples, magnitudes);

        Assert.InRange(magnitudes[bin], amplitude * 0.99f, amplitude * 1.01f);
    }

    [Fact]
    public void LoadingAMissingFileGivesAnInvalidHandle()
    {
        using var engine = NativeAudioEngine.TryCreate(NativeAudio.Mode.Offline, NativeAudio.Backend.Null, SampleRate);
        Assert.NotNull(engine);
        Assert.False(engine.LoadFile("/does/not/exist.wav").IsValid);
    }

    private static float RampValueAt(int frame) => (frame % 1000) / 1000f - 0.5f;

    /** A sawtooth rather than a tone: every position has its own value, so a late seek can't pass by accident. */
    private static string WriteRampWav(int seconds) => WriteWav(seconds, RampValueAt);

    private static string WriteWav(int seconds, Func<int, float> sampleAt)
    {
        var path = Path.Combine(Path.GetTempPath(), $"tixl-native-audio-{Guid.NewGuid():N}.wav");
        var frameCount = seconds * SampleRate;
        using var writer = new BinaryWriter(File.Create(path));
        writer.Write("RIFF"u8);
        writer.Write(36 + frameCount * 8);
        writer.Write("WAVEfmt "u8);
        writer.Write(16);
        writer.Write((short)3); // IEEE float
        writer.Write((short)2);
        writer.Write(SampleRate);
        writer.Write(SampleRate * 8);
        writer.Write((short)8);
        writer.Write((short)32);
        writer.Write("data"u8);
        writer.Write(frameCount * 8);
        for (var frame = 0; frame < frameCount; frame++)
        {
            writer.Write(sampleAt(frame));
            writer.Write(sampleAt(frame));
        }

        return path;
    }
}
