using System.Diagnostics;
using System.Text;

namespace T3.Spikes.MiniaudioSpike;

/// <summary>
/// Checks whether miniaudio can replace BASS without a realtime cost: soundtrack playback, seeking and
/// position accuracy, a native effect node, spatial audio, waveform decoding, an output analysis tap with a
/// managed FFT, and capture. It runs a 60 Hz frame loop like the editor and reports allocations per frame
/// and audio-callback timing, optionally while forcing garbage collections on another thread.
/// </summary>
/// <remarks>
/// <c>dotnet run --project Spikes/MiniaudioSpike -- [command] [--null] [--audible] [--gc-stress] [--capture]
/// [--seconds N] [--file path]</c>. Commands: basics (default), export, bass, routing, push, latency,
/// device-switch, all. Output is muted unless <c>--audible</c> is passed; <c>--null</c> uses miniaudio's null
/// backend so it runs without a sound device.
/// </remarks>
internal static unsafe class Program
{
    private static int Main(string[] args)
    {
        var options = Options.Parse(args);
        if (!File.Exists(options.FilePath))
        {
            Console.WriteLine($"Audio file not found: {options.FilePath}");
            return 1;
        }

        var failures = 0;
        var runAll = options.Command == "all";
        var known = false;
        foreach (var (name, check) in Commands)
        {
            if (!runAll && options.Command != name)
                continue;
            known = true;
            Console.WriteLine($"=== {name} ===");
            failures += check(options);
        }

        if (!known)
        {
            Console.WriteLine($"Unknown command '{options.Command}'.");
            return 2;
        }

        Console.WriteLine(failures == 0 ? "RESULT: all checks passed" : $"RESULT: {failures} check(s) failed");
        return failures == 0 ? 0 : 1;
    }

    private static readonly (string Name, Func<Options, int> Check)[] Commands =
        [
            ("basics", RunBasics),
            ("export", ExportCheck.Run),
            ("bass", BassComparison.Run),
            ("routing", RoutingCheck.Run),
            ("push", PushStreamCheck.Run),
            ("latency", LatencyCheck.Run),
            ("device-switch", DeviceSwitchCheck.Run),
        ];

    private static int RunBasics(Options options)
    {
        var failures = 0;
        failures += CheckFft();

        var backend = options.UseNullBackend ? NativeAudio.Backend.Null : NativeAudio.Backend.Default;
        var engine = NativeAudio.Create(NativeAudio.Mode.Device, backend, 48000, 480, out var createResult);
        if (engine == IntPtr.Zero)
        {
            Console.WriteLine($"Creating the audio engine failed: miniaudio result {createResult}");
            return 1;
        }

        try
        {
            NativeAudio.GetInfo(engine, out var info);
            Console.WriteLine($"Device: {NativeAudio.GetBackendName(info)}, {info.SampleRate} Hz, {info.Channels} ch, " +
                              $"{info.Periods} × {info.PeriodFrames} frames ({info.PeriodFrames * 1000.0 / info.SampleRate:F1} ms)");
            NativeAudio.SetOutputGain(engine, options.Audible ? 0.5f : 0f);

            ListDevices(engine);
            failures += CheckWaveform(options.FilePath);
            failures += CheckLoadAndSeek(engine, options.FilePath);
            failures += RunFrameLoop(engine, info, options);
            if (options.Capture)
                failures += CheckCapture(engine);
        }
        finally
        {
            NativeAudio.Destroy(engine);
        }

        return failures;
    }

    private static int CheckFft()
    {
        const int size = 2048;
        const int sineBin = 100;
        var fft = new Fft(size);
        var samples = new float[size];
        var magnitudes = new float[size / 2];
        for (var sampleIndex = 0; sampleIndex < size; sampleIndex++)
            samples[sampleIndex] = MathF.Sin(2f * MathF.PI * sineBin * sampleIndex / size);

        fft.ComputeMagnitudes(samples, magnitudes);
        var peakBin = 0;
        for (var bin = 1; bin < magnitudes.Length; bin++)
        {
            if (magnitudes[bin] > magnitudes[peakBin])
                peakBin = bin;
        }

        const int iterations = 10000;
        var stopwatch = Stopwatch.StartNew();
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        for (var iteration = 0; iteration < iterations; iteration++)
            fft.ComputeMagnitudes(samples, magnitudes);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        var microsecondsPerFft = stopwatch.Elapsed.TotalMicroseconds / iterations;

        var passed = peakBin == sineBin && MathF.Abs(magnitudes[peakBin] - 1f) < 0.05f && allocated == 0;
        Console.WriteLine($"FFT {size}: peak at bin {peakBin} = {magnitudes[peakBin]:F3} (expected bin {sineBin} = 1.0), " +
                          $"{microsecondsPerFft:F1} µs per FFT, {allocated} bytes allocated over {iterations} runs " +
                          (passed ? "[ok]" : "[FAILED]"));
        return passed ? 0 : 1;
    }

