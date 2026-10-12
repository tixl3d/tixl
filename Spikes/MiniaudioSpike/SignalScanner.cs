namespace T3.Spikes.MiniaudioSpike;

/// <summary>
/// Follows a continuous mono stream and counts jumps that a smooth test tone can't produce: a click from
/// a rerouting glitch, or the step into silence of an underrun.
/// </summary>
internal sealed class SignalScanner
{
    /// <param name="maxNaturalStep">Largest sample-to-sample change the test signal can make on its own.</param>
    public SignalScanner(float maxNaturalStep)
    {
        _threshold = maxNaturalStep * 2;
    }

    public long SamplesScanned { get; private set; }
    public int Discontinuities { get; private set; }
    public float LargestStep { get; private set; }

    public void Scan(ReadOnlySpan<float> samples)
    {
        for (var index = 0; index < samples.Length; index++)
        {
            var sample = samples[index];
            if (_hasPrevious)
            {
                var step = MathF.Abs(sample - _previous);
                if (step > LargestStep)
                    LargestStep = step;
                if (step > _threshold)
                    Discontinuities++;
            }
            _previous = sample;
            _hasPrevious = true;
        }
        SamplesScanned += samples.Length;
    }

    /** Largest step a sine of this frequency and amplitude makes between two samples. */
    public static float MaxSineStep(double frequency, double amplitude, int sampleRate)
    {
        return (float)(2 * Math.PI * frequency / sampleRate * amplitude);
    }

    private readonly float _threshold;
    private float _previous;
    private bool _hasPrevious;
}
