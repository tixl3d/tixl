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
    public void LoadingAMissingFileGivesAnInvalidHandle()
    {
        using var engine = NativeAudioEngine.TryCreate(NativeAudio.Mode.Offline, NativeAudio.Backend.Null, SampleRate);
        Assert.NotNull(engine);
        Assert.False(engine.LoadFile("/does/not/exist.wav").IsValid);
    }

    private static float RampValueAt(int frame) => (frame % 1000) / 1000f - 0.5f;

    /** A sawtooth rather than a tone: every position has its own value, so a late seek can't pass by accident. */
    private static string WriteRampWav(int seconds)
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
            writer.Write(RampValueAt(frame));
            writer.Write(RampValueAt(frame));
        }

        return path;
    }
}
