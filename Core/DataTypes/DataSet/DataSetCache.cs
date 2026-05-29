#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using Newtonsoft.Json.Linq;
using T3.Core.Logging;

namespace T3.Core.DataTypes.DataSet;

/// <summary>
/// Shared JSON-backed cache for <see cref="DataSet"/> assets loaded from <c>.data</c> files.
/// </summary>
/// <remarks>
/// <para>
/// A single parsed <see cref="DataSet"/> is shared across all <c>DataClip</c> operator
/// instances that reference the same file. The cache key is the absolute file path; the
/// stored entry remembers the file's last-write timestamp so an out-of-band edit (or the
/// <see cref="Resource{T}"/> file-watch firing) re-parses on demand.
/// </para>
/// <para>
/// The cache is the recommended way to access <c>.data</c> contents — direct
/// <see cref="TryLoadFile"/> calls bypass it only if you specifically need a private copy.
/// </para>
/// </remarks>
public static class DataSetCache
{
    /// <summary>
    /// Returns a shared <see cref="DataSet"/> for <paramref name="absolutePath"/>. Parses the
    /// file once per (path, last-write timestamp) tuple and serves subsequent callers from
    /// the cache. Reflects out-of-band file changes the next time the call is made.
    /// </summary>
    public static bool TryGet(string absolutePath, [NotNullWhen(true)] out DataSet? dataSet, out string failureReason)
    {
        dataSet = null;
        failureReason = string.Empty;

        if (string.IsNullOrEmpty(absolutePath))
        {
            failureReason = "Empty path";
            return false;
        }

        DateTime lastWrite;
        try
        {
            lastWrite = File.GetLastWriteTimeUtc(absolutePath);
        }
        catch (Exception e)
        {
            failureReason = $"Cannot stat file: {e.Message}";
            return false;
        }

        lock (_cache)
        {
            if (_cache.TryGetValue(absolutePath, out var cached) && cached.LastWrite == lastWrite)
            {
                dataSet = cached.DataSet;
                return true;
            }
        }

        if (!TryLoadFile(absolutePath, out var parsed, out failureReason))
            return false;

        lock (_cache)
        {
            // Race-safe replacement: even if a concurrent caller raced to parse first, both
            // parses are equivalent for the same on-disk content. The last writer wins; any
            // earlier reader keeps its DataSet reference (the entry just isn't deduped).
            _cache[absolutePath] = new CacheEntry(lastWrite, parsed!);
        }

        dataSet = parsed;
        return true;
    }

    /// <summary>
    /// Drops the cached entry for <paramref name="absolutePath"/>, forcing a re-parse on the
    /// next <see cref="TryGet"/>. Called by the <c>Resource&lt;DataSet&gt;</c> file-change
    /// hook in DataClip so the file watcher and the cache stay in sync.
    /// </summary>
    public static void Invalidate(string absolutePath)
    {
        lock (_cache)
        {
            _cache.Remove(absolutePath);
        }
    }

    /// <summary>
    /// Resolves the source path the given <see cref="DataSet"/> was loaded from. Returns
    /// false when the dataset isn't cache-owned (e.g. an in-flight recording's live set,
    /// or a programmatically-constructed set that never went through <see cref="TryGet"/>).
    /// </summary>
    public static bool TryGetPathFor(DataSet dataSet, [NotNullWhen(true)] out string? absolutePath)
    {
        lock (_cache)
        {
            foreach (var (path, entry) in _cache)
            {
                if (!ReferenceEquals(entry.DataSet, dataSet))
                    continue;
                absolutePath = path;
                return true;
            }
        }
        absolutePath = null;
        return false;
    }

    /// <summary>
    /// Persists in-memory edits to disk for a cache-owned <see cref="DataSet"/>. Used by
    /// commands that mutate the live set (Remove channel / events) so the change survives
    /// project reload and any subsequent cache miss. After the write the cache entry's
    /// timestamp is refreshed so a follow-up <see cref="TryGet"/> doesn't see "file newer
    /// than cache" and re-parse — which would orphan every existing consumer pointing at
    /// the old instance.
    /// </summary>
    public static bool TryWriteBack(DataSet dataSet, out string failureReason)
    {
        if (!TryGetPathFor(dataSet, out var absolutePath))
        {
            failureReason = "DataSet is not cache-owned; no source path to write to.";
            return false;
        }

        try
        {
            dataSet.WriteToFile(absolutePath);
        }
        catch (Exception e)
        {
            failureReason = $"Failed to write {absolutePath}: {e.Message}";
            return false;
        }

        // Stamp the cache entry with the file's new last-write so TryGet keeps returning
        // the same DataSet instance. If reading the timestamp fails we log and carry on —
        // worst case is a one-time re-parse on the next TryGet, which still produces an
        // equivalent set (we just wrote the file).
        try
        {
            var lastWrite = File.GetLastWriteTimeUtc(absolutePath);
            lock (_cache)
            {
                _cache[absolutePath] = new CacheEntry(lastWrite, dataSet);
            }
        }
        catch (Exception e)
        {
            Log.Warning($"DataSetCache.TryWriteBack: wrote {absolutePath} but failed to refresh cache timestamp: {e.Message}");
        }

        failureReason = string.Empty;
        return true;
    }

    /// <summary>
    /// Parses a <c>.data</c> JSON file into a fresh <see cref="DataSet"/> without touching
    /// the cache. Prefer <see cref="TryGet"/> for normal use.
    /// </summary>
    public static bool TryLoadFile(string absolutePath, [NotNullWhen(true)] out DataSet? dataSet, out string failureReason)
    {
        dataSet = null;
        failureReason = string.Empty;

        if (!File.Exists(absolutePath))
        {
            failureReason = $"File not found: {absolutePath}";
            return false;
        }

        try
        {
            var text = File.ReadAllText(absolutePath);
            var root = JObject.Parse(text);
            dataSet = ParseDataSet(root);
            return true;
        }
        catch (Exception e)
        {
            failureReason = $"Failed to parse {absolutePath}: {e.Message}";
            return false;
        }
    }

    // ---------------------------------------------------------------------------
    // JSON → DataSet. Mirrors the v1 layout produced by DataSet.WriteToFile:
    //   {
    //     "Version": 1,                        // optional, defaults to 1
    //     "Metadata": { ... },                 // optional
    //     "Guid": "<guid>",                            // dataset identity; healed on load if absent
    //     "Channels": [
    //       {
    //         "Guid": "<guid>",                        // channel identity; healed on load if absent
    //         "Path": ["a", "b", "c"],                  // array of segments (no '/' join)
    //         "Type": "float",
    //         "DurationType": "Tick" | "Interval",      // optional; absent → sniff per event
    //         "Metadata": { ... },                      // optional
    //         "Events": [
    //           { "Time": <num>, "Value": <typed> },                       // Tick
    //           { "Time": <num>, "EndTime": <num>, "Value": <typed> }      // Interval
    //         ]
    //       }
    //     ]
    //   }
    //
    // Duration-type discrimination:
    //   - "Tick"      → construct DataEvent regardless of whether EndTime is present.
    //   - "Interval"  → construct DataIntervalEvent (EndTime defaults to PositiveInfinity).
    //   - absent      → sniff per event: presence of "EndTime" → DataIntervalEvent, otherwise DataEvent.
    //                   Same as the original behaviour for pre-DurationType files.
    //
    // For v1 only "float" channels are reconstructed — that's everything the current
    // recorder (MIDI / OSC) produces. Non-float channels are skipped with a warning so
    // a future extension can land without breaking older files.
    // ---------------------------------------------------------------------------
    /// <summary>
    /// Parses a Guid from a JSON token if present and well-formed. Returns null when
    /// the token is missing, null, or unparseable — the caller heals with a fresh
    /// <see cref="Guid.NewGuid"/> so old files transparently gain identity on load.
    /// </summary>
    private static Guid? TryParseGuid(JToken? token)
    {
        if (token == null)
            return null;
        var s = (string?)token;
        if (string.IsNullOrEmpty(s))
            return null;
        return Guid.TryParse(s, out var guid) ? guid : null;
    }

    private static DataSet ParseDataSet(JObject root)
    {
        var dataSet = new DataSet
                          {
                              Guid = TryParseGuid(root["Guid"]) ?? Guid.NewGuid(),
                              Version = (int?)root["Version"] ?? 1,
                              Metadata = root["Metadata"] as JObject,
                          };

        var channelsToken = root["Channels"];
        if (channelsToken is not JArray channels)
            return dataSet;

        foreach (var channelToken in channels)
        {
            if (channelToken is not JObject channelObj)
                continue;

            var typeName = (string?)channelObj["Type"] ?? "float";
            if (typeName != "float")
            {
                Log.Warning($"DataSetCache: skipping channel of unsupported type '{typeName}' (only 'float' implemented in v1).");
                continue;
            }

            var pathSegments = new List<string>();
            if (channelObj["Path"] is JArray pathArr)
            {
                foreach (var seg in pathArr)
                {
                    var s = (string?)seg;
                    if (s != null)
                        pathSegments.Add(s);
                }
            }

            // DurationType is optional on the wire — absent means "pre-DurationType
            // file, sniff per event". Default to Tick in memory because that's the
            // conservative assumption; the sniff logic below promotes individual events
            // to interval when EndTime is present.
            var declaredDurationType = (string?)channelObj["DurationType"];
            var inMemoryDurationType = declaredDurationType ?? ChannelDurationTypes.Tick;

            var channel = new DataChannel(typeof(float), inMemoryDurationType)
                              {
                                  Guid = TryParseGuid(channelObj["Guid"]) ?? Guid.NewGuid(),
                                  Path = pathSegments,
                                  Metadata = channelObj["Metadata"] as JObject,
                              };
            dataSet.Channels.Add(channel);

            if (channelObj["Events"] is not JArray eventArr)
                continue;

            foreach (var eventToken in eventArr)
            {
                if (eventToken is not JObject ev)
                    continue;

                var time = (double?)ev["Time"] ?? 0.0;
                var value = (float?)ev["Value"] ?? 0f;
                var endTimeToken = ev["EndTime"];

                // Promote to interval if either (a) the channel declares DurationType =
                // Interval, or (b) DurationType is absent (legacy) and the event has an
                // EndTime field. A Tick channel never produces intervals even if a stray
                // EndTime appears — defensive against writer bugs.
                bool isInterval;
                if (declaredDurationType == ChannelDurationTypes.Interval)
                {
                    isInterval = true;
                }
                else if (declaredDurationType == ChannelDurationTypes.Tick)
                {
                    isInterval = false;
                }
                else
                {
                    isInterval = endTimeToken != null;
                }

                if (isInterval)
                {
                    var endTime = (double?)endTimeToken ?? double.PositiveInfinity;
                    channel.Events.Add(new DataIntervalEvent
                                           {
                                               Time = time,
                                               EndTime = endTime,
                                               Value = value,
                                           });
                }
                else
                {
                    channel.Events.Add(new DataEvent
                                           {
                                               Time = time,
                                               Value = value,
                                           });
                }
            }
        }

        return dataSet;
    }

    private readonly record struct CacheEntry(DateTime LastWrite, DataSet DataSet);

    private static readonly Dictionary<string, CacheEntry> _cache = new();
}
