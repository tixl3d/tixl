namespace T3.Spikes.MiniaudioSpike;

/// <summary>
/// In-place radix-2 FFT over real input with a Hann window. All tables and work buffers are allocated in
/// the constructor, so <see cref="ComputeMagnitudes"/> is allocation-free and safe to call every frame.
/// </summary>
internal sealed class Fft
{
    public Fft(int size)
    {
        if (size < 2 || (size & (size - 1)) != 0)
            throw new ArgumentException("FFT size must be a power of two.", nameof(size));

        Size = size;
        _real = new float[size];
        _imaginary = new float[size];
        _window = new float[size];
        _cosTable = new float[size / 2];
        _sinTable = new float[size / 2];
        _bitReversed = new int[size];

        for (var sampleIndex = 0; sampleIndex < size; sampleIndex++)
            _window[sampleIndex] = 0.5f * (1f - MathF.Cos(2f * MathF.PI * sampleIndex / (size - 1)));

        for (var twiddleIndex = 0; twiddleIndex < size / 2; twiddleIndex++)
        {
            _cosTable[twiddleIndex] = MathF.Cos(-2f * MathF.PI * twiddleIndex / size);
            _sinTable[twiddleIndex] = MathF.Sin(-2f * MathF.PI * twiddleIndex / size);
        }

        var bits = (int)Math.Log2(size);
        for (var index = 0; index < size; index++)
        {
            var reversed = 0;
            for (var bit = 0; bit < bits; bit++)
                reversed |= ((index >> bit) & 1) << (bits - 1 - bit);
            _bitReversed[index] = reversed;
        }
    }

    public int Size { get; }

    /// <summary>
    /// Writes <c>Size / 2</c> magnitudes, scaled so a full-scale sine reads about 1.
    /// </summary>
    public void ComputeMagnitudes(ReadOnlySpan<float> samples, Span<float> magnitudes)
    {
        for (var index = 0; index < Size; index++)
        {
            var source = _bitReversed[index];
            _real[index] = samples[source] * _window[source];
            _imaginary[index] = 0;
        }

        for (var halfSpan = 1; halfSpan < Size; halfSpan <<= 1)
        {
            var twiddleStride = Size / (halfSpan * 2);
            for (var blockStart = 0; blockStart < Size; blockStart += halfSpan * 2)
            {
                for (var offset = 0; offset < halfSpan; offset++)
                {
                    var twiddleIndex = offset * twiddleStride;
                    var cos = _cosTable[twiddleIndex];
                    var sin = _sinTable[twiddleIndex];
                    var even = blockStart + offset;
                    var odd = even + halfSpan;

                    var oddReal = _real[odd] * cos - _imaginary[odd] * sin;
                    var oddImaginary = _real[odd] * sin + _imaginary[odd] * cos;
                    _real[odd] = _real[even] - oddReal;
                    _imaginary[odd] = _imaginary[even] - oddImaginary;
                    _real[even] += oddReal;
                    _imaginary[even] += oddImaginary;
                }
            }
        }

        // A Hann window halves a sine's amplitude, and a real sine splits its energy across two bins.
        var scale = 4f / Size;
        for (var bin = 0; bin < Size / 2; bin++)
            magnitudes[bin] = MathF.Sqrt(_real[bin] * _real[bin] + _imaginary[bin] * _imaginary[bin]) * scale;
    }

    private readonly float[] _real;
    private readonly float[] _imaginary;
    private readonly float[] _window;
    private readonly float[] _cosTable;
    private readonly float[] _sinTable;
    private readonly int[] _bitReversed;
}
