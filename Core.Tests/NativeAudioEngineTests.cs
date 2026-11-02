using System;
using System.IO;
using System.Numerics;
using T3.Core.Audio;
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

    [Theory]
    [InlineData(29.97, 17982)]
    [InlineData(23.976, 14386)]
    [InlineData(59.94, 35964)]
    [InlineData(60.0, 36000)]
    public void ExportedFramesAddUpToTheVideoDuration(double fps, int frameCount)
    {
        var frameDuration = 1.0 / fps;
        long total = 0;
        for (var frame = 0; frame < frameCount; frame++)
            total += AudioRendering.GetFrameSampleCount(frame, frameDuration, SampleRate);

        Assert.InRange(total - frameCount * frameDuration * SampleRate, -1.0, 1.0);
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

    [Theory]
    [InlineData(5f, true)]
    [InlineData(-5f, false)]
    public void SpatialSourceOnOneSideIsLouderOnThatSide(float x, bool expectRightLouder)
    {
        var (left, right) = RenderSpatial(new Vector3(x, 0, 0), minDistance: 1, maxDistance: 100);
        Assert.Equal(expectRightLouder, right > left);
        Assert.True(MathF.Abs(left - right) > 0.05f);
    }

    [Fact]
    public void SpatialSourceFallsOffLinearlyBetweenItsDistances()
    {
        Assert.InRange(RenderSpatial(new Vector3(0, 0, 5), 10, 20).right, 0.1f, 1f);
        var near = RenderSpatial(new Vector3(0, 0, 5), 10, 20).right;
        var halfway = RenderSpatial(new Vector3(0, 0, 15), 10, 20).right;
        Assert.InRange(halfway / near, 0.45f, 0.55f);
        Assert.Equal(0f, RenderSpatial(new Vector3(0, 0, 25), 10, 20).right, 3);
    }

    [Fact]
    public void SpatialConeAttenuatesBehindTheSource()
    {
        // The listener sits at the origin and the source 5 units ahead of it, so facing -Z points at the listener.
        var facing = RenderSpatial(new Vector3(0, 0, 5), 1, 100, direction: new Vector3(0, 0, -1), innerCone: 60, outerCone: 120, outerGain: 0.25f).right;
        var away = RenderSpatial(new Vector3(0, 0, 5), 1, 100, direction: new Vector3(0, 0, 1), innerCone: 60, outerCone: 120, outerGain: 0.25f).right;
        Assert.InRange(away / facing, 0.2f, 0.3f);
    }

    [Fact]
    public void SpatialModeOffIgnoresPositionAndRelativeFollowsTheListener()
    {
        var off = RenderSpatial(new Vector3(0, 0, 25), 1, 20, mode: NativeAudioEngine.SpatialMode.Off);
        Assert.True(off.right > 0.3f && MathF.Abs(off.left - off.right) < 0.01f);

        // In world mode this listener is 1000 units away; relative to it the source stays 5 units to the right.
        var relative = RenderSpatial(new Vector3(5, 0, 0), 1, 100, mode: NativeAudioEngine.SpatialMode.RelativeToListener, listener: new Vector3(1000, 0, 0));
        Assert.True(relative.right > relative.left);
        Assert.True(relative.right > 0.1f);
    }

    private static (float left, float right) RenderSpatial(Vector3 position, float minDistance, float maxDistance, Vector3? direction = null,
                                                           float innerCone = 360, float outerCone = 360, float outerGain = 1,
                                                           NativeAudioEngine.SpatialMode mode = NativeAudioEngine.SpatialMode.World, Vector3? listener = null)
    {
        var path = WriteWav(seconds: 1, _ => 0.5f);
        try
        {
            using var engine = NativeAudioEngine.TryCreate(NativeAudio.Mode.Offline, NativeAudio.Backend.Null, SampleRate);
            Assert.NotNull(engine);

            engine.SetListener(listener ?? Vector3.Zero, new Vector3(0, 0, 1), Vector3.UnitY, Vector3.Zero);
            var source = engine.LoadFile(path, spatial: true);
            engine.SetSpatial(source, position, direction ?? new Vector3(0, 0, 1), Vector3.Zero, minDistance, maxDistance, 1f, 0f, innerCone, outerCone, outerGain, mode);
            PlayToOutput(engine, source);

            var rendered = new float[4096 * 2];
            engine.Render(rendered, 4096);
            return (Math.Abs(rendered[4000 * 2]), Math.Abs(rendered[4000 * 2 + 1]));
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static (NativeAudioEngine Engine, AudioHandle Bus, AudioHandle Source, string Path) StartEffect(AudioEffectKind kind, Func<int, float> signal, int seconds = 2)
    {
        var path = WriteWav(seconds, signal);
        var engine = NativeAudioEngine.TryCreate(NativeAudio.Mode.Offline, NativeAudio.Backend.Null, SampleRate);
        Assert.NotNull(engine);
        var bus = engine.CreateBus(kind);
        Assert.True(bus.IsValid);
        Assert.True(engine.Route(bus, AudioHandle.Output));
        var source = engine.LoadFile(path);
        Assert.True(engine.Route(source, bus));
        return (engine, bus, source, path);
    }

    private static float[] RenderFrames(NativeAudioEngine engine, int frames)
    {
        var all = new float[frames * 2];
        const int block = 512;
        var block_ = new float[block * 2];
        for (var offset = 0; offset < frames; offset += block)
        {
            engine.Render(block_, block);
            Array.Copy(block_, 0, all, offset * 2, Math.Min(block, frames - offset) * 2);
        }

        return all;
    }

    [Fact]
    public void EchoRepeatsAfterItsDelayAndKeepsRingingOnceTheSourceStops()
    {
        var (engine, bus, source, path) = StartEffect(AudioEffectKind.Echo, frame => frame < 480 ? 0.5f : 0f);
        using (engine)
        {
            try
            {
                engine.SetEcho(bus, 0.25f, 0.5f, 0.5f, false);
                engine.Play(source);
                var rendered = RenderFrames(engine, 48000);

                Assert.InRange(rendered[100 * 2], 0.2f, 0.3f); // dry at half mix
                Assert.Equal(0f, rendered[6000 * 2], 4); // silent before the first repeat
                Assert.InRange(rendered[(12000 + 100) * 2], 0.2f, 0.3f); // first repeat
                Assert.InRange(rendered[(24000 + 100) * 2], 0.1f, 0.15f); // feedback halves the second

                engine.Pause(source);
                Assert.True(Peak(RenderFrames(engine, 24000)) > 0.01f); // the tail outlives the source
            }
            finally
            {
                File.Delete(path);
            }
        }
    }

    [Fact]
    public void PingPongEchoAlternatesSides()
    {
        var (engine, bus, source, path) = StartEffect(AudioEffectKind.Echo, frame => frame < 480 ? 0.5f : 0f);
        using (engine)
        {
            try
            {
                engine.SetEcho(bus, 0.25f, 0.5f, 1f, true);
                engine.Play(source);
                var rendered = RenderFrames(engine, 48000);

                var first = (Left: Math.Abs(rendered[(12000 + 100) * 2]), Right: Math.Abs(rendered[(12000 + 100) * 2 + 1]));
                var second = (Left: Math.Abs(rendered[(24000 + 100) * 2]), Right: Math.Abs(rendered[(24000 + 100) * 2 + 1]));
                Assert.True(first.Left > 0.1f && first.Right < 0.01f);
                Assert.True(second.Right > 0.1f && second.Left < 0.01f);
            }
            finally
            {
                File.Delete(path);
            }
        }
    }

    [Fact]
    public void CompressorReducesWhatIsOverItsThresholdByItsRatio()
    {
        // 0.5 is -6 dB. Over a -18 dB threshold by 12 dB at 4:1 leaves 3 dB over: -15 dB, 0.178.
        var (engine, bus, source, path) = StartEffect(AudioEffectKind.Compressor, _ => 0.5f);
        using (engine)
        {
            try
            {
                engine.SetCompressor(bus, -18f, 4f, 0.001f, 0.05f, 0f);
                engine.Play(source);
                var rendered = RenderFrames(engine, 24000);
                Assert.InRange(rendered[20000 * 2], 0.17f, 0.185f);

                engine.SetCompressor(bus, -18f, 4f, 0.001f, 0.05f, 6f);
                rendered = RenderFrames(engine, 2048);
                Assert.InRange(rendered[2000 * 2], 0.34f, 0.37f); // 6 dB of makeup doubles it
            }
            finally
            {
                File.Delete(path);
            }
        }
    }

    [Fact]
    public void CompressorLeavesQuietAudioAlone()
    {
        var (engine, bus, source, path) = StartEffect(AudioEffectKind.Compressor, _ => 0.05f);
        using (engine)
        {
            try
            {
                engine.SetCompressor(bus, -18f, 4f, 0.001f, 0.05f, 0f);
                engine.Play(source);
                var rendered = RenderFrames(engine, 12000);
                Assert.Equal(0.05f, rendered[10000 * 2], 3);
            }
            finally
            {
                File.Delete(path);
            }
        }
    }

    [Theory]
    [InlineData(0.5f)]
    [InlineData(1.5f)]
    public void ReverbDecaysByAboutSixtyDecibelsOverItsTime(float decaySecs)
    {
        var (engine, bus, source, path) = StartEffect(AudioEffectKind.Reverb, frame => frame < 480 ? 0.5f : 0f);
        using (engine)
        {
            try
            {
                engine.SetReverb(bus, 1f, decaySecs, 0.99f);
                engine.Play(source);

                var early = Rms(RenderFrames(engine, 9600).AsSpan(4800 * 2));
                Assert.True(early > 1e-4f);
                RenderFrames(engine, (int)(decaySecs * SampleRate) - 9600);
                var late = Rms(RenderFrames(engine, 4800));

                var dropDb = 20f * MathF.Log10(early / Math.Max(late, 1e-9f));
                Assert.InRange(dropDb, 35f, 85f);
            }
            finally
            {
                File.Delete(path);
            }
        }
    }

    [Fact]
    public void ReverbIsStereoAndMixZeroStaysDry()
    {
        var (engine, bus, source, path) = StartEffect(AudioEffectKind.Reverb, frame => frame < 480 ? 0.5f : 0f);
        using (engine)
        {
            try
            {
                engine.SetReverb(bus, 1f, 1f, 0.5f);
                engine.Play(source);
                var rendered = RenderFrames(engine, 24000);
                var differences = 0;
                for (var frame = 4800; frame < 24000; frame++)
                {
                    if (Math.Abs(rendered[frame * 2] - rendered[frame * 2 + 1]) > 1e-5f)
                        differences++;
                }

                Assert.True(differences > 1000);

                engine.SetReverb(bus, 0f, 1f, 0.5f);
                Assert.Equal(0f, Peak(RenderFrames(engine, 2048)), 5);
            }
            finally
            {
                File.Delete(path);
            }
        }
    }

    [Theory]
    [InlineData(AudioEffectKind.Echo)]
    [InlineData(AudioEffectKind.Reverb)]
    public void ResettingAnEffectClearsItsTailAndFadingEndsIt(AudioEffectKind kind)
    {
        var (engine, bus, source, path) = StartEffect(kind, frame => frame < 4800 ? 0.5f : 0f);
        using (engine)
        {
            try
            {
                if (kind == AudioEffectKind.Echo)
                    engine.SetEcho(bus, 0.1f, 0.8f, 1f, false);
                else
                    engine.SetReverb(bus, 1f, 3f, 0.9f);

                engine.Play(source);
                RenderFrames(engine, 12000);
                engine.Pause(source);
                Assert.True(Peak(RenderFrames(engine, 2048)) > 0.0001f);

                engine.ResetEffect(bus);
                Assert.Equal(0f, Peak(RenderFrames(engine, 2048)), 6);

                engine.Seek(source, 0);
                engine.Play(source);
                RenderFrames(engine, 12000);
                engine.Pause(source);
                engine.FadeOutEffect(bus, 0.1f);
                RenderFrames(engine, 4800);
                Assert.Equal(0f, Peak(RenderFrames(engine, 2048)), 6);
            }
            finally
            {
                File.Delete(path);
            }
        }
    }

    [Theory]
    [InlineData(AudioEffectKind.Echo)]
    [InlineData(AudioEffectKind.Compressor)]
    [InlineData(AudioEffectKind.Reverb)]
    public void EffectsRenderIdenticallyAfterAReset(AudioEffectKind kind)
    {
        var (engine, bus, source, path) = StartEffect(kind, frame => 0.4f * MathF.Sin(frame * 0.05f));
        using (engine)
        {
            try
            {
                if (kind == AudioEffectKind.Echo)
                    engine.SetEcho(bus, 0.1f, 0.5f, 0.5f, true);
                else if (kind == AudioEffectKind.Compressor)
                    engine.SetCompressor(bus, -20f, 4f, 0.01f, 0.2f, 0f);
                else
                    engine.SetReverb(bus, 0.5f, 1f, 0.5f);

                float[] Pass()
                {
                    engine.Seek(source, 0.25);
                    engine.ResetEffect(bus);
                    engine.Play(source);
                    var result = RenderFrames(engine, 24000);
                    engine.Pause(source);
                    return result;
                }

                Assert.Equal(Pass(), Pass());
            }
            finally
            {
                File.Delete(path);
            }
        }
    }

    private static float Rms(ReadOnlySpan<float> samples)
    {
        var sum = 0.0;
        foreach (var sample in samples)
            sum += sample * sample;

        return (float)Math.Sqrt(sum / samples.Length);
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
