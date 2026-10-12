using System.Diagnostics;

namespace T3.Spikes.MiniaudioSpike;

/// <summary>
/// Offline rendering for video export: an engine without a device, pulled once per video frame. Export must be
/// bit-identical between runs, start without a gap, run faster than realtime, and keep the audio length
/// exact at frame rates whose frames don't hold a whole number of samples.
/// </summary>
internal static unsafe class ExportCheck
{
    public static int Run(Program.Options options)
    {
        var failures = 0;
        const uint sampleRate = 48000;
        const double startSeconds = 30;
        const double lengthSeconds = 10;
        var frameRate = new FrameRate(30000, 1001);

        foreach (var mode in new[] { NativeAudio.LoadFlags.DecoderWithSeekTable, NativeAudio.LoadFlags.StreamResourceManager })
        {
            var first = RenderClip(options.FilePath, mode, sampleRate, frameRate, startSeconds, lengthSeconds, out var firstStats);
            var second = RenderClip(options.FilePath, mode, sampleRate, frameRate, startSeconds, lengthSeconds, out _);
            if (first == null || second == null)
            {
                failures += Program.Check($"Export [{mode}]", "rendering failed", false);
                continue;
            }

            var identical = first.AsSpan().SequenceEqual(second);
            var label = mode == NativeAudio.LoadFlags.DecoderWithSeekTable ? "own decoder" : "resource manager stream";
            Console.WriteLine($"Export [{label}]: {lengthSeconds} s at {frameRate} fps in {firstStats.ElapsedMs:F0} ms " +
                              $"({lengthSeconds * 1000 / firstStats.ElapsedMs:F0}× realtime), {firstStats.SilentLeadingFrames} silent leading video frames, " +
                              $"runs identical: {identical}");

            // Only the decoder that reads synchronously is expected to pass; the resource manager streams asynchronously.
            if (mode == NativeAudio.LoadFlags.DecoderWithSeekTable)
            {
                failures += Program.Check("  Deterministic and gapless", $"identical {identical}, silent frames {firstStats.SilentLeadingFrames}",
                                          identical && firstStats.SilentLeadingFrames == 0);
            }
        }

        failures += CheckLiveGraphExport(options.FilePath, frameRate);
        failures += CheckSampleCountsPerFrame();
        return failures;
    }

    /// <summary>
    /// Export without a second engine: detach the device from the engine the editor plays through, reset the
    /// sources, and pull the same graph once per video frame. Video audio is pushed synchronously before each
    /// pull, as <c>VideoAudioTrack.RequestForExport</c> does today. Two renders must be identical, and live
    /// playback must resume afterwards.
    /// </summary>
    private static int CheckLiveGraphExport(string path, FrameRate rate)
    {
        const uint sampleRate = 48000;
        var engine = NativeAudio.Create(NativeAudio.Mode.Device, NativeAudio.Backend.Null, sampleRate, 480, out var result);
        if (engine == IntPtr.Zero)
            return Program.Check("Live-graph export", $"engine creation failed ({result})", false);

        try
        {
            var soundtrack = NativeAudio.SoundLoad(engine, path, NativeAudio.LoadFlags.DecoderWithSeekTable);
            var tone = NativeAudio.SoundCreateSine(engine, 220, 0.1);
            var videoAudio = NativeAudio.SoundCreatePush(engine, 2, sampleRate, sampleRate);
            var bus = NativeAudio.BusCreate(engine);
            var tap = NativeAudio.TapCreate(engine);
            NativeAudio.Connect(engine, NativeAudio.NodeKind.Sound, soundtrack, NativeAudio.NodeKind.Bus, bus);
            NativeAudio.Connect(engine, NativeAudio.NodeKind.Bus, bus, NativeAudio.NodeKind.Tap, tap);
            NativeAudio.SoundPlay(engine, soundtrack);
            NativeAudio.SoundPlay(engine, tone);
            NativeAudio.SoundPlay(engine, videoAudio);

            // Live playback with leftover video audio in the queue, like an editor session before a render starts.
            var leftover = new float[sampleRate * 2];
            fixed (float* buffer = leftover)
            {
                leftover.AsSpan().Fill(0.3f);
                NativeAudio.PushWrite(engine, videoAudio, buffer, sampleRate / 2);
            }
            Thread.Sleep(500);

            NativeAudio.DetachDevice(engine);
            foreach (var (name, solo) in new[] { ("soundtrack", soundtrack), ("tone", tone), ("video audio", videoAudio) })
            {
                foreach (var other in new[] { soundtrack, tone, videoAudio })
                {
                    if (other != solo)
                        NativeAudio.SoundPause(engine, other);
                }
                var soloFirst = RenderLive(engine, soundtrack, tone, videoAudio, sampleRate, rate, 2, out _);
                var soloSecond = RenderLive(engine, soundtrack, tone, videoAudio, sampleRate, rate, 2, out _);
                Console.WriteLine($"  Only {name}: identical {soloFirst.AsSpan().SequenceEqual(soloSecond)}");
                if (!soloFirst.AsSpan().SequenceEqual(soloSecond))
                    ReportDifference(soloFirst, soloSecond);
                foreach (var other in new[] { soundtrack, tone, videoAudio })
                    NativeAudio.SoundPlay(engine, other);
            }

            var first = RenderLive(engine, soundtrack, tone, videoAudio, sampleRate, rate, 5, out var firstMs);
            var second = RenderLive(engine, soundtrack, tone, videoAudio, sampleRate, rate, 5, out _);
            var identical = first.AsSpan().SequenceEqual(second);
            if (!identical)
                ReportDifference(first, second);

            NativeAudio.GetStats(engine, out var statsBefore);
            var attachResult = NativeAudio.AttachDevice(engine, -1);
            Thread.Sleep(300);
            NativeAudio.GetStats(engine, out var statsAfter);
            var resumed = attachResult == 0 && statsAfter.CallbackCount > statsBefore.CallbackCount;

            var leadingLoudest = 0f;
            for (var index = 0; index < 1600; index++)
                leadingLoudest = MathF.Max(leadingLoudest, MathF.Abs(first[index]));

            Console.WriteLine($"Live-graph export: device detached, 5 s rendered in {firstMs:F0} ms; runs identical: {identical}, " +
                              $"first frame loudest {leadingLoudest:F2}, live playback resumed: {resumed}");
            return Program.Check("  Same engine renders deterministically and resumes live", $"identical {identical}, resumed {resumed}",
                                 identical && resumed && leadingLoudest > 0.11f);
        }
        finally
        {
            NativeAudio.Destroy(engine);
        }
    }

