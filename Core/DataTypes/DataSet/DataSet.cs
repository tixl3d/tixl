#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using T3.Core.Logging;
using T3.Core.Model;
using T3.Serialization;

namespace T3.Core.DataTypes.DataSet;

/// <summary>
/// Defines a set of <see cref="DataChannel"/> event channels. 
/// </summary>
public sealed class DataSet
{
    public List<DataChannel> Channels { get; set; } = new();


    public void Clear()
    {
        Channels.Clear();
    }

    /// <summary>
    /// Writes the dataset to a hardcoded <c>dataset.json</c> in the working directory.
    /// Kept for compatibility with the always-on IO window snapshot path; new callers
    /// should use <see cref="WriteToFile(string)"/> with an explicit destination.
    /// </summary>
    public void WriteToFile() => WriteToFile("dataset.json");

    /// <summary>
    /// Serialises the dataset to <paramref name="path"/> as JSON. Used by the live-session
    /// recording feature to persist a recorded session as a <c>.data</c> asset for later
    /// playback through a <c>DataClip</c> operator. The on-disk format is the same as
    /// <see cref="WriteToFile()"/> — only the destination is parameterised.
    /// </summary>
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
        {
            if (Events.Count == 0)
                return null;

            return Events[^1];
        }
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
            writer.WriteObject("Path", string.Join('/', Path));
            writer.WriteObject("Type", _typeName);

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
    
    public int FindIndexForTime(double time, bool findUpperIndex= true)
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
    internal double TimeCode;

    public required object Value { get; set; }

    internal virtual void ToJson(Action<JsonTextWriter, object> converter, JsonTextWriter writer)
    {
        writer.WriteStartObject();
        writer.WriteValue("TimeCode", TimeCode);
        writer.WritePropertyName("Value");
        converter(writer, Value);
        writer.WriteEndObject();
    }

    public bool TryGetNumericValue(out double v)
    {
        switch (Value)
        {
            case float f: v = f; break;
            case double d: v = d;break;
            case int i: v = i; break;
            case long l: v = l; break;
            default: v= double.NaN; return false;
        }

        return !double.IsNaN(v);
    }
}

public sealed class DataIntervalEvent :DataEvent
{
    public double EndTime = double.PositiveInfinity;
    
    public bool IsUnfinished => double.IsInfinity(EndTime);

    internal void Finish(double someTime)
    {
        if (!IsUnfinished)
        {
            //Log.Warning($"setting finish time of finished note? {EndTime} vs {someTime}");
        }

        EndTime = someTime;
    }

    internal override void ToJson(Action<JsonTextWriter, object> converter, JsonTextWriter writer)
    {
        writer.WriteStartObject();
        writer.WriteValue("TimeCode", TimeCode);
        writer.WriteValue("Time", Time);
        writer.WriteValue("EndTime", EndTime);
        writer.WritePropertyName("Value");
        converter(writer, Value);
        writer.WriteEndObject();
    }
}