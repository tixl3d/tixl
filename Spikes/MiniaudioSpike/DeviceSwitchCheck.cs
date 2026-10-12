using System.Diagnostics;

namespace T3.Spikes.MiniaudioSpike;

/// <summary>
/// Switching the output device must not rebuild the graph: today a device change tears down every BASS stream
/// and bumps <c>ResetGeneration</c>. Here only the device is reopened; playback should continue where it was.
/// </summary>
internal static unsafe class DeviceSwitchCheck
{
    public static int Run(Program.Options options)
    {
        var backend = options.UseNullBackend ? NativeAudio.Backend.Null : NativeAudio.Backend.Default;
        var engine = NativeAudio.Create(NativeAudio.Mode.Device, backend, 48000, 480, out var result);
        if (engine == IntPtr.Zero)
            return Program.Check("Device switch", $"engine creation failed ({result})", false);

        try
        {
            NativeAudio.SetOutputGain(engine, options.Audible ? 0.5f : 0f);
            var sound = NativeAudio.SoundLoad(engine, options.FilePath, NativeAudio.LoadFlags.DecoderWithSeekTable);
            NativeAudio.SetTrackedSound(engine, sound);
            NativeAudio.SoundSeekSeconds(engine, sound, 40);
            NativeAudio.SoundPlay(engine, sound);
            Thread.Sleep(1500);

            var failures = 0;
            var deviceCount = NativeAudio.GetDeviceCount(engine, 0);
            foreach (var target in new[] { -1, 0, deviceCount > 1 ? 1 : -1 })
            {
                var before = NativeAudio.GetPlaybackSeconds(engine, NativeAudio.NowNs());
                var stopwatch = Stopwatch.StartNew();
                var switchResult = NativeAudio.SwitchDevice(engine, target);
                var switchMs = stopwatch.Elapsed.TotalMilliseconds;
                Thread.Sleep(1000);
                var after = NativeAudio.GetPlaybackSeconds(engine, NativeAudio.NowNs());

                // Audio time stands still while no device pulls, so expect ~1 s of progress, not 1 s plus the switch.
                var progressed = after - before;
                failures += Program.Check($"Switch to device {target}", $"result {switchResult}, took {switchMs:F0} ms, position {before:F2} s → {after:F2} s",
                                          switchResult == 0 && progressed > 0.8 && progressed < 1.2 + switchMs / 1000);
            }
            return failures;
        }
        finally
        {
            NativeAudio.Destroy(engine);
        }
    }
}
