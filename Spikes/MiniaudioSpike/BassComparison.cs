using ManagedBass;

namespace T3.Spikes.MiniaudioSpike;

/// <summary>
/// Every audio reaction in existing projects is tuned to BASS's numbers. This decodes the same samples with
/// both libraries and compares (1) the FFT scale on a calibration sine, (2) the spectra of real music in the
/// -80..0 dB space TiXL's analysis maps to 0..1, and (3) whether both decoders put the same sample at the
/// same position, which the soundtrack sync depends on.
/// </summary>
internal static unsafe class BassComparison
{
    public static int Run(Program.Options options)
    {
        if (!Bass.Init(0, 48000))
            return Program.Check("BASS comparison", $"Bass.Init failed: {Bass.LastError}", false);

        try
        {
            var failures = 0;
            var calibrationPath = Path.Combine(Path.GetTempPath(), $"tixl-bass-calibration-{Environment.ProcessId}.wav");
            try
            {
                WriteSineWav(calibrationPath, 44100, 100 * 44100.0 / FftSize, 0.5, 2);
                failures += CompareCalibrationSine(calibrationPath);
            }
            finally
            {
                File.Delete(calibrationPath);
            }

            var lag = 0;
            foreach (var (position, seek) in new[] { (20.0, false), (20.0, true), (60.0, true), (300.0, true) })
                failures += ComparePcmAlignment(options.FilePath, position, seek, out lag);
            failures += CompareMusicSpectra(options.FilePath, lag);
            failures += CompareOwnSeeking(options.FilePath);
            return failures;
        }
        finally
        {
            Bass.Free();
        }
    }

    private static int CompareCalibrationSine(string path)
    {
        var bass = BassFftAt(path, 0.5);
        var ours = OurFftAt(path, 0.5, 0);
        if (bass == null || ours == null)
            return Program.Check("Calibration sine", "decoding failed", false);

        var bassPeak = PeakBin(bass);
        var ourPeak = PeakBin(ours);
        var ratio = bass[bassPeak] / ours[ourPeak];
        Console.WriteLine($"Calibration sine (amplitude 0.5, bin 100): BASS peak bin {bassPeak} = {bass[bassPeak]:F4}, " +
                          $"ours bin {ourPeak} = {ours[ourPeak]:F4}, BASS/ours = {ratio:F4}");
        return Program.Check("  Same peak bin", $"{bassPeak} vs {ourPeak}", bassPeak == ourPeak);
    }

    /// <param name="lagFrames">Offset of miniaudio's sample numbering against BASS's, so both FFTs see the same audio.</param>
    private static int CompareMusicSpectra(string path, int lagFrames)
    {
        double[] positions = [15, 45, 90, 180, 300, 500];
        double sumBassOurs = 0, sumOursOurs = 0;
        var spectra = new List<(float[] Bass, float[] Ours)>();
        foreach (var position in positions)
        {
            var bass = BassFftAt(path, position);
            var ours = OurFftAt(path, position, lagFrames);
            if (bass == null || ours == null)
                continue;
            spectra.Add((bass, ours));
            for (var bin = 1; bin < bass.Length; bin++)
            {
                sumBassOurs += bass[bin] * ours[bin];
                sumOursOurs += ours[bin] * ours[bin];
            }
        }

        if (spectra.Count == 0)
            return Program.Check("Music spectra", "decoding failed", false);

        // One global factor, as a replacement would apply it: fit, then measure what remains in TiXL's 0..1 space.
        var scale = (float)(sumBassOurs / sumOursOurs);
        double totalDifference = 0;
        var worstDifference = 0.0;
        var binCount = 0;
        foreach (var (bass, ours) in spectra)
        {
            for (var bin = 1; bin < bass.Length; bin++)
            {
                var difference = Math.Abs(Normalized(bass[bin]) - Normalized(ours[bin] * scale));
                totalDifference += difference;
                worstDifference = Math.Max(worstDifference, difference);
                binCount++;
            }
        }

        var meanDifference = totalDifference / binCount;
        Console.WriteLine($"Music spectra at {spectra.Count} positions (windows aligned by {lagFrames} samples): best global scale BASS/ours = {scale:F4}; after scaling, " +
                          $"mean |Δ| = {meanDifference:F4}, worst |Δ| = {worstDifference:F3} in TiXL's normalized (-80..0 dB → 0..1) space");
        return Program.Check("  Spectra match after one scale factor", $"mean |Δ| {meanDifference:F4}", meanDifference < 0.02);
    }

