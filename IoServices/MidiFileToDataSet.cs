#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using NAudio.Midi;
using Newtonsoft.Json.Linq;
using T3.Core.DataTypes.DataSet;

namespace T3.IoServices;

/// <summary>
/// Converts a standard MIDI file (<c>.mid</c> / <c>.midi</c>) into a <see cref="DataSet"/>
/// using the same channel conventions as live MIDI recording, so a loaded file can be
/// replayed through <c>SimulateIoData</c> exactly like a recorded <c>.data</c> clip.
/// </summary>
/// <remarks>
/// Channel paths follow <see cref="IoDataSetRecorder.ChannelPaths"/> with the file name in
/// the device-name segment: <c>["Midi", "MyTrack.mid", "Ch1", "N60"]</c>. Notes become
/// interval events carrying the raw NoteOn velocity (0–127); control changes, pitch bend
/// and channel pressure become tick events with raw MIDI values. Values stay in MIDI range
/// because replay casts them straight back to MIDI bytes — normalization is a consumer-side
/// concern.
///
/// Event times are absolute seconds from file start, computed by walking the file's tempo
/// map. Tracks of a format-1 file are merged into one time-ordered stream; the source
/// track number is not preserved (the recording convention groups by MIDI channel only).
/// </remarks>
public static class MidiFileToDataSet
{
    /// <summary>
    /// Returns a shared <see cref="DataSet"/> for the MIDI file at <paramref name="absolutePath"/>.
    /// Parses once per (path, last-write timestamp) and serves subsequent callers from the cache,
    /// mirroring <see cref="DataSetCache"/> for <c>.data</c> files.
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

        if (!TryConvertFile(absolutePath, out var parsed, out failureReason))
            return false;

        lock (_cache)
        {
            _cache[absolutePath] = new CacheEntry(lastWrite, parsed);
        }

