#nullable enable
using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using T3.Core.Logging;

namespace T3.Editor.App.DebugProtocol;

/// <summary>
/// Bounded, sequence-numbered ring buffer of log records serving the debug protocol's getLogTail.
/// Registered as a log writer only when the debug server is enabled.
/// <see cref="ProcessEntry"/> can be called from any thread; reads happen on the main thread.
/// </summary>
internal sealed class DebugLogBuffer : ILogWriter
{
    public static readonly DebugLogBuffer Instance = new();

    public ILogEntry.EntryLevel Filter { get; set; } = ILogEntry.EntryLevel.All;

    public void ProcessEntry(ILogEntry entry)
    {
        var buffered = new BufferedEntry(0, entry.TimeStamp, entry.Level, entry.Message, entry.SourceId);
        lock (_lock)
        {
            buffered.Seq = _nextSeq;
            _entries[_nextSeq % Capacity] = buffered;
            _nextSeq++;
        }
    }

    /// <summary>
    /// Appends entries with seq &gt; <paramref name="sinceSeq"/> and level &gt;= <paramref name="minLevel"/>
    /// to <paramref name="into"/>, at most <paramref name="limit"/>. A negative <paramref name="sinceSeq"/>
    /// returns the newest entries up to the limit.
    /// </summary>
    public void CollectEntries(long sinceSeq, ILogEntry.EntryLevel minLevel, int limit, JArray into,
                               out long latestSeq, out long oldestAvailableSeq)
    {
        lock (_lock)
        {
            latestSeq = _nextSeq - 1;
            oldestAvailableSeq = Math.Max(0, _nextSeq - Capacity);

            var firstSeq = sinceSeq < 0
                               ? Math.Max(oldestAvailableSeq, _nextSeq - limit)
                               : Math.Max(oldestAvailableSeq, sinceSeq + 1);

            var appended = 0;
            for (var seq = firstSeq; seq < _nextSeq && appended < limit; seq++)
            {
                var entry = _entries[seq % Capacity];
                if (entry.Level < minLevel)
                    continue;

                into.Add(new JObject
                             {
                                 ["seq"] = entry.Seq,
                                 ["time"] = entry.TimeStamp.ToString("HH:mm:ss.fff"),
                                 ["level"] = entry.Level.ToString(),
                                 ["message"] = entry.Message,
                                 ["sourceId"] = entry.SourceId == Guid.Empty ? null : entry.SourceId.ToString(),
                             });
                appended++;
            }
        }
    }

    public void Dispose()
    {
    }

    private struct BufferedEntry(long seq, DateTime timeStamp, ILogEntry.EntryLevel level, string message, Guid sourceId)
    {
        public long Seq = seq;
        public readonly DateTime TimeStamp = timeStamp;
        public readonly ILogEntry.EntryLevel Level = level;
        public readonly string Message = message;
        public readonly Guid SourceId = sourceId;
    }

    private const int Capacity = 4096;
    private readonly BufferedEntry[] _entries = new BufferedEntry[Capacity];
    private long _nextSeq;
    private readonly object _lock = new();
}
