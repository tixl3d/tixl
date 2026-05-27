#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
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
    public List<DataChannel> Channels { get; set; } = new();

    public void Clear()
    {
        Channels.Clear();
    }

    /// <summary>
    /// Serialises the dataset to <paramref name="path"/> as JSON. Used by the live-session
    /// recording feature (<see cref="T3.Core.IO.IoDataSetRecorder"/>) to persist a captured
    /// session as a <c>.data</c> asset for later playback through an <c>LoadDataClip</c>
    /// operator.
    /// </summary>
    /// <remarks>
    /// On-disk shape (v1):
    /// <code>
    /// {
    ///   "Channels": [
    ///     {
    ///       "Path": ["Midi", "&lt;device&gt;", "Ch&lt;n&gt;", "CC74"],
    ///       "Type": "float",
    ///       "Events": [
    ///         { "Time": 0.301, "Value": 70.0 },                          // plain event
    ///         { "Time": 1.5,   "EndTime": 2.0, "Value": 1.0 }            // interval event
    ///       ]
    ///     }
    ///   ]
    /// }
    /// </code>
    /// </remarks>
    public void WriteToFile(string path)
    {
        using var sw = new StreamWriter(path);
        using var writer = new JsonTextWriter(sw);

        writer.Formatting = Formatting.Indented;
        writer.WriteStartObject();
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
    internal DataChannel(Type type)
    {
        _type = type;

        if (!TypeNameRegistry.Entries.TryGetValue(type, out var typeName))
        {
            throw new Exception("Can't create channel for unregistered value type");
        }

        _typeName = typeName;
    }

    public required List<string> Path { get; init; }
    public List<DataEvent?> Events { get; set; } = new(100);
    private readonly Type _type;
    private readonly string _typeName;

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
            // Path serialises as a JSON array — segments stay separate, no '/' join /
            // split round-trip, no need to escape forward slashes in device names.
            writer.WritePropertyName("Path");
            writer.WriteStartArray();
            foreach (var segment in Path)
                writer.WriteValue(segment);
            writer.WriteEndArray();

            writer.WritePropertyName("Type");
            writer.WriteValue(_typeName);

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
