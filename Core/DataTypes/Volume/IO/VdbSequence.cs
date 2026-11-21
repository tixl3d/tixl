#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace T3.Core.DataTypes.Volume.IO;

/// <summary>
/// Frame addressing for animated NanoVDB caches, which are one file per frame. Houdini, Blender and
/// EmberGen all write numbered sequences, so a single pattern covers the common cases.
/// </summary>
public static class VdbSequence
{
    /// <summary>
    /// Expands the numeric token in <paramref name="pattern"/> for <paramref name="frame"/>.
    /// Supports <c>####</c> (as many digits as there are <c>#</c>) and printf forms such as
    /// <c>%04d</c>. A pattern without a token is returned unchanged, so a still image still works.
    /// </summary>
    public static string Expand(string pattern, int frame)
    {
        if (string.IsNullOrEmpty(pattern))
            return pattern;

        // #### or ##: the token runs from the first '#' to the last one before a separator.
        var hashStart = pattern.IndexOf('#');
        if (hashStart >= 0)
        {
            var hashEnd = hashStart;
            while (hashEnd + 1 < pattern.Length && pattern[hashEnd + 1] == '#')
                hashEnd++;

            var digits = hashEnd - hashStart + 1;
            var number = frame.ToString(CultureInfo.InvariantCulture);
            if (number.Length < digits)
                number = number.PadLeft(digits, '0');

            return string.Concat(pattern.AsSpan(0, hashStart), number, pattern.AsSpan(hashEnd + 1));
        }

        // %d and friends. Negative frames are not meaningful for a cache, so the sign is dropped.
        var percent = pattern.IndexOf('%');
        if (percent >= 0)
        {
            var i = percent + 1;
            var width = 0;
            while (i < pattern.Length && char.IsDigit(pattern[i]))
            {
                width = width * 10 + (pattern[i] - '0');
                i++;
            }

            if (i < pattern.Length && (pattern[i] == 'd' || pattern[i] == 'D'))
            {
                var number = Math.Abs(frame).ToString(CultureInfo.InvariantCulture);
                if (width > 0 && number.Length < width)
                    number = number.PadLeft(width, '0');

                return string.Concat(pattern.AsSpan(0, percent), number, pattern.AsSpan(i + 1));
            }
        }

        return pattern;
    }

    /// <summary>True when the pattern actually addresses a sequence, i.e. expanding two frames differs.</summary>
    public static bool IsAnimated(string pattern)
        => !string.IsNullOrEmpty(pattern) && Expand(pattern, 1) != Expand(pattern, 2);
}

/// <summary>
/// A byte budget for preloaded sequence frames. Animated caches are 50-500 MB per frame, so the
/// budget has to be in bytes rather than frames. Least recently used frames are dropped first, and
/// a frame larger than the whole budget is simply not retained.
///
/// Thread-safe: the preloader fills it from a worker while the evaluation thread takes from it.
/// </summary>
public sealed class VdbFrameCache
{
    public VdbFrameCache(long budgetBytes)
    {
        _budgetBytes = Math.Max(0, budgetBytes);
    }

    public long BudgetBytes => _budgetBytes;
    public long UsedBytes { get; private set; }
    public int Count => _entries.Count;

    /// <summary>Takes a frame out of the cache, or returns false when it has not been preloaded.</summary>
    public bool TryTake(string path, out byte[]? bytes)
    {
        if (string.IsNullOrEmpty(path))
        {
            bytes = null;
            return false;
        }

        lock (_gate)
        {
            if (!_entries.TryGetValue(path, out var entry))
            {
                bytes = null;
                return false;
            }

            // Move to the recent end so the preloader evicts frames the playhead has not reached.
            _recency.Remove(entry.RecencyNode);
            entry.RecencyNode = _recency.AddLast(path);
            bytes = entry.Bytes;
            return true;
        }
    }

    /// <summary>Stores a preloaded frame, evicting the least recently used frames to stay in budget.</summary>
    public void Put(string path, byte[] bytes)
    {
        if (string.IsNullOrEmpty(path) || bytes == null)
            return;

        lock (_gate)
        {
            if (_entries.TryGetValue(path, out var existing))
            {
                UsedBytes -= existing.Bytes.Length;
                _recency.Remove(existing.RecencyNode);
                _entries.Remove(path);
            }

            // A frame that cannot fit on its own would only evict everything else and still be dropped.
            if (bytes.Length > _budgetBytes)
                return;

            while (UsedBytes + bytes.Length > _budgetBytes && _recency.First != null)
            {
                var oldest = _recency.First.Value;
                _recency.RemoveFirst();
                if (_entries.Remove(oldest, out var evicted))
                    UsedBytes -= evicted.Bytes.Length;
            }

            var node = _recency.AddLast(path);
            _entries[path] = new Entry(bytes, node);
            UsedBytes += bytes.Length;
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _entries.Clear();
            _recency.Clear();
            UsedBytes = 0;
        }
    }

    private sealed class Entry(byte[] bytes, LinkedListNode<string> recencyNode)
    {
        public readonly byte[] Bytes = bytes;
        public LinkedListNode<string> RecencyNode = recencyNode;
    }

    private readonly object _gate = new();
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    private readonly LinkedList<string> _recency = new();
    private readonly long _budgetBytes;
}
