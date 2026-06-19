using System.Threading;
using Sdcb.FFmpeg.Utils;

namespace T3.VideoServices;

/// <summary>
/// Per-stream LRU cache of decoded frames in their native (source) pixel format, keyed by PTS. Frames are
/// retained as ref-counted FFmpeg <see cref="Frame"/>s — the pixel bytes stay in native memory, never on the
/// managed heap — so revisiting a frame (scrub back, loop, step) becomes a cache hit instead of a keyframe
/// seek plus decode. Bounded by a byte budget; least-recently-used frames evict first. Owned by a single
/// decode worker; not thread-safe.
/// </summary>
public sealed class VideoFrameCache : IDisposable
{
    private struct Entry
    {
        public Frame Frame;
        public int Bytes;
        public long LastAccess;
    }

    public VideoFrameCache(long byteBudget)
    {
        _byteBudget = byteBudget;
    }

    public long ByteCount => _totalBytes;
    public int Count => _entries.Count;

    /// <summary>Returns the cached frame for <paramref name="pts"/> as most-recently-used, or false on a miss.</summary>
    public bool TryGet(long pts, out Frame frame)
    {
        if (_entries.TryGetValue(pts, out var entry))
        {
            entry.LastAccess = ++_accessCounter;
            _entries[pts] = entry;
            frame = entry.Frame;
            return true;
        }

        frame = null!;
        return false;
    }

    /// <summary>
    /// Retains a ref-counted clone of <paramref name="source"/> under <paramref name="pts"/> (no pixel copy),
    /// then evicts least-recently-used entries to stay within the budget. <paramref name="frameByteSize"/> is
    /// the per-frame source size, constant for a stream. A pts already present is a no-op (bumped to MRU).
    /// </summary>
    public void Add(long pts, Frame source, int frameByteSize)
    {
        if (_entries.TryGetValue(pts, out var existing))
        {
            existing.LastAccess = ++_accessCounter;
            _entries[pts] = existing;
            return;
        }

        _entries[pts] = new Entry { Frame = source.Clone(), Bytes = frameByteSize, LastAccess = ++_accessCounter };
        _totalBytes += frameByteSize;
        EvictToBudget();
    }

    /// <summary>
    /// Updates the byte budget (the engine divides one shared global budget across live streams). Called from
    /// the engine's eval thread while the worker owns this cache, so it only stores the new value; eviction to
    /// it happens on the worker's next <see cref="Add"/>.
    /// </summary>
    public void SetBudget(long byteBudget) => Volatile.Write(ref _byteBudget, byteBudget);

    public void Clear()
    {
        foreach (var entry in _entries.Values)
            entry.Frame.Dispose();

        _entries.Clear();
        _totalBytes = 0;
    }

    public void Dispose() => Clear();

    // Drops least-recently-used entries until within budget, always keeping at least the just-added frame.
    // The O(n) min-scan is allocation-free and n stays small (hundreds of frames); it only runs when full, so
    // it beats a LinkedList LRU that would allocate a node per cached frame on the per-frame decode path.
    private void EvictToBudget()
    {
        var budget = Volatile.Read(ref _byteBudget);
        while (_totalBytes > budget && _entries.Count > 1)
        {
            var oldestPts = 0L;
            var oldestAccess = long.MaxValue;
            foreach (var pair in _entries)
            {
                if (pair.Value.LastAccess >= oldestAccess)
                    continue;

                oldestAccess = pair.Value.LastAccess;
                oldestPts = pair.Key;
            }

            var evicted = _entries[oldestPts];
            _totalBytes -= evicted.Bytes;
            evicted.Frame.Dispose();
            _entries.Remove(oldestPts);
        }
    }

    private readonly Dictionary<long, Entry> _entries = new();
    private long _byteBudget;
    private long _totalBytes;
    private long _accessCounter;
}