    private static void ReportDifference(float[] first, float[] second)
    {
        var firstDifference = -1;
        var differing = 0;
        var largest = 0f;
        for (var index = 0; index < first.Length; index++)
        {
            var difference = MathF.Abs(first[index] - second[index]);
            if (difference == 0)
                continue;
            if (firstDifference < 0)
                firstDifference = index;
            differing++;
            largest = MathF.Max(largest, difference);
        }
        Console.WriteLine($"  Renders differ from interleaved sample {firstDifference} (frame {firstDifference / 2}), {differing} samples, largest {largest:G3}");
    }

    private static float[] RenderLive(IntPtr engine, int soundtrack, int tone, int videoAudio, uint sampleRate, FrameRate rate,
                                      double lengthSeconds, out double elapsedMs)
    {
        var stopwatch = Stopwatch.StartNew();
        NativeAudio.SoundSeekSeconds(engine, soundtrack, 30);
        NativeAudio.SoundSeekSeconds(engine, tone, 0);
        NativeAudio.PushClear(engine, videoAudio);
        NativeAudio.SoundResetState(engine, soundtrack);
        NativeAudio.SoundResetState(engine, tone);
        NativeAudio.SoundResetState(engine, videoAudio);

        var frameCount = (long)(lengthSeconds * rate.Numerator / rate.Denominator);
        var totalSamples = frameCount * sampleRate * rate.Denominator / rate.Numerator;
        var output = new float[totalSamples * 2];
        var pushed = new float[sampleRate * 2];
        var phase = 0.0;
        fixed (float* buffer = output)
        fixed (float* pushBuffer = pushed)
        {
            var offset = 0L;
            for (long frame = 0; frame < frameCount; frame++)
            {
                var count = SamplesInFrame(frame, sampleRate, rate);
                for (var index = 0; index < count; index++)
                {
                    var sample = (float)(0.1 * Math.Sin(phase));
                    pushed[index * 2] = sample;
                    pushed[index * 2 + 1] = sample;
                    phase += 2 * Math.PI * 660 / sampleRate;
                }
                NativeAudio.PushWrite(engine, videoAudio, pushBuffer, (uint)count);
                NativeAudio.Render(engine, buffer + offset * 2, (uint)count);
                offset += count;
            }
        }

        elapsedMs = stopwatch.Elapsed.TotalMilliseconds;
        return output;
    }

