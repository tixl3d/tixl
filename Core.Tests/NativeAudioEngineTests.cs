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

            Assert.True(engine.Route(source, AudioHandle.Output));
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
            PlayToOutput(engine, source);
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
            PlayToOutput(engine, engine.LoadFile(path));

            var rendered = new float[1024 * 2];
            engine.Render(rendered, 1024);

            var stereo = new float[256 * 2];
            var mono = new float[256];
            engine.ReadOutputStereo(stereo);
            engine.ReadLatestMono(AudioHandle.Output, mono);

            for (var frame = 0; frame < 256; frame++)
            {
                var renderedFrame = 1024 - 256 + frame;
                Assert.Equal(rendered[renderedFrame * 2], stereo[frame * 2]);
                Assert.Equal(rendered[renderedFrame * 2 + 1], stereo[frame * 2 + 1]);
                Assert.Equal(rendered[renderedFrame * 2], mono[frame], 5);
            }

            Assert.True(engine.GetLevel(AudioHandle.Output) > 0);
            Assert.True(engine.GetLevel(AudioHandle.Output) > 0); // reading a meter doesn't consume it
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
            PlayToOutput(engine, engine.LoadFile(path));
            engine.Render(new float[4096 * 2], 4096);

            var samples = new float[fftSize];
            var magnitudes = new float[fftSize / 2];
            engine.ReadLatestMono(AudioHandle.Output, samples);
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

    [Fact]
    public void UnroutedSourceIsSilentAndBusAppliesItsVolume()
    {
        var path = WriteWav(seconds: 1, _ => 0.5f);
        try
        {
            using var engine = NativeAudioEngine.TryCreate(NativeAudio.Mode.Offline, NativeAudio.Backend.Null, SampleRate);
            Assert.NotNull(engine);

            var source = engine.LoadFile(path);
            engine.Play(source);
            var rendered = new float[1024 * 2];
            engine.Render(rendered, 1024);
            Assert.Equal(0f, Peak(rendered));

            var bus = engine.CreateBus();
            Assert.True(bus.IsValid);
            Assert.True(engine.Route(source, bus));
            Assert.Equal(bus, engine.GetRouteTarget(source));
            engine.SetBusVolume(bus, 0.5f);
            engine.Render(rendered, 1024);

            Assert.InRange(rendered[1000 * 2], 0.24f, 0.26f);
            Assert.InRange(engine.GetLevel(source), 0.49f, 0.51f); // the source's own tap sits before the bus
            Assert.InRange(engine.GetLevel(bus), 0.24f, 0.26f);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void NestedBusesFoldTheirVolumesAndStoppingOneStopsItsSources()
    {
        var path = WriteRampWav(seconds: 1);
        try
        {
            using var engine = NativeAudioEngine.TryCreate(NativeAudio.Mode.Offline, NativeAudio.Backend.Null, SampleRate);
            Assert.NotNull(engine);

            var outer = engine.CreateBus();
            var inner = engine.CreateBus();
            Assert.True(engine.Route(inner, outer));
            var source = engine.LoadFile(path);
            Assert.True(engine.Route(source, inner));
            engine.SetBusVolume(inner, 0.5f);
            engine.SetBusVolume(outer, 0.5f);
            engine.Play(source);

            var rendered = new float[2048 * 2];
            engine.Render(rendered, 2048);
            Assert.InRange(Peak(rendered), 0.11f, 0.13f);

            engine.SetBusPlaying(outer, false);
            var cursorWhileStopped = engine.GetCursorSecs(source);
            engine.Render(rendered, 2048);
            Assert.Equal(0f, Peak(rendered));
            Assert.Equal(cursorWhileStopped, engine.GetCursorSecs(source));

            engine.SetBusPlaying(outer, true);
            engine.Render(rendered, 2048);
            Assert.True(Peak(rendered) > 0.1f);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void StoppedSourceMetersAsSilenceAndDestroyedBusRejectsItsHandle()
    {
        var path = WriteWav(seconds: 1, _ => 0.5f);
        try
        {
            using var engine = NativeAudioEngine.TryCreate(NativeAudio.Mode.Offline, NativeAudio.Backend.Null, SampleRate);
            Assert.NotNull(engine);

            var source = engine.LoadFile(path);
            PlayToOutput(engine, source);
            var rendered = new float[8192 * 2];
            engine.Render(rendered, 1024);
            Assert.True(engine.GetLevel(source) > 0.4f);

            engine.Pause(source);
            engine.Render(rendered, 8192);
            Assert.Equal(0f, engine.GetLevel(source));
            Assert.False(engine.ReadLatestMono(source, new float[64]));

            var bus = engine.CreateBus();
            engine.DestroyBus(bus);
            Assert.False(engine.Route(source, bus));
            Assert.NotEqual(bus, engine.CreateBus());
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void PanMovesASourceToOneSide()
    {
        var path = WriteWav(seconds: 1, _ => 0.5f);
        try
        {
            using var engine = NativeAudioEngine.TryCreate(NativeAudio.Mode.Offline, NativeAudio.Backend.Null, SampleRate);
            Assert.NotNull(engine);

            var source = engine.LoadFile(path);
            engine.SetPan(source, -1f);
            PlayToOutput(engine, source);
            var rendered = new float[1024 * 2];
            engine.Render(rendered, 1024);

            Assert.True(Math.Abs(rendered[1000 * 2]) > 0.4f);
            Assert.True(Math.Abs(rendered[1000 * 2 + 1]) < 0.01f);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void GeneratorFollowsItsEnvelope()
    {
        using var engine = NativeAudioEngine.TryCreate(NativeAudio.Mode.Offline, NativeAudio.Backend.Null, SampleRate);
        Assert.NotNull(engine);

        var tone = engine.CreateGenerator();
        Assert.True(engine.Route(tone, AudioHandle.Output));
        engine.SetGenerator(tone, 480f, 0, attackSecs: 0.001f, decaySecs: 0.001f, sustainLevel: 0.5f, releaseSecs: 0.01f, durationSecs: -1f);

        var rendered = new float[4800 * 2];
        engine.Render(rendered, 4800);
        Assert.Equal(0f, Peak(rendered));
        Assert.False(engine.IsGeneratorActive(tone));

        engine.TriggerGeneratorAttack(tone);
        Assert.True(engine.IsGeneratorActive(tone));
        engine.Render(rendered, 4800);
        Assert.InRange(Peak(rendered.AsSpan(2400 * 2)), 0.49f, 0.51f); // sustain

        engine.TriggerGeneratorRelease(tone);
        engine.Render(rendered, 4800);
        Assert.Equal(0f, Peak(rendered.AsSpan(1000 * 2))); // a 10 ms release is over after 480 frames
        Assert.False(engine.IsGeneratorActive(tone));
    }

    [Fact]
    public void GeneratorTriggerModeReleasesAfterItsDuration()
    {
        using var engine = NativeAudioEngine.TryCreate(NativeAudio.Mode.Offline, NativeAudio.Backend.Null, SampleRate);
        Assert.NotNull(engine);

        var tone = engine.CreateGenerator();
        engine.Route(tone, AudioHandle.Output);
        engine.SetGenerator(tone, 480f, 1, 0.001f, 0.001f, 1f, 0.001f, durationSecs: 0.05f);
        engine.TriggerGeneratorAttack(tone);

        var rendered = new float[4800 * 2];
        engine.Render(rendered, 4800);
        Assert.True(Peak(rendered.AsSpan(0, 1200 * 2)) > 0.7f);
        Assert.Equal(0f, Peak(rendered.AsSpan(2600 * 2)));
    }

    [Fact]
    public void PushSourcePlaysWhatIsFedThenSilence()
    {
        using var engine = NativeAudioEngine.TryCreate(NativeAudio.Mode.Offline, NativeAudio.Backend.Null, SampleRate);
        Assert.NotNull(engine);

        var source = engine.CreatePushSource(2, SampleRate, SampleRate);
        PlayToOutput(engine, source);

        var fed = new float[480 * 2];
        Array.Fill(fed, 0.25f);
        Assert.Equal(480, engine.PushWrite(source, fed, 480));
        Assert.Equal(480, engine.GetPushQueuedFrames(source));

        var rendered = new float[960 * 2];
        engine.Render(rendered, 960);
        Assert.InRange(rendered[100 * 2], 0.24f, 0.26f);
        Assert.Equal(0f, Peak(rendered.AsSpan(480 * 2)));
        Assert.Equal(0, engine.GetPushQueuedFrames(source));
    }

    [Fact]
    public void DeferredReleaseHappensOnProcessing()
    {
        using var engine = NativeAudioEngine.TryCreate(NativeAudio.Mode.Offline, NativeAudio.Backend.Null, SampleRate);
        Assert.NotNull(engine);

        var bus = engine.CreateBus();
        var tone = engine.CreateGenerator();
        Assert.True(engine.Route(tone, bus));
        engine.ReleaseDeferred(tone);
        engine.ReleaseDeferred(bus);

        Assert.True(engine.IsPlaying(tone));
        engine.ProcessDeferredReleases();
        Assert.False(engine.IsPlaying(tone));
        Assert.False(engine.Route(bus, AudioHandle.Output));
    }

    [Fact]
    public void CaptureFillsARingThatCanBeReadByPosition()
    {
        using var engine = NativeAudioEngine.TryCreate(NativeAudio.Mode.Device, NativeAudio.Backend.Null, SampleRate);
        Assert.NotNull(engine);
        Assert.False(engine.IsCaptureRunning);
        Assert.Equal(0, engine.ReadCapture(0, new float[16]));

        Assert.Equal(0, engine.StartCapture(-1, loopback: false));
        Assert.True(engine.IsCaptureRunning);

        var deadline = Environment.TickCount64 + 5000;
        while (engine.CaptureFramesWritten < 960 && Environment.TickCount64 < deadline)
            System.Threading.Thread.Sleep(5);

        Assert.True(engine.CaptureFramesWritten >= 960);
        var frames = new float[480 * 2];
        Assert.Equal(480, engine.ReadCapture(0, frames));
        Assert.Equal(0, engine.ReadCapture(engine.CaptureFramesWritten + 100000, frames));

        engine.StopCapture();
        Assert.False(engine.IsCaptureRunning);
        Assert.Equal(0, engine.ReadCapture(0, frames));
    }

    private static void PlayToOutput(NativeAudioEngine engine, AudioHandle source)
    {
        engine.Route(source, AudioHandle.Output);
        engine.Play(source);
    }

    private static float Peak(ReadOnlySpan<float> samples)
    {
        var peak = 0f;
        foreach (var sample in samples)
            peak = Math.Max(peak, Math.Abs(sample));

        return peak;
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
