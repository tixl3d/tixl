namespace T3.Spikes.MiniaudioSpike;

/// <summary>
/// Video audio and frame-driven generators push PCM from a managed thread instead of being pulled from a file
/// (BASS's <c>StreamPutData</c>). Checks that a queue kept at a target fill plays without underruns or clicks,
/// and how far the frame clock and the audio clock drift apart when a fixed amount is pushed per frame.
/// </summary>
internal static unsafe class PushStreamCheck
{
    public static int Run(Program.Options options)
    {
        const int sampleRate = 48000;
        const double frequency = 440;
        const double amplitude = 0.5;
        const int targetQueuedFrames = 4800;

        var backend = options.UseNullBackend ? NativeAudio.Backend.Null : NativeAudio.Backend.Default;
        var engine = NativeAudio.Create(NativeAudio.Mode.Device, backend, sampleRate, 480, out var result);
        if (engine == IntPtr.Zero)
            return Program.Check("Push stream", $"engine creation failed ({result})", false);

        try
        {
            NativeAudio.SetOutputGain(engine, options.Audible ? 0.5f : 0f);
            var push = NativeAudio.SoundCreatePush(engine, 2, sampleRate, sampleRate);
            if (push < 0)
                return Program.Check("Push stream", $"creating the source failed ({-push})", false);

            var generator = new SineGenerator(frequency, amplitude, sampleRate);
            var frames = new float[sampleRate * 2];
            fixed (float* buffer = frames)
            {
                generator.Fill(frames.AsSpan(0, targetQueuedFrames * 2));
                NativeAudio.PushWrite(engine, push, buffer, targetQueuedFrames);
            }
            NativeAudio.SoundPlay(engine, push);

            var failures = 0;
            failures += RunPhase(engine, push, generator, frames, options.Seconds / 2, targetQueuedFrames, keepTargetFill: true, out var fedQueue);
            Console.WriteLine($"Push stream, refilled to {targetQueuedFrames} frames each video frame: queue {fedQueue.MinQueued}..{fedQueue.MaxQueued} frames, " +
                              $"{fedQueue.UnderrunFrames} underrun frames, {fedQueue.Discontinuities} discontinuities");
            failures += Program.Check("  No underruns or clicks", $"{fedQueue.UnderrunFrames} underrun frames, {fedQueue.Discontinuities} discontinuities",
                                      fedQueue.UnderrunFrames == 0 && fedQueue.Discontinuities == 0);

            RunPhase(engine, push, generator, frames, options.Seconds / 2, targetQueuedFrames, keepTargetFill: false, out var fixedRate);
            var driftMsPerMinute = (fixedRate.EndQueued - fixedRate.StartQueued) * 1000.0 / sampleRate / (options.Seconds / 2 / 60);
            Console.WriteLine($"Push stream, exactly {sampleRate / 60} frames per video frame: queue went {fixedRate.StartQueued} → {fixedRate.EndQueued} frames, " +
                              $"clock drift {driftMsPerMinute:+0.0;-0.0} ms per minute (the reason video audio needs drift control)");
            return failures;
        }
        finally
        {
            NativeAudio.Destroy(engine);
        }
    }

    private static int RunPhase(IntPtr engine, int push, SineGenerator generator, float[] frames, double seconds, int targetQueuedFrames,
                                bool keepTargetFill, out PhaseResult phaseResult)
    {
        var scanner = new SignalScanner(SignalScanner.MaxSineStep(generator.Frequency, generator.Amplitude, generator.SampleRate));
        var tapBuffer = new float[8192];
        NativeAudio.TapReadSince(engine, NativeAudio.OutputTap, ulong.MaxValue, null, 0, out var readPosition);
        var underrunsBefore = NativeAudio.PushUnderrunFrames(engine, push);
        var startQueued = NativeAudio.PushQueuedFrames(engine, push);
        var minQueued = int.MaxValue;
        var maxQueued = 0;
        var framesPerVideoFrame = generator.SampleRate / 60;

        var frameCount = (int)(seconds * 60);
        var loopStart = System.Diagnostics.Stopwatch.GetTimestamp();
        for (var frame = 0; frame < frameCount; frame++)
        {
            var queued = NativeAudio.PushQueuedFrames(engine, push);
            minQueued = Math.Min(minQueued, queued);
            maxQueued = Math.Max(maxQueued, queued);

            var toWrite = keepTargetFill ? Math.Max(0, targetQueuedFrames - queued) : framesPerVideoFrame;
            fixed (float* buffer = frames)
            {
                generator.Fill(frames.AsSpan(0, toWrite * 2));
                NativeAudio.PushWrite(engine, push, buffer, (uint)toWrite);

                fixed (float* tap = tapBuffer)
                {
                    var count = NativeAudio.TapReadSince(engine, NativeAudio.OutputTap, readPosition, tap, tapBuffer.Length, out var total);
                    readPosition = total;
                    scanner.Scan(tapBuffer.AsSpan(0, count));
                }
            }
            Program.WaitForNextFrame(loopStart, frame + 1);
        }

        phaseResult = new PhaseResult(minQueued, maxQueued, startQueued, NativeAudio.PushQueuedFrames(engine, push),
                                      (long)(NativeAudio.PushUnderrunFrames(engine, push) - underrunsBefore), scanner.Discontinuities);
        return 0;
    }

    private readonly record struct PhaseResult(int MinQueued, int MaxQueued, int StartQueued, int EndQueued, long UnderrunFrames, int Discontinuities);

    /** Phase-continuous stereo sine, so the pushed stream is smooth across writes. */
    private sealed class SineGenerator(double frequency, double amplitude, int sampleRate)
    {
        public double Frequency { get; } = frequency;
        public double Amplitude { get; } = amplitude;
        public int SampleRate { get; } = sampleRate;

        public void Fill(Span<float> interleavedStereo)
        {
            var phaseStep = 2 * Math.PI * Frequency / SampleRate;
            for (var index = 0; index < interleavedStereo.Length; index += 2)
            {
                var sample = (float)(Amplitude * Math.Sin(_phase));
                interleavedStereo[index] = sample;
                interleavedStereo[index + 1] = sample;
                _phase += phaseStep;
                if (_phase > 2 * Math.PI)
                    _phase -= 2 * Math.PI;
            }
        }

        private double _phase;
    }
}