    /// <summary>
    /// Rounding each video frame's sample count on its own loses or gains a fraction of a sample per frame;
    /// deriving each frame's range from the running frame index keeps the total exact.
    /// </summary>
    private static int CheckSampleCountsPerFrame()
    {
        var failures = 0;
        var cases = new[]
                        {
                            (SampleRate: 44100L, Rate: new FrameRate(24000, 1001)),
                            (SampleRate: 48000L, Rate: new FrameRate(30000, 1001)),
                            (SampleRate: 44100L, Rate: new FrameRate(60, 1)),
                        };
        const double exportMinutes = 10;
        foreach (var (sampleRate, rate) in cases)
        {
            var frameCount = (long)(exportMinutes * 60 * rate.Numerator / rate.Denominator);
            var exactSamples = frameCount * sampleRate * rate.Denominator / rate.Numerator;
            var roundedPerFrame = (long)Math.Round((double)sampleRate * rate.Denominator / rate.Numerator) * frameCount;
            long carried = 0;
            for (long frame = 0; frame < frameCount; frame++)
                carried += SamplesInFrame(frame, sampleRate, rate);

            var roundingDriftMs = (roundedPerFrame - exactSamples) * 1000.0 / sampleRate;
            Console.WriteLine($"Samples per frame at {sampleRate} Hz, {rate} fps over {exportMinutes} min: rounding each frame drifts " +
                              $"{roundingDriftMs:+0.0;-0.0} ms, frame-index ranges drift {(carried - exactSamples) * 1000.0 / sampleRate:F3} ms");
            failures += carried == exactSamples ? 0 : 1;
        }
        return failures;
    }

    private static long SamplesInFrame(long frame, long sampleRate, FrameRate rate)
    {
        var start = frame * sampleRate * rate.Denominator / rate.Numerator;
        var end = (frame + 1) * sampleRate * rate.Denominator / rate.Numerator;
        return end - start;
    }

    private static float[]? RenderClip(string path, NativeAudio.LoadFlags mode, uint sampleRate, FrameRate rate, double startSeconds,
                                       double lengthSeconds, out RenderStats stats)
    {
        stats = default;
        var engine = NativeAudio.Create(NativeAudio.Mode.Offline, NativeAudio.Backend.Default, sampleRate, 0, out _);
        if (engine == IntPtr.Zero)
            return null;

        try
        {
            var stopwatch = Stopwatch.StartNew();
            var sound = NativeAudio.SoundLoad(engine, path, mode);
            var tone = NativeAudio.SoundCreateSine(engine, 220, 0.1);
            var echo = NativeAudio.EchoCreate(engine);
            if (sound < 0 || tone < 0 || echo < 0)
                return null;

            NativeAudio.Connect(engine, NativeAudio.NodeKind.Sound, sound, NativeAudio.NodeKind.Echo, echo);
            NativeAudio.EchoSet(engine, echo, 0.2f, 0.4f, 0.3f);
            NativeAudio.SoundSeekSeconds(engine, sound, startSeconds);
            NativeAudio.SoundPlay(engine, sound);
            NativeAudio.SoundPlay(engine, tone);

            var frameCount = (long)(lengthSeconds * rate.Numerator / rate.Denominator);
            var totalSamples = (int)(frameCount * sampleRate * rate.Denominator / rate.Numerator);
            var output = new float[totalSamples * 2];
            var silentLeadingFrames = 0;
            var stillSilent = true;
            fixed (float* buffer = output)
            {
                var offset = 0L;
                for (long frame = 0; frame < frameCount; frame++)
                {
                    var count = SamplesInFrame(frame, sampleRate, rate);
                    NativeAudio.Render(engine, buffer + offset * 2, (uint)count);

                    // The tone is always there, so look for the soundtrack: anything louder than the tone alone.
                    if (stillSilent)
                    {
                        var loudest = 0f;
                        for (var index = offset * 2; index < (offset + count) * 2; index++)
                            loudest = MathF.Max(loudest, MathF.Abs(output[index]));
                        if (loudest > 0.11f)
                            stillSilent = false;
                        else
                            silentLeadingFrames++;
                    }
                    offset += count;
                }
            }

            stats = new RenderStats(stopwatch.Elapsed.TotalMilliseconds, silentLeadingFrames);
            return output;
        }
        finally
        {
            NativeAudio.Destroy(engine);
        }
    }

    private readonly record struct RenderStats(double ElapsedMs, int SilentLeadingFrames);

    private readonly record struct FrameRate(long Numerator, long Denominator)
    {
        public override string ToString() => Denominator == 1 ? $"{Numerator}" : $"{(double)Numerator / Denominator:F3}";
    }
}