    /// <summary>
    /// Finds the lag between the two decoders' samples at the same nominal position. Near the start both read
    /// from the beginning instead of seeking, which separates start-of-file handling from seek accuracy.
    /// </summary>
    private static int ComparePcmAlignment(string path, double position, bool seeks, out int bestLag)
    {
        const int windowFrames = 8192;
        const int maxLag = 3000;
        bestLag = 0;

        var stream = Bass.CreateStream(path, 0, 0, BassFlags.Decode | BassFlags.Float | BassFlags.Prescan);
        if (stream == 0)
            return Program.Check("PCM alignment", $"BASS failed to open the file: {Bass.LastError}", false);

        float[] bassMono;
        int sampleRate;
        try
        {
            var info = Bass.ChannelGetInfo(stream);
            sampleRate = info.Frequency;
            var positionFrames = (int)(position * sampleRate);
            if (seeks)
            {
                Bass.ChannelSetPosition(stream, Bass.ChannelSeconds2Bytes(stream, position));
            }
            else
            {
                var skipped = new float[positionFrames * info.Channels];
                Bass.ChannelGetData(stream, skipped, skipped.Length * sizeof(float));
            }

            var interleaved = new float[windowFrames * info.Channels];
            Bass.ChannelGetData(stream, interleaved, interleaved.Length * sizeof(float));
            bassMono = ToMono(interleaved, info.Channels);
        }
        finally
        {
            Bass.StreamFree(stream);
        }

        // Our window starts maxLag before the position (or at 0), so index offset = position - start.
        var positionFrame = (long)(position * sampleRate);
        var startFrame = seeks ? positionFrame - maxLag : 0;

        var ours = new float[positionFrame - startFrame + windowFrames + maxLag];
        fixed (float* buffer = ours)
        {
            if (NativeAudio.DecodeWindow(path, (ulong)startFrame, (uint)ours.Length, 1, 1, buffer, out _) != 0)
                return Program.Check("PCM alignment", "miniaudio failed to decode the window", false);
        }

        var offset = (int)(positionFrame - startFrame);
        var bestError = double.MaxValue;
        for (var lag = -Math.Min(maxLag, offset); lag <= maxLag; lag++)
        {
            double error = 0;
            for (var index = 0; index < windowFrames; index += 4)
            {
                var difference = bassMono[index] - ours[index + offset + lag];
                error += difference * difference;
            }
            if (error < bestError)
            {
                bestError = error;
                bestLag = lag;
            }
        }

        double residual = 0, energy = 0;
        for (var index = 0; index < windowFrames; index++)
        {
            var difference = bassMono[index] - ours[index + offset + bestLag];
            residual += difference * difference;
            energy += bassMono[index] * bassMono[index];
        }

        var residualDb = 10 * Math.Log10(residual / Math.Max(energy, 1e-20));
        Console.WriteLine($"PCM alignment at {position} s ({(seeks ? "both seek" : "both read from the start")}): BASS's sample n is miniaudio's sample " +
                          $"n{bestLag:+0;-0} ({bestLag * 1000.0 / sampleRate:+0.00;-0.00} ms); residual after alignment {residualDb:F1} dB");
        return Program.Check("  Decoders agree on sample positions", $"lag {bestLag} samples", Math.Abs(bestLag) <= 1 && residualDb < -40);
    }

    /// <summary>Whether miniaudio lands on the same sample when seeking as when decoding linearly from the start.</summary>
    private static int CompareOwnSeeking(string path)
    {
        const int windowFrames = 4096;
        const double position = 20;
        uint sampleRate;
        var probe = new float[1];
        fixed (float* buffer = probe)
            NativeAudio.DecodeWindow(path, 0, 1, 1, 1, buffer, out sampleRate);

        var positionFrame = (int)(position * sampleRate);
        var linear = new float[positionFrame + windowFrames + 2000];
        var withTable = new float[windowFrames];
        var withoutTable = new float[windowFrames];
        fixed (float* linearBuffer = linear)
        fixed (float* tableBuffer = withTable)
        fixed (float* plainBuffer = withoutTable)
        {
            NativeAudio.DecodeWindow(path, 0, (uint)linear.Length, 1, 0, linearBuffer, out _);
            NativeAudio.DecodeWindow(path, (ulong)positionFrame, windowFrames, 1, 1, tableBuffer, out _);
            NativeAudio.DecodeWindow(path, (ulong)positionFrame, windowFrames, 1, 0, plainBuffer, out _);
        }

        var tableLag = FindLag(linear.AsSpan(positionFrame - 2000, windowFrames + 4000), withTable, 2000);
        var plainLag = FindLag(linear.AsSpan(positionFrame - 2000, windowFrames + 4000), withoutTable, 2000);
        Console.WriteLine($"miniaudio seek vs. linear decode at {position} s: with seek table {tableLag:+0;-0} samples, without {plainLag:+0;-0} samples");
        return Program.Check("  miniaudio seeks are sample-accurate", $"seek table {tableLag}, brute force {plainLag}", tableLag == 0 && plainLag == 0);
    }

