using System.Collections.Generic;
using System.Linq;
using T3.Core.Animation;
using T3.Core.Operator;

namespace T3.Core.DataTypes.DataSet;

public static class DebugDataRecording
{
    public static void KeepTraceData(string path, object data)
    {
        DataChannel channel = null;
        KeepTraceData(path, data, ref channel);
    }


    public static void KeepTraceData(string path, object data, ref DataChannel channel)
    {
        channel ??= FindOrCreateChannel(path, ChannelDurationTypes.Tick);

        var dataEvent = new DataEvent
                            {
                                Time = Playback.RunTimeInSecs,
                                Value = data,
                            };

        lock (channel.Events)
        {
            channel.Events.Add(dataEvent);
        }
    }

    public static void KeepTraceData(Instance instance, string path, object data)
    {
        DataChannel channel = null;
        KeepTraceData(instance, path, data, ref channel);
    }

    public static void KeepTraceData(Instance instance, string path, object data, ref DataChannel channel)
    {
        var instancePath = instance.Symbol.Name + instance.SymbolChildId.ToString()[..4] + "/" + path;
        channel ??= FindOrCreateChannel(instancePath, ChannelDurationTypes.Tick);

        channel.Events.Add(new DataEvent
                               {
                                   Time = Playback.RunTimeInSecs,
                                   Value = data,
                               });
    }

    public static void StartRegion(Instance instance, string path, object data, ref DataChannel channel)
    {
        var instancePath = instance.Symbol.Name + instance.SymbolChildId.ToString()[..4] + "/" + path;
        StartRegion(instancePath, data, ref channel);
    }

    public static void StartRegion(string path, object data, ref DataChannel channel)
    {
        channel ??= FindOrCreateChannel(path, ChannelDurationTypes.Interval);

        channel.Events.Add(new DataIntervalEvent()
                               {
                                   Time = Playback.RunTimeInSecs,
                                   EndTime = double.PositiveInfinity,
                                   Value = data,
                               });
    }
    
    public static void EndRegion(DataChannel channel, object data = null)
    {
        if (channel.GetLastEvent() is not DataIntervalEvent lastEvent)
            return;

        if (data != null)
        {
            lastEvent.Value = data;
        }
        lastEvent.Finish(Playback.RunTimeInSecs);
    }
    
    
    private static DataChannel FindOrCreateChannel(string path, string kind)
    {
        var hash = path.GetHashCode();

        if (_channelsByHash.TryGetValue(hash, out var channel))
            return channel;

        var pathSegments = string.IsNullOrEmpty(path)
                               ? new List<string>() { "/" }
                               : path.Split("/").ToList();

        pathSegments.Insert(0, ChannelNamespacePrefix);

        var newChannel = new DataChannel(typeof(float), kind)
                             {
                                 Path = pathSegments
                             };
        _channelsByHash[hash] = newChannel;
        _dataSet.Channels.Add(newChannel);
        return newChannel;
    }
    
    private static readonly DataSet _dataSet = DataRecording.ActiveRecordingSet;
    private const string ChannelNamespacePrefix = "Debug";
    private static readonly Dictionary<int, DataChannel> _channelsByHash = new();


}