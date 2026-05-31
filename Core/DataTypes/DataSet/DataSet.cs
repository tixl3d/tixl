#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using T3.Core.Logging;
using T3.Core.Model;

namespace T3.Core.DataTypes.DataSet;

/// <summary>
/// A set of <see cref="DataChannel"/> event channels — the content of a recorded session
/// (MIDI / OSC capture, debug telemetry, etc.) or any other time-stamped event stream a
/// graph op wants to expose. Persisted as <c>.data</c> JSON files via
/// <see cref="WriteToFile(string)"/> and read back through
/// <see cref="DataSetCache.TryGet"/>.
/// </summary>
public sealed class DataSet
{
    /// <summary>
    /// Stable identity for the dataset, persisted to disk. Lets caches and future
    /// cross-references survive file renames. Generated fresh by new in-memory datasets
    /// (recording or programmatic creation); old files without the field get a fresh
    /// Guid healed on load and persisted on the next save.
    /// </summary>
    public Guid Guid { get; set; } = Guid.NewGuid();

    /// <summary>
    /// On-disk format version. New files always write <see cref="CurrentVersion"/>;
    /// old files without the key are treated as v1. Bumping the version lets a future
    /// reader degrade gracefully (warn and load what it understands) rather than fail.
    /// </summary>
    public int Version { get; set; } = CurrentVersion;

    /// <summary>
    /// Optional session-level metadata bag — null when unused. Use for things that don't
    /// belong on a channel or event: recording-fps, source device tree, session notes,
    /// the wall-clock timestamp the recording started, etc. Backed by <see cref="JObject"/>
    /// so arbitrary JSON shapes round-trip without per-key allocations.
    /// </summary>
    public JObject? Metadata { get; set; }

    public List<DataChannel> Channels { get; set; } = new();

    /// <summary>Current writer version. Old readers ignore the key; new readers honour it.</summary>
    public const int CurrentVersion = 1;

    /// <summary>
    /// Well-known <see cref="Metadata"/> keys and reserved values — the read/write contract
    /// for the session metadata bag, so the recorder and consumers can't drift on spelling.
    /// Keys not listed here are free-form.
    /// </summary>
    public static class MetadataKeys
    {
        /// <summary>TiXL build that produced the recording (provenance).</summary>
        public const string TixlVersion = "TixlVersion";

        /// <summary>ISO-8601 wall-clock moment the session started (provenance).</summary>
        public const string RecordedAtUtc = "RecordedAtUtc";

        /// <summary>Playback BPM at record-start — the tempo anchor for the clip's bars↔seconds mapping.</summary>
        public const string Bpm = "Bpm";

        /// <summary>
        /// Unit that <see cref="DataEvent.Time"/> / <see cref="DataIntervalEvent.EndTime"/>
        /// are expressed in. Absent ⇒ <see cref="TimeUnitSeconds"/> (back-compat default).
        /// A string discriminator rather than a bool so it can grow without a format bump.
        /// </summary>
        public const string TimeUnits = "TimeUnits";

        /// <summary>Absolute seconds since record-start — tempo-independent. The only mode written today.</summary>
        public const string TimeUnitSeconds = "seconds";

        /// <summary>
        /// Reserved: bar-relative time that re-stretches with the project BPM (for piano-roll
        /// MIDI clips). Not yet written or consumed — documented so future work uses this token.
        /// </summary>
        public const string TimeUnitBars = "bars";
    }

    public void Clear()
    {
        Channels.Clear();
        Metadata = null;
    }

    /// <summary>
    /// Serialises the dataset to <paramref name="path"/> as JSON.
    /// </summary>
    /// <remarks>
    /// On-disk shape (v1):
    /// <code>
    /// {
    ///   "Guid": "&lt;guid&gt;",                     // identity, healed on load if absent
    ///   "Version": 1,                         // optional, defaults to 1 if absent
    ///   "Metadata": { ... },                  // optional, omitted when null
    ///   "Channels": [
    ///     {
    ///       "Guid": "&lt;guid&gt;",                 // per-channel identity, healed on load
    ///       "Path": ["Midi", "&lt;device&gt;", "Ch&lt;n&gt;", "CC74"],
    ///       "Type": "float",
    ///       "DurationType": "Tick",           // optional; old files sniff per event
    ///       "Metadata": { ... },              // optional, omitted when null
    ///       "Events": [
    ///         { "Time": 0.301, "Value": 70.0 },                          // point event
    ///         { "Time": 1.5,   "EndTime": 2.0, "Value": 1.0 }            // interval event
    ///       ]
    ///     }
    ///   ]
    /// }
    /// </code>
    /// All event-shape information lives on the channel (<see cref="DataChannel.DurationType"/>),
    /// not per event — events are the hot multiplicand, so duration-type / metadata stay
    /// channel-level.
    /// </remarks>
    public void WriteToFile(string path)
    {
        using var sw = new StreamWriter(path);
        using var writer = new JsonTextWriter(sw);

        writer.Formatting = Formatting.Indented;
        writer.WriteStartObject();

        writer.WritePropertyName("Guid");
        writer.WriteValue(Guid.ToString());

        writer.WritePropertyName("Version");
        writer.WriteValue(Version);

        if (Metadata != null && Metadata.HasValues)
        {
            writer.WritePropertyName("Metadata");
            Metadata.WriteTo(writer);
        }

        writer.WritePropertyName("Channels");
        writer.WriteStartArray();

        foreach (var c in Channels)
        {
            c.WriteToJson(writer);
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
    }
}

public sealed class DataChannel
{
    internal DataChannel(Type type, string durationType = ChannelDurationTypes.Tick)
    {
        _type = type;

        if (!TypeNameRegistry.Entries.TryGetValue(type, out var typeName))
        {
            throw new Exception("Can't create channel for unregistered value type");
        }

        _typeName = typeName;
        DurationType = durationType;
    }