    private static void ListDevices(IntPtr engine)
    {
        var nameBuffer = stackalloc byte[256];
        for (var capture = 0; capture <= 1; capture++)
        {
            var count = NativeAudio.GetDeviceCount(engine, capture);
            Console.WriteLine($"{(capture == 1 ? "Capture" : "Playback")} devices: {count}");
            for (var deviceIndex = 0; deviceIndex < count; deviceIndex++)
            {
                var length = NativeAudio.GetDeviceName(engine, capture, deviceIndex, nameBuffer, 256);
                Console.WriteLine($"  [{deviceIndex}] {Encoding.UTF8.GetString(nameBuffer, length)}");
            }
        }
    }

    private static int CheckWaveform(string path)
    {
        const int bucketCount = 2048;
        var minMaxPairs = new float[bucketCount * 2];
        var stopwatch = Stopwatch.StartNew();
        int result;
        double lengthSeconds;
        fixed (float* pairs = minMaxPairs)
        {
            result = NativeAudio.DecodePeaks(path, bucketCount, pairs, out lengthSeconds);
        }

        var loudest = 0f;
        for (var index = 0; index < minMaxPairs.Length; index++)
            loudest = MathF.Max(loudest, MathF.Abs(minMaxPairs[index]));

        var passed = result == 0 && lengthSeconds > 0 && loudest > 0.01f;
        Console.WriteLine($"Waveform: decoded {lengthSeconds:F1} s into {bucketCount} min/max buckets in " +
                          $"{stopwatch.Elapsed.TotalMilliseconds:F0} ms, loudest {loudest:F2} " + (passed ? "[ok]" : $"[FAILED, result {result}]"));
        return passed ? 0 : 1;
    }

    /// <summary>Compares the load modes; seek time matters for scrubbing the timeline.</summary>
    private static int CheckLoadAndSeek(IntPtr engine, string path)
    {
        var failures = 0;
        var modes = new[]
                        {
                            (Name: "own decoder + seek table", Flags: NativeAudio.LoadFlags.DecoderWithSeekTable),
                            (Name: "resource manager stream ", Flags: NativeAudio.LoadFlags.StreamResourceManager),
                            (Name: "decoded to memory       ", Flags: NativeAudio.LoadFlags.DecodeToMemory),
                        };

        foreach (var mode in modes)
        {
            var stopwatch = Stopwatch.StartNew();
            var sound = NativeAudio.SoundLoad(engine, path, mode.Flags);
            var loadMs = stopwatch.Elapsed.TotalMilliseconds;
            if (sound < 0)
            {
                Console.WriteLine($"Load [{mode.Name}]: failed with miniaudio result {-sound} [FAILED]");
                failures++;
                continue;
            }

            var length = NativeAudio.SoundGetLengthSeconds(engine, sound);
            double[] targets = [length * 0.8, length * 0.1, length * 0.6, length * 0.05];
            var worstSeekMs = 0.0;
            foreach (var target in targets)
            {
                stopwatch.Restart();
                NativeAudio.SoundSeekSeconds(engine, sound, target);
                worstSeekMs = Math.Max(worstSeekMs, stopwatch.Elapsed.TotalMilliseconds);
            }

            Console.WriteLine($"Load [{mode.Name}]: {loadMs,7:F1} ms, length {length:F1} s, worst seek {worstSeekMs,6:F2} ms");
            NativeAudio.SoundUnload(engine, sound);
        }

        return failures;
    }

