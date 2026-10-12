namespace T3.Spikes.MiniaudioSpike;

/// <summary>
/// TiXL's audio graph moves sources between bus submixes and effect groups while playing, every frame if the
/// graph changes. This reroutes a sine between two buses and an effect insert on every frame while the
/// audio thread runs, and checks the output for clicks and each bus tap for signal.
/// </summary>
internal static unsafe class RoutingCheck
{
    public static int Run(Program.Options options)
    {
        var failures = 0;
        failures += RunWith(options, applyImmediately: true);
        failures += RunWith(options, applyImmediately: false);
        return failures;
    }

    private static int RunWith(Program.Options options, bool applyImmediately)
    {
        const int sampleRate = 48000;
        const double frequency = 440;
        const double amplitude = 0.5;

        var engine = NativeAudio.Create(NativeAudio.Mode.Device, NativeAudio.Backend.Null, sampleRate, 480, out var result);
        if (engine == IntPtr.Zero)
            return Program.Check("Routing", $"engine creation failed ({result})", false);

        try
        {
            NativeAudio.SetApplyRoutesImmediately(engine, applyImmediately ? 1 : 0);
            var sine = NativeAudio.SoundCreateSine(engine, frequency, amplitude);
            var busA = NativeAudio.BusCreate(engine);
            var busB = NativeAudio.BusCreate(engine);
            var tapA = NativeAudio.TapCreate(engine);
            var tapB = NativeAudio.TapCreate(engine);
            var echo = NativeAudio.EchoCreate(engine);
            NativeAudio.Connect(engine, NativeAudio.NodeKind.Bus, busA, NativeAudio.NodeKind.Tap, tapA);
            NativeAudio.Connect(engine, NativeAudio.NodeKind.Bus, busB, NativeAudio.NodeKind.Tap, tapB);

            // A dry insert must pass the signal unchanged, so any step in the output comes from the rewiring.
            NativeAudio.EchoSet(engine, echo, 0.3f, 0f, 0f);
            NativeAudio.SetOutputGain(engine, 0);
            NativeAudio.Connect(engine, NativeAudio.NodeKind.Sound, sine, NativeAudio.NodeKind.Bus, busA);
            NativeAudio.SoundPlay(engine, sine);
            Thread.Sleep(100);

            var scanner = new SignalScanner(SignalScanner.MaxSineStep(frequency, amplitude, sampleRate));
            var buffer = new float[8192];
            // Asking for samples after the end only returns the current write position.
            NativeAudio.TapReadSince(engine, NativeAudio.OutputTap, ulong.MaxValue, null, 0, out var readPosition);
            NativeAudio.ResetStats(engine);
            NativeAudio.TapTakePeak(engine, tapA);
            NativeAudio.TapTakePeak(engine, tapB);

            var frameCount = (int)(options.Seconds * 60);
            var reroutes = 0;
            var peakA = 0f;
            var peakB = 0f;
            var gapFrames = 0;
            var loopStart = System.Diagnostics.Stopwatch.GetTimestamp();
            for (var frame = 0; frame < frameCount; frame++)
            {
                switch (frame % 3)
                {
                    case 0: NativeAudio.Connect(engine, NativeAudio.NodeKind.Sound, sine, NativeAudio.NodeKind.Bus, busA); break;
                    case 1: NativeAudio.Connect(engine, NativeAudio.NodeKind.Sound, sine, NativeAudio.NodeKind.Bus, busB); break;
                    default: NativeAudio.Connect(engine, NativeAudio.NodeKind.Sound, sine, NativeAudio.NodeKind.Echo, echo); break;
                }
                reroutes++;

                fixed (float* samples = buffer)
                {
                    var count = NativeAudio.TapReadSince(engine, NativeAudio.OutputTap, readPosition, samples, buffer.Length, out var total);
                    if (total - readPosition > (ulong)count)
                        gapFrames++;
                    readPosition = total;
                    scanner.Scan(buffer.AsSpan(0, count));
                }

                peakA = MathF.Max(peakA, NativeAudio.TapTakePeak(engine, tapA));
                peakB = MathF.Max(peakB, NativeAudio.TapTakePeak(engine, tapB));
                Program.WaitForNextFrame(loopStart, frame + 1);
            }

            NativeAudio.GetStats(engine, out var stats);
            Console.WriteLine($"Routing, applied {(applyImmediately ? "immediately from the frame thread" : "by the audio thread between blocks")}: {reroutes} reroutes (bus A → bus B → echo insert) over {scanner.SamplesScanned} output samples, " +
                              $"largest step {scanner.LargestStep:F4} (sine alone ≤ {SignalScanner.MaxSineStep(frequency, amplitude, sampleRate):F4})");

            // Rewiring mid-block is expected to click; it's measured to show why changes are queued.
            var failures = 0;
            if (applyImmediately)
            {
                Console.WriteLine($"  Clicks when rewiring mid-block: {scanner.Discontinuities} discontinuities (expected, for comparison)");
                return 0;
            }
            failures += Program.Check("  No clicks while rerouting", $"{scanner.Discontinuities} discontinuities", scanner.Discontinuities == 0);
            failures += Program.Check("  Bus taps meter their submix", $"peak A {peakA:F2}, peak B {peakB:F2}", peakA > 0.4f && peakB > 0.4f);
            failures += Program.Check("  Tap read kept up", $"{gapFrames} frames lost samples", gapFrames == 0);
            failures += Program.Check("  No late audio callbacks", $"{stats.LateCallbackCount}", stats.LateCallbackCount == 0);
            return failures;
        }
        finally
        {
            NativeAudio.Destroy(engine);
        }
    }
}