    /** Offset of window inside reference (centered at maxLag) with the least squared error. */
    private static int FindLag(ReadOnlySpan<float> reference, ReadOnlySpan<float> window, int maxLag)
    {
        var bestLag = 0;
        var bestError = double.MaxValue;
        for (var lag = -maxLag; lag <= maxLag; lag++)
        {
            double error = 0;
            for (var index = 0; index < window.Length; index += 2)
            {
                var difference = window[index] - reference[index + maxLag + lag];
                error += difference * difference;
            }
            if (error < bestError)
            {
                bestError = error;
                bestLag = lag;
            }
        }
        return bestLag;
    }

    private static float[]? BassFftAt(string path, double seconds)
    {
        var stream = Bass.CreateStream(path, 0, 0, BassFlags.Decode | BassFlags.Float | BassFlags.Prescan);
        if (stream == 0)
            return null;
        try
        {
            Bass.ChannelSetPosition(stream, Bass.ChannelSeconds2Bytes(stream, seconds));
            var bins = new float[FftSize / 2];
            return Bass.ChannelGetData(stream, bins, (int)DataFlags.FFT2048) > 0 ? bins : null;
        }
        finally
        {
            Bass.StreamFree(stream);
        }
    }

    private static float[]? OurFftAt(string path, double seconds, int lagFrames)
    {
        var stereo = new float[FftSize * 2];
        fixed (float* buffer = stereo)
        {
            if (NativeAudio.DecodeWindow(path, 0, 1, 2, 1, buffer, out var sampleRate) != 0)
                return null;
            var startFrame = (long)(seconds * sampleRate) + lagFrames;
            if (startFrame < 0 || NativeAudio.DecodeWindow(path, (ulong)startFrame, FftSize, 2, 1, buffer, out _) != 0)
                return null;
        }

        var magnitudes = new float[FftSize / 2];
        new Fft(FftSize).ComputeMagnitudes(ToMono(stereo, 2), magnitudes);
        return magnitudes;
    }

    private static float[] ToMono(float[] interleaved, int channels)
    {
        var mono = new float[interleaved.Length / channels];
        for (var frame = 0; frame < mono.Length; frame++)
        {
            var sum = 0f;
            for (var channel = 0; channel < channels; channel++)
                sum += interleaved[frame * channels + channel];
            mono[frame] = sum / channels;
        }
        return mono;
    }

    private static int PeakBin(float[] magnitudes)
    {
        var peak = 1;
        for (var bin = 1; bin < magnitudes.Length; bin++)
        {
            if (magnitudes[bin] > magnitudes[peak])
                peak = bin;
        }
        return peak;
    }

    /** TiXL's mapping of an FFT gain to 0..1 (AudioAnalysisContext, gain factor 1). */
    private static double Normalized(float gain)
    {
        var decibels = 20 * Math.Log10(Math.Max(gain, 1e-10));
        return Math.Clamp((decibels + 80) / 80, 0, 1);
    }

    private static void WriteSineWav(string path, int sampleRate, double frequency, double amplitude, double seconds)
    {
        var frameCount = (int)(sampleRate * seconds);
        using var writer = new BinaryWriter(File.Create(path));
        var dataBytes = frameCount * 2 * sizeof(float);
        writer.Write("RIFF"u8);
        writer.Write(36 + dataBytes);
        writer.Write("WAVEfmt "u8);
        writer.Write(16);
        writer.Write((short)3); // IEEE float
        writer.Write((short)2);
        writer.Write(sampleRate);
        writer.Write(sampleRate * 2 * sizeof(float));
        writer.Write((short)(2 * sizeof(float)));
        writer.Write((short)32);
        writer.Write("data"u8);
        writer.Write(dataBytes);
        for (var frame = 0; frame < frameCount; frame++)
        {
            var sample = (float)(amplitude * Math.Sin(2 * Math.PI * frequency * frame / sampleRate));
            writer.Write(sample);
            writer.Write(sample);
        }
    }

    private const int FftSize = 2048;
}