    public required List<string> Path { get; init; }

    /// <summary>
    /// Stable identity for the channel within its <see cref="DataSet"/>, persisted to
    /// disk. Survives <see cref="Path"/> rewrites — useful once channel renaming /
    /// editing arrives. Generated fresh on construction; healed on load when missing.
    /// </summary>
    public Guid Guid { get; set; } = Guid.NewGuid();

    /// <summary>
    /// Whether events on this channel occupy a moment in time (tick) or span a duration
    /// (interval). Channel-level so high-cardinality streams (e.g. 30 Hz CC) don't pay
    /// per-event overhead. Use <see cref="ChannelDurationTypes"/> constants — the value
    /// is a string so future shapes can be added without subclass churn.
    /// </summary>
    public string DurationType { get; init; }

    /// <summary>
    /// Optional channel-level metadata bag — null when unused. Use for display label,
    /// color, source device info, controller-friendly-name, etc. Backed by
    /// <see cref="JObject"/> so arbitrary JSON shapes round-trip without per-key allocations.
    /// </summary>
    public JObject? Metadata { get; set; }

    public List<DataEvent?> Events { get; set; } = new(100);

    public DataEvent? GetLastEvent()
    {
        if (Events.Count == 0)
            return null;

        return Events[^1];
    }

    internal void WriteToJson(JsonTextWriter writer)
    {
        if (!TypeValueToJsonConverters.Entries.TryGetValue(_type, out var converter))
        {
            Log.Debug($"Can't find converter for type {_type}");
            return;
        }

        writer.WriteStartObject();
        {
            writer.WritePropertyName("Guid");
            writer.WriteValue(Guid.ToString());

            // Path serialises as a JSON array — segments stay separate, no '/' join /
            // split round-trip, no need to escape forward slashes in device names.
            writer.WritePropertyName("Path");
            writer.WriteStartArray();
            foreach (var segment in Path)
                writer.WriteValue(segment);
            writer.WriteEndArray();

            writer.WritePropertyName("Type");
            writer.WriteValue(_typeName);

            writer.WritePropertyName("DurationType");
            writer.WriteValue(DurationType);

            if (Metadata != null && Metadata.HasValues)
            {
                writer.WritePropertyName("Metadata");
                Metadata.WriteTo(writer);
            }

            writer.WritePropertyName("Events");
            writer.WriteStartArray();
            lock (Events)
            {
                foreach (var dataEvent in Events.ToList())
                {
                    dataEvent?.ToJson(converter, writer);
                }
            }

            writer.WriteEndArray();
        }

        writer.WriteEndObject();
    }

    public int FindIndexForTime(double time, bool findUpperIndex = true)
    {
        if (Events.Count == 0)
            return -1;

        var lastIndex = Events.Count - 1;
        var firstIndex = 0;

        if (Events[lastIndex]?.Time <= time)
            return lastIndex;

        if (Events[firstIndex]?.Time >= time)
            return firstIndex;

        while (lastIndex - firstIndex > 1)
        {
            var middleIndex = (firstIndex + lastIndex) / 2;

            var delta = (Events[middleIndex]?.Time ?? 0) - time;

            if (delta < 0)
                firstIndex = middleIndex;
            else
                lastIndex = middleIndex;
        }
        return firstIndex;
    }

    private readonly Type _type;
    private readonly string _typeName;
}

/// <summary>
/// Event duration-type discriminators stored in <see cref="DataChannel.DurationType"/>.
/// New values can be added freely; readers that don't recognise the string fall back to
/// sniffing the event payload, so additions are safe for old code.
/// </summary>
public static class ChannelDurationTypes
{
    /// <summary>Moment-in-time events: <c>{ Time, Value }</c>. The common case (MIDI CC, OSC, telemetry).</summary>
    public const string Tick = "Tick";

    /// <summary>Events with duration: <c>{ Time, EndTime, Value }</c>. MIDI notes, subtitles, regions.</summary>
    public const string Interval = "Interval";
}

public class DataEvent
{
    public double Time;
    public required object Value { get; set; }

    internal virtual void ToJson(Action<JsonTextWriter, object> converter, JsonTextWriter writer)
    {
        writer.WriteStartObject();
        writer.WritePropertyName("Time");
        writer.WriteValue(Time);
        writer.WritePropertyName("Value");
        converter(writer, Value);
        writer.WriteEndObject();
    }

    public bool TryGetNumericValue(out double v)
    {
        switch (Value)
        {
            case float f: v = f; break;
            case double d: v = d; break;
            case int i: v = i; break;
            case long l: v = l; break;
            default: v = double.NaN; return false;
        }

        return !double.IsNaN(v);
    }
}

public sealed class DataIntervalEvent : DataEvent
{
    public double EndTime = double.PositiveInfinity;

    public bool IsUnfinished => double.IsInfinity(EndTime);

    internal void Finish(double someTime)
    {
        EndTime = someTime;
    }

    internal override void ToJson(Action<JsonTextWriter, object> converter, JsonTextWriter writer)
    {
        writer.WriteStartObject();
        writer.WritePropertyName("Time");
        writer.WriteValue(Time);
        writer.WritePropertyName("EndTime");
        writer.WriteValue(EndTime);
        writer.WritePropertyName("Value");
        converter(writer, Value);
        writer.WriteEndObject();
    }
}
