namespace T3.Spikes.MiniaudioSpike;

/// <summary>
/// Which buffer sizes each Linux backend actually grants, and whether callbacks stay on time at small sizes.
/// Live input and VJ use want the device buffer near BASS's 20 ms or below.
/// </summary>
internal static class LatencyCheck
{
    public static int Run(Program.Options options)
    {
        var backends = new[] { NativeAudio.Backend.PulseAudio, NativeAudio.Backend.Alsa, NativeAudio.Backend.Jack };
        uint[] periods = [64, 128, 256, 512];
        Console.WriteLine("Backend     requested  granted (periods × frames)  buffer ms  callbacks  avg frames/cb  late  max gap ms");

        foreach (var backend in backends)
        {
            foreach (var period in periods)
            {
                var engine = NativeAudio.Create(NativeAudio.Mode.Device, backend, 48000, period, out var result);
                if (engine == IntPtr.Zero)
                {
                    Console.WriteLine($"{backend,-11} {period,9}  unavailable (miniaudio result {result})");
                    break;
                }

                try
                {
                    NativeAudio.SetOutputGain(engine, options.Audible ? 0.3f : 0f);
                    var sine = NativeAudio.SoundCreateSine(engine, 440, 0.5);
                    NativeAudio.SoundPlay(engine, sine);
                    Thread.Sleep(300);
                    NativeAudio.ResetStats(engine);
                    Thread.Sleep(3000);

                    NativeAudio.GetInfo(engine, out var info);
                    NativeAudio.GetStats(engine, out var stats);
                    var bufferMs = info.Periods * info.PeriodFrames * 1000.0 / info.SampleRate;
                    var averageFrames = stats.CallbackCount > 0 ? (double)stats.TotalFramesRendered / stats.CallbackCount : 0;
                    Console.WriteLine($"{backend,-11} {period,9}  {info.Periods,7} × {info.PeriodFrames,-16}  {bufferMs,9:F1}  {stats.CallbackCount,9}  " +
                                      $"{averageFrames,13:F0}  {stats.LateCallbackCount,4}  {stats.MaxCallbackGapNs / 1e6,10:F2}");
                }
                finally
                {
                    NativeAudio.Destroy(engine);
                }
            }
        }

        // Informational: which backend and size TiXL should default to is a decision for the plan, not a pass/fail.
        return 0;
    }
}