    private static int RunFrameLoop(IntPtr engine, NativeAudio.Info info, Options options)
    {
        var soundtrack = NativeAudio.SoundLoad(engine, options.FilePath, NativeAudio.LoadFlags.DecoderWithSeekTable);
        var spatial = NativeAudio.SoundLoad(engine, options.FilePath, NativeAudio.LoadFlags.Spatial);
        var echo = NativeAudio.EchoCreate(engine);
        if (soundtrack < 0 || spatial < 0 || echo < 0)
        {
            Console.WriteLine("Loading the frame-loop sounds failed [FAILED]");
            return 1;
        }
        NativeAudio.Connect(engine, NativeAudio.NodeKind.Sound, soundtrack, NativeAudio.NodeKind.Echo, echo);

        const double startSeconds = 20;
        NativeAudio.SoundSeekSeconds(engine, soundtrack, startSeconds);
        NativeAudio.SoundSetVolume(engine, spatial, 0.3f);
        NativeAudio.SetTrackedSound(engine, soundtrack);
        NativeAudio.SoundPlay(engine, soundtrack);
        NativeAudio.SoundPlay(engine, spatial);

        // .NET binds a P/Invoke stub on its first call, which allocates once; bind the ones the loop uses late.
        NativeAudio.SoundPause(engine, -1);

        using var gcStress = options.GcStress ? new GcStress() : null;
        var gen0Before = GC.CollectionCount(0);
        var gen2Before = GC.CollectionCount(2);

        var fft = new Fft(2048);
        var tapSamples = new float[fft.Size];
        var magnitudes = new float[fft.Size / 2];
        var periodSeconds = info.PeriodFrames / (double)info.SampleRate;

        var report = new FrameLoopReport();
        var frameCount = (int)(options.Seconds * 60);
        var seekFrame = frameCount / 2;
        var pauseFrame = frameCount * 3 / 4;
        var resumeFrame = pauseFrame + 30;
        const double seekTargetSeconds = 60;
        const int warmupFrames = 30;

        // The position can lag a jump until the next audio callback, so skip a few frames after each one.
        const int framesToSettleAfterJump = 3;
        var settledFromFrame = warmupFrames;
        var lastPosition = -1.0;
        var referencePosition = -1.0;
        var referenceNs = 0ul;
        var lastTapFrames = 0ul;
        var allocatedAtWarmup = 0L;
        var loopStart = Stopwatch.GetTimestamp();

        for (var frame = 0; frame < frameCount; frame++)
        {
            if (frame == warmupFrames)
            {
                NativeAudio.ResetStats(engine);
                allocatedAtWarmup = GC.GetAllocatedBytesForCurrentThread();
            }

            var allocatedAtFrameStart = GC.GetAllocatedBytesForCurrentThread();
            var nowNs = NativeAudio.NowNs();
            var position = NativeAudio.GetPlaybackSeconds(engine, nowNs);

            if (frame == seekFrame)
            {
                NativeAudio.SoundSeekSeconds(engine, soundtrack, seekTargetSeconds);
                settledFromFrame = frame + framesToSettleAfterJump;
                referencePosition = -1;
                lastPosition = -1;
            }
            else if (frame == seekFrame + 6)
            {
                report.PositionAfterSeek = position;
            }
            else if (frame == pauseFrame)
            {
                NativeAudio.SoundPause(engine, soundtrack);
            }
            else if (frame == pauseFrame + 5)
            {
                report.PositionAtPauseStart = position;
            }
            else if (frame == resumeFrame - 1)
            {
                report.PositionAtPauseEnd = position;
            }
            else if (frame == resumeFrame)
            {
                NativeAudio.SoundPlay(engine, soundtrack);
                settledFromFrame = frame + framesToSettleAfterJump;
                referencePosition = -1;
                lastPosition = -1;
            }

            var isPlaying = frame < pauseFrame || frame >= resumeFrame;
            if (isPlaying && frame >= settledFromFrame && position >= 0)
            {
                if (lastPosition >= 0 && position < lastPosition)
                    report.BackwardSteps++;
                lastPosition = position;

                // Measured against the wall clock from a reference a few frames after each jump.
                if (referencePosition < 0)
                {
                    referencePosition = position;
                    referenceNs = nowNs;
                }
                else
                {
                    var drift = Math.Abs(position - referencePosition - (nowNs - referenceNs) * 1e-9);
                    report.MaxDriftSeconds = Math.Max(report.MaxDriftSeconds, drift);
                }
            }

            fixed (float* samples = tapSamples)
            {
                var tapFrames = NativeAudio.TapReadLatest(engine, NativeAudio.OutputTap, samples, tapSamples.Length);
                if (tapFrames == lastTapFrames && frame >= warmupFrames)
                    report.FramesWithoutNewAudio++;
                lastTapFrames = tapFrames;
            }

            fft.ComputeMagnitudes(tapSamples, magnitudes);
            var spectrumPeak = 0f;
            for (var bin = 1; bin < magnitudes.Length; bin++)
                spectrumPeak = MathF.Max(spectrumPeak, magnitudes[bin]);
            report.LoudestSpectrumPeak = MathF.Max(report.LoudestSpectrumPeak, spectrumPeak);

            var time = frame / 60f;
            NativeAudio.EchoSet(engine, echo, 0.25f + 0.1f * MathF.Sin(time), 0.35f, 0.3f);
            NativeAudio.SoundSetPosition(engine, spatial, 3f * MathF.Cos(time * 2f), 0, 3f * MathF.Sin(time * 2f));

            if (report.FirstAllocatingFrame < 0 && frame >= warmupFrames && GC.GetAllocatedBytesForCurrentThread() > allocatedAtFrameStart)
                report.FirstAllocatingFrame = frame;

            WaitForNextFrame(loopStart, frame + 1);
        }

        var allocatedInLoop = GC.GetAllocatedBytesForCurrentThread() - allocatedAtWarmup;
        NativeAudio.GetStats(engine, out var stats);
        var gen0Collections = GC.CollectionCount(0) - gen0Before;
        var gen2Collections = GC.CollectionCount(2) - gen2Before;
        NativeAudio.SoundUnload(engine, spatial);
        NativeAudio.SoundUnload(engine, soundtrack);

        var measuredFrames = frameCount - warmupFrames;
        var wallSeconds = measuredFrames / 60.0;
        var averageCallbackUs = stats.CallbackCount > 0 ? stats.TotalCallbackDurationNs / 1000.0 / stats.CallbackCount : 0;
        var audioThreadLoad = stats.TotalCallbackDurationNs * 1e-9 / wallSeconds;

        Console.WriteLine($"Frame loop: {measuredFrames} frames over {wallSeconds:F1} s" +
                          (options.GcStress ? $", GC stress on ({gen0Collections} gen0 / {gen2Collections} gen2 collections)" : ""));
        Console.WriteLine($"  Audio callbacks: {stats.CallbackCount}, late (gap > 2 periods): {stats.LateCallbackCount}, " +
                          $"max gap {stats.MaxCallbackGapNs / 1e6:F2} ms, max duration {stats.MaxCallbackDurationNs / 1e6:F2} ms, " +
                          $"avg duration {averageCallbackUs:F0} µs, audio thread load {audioThreadLoad:P1}");

        var failures = 0;
        failures += Check("  Managed allocations in frame loop", $"{allocatedInLoop} bytes" +
                                                                 (report.FirstAllocatingFrame >= 0 ? $", first in frame {report.FirstAllocatingFrame}" : ""),
                          allocatedInLoop == 0);
        failures += Check("  Position drift vs. wall clock", $"max {report.MaxDriftSeconds * 1000:F2} ms (period {periodSeconds * 1000:F1} ms)",
                          report.MaxDriftSeconds < periodSeconds * 2 + 0.002);
        failures += Check("  Position never steps backwards", $"{report.BackwardSteps} backward steps", report.BackwardSteps == 0);
        failures += Check("  Seek lands on target", $"{report.PositionAfterSeek:F3} s, 6 frames after seeking to {seekTargetSeconds} s",
                          Math.Abs(report.PositionAfterSeek - (seekTargetSeconds + 0.1)) < 0.05);
        failures += Check("  Position holds while paused", $"{report.PositionAtPauseStart:F3} s -> {report.PositionAtPauseEnd:F3} s",
                          Math.Abs(report.PositionAtPauseEnd - report.PositionAtPauseStart) < 0.001);
        failures += Check("  Analysis tap delivers new audio", $"{report.FramesWithoutNewAudio} frames without new samples, " +
                                                               $"loudest spectrum peak {report.LoudestSpectrumPeak:F3}",
                          report.LoudestSpectrumPeak > 0.001f && report.FramesWithoutNewAudio < measuredFrames / 10);
        failures += Check("  No late audio callbacks", $"{stats.LateCallbackCount}", stats.LateCallbackCount == 0);
        return failures;
    }

    private static int CheckCapture(IntPtr engine)
    {
        var result = NativeAudio.CaptureStart(engine, -1, 0);
        if (result != 0)
            return Check("Capture (default device)", $"start failed with miniaudio result {result}", false);

        Thread.Sleep(100);
        NativeAudio.CaptureTakePeak(engine);
        Thread.Sleep(1000);
        var peak = NativeAudio.CaptureTakePeak(engine);
        NativeAudio.CaptureStop(engine);
        Console.WriteLine($"Capture (default device): peak {peak:F4} over 1 s (silence is fine, it only proves the stream runs)");
        return 0;
    }

    internal static int Check(string label, string detail, bool passed)
    {
        Console.WriteLine($"{label}: {detail} {(passed ? "[ok]" : "[FAILED]")}");
        return passed ? 0 : 1;
    }

    internal static void WaitForNextFrame(long loopStart, int nextFrame)
    {
        var targetTicks = loopStart + (long)(nextFrame * Stopwatch.Frequency / 60.0);
        while (true)
        {
            var remainingTicks = targetTicks - Stopwatch.GetTimestamp();
            if (remainingTicks <= 0)
                return;

            if (remainingTicks > Stopwatch.Frequency / 500)
                Thread.Sleep(1);
            else
                Thread.SpinWait(50);
        }
    }

    private sealed class FrameLoopReport
    {
        public double MaxDriftSeconds;
        public int BackwardSteps;
        public double PositionAfterSeek;
        public double PositionAtPauseStart;
        public double PositionAtPauseEnd;
        public int FramesWithoutNewAudio;
        public float LoudestSpectrumPeak;
        public int FirstAllocatingFrame = -1;
    }

    /// <summary>Allocates on a background thread and forces full collections, to show the native audio thread doesn't care.</summary>
    internal sealed class GcStress : IDisposable
    {
        public GcStress()
        {
            _thread = new Thread(Run) { IsBackground = true, Name = "GC stress" };
            _thread.Start();
        }

        public void Dispose()
        {
            _stop = true;
            _thread.Join();
        }

        private void Run()
        {
            var survivors = new List<byte[]>();
            var lastFullCollection = Stopwatch.StartNew();
            while (!_stop)
            {
                survivors.Add(new byte[32 * 1024]);
                if (survivors.Count > 2000)
                    survivors.RemoveRange(0, 1000);

                if (lastFullCollection.ElapsedMilliseconds > 100)
                {
                    GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
                    lastFullCollection.Restart();
                }
            }
        }

        private readonly Thread _thread;
        private volatile bool _stop;
    }

    internal sealed record Options(string Command, bool UseNullBackend, bool Audible, bool GcStress, bool Capture, double Seconds, string FilePath)
    {
        public static Options Parse(string[] args)
        {
            var command = args.Length > 0 && !args[0].StartsWith("--") ? args[0] : "basics";
            var useNullBackend = args.Contains("--null");
            var audible = args.Contains("--audible");
            var gcStress = args.Contains("--gc-stress");
            var capture = args.Contains("--capture");
            var seconds = 12.0;
            var filePath = Path.Combine(FindRepositoryRoot(), "Operators/Examples/Assets/user/still/proj-worksforeverybody/soundtrack/works-for-everybody.mp3");

            for (var index = 0; index < args.Length - 1; index++)
            {
                if (args[index] == "--seconds")
                    seconds = double.Parse(args[index + 1], System.Globalization.CultureInfo.InvariantCulture);
                else if (args[index] == "--file")
                    filePath = Path.GetFullPath(args[index + 1]);
            }

            return new Options(command, useNullBackend, audible, gcStress, capture, seconds, filePath);
        }

        public static string FindRepositoryRoot()
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory != null && !File.Exists(Path.Combine(directory.FullName, "t3.sln")))
                directory = directory.Parent;
            return directory?.FullName ?? Directory.GetCurrentDirectory();
        }
    }
}