        dataSet = parsed;
        return true;
    }

    /// <summary>
    /// Parses and converts a MIDI file into a fresh <see cref="DataSet"/> without touching
    /// the cache. Prefer <see cref="TryGet"/> for normal use.
    /// </summary>
    public static bool TryConvertFile(string absolutePath, [NotNullWhen(true)] out DataSet? dataSet, out string failureReason)
    {
        dataSet = null;
        failureReason = string.Empty;

        if (!File.Exists(absolutePath))
        {
            failureReason = $"File not found: {absolutePath}";
            return false;
        }

        MidiFile midiFile;
        try
        {
            midiFile = new MidiFile(absolutePath, strictChecking: false);
        }
        catch (Exception e)
        {
            failureReason = $"Failed to parse {absolutePath}: {e.Message}";
            return false;
        }

        try
        {
            dataSet = Convert(midiFile, Path.GetFileName(absolutePath));
            return true;
        }
        catch (Exception e)
        {
            failureReason = $"Failed to convert {absolutePath}: {e.Message}";
            return false;
        }
    }

    private static DataSet Convert(MidiFile midiFile, string fileName)
    {
        // Same sanitization as the recorder's device names — path segments must not
        // contain the '/' the UI uses when joining segments for display.
        var deviceName = fileName.Replace("/", "_");

        var sortedEvents = CollectEventsSorted(midiFile);
        var tempoMap = new TempoMap(sortedEvents, midiFile.DeltaTicksPerQuarterNote);

        var dataSet = new DataSet();
        var channelsByKey = new Dictionary<(int MidiChannel, string Tag), DataChannel>();
        var lastEventSecs = 0.0;

        foreach (var midiEvent in sortedEvents)
        {
            var time = tempoMap.TicksToSeconds(midiEvent.AbsoluteTime);

            switch (midiEvent)
            {
                case NoteOnEvent noteOn when noteOn.Velocity > 0:
                {
                    var channel = FindOrCreateChannel(dataSet, channelsByKey, deviceName, noteOn.Channel,
                                                      IoDataSetRecorder.ChannelPaths.MidiNoteTag + noteOn.NoteNumber,
                                                      ChannelDurationTypes.Interval);
                    // A retriggered note ends the previous interval — same as the recorder.
                    if (channel.GetLastEvent() is DataIntervalEvent { IsUnfinished: true } unfinished)
                        unfinished.Finish(time);

                    channel.Events.Add(new DataIntervalEvent
                                           {
                                               Time = time,
                                               EndTime = double.PositiveInfinity,
                                               Value = (float)noteOn.Velocity,
                                           });
                    lastEventSecs = Math.Max(lastEventSecs, time);
                    break;
                }

                // NoteOff, or the NoteOn-with-velocity-0 shorthand for it (velocity > 0 was
                // handled above, so any NoteOn reaching this arm is a release).
                case NoteEvent noteOff when noteOff.CommandCode is MidiCommandCode.NoteOff or MidiCommandCode.NoteOn:
                {
                    var key = (noteOff.Channel, IoDataSetRecorder.ChannelPaths.MidiNoteTag + noteOff.NoteNumber);
                    if (channelsByKey.TryGetValue(key, out var channel)
                        && channel.GetLastEvent() is DataIntervalEvent { IsUnfinished: true } unfinished)
                    {
                        unfinished.Finish(time);
                        lastEventSecs = Math.Max(lastEventSecs, time);
                    }

                    break;
                }

                case ControlChangeEvent cc:
                    AddTickEvent(dataSet, channelsByKey, deviceName, cc.Channel,
                                 IoDataSetRecorder.ChannelPaths.MidiControlChangeTag + (int)cc.Controller,
                                 (float)cc.ControllerValue, time);
                    lastEventSecs = Math.Max(lastEventSecs, time);
                    break;

                case PitchWheelChangeEvent pb:
                    AddTickEvent(dataSet, channelsByKey, deviceName, pb.Channel,
                                 IoDataSetRecorder.ChannelPaths.MidiPitchBendTag,
                                 (float)pb.Pitch, time);
                    lastEventSecs = Math.Max(lastEventSecs, time);
                    break;

                case ChannelAfterTouchEvent cp:
                    AddTickEvent(dataSet, channelsByKey, deviceName, cp.Channel,
                                 IoDataSetRecorder.ChannelPaths.MidiChannelPressureTag,
                                 (float)cp.AfterTouchPressure, time);
                    lastEventSecs = Math.Max(lastEventSecs, time);
                    break;
            }
        }

        // Notes without a matching NoteOff (truncated files) end at the last event time.
        foreach (var channel in dataSet.Channels)
        {
            if (channel.GetLastEvent() is DataIntervalEvent { IsUnfinished: true } unfinished)
                unfinished.Finish(lastEventSecs);
        }

        dataSet.Metadata = new JObject
                               {
                                   [DataSet.MetadataKeys.Bpm] = tempoMap.InitialBpm,
                                   [DataSet.MetadataKeys.TimeUnits] = DataSet.MetadataKeys.TimeUnitSeconds,
                                   [SourceDurationSecsKey] = lastEventSecs,
                               };
        return dataSet;
    }

    /// <summary>
    /// Metadata key for the source duration in seconds (time of the last event). Written on
    /// import so consumers (e.g. the timeline drop probe) don't have to rescan all channels.
    /// </summary>
    public const string SourceDurationSecsKey = "SourceDurationSecs";

    private static void AddTickEvent(DataSet dataSet, Dictionary<(int, string), DataChannel> channelsByKey,
                                     string deviceName, int midiChannel, string tag, float value, double time)
    {
        FindOrCreateChannel(dataSet, channelsByKey, deviceName, midiChannel, tag, ChannelDurationTypes.Tick)
           .Events
           .Add(new DataEvent
                    {
                        Time = time,
                        Value = value,
                    });
    }

    private static DataChannel FindOrCreateChannel(DataSet dataSet, Dictionary<(int, string), DataChannel> channelsByKey,
                                                   string deviceName, int midiChannel, string tag, string durationType)
    {
        var key = (midiChannel, tag);
        if (channelsByKey.TryGetValue(key, out var channel))
            return channel;

        var newChannel = new DataChannel(typeof(float), durationType)
                             {
                                 Path = new List<string>
                                            {
                                                IoDataSetRecorder.ChannelPaths.MidiNamespacePrefix,
                                                deviceName,
                                                IoDataSetRecorder.ChannelPaths.ChannelPathPrefix + midiChannel,
                                                tag,
                                            },
                             };
        channelsByKey[key] = newChannel;
        dataSet.Channels.Add(newChannel);
        return newChannel;
    }

    private static List<MidiEvent> CollectEventsSorted(MidiFile midiFile)
    {
        var all = new List<MidiEvent>();
        for (var trackIndex = 0; trackIndex < midiFile.Tracks; trackIndex++)
        {
            all.AddRange(midiFile.Events[trackIndex]);
        }

        // Stable sort keeps same-tick events in track order, so a NoteOff never
        // overtakes the NoteOn it belongs to.
        all.Sort(static (a, b) => a.AbsoluteTime.CompareTo(b.AbsoluteTime));
        return all;
    }

    /// <summary>
    /// Piecewise tick→seconds conversion over the file's tempo changes. MIDI stores delta
    /// ticks whose wall-clock length depends on the tempo in effect, so each segment between
    /// tempo events accumulates its own seconds span.
    /// </summary>
    private sealed class TempoMap
    {
        public TempoMap(List<MidiEvent> sortedEvents, int deltaTicksPerQuarterNote)
        {
            _deltaTicksPerQuarterNote = Math.Max(deltaTicksPerQuarterNote, 1);

            var tick = 0L;
            var seconds = 0.0;
            var microsecondsPerQuarter = DefaultMicrosecondsPerQuarter;

            foreach (var midiEvent in sortedEvents)
            {
                if (midiEvent is not TempoEvent tempoEvent)
                    continue;

                seconds += TicksToSecondsAtTempo(tempoEvent.AbsoluteTime - tick, microsecondsPerQuarter);
                tick = tempoEvent.AbsoluteTime;
                microsecondsPerQuarter = tempoEvent.MicrosecondsPerQuarterNote;
                _segments.Add(new Segment(tick, seconds, microsecondsPerQuarter));
            }

            if (_segments.Count == 0 || _segments[0].StartTick > 0)
            {
                _segments.Insert(0, new Segment(0, 0, DefaultMicrosecondsPerQuarter));
            }

            InitialBpm = 60_000_000.0 / _segments[0].MicrosecondsPerQuarter;
        }

        public double InitialBpm { get; }

        public double TicksToSeconds(long tick)
        {
            // Events are converted in ascending tick order; advance the cursor instead of
            // binary-searching per call.
            while (_cursor + 1 < _segments.Count && _segments[_cursor + 1].StartTick <= tick)
                _cursor++;

            var segment = _segments[_cursor];
            return segment.StartSeconds + TicksToSecondsAtTempo(tick - segment.StartTick, segment.MicrosecondsPerQuarter);
        }

        private double TicksToSecondsAtTempo(long ticks, int microsecondsPerQuarter)
        {
            return ticks * (microsecondsPerQuarter / 1_000_000.0) / _deltaTicksPerQuarterNote;
        }

        private readonly record struct Segment(long StartTick, double StartSeconds, int MicrosecondsPerQuarter);

        private const int DefaultMicrosecondsPerQuarter = 500_000; // 120 BPM, the MIDI spec default
        private readonly List<Segment> _segments = new();
        private readonly int _deltaTicksPerQuarterNote;
        private int _cursor;
    }

    private readonly record struct CacheEntry(DateTime LastWrite, DataSet DataSet);

    private static readonly Dictionary<string, CacheEntry> _cache = new();
}
