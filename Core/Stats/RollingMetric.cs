using System;

namespace T3.Core.Stats;

/// <summary>
/// Sliding-window sample buffer with a bucketed histogram and running aggregates.
/// Pure data: no drawing. Pair with a renderer (e.g. Editor's <c>MetricGraphView</c>) to visualise.
///
/// All updates are O(1) amortized and allocation-free after construction:
///   - cyclic sample buffer
///   - histogram increment/decrement on head/tail only
///   - sliding sum for average
///   - monotonic deques for exact min/max over the window
/// </summary>
public sealed class RollingMetric
{
    /// <summary>Per-bucket state inside a <see cref="RollingMetric"/>.</summary>
    public struct HistogramSlot
    {
        public int CountRecent;       // values in the current sliding window
        public long CountTotal;       // all-time count since construction
        public double LastIncrementTime;
        public float ValueRangeMin;   // lower edge of the bucket's value range
    }

    public delegate int BucketIndexer(float value);

    public int Capacity => _capacity;
    public int Count => _count;
    public int BucketCount => _slots.Length;
    public ReadOnlySpan<HistogramSlot> Slots => _slots;

    public float Min => _minCount > 0 ? _values[_minDeque[_minHead]] : 0f;
    public float Max => _maxCount > 0 ? _values[_maxDeque[_maxHead]] : 0f;
    public float Average => _count > 0 ? (float)(_slidingSum / _count) : 0f;
    public float Last => _count > 0 ? _values[(_tail - 1 + _capacity) % _capacity] : 0f;

    public RollingMetric(int capacity, int bucketCount, BucketIndexer bucketOf, Func<int, float> bucketRangeMin)
    {
        if (capacity <= 0) throw new ArgumentException("capacity must be > 0", nameof(capacity));
        if (bucketCount <= 0) throw new ArgumentException("bucketCount must be > 0", nameof(bucketCount));

        _capacity = capacity;
        _values = new float[capacity];
        _bucketOfValue = new int[capacity];
        _bucketOf = bucketOf;

        _slots = new HistogramSlot[bucketCount];
        for (var i = 0; i < bucketCount; i++)
            _slots[i].ValueRangeMin = bucketRangeMin(i);

        _minDeque = new int[capacity];
        _maxDeque = new int[capacity];
    }

    public void Update(float value, double time)
    {
        if (_count == _capacity)
            EvictOldest();

        var idx = _tail;
        _values[idx] = value;

        var b = _bucketOf(value);
        if (b < 0) b = 0;
        else if (b >= _slots.Length) b = _slots.Length - 1;

        _bucketOfValue[idx] = b;
        ref var slot = ref _slots[b];
        slot.CountRecent++;
        slot.CountTotal++;
        slot.LastIncrementTime = time;

        _slidingSum += value;

        // Min deque: strictly ascending from head to tail.
        while (_minCount > 0)
        {
            var backIdx = (_minTail - 1 + _capacity) % _capacity;
            if (_values[_minDeque[backIdx]] < value) break;
            _minTail = backIdx;
            _minCount--;
        }
        _minDeque[_minTail] = idx;
        _minTail = (_minTail + 1) % _capacity;
        _minCount++;

        // Max deque: strictly descending from head to tail.
        while (_maxCount > 0)
        {
            var backIdx = (_maxTail - 1 + _capacity) % _capacity;
            if (_values[_maxDeque[backIdx]] > value) break;
            _maxTail = backIdx;
            _maxCount--;
        }
        _maxDeque[_maxTail] = idx;
        _maxTail = (_maxTail + 1) % _capacity;
        _maxCount++;

        _tail = (_tail + 1) % _capacity;
        _count++;
    }

    /// <summary>
    /// Returns the samples in chronological order (oldest first) as a contiguous span, without allocating.
    /// When the data is already contiguous inside the internal buffer the span points directly at it
    /// (zero-copy); otherwise the samples are copied into <paramref name="scratch"/>.
    /// The caller owns <paramref name="scratch"/> and should reuse the same array across frames.
    /// </summary>
    public ReadOnlySpan<float> AsOrderedSpan(float[] scratch)
    {
        if (_count == 0)
            return ReadOnlySpan<float>.Empty;

        if (_head < _tail)
            return new ReadOnlySpan<float>(_values, _head, _count);

        if (scratch.Length < _count)
            throw new ArgumentException("Scratch array is too small to hold the samples.", nameof(scratch));

        var firstLen = _capacity - _head;
        _values.AsSpan(_head, firstLen).CopyTo(scratch);
        _values.AsSpan(0, _tail).CopyTo(scratch.AsSpan(firstLen));
        return new ReadOnlySpan<float>(scratch, 0, _count);
    }

    // ---------- factories ----------

    /// <summary>
    /// Linear bucketing. Bucket i covers [minValue + i*w, minValue + (i+1)*w) where w = (maxValue - minValue) / bucketCount.
    /// </summary>
    public static RollingMetric CreateLinear(int capacity, int bucketCount, float minValue, float maxValue)
    {
        var width = (maxValue - minValue) / bucketCount;
        return new RollingMetric(
            capacity, bucketCount,
            v => (int)MathF.Floor((v - minValue) / width),
            i => minValue + i * width);
    }
    
    /// <summary>
    /// Log10 bucketing over [10^log10Min, 10^log10Max]. Non-positive values fall in bucket 0.
    /// </summary>
    public static RollingMetric CreateLog10(int capacity, int bucketCount, float log10Min, float log10Max)
    {
        var step = (log10Max - log10Min) / bucketCount;
        return new RollingMetric(
            capacity, bucketCount,
            v => v > 0f ? (int)MathF.Floor((MathF.Log10(v) - log10Min) / step) : 0,
            i => MathF.Pow(10f, log10Min + i * step));
    }

    private void EvictOldest()
    {
        var idx = _head;
        var v = _values[idx];
        var b = _bucketOfValue[idx];
        _slots[b].CountRecent--;
        _slidingSum -= v;

        if (_minCount > 0 && _minDeque[_minHead] == idx)
        {
            _minHead = (_minHead + 1) % _capacity;
            _minCount--;
        }
        if (_maxCount > 0 && _maxDeque[_maxHead] == idx)
        {
            _maxHead = (_maxHead + 1) % _capacity;
            _maxCount--;
        }

        _head = (_head + 1) % _capacity;
        _count--;
    }

    // --- sample buffer ---
    private readonly float[] _values;
    private readonly int[] _bucketOfValue; // parallel: which bucket each stored value was placed in
    private int _head;   // index of oldest sample
    private int _tail;   // next write position
    private int _count;
    private readonly int _capacity;

    // --- histogram ---
    private readonly HistogramSlot[] _slots;
    private readonly BucketIndexer _bucketOf;

    // --- aggregates ---
    private double _slidingSum;

    // --- monotonic deques for running min / max (ring-buffer storage of value-array indices) ---
    private readonly int[] _minDeque;
    private int _minHead, _minTail, _minCount;
    private readonly int[] _maxDeque;
    private int _maxHead, _maxTail, _maxCount;
}
