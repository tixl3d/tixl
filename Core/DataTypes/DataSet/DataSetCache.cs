#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using Newtonsoft.Json.Linq;
using T3.Core.Logging;

namespace T3.Core.DataTypes.DataSet;

/// <summary>
/// Shared JSON-backed cache for <see cref="DataSet"/> assets loaded from <c>.data</c> files
/// (live-session recording, see <c>.agentic/Plans/Plan_LiveSessionRecording.md</c> Phase 3b).
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
    // JSON → DataSet. Mirrors the layout produced by DataSet.WriteToFile:
    //   { "Channels": [ { "Path": "a/b/c", "Type": "float", "Events": [ ... ] } ] }
    //
    // Event shape:
    //   regular DataEvent       — { "TimeCode": <num>, "Value": <typed> }
    //   DataIntervalEvent       — { "TimeCode": <num>, "Time": <num>, "EndTime": <num>, "Value": <typed> }
    //
    // For v1 only "float" channels are reconstructed — that's everything the current
    // recorder (MIDI / OSC) produces. Non-float channels are skipped with a warning so
    // a future extension can land without breaking older files.
    // ---------------------------------------------------------------------------
    private static DataSet ParseDataSet(JObject root)
    {
        var dataSet = new DataSet();
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

            var pathRaw = (string?)channelObj["Path"] ?? string.Empty;
            var pathSegments = pathRaw.Length == 0
                                   ? new List<string>()
                                   : new List<string>(pathRaw.Split('/'));

            var channel = new DataChannel(typeof(float)) { Path = pathSegments };
            dataSet.Channels.Add(channel);

            if (channelObj["Events"] is not JArray eventArr)
                continue;

            foreach (var eventToken in eventArr)
            {
                if (eventToken is not JObject ev)
                    continue;

                var timeCode = (double?)ev["TimeCode"] ?? 0.0;
                var endTimeToken = ev["EndTime"];
                var value = (float?)ev["Value"] ?? 0f;

                if (endTimeToken != null)
                {
                    // Interval event. "Time" field is explicit; fall back to TimeCode if absent.
                    var time = (double?)ev["Time"] ?? timeCode;
                    var endTime = (double?)endTimeToken ?? double.PositiveInfinity;
                    channel.Events.Add(new DataIntervalEvent
                                           {
                                               Time = time,
                                               TimeCode = timeCode,
                                               EndTime = endTime,
                                               Value = value,
                                           });
                }
                else
                {
                    // Plain event. WriteToFile only persists TimeCode for these; mirror it into Time
                    // so DataChannel.FindIndexForTime (which reads Time) works on the loaded data.
                    channel.Events.Add(new DataEvent
                                           {
                                               Time = timeCode,
                                               TimeCode = timeCode,
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
