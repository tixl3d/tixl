#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using NAudio.Midi;
using Operators.Utils;
using Operators.Utils.Recording;
using Rug.Osc;
using T3.Core.Animation;
using T3.Core.DataTypes.DataSet;
using T3.Core.Logging;
using T3.Core.Settings;

namespace T3.Core.IO;

/// <summary>
/// Session-scoped MIDI / OSC recorder for the live-session recording feature
/// (see <c>.agentic/Plans/Plan_LiveSessionRecording.md</c>, Phase 3a).
/// </summary>
/// <remarks>
/// <para>
/// Mirrors <see cref="T3.Core.Audio.WasapiAudioInput.BeginRecording"/> on the data side:
/// a single static façade starts a session, captures incoming MIDI and OSC events into a
/// fresh <see cref="DataSet"/> with timestamps relative to record-start, and serialises
/// the result to a <c>.data</c> file on stop.
/// </para>
/// <para>
/// This is a parallel consumer to the always-on <see cref="MidiDataRecording"/> and
/// <see cref="OscDataRecording"/> singletons that feed the IO window's live view —
/// the two systems coexist (events go to both) and use different time bases. The
/// always-on path uses absolute <see cref="Playback.RunTimeInSecs"/> for a continuously
/// scrolling visualisation; the session path uses elapsed seconds since record-start
/// so the saved file's events start at <c>t = 0</c>.
/// </para>
/// <para>
/// Allocation behaviour: per-event allocations (<see cref="DataEvent"/>, possibly a new
/// channel on first encounter) are unavoidable, but registration / un-registration only
/// happens at <see cref="BeginRecording"/> / <see cref="EndRecording"/>, not per frame.
/// </para>
/// </remarks>
public sealed class DataSetSessionRecorder : MidiConnectionManager.IMidiConsumer, OscConnectionManager.IOscConsumer, IDisposable
{
    /// <summary>
    /// Starts a new IO recording session. Returns the destination file path that will be
    /// written when <see cref="EndRecording"/> is called, or <c>null</c> if a session is
    /// already active or the destination cannot be prepared.
    /// </summary>
    /// <param name="suffix">
    /// Optional filename suffix. Audio sessions use suffixes per source (e.g. <c>mic1</c>);
    /// for data we record all selected MIDI / OSC sources into a single file so the suffix
    /// is normally left null. Kept here for symmetry with
    /// <see cref="T3.Core.Audio.WasapiAudioInput.BeginRecording"/>.
    /// </param>
    public static string? BeginRecording(string? suffix = null)
    {
        if (_active != null)
        {
            Log.Warning($"IO data recording already active at '{_active.Path}'. Call EndRecording first.");
            return null;
        }

        var directory = T3.Core.Audio.RecordingPaths.DevRecordingsDirectory;
        try
        {
            Directory.CreateDirectory(directory);
        }
        catch (Exception e)
        {
            Log.Warning($"Cannot create recordings directory '{directory}': {e.Message}");
            return null;
        }

        var sessionIndex = T3.Core.Audio.RecordingPaths.NextSessionIndex(directory);
        var fileName = T3.Core.Audio.RecordingPaths.BuildFileName(sessionIndex, ".data", suffix);
        var path = System.IO.Path.Combine(directory, fileName);

        try
        {
            _active = new DataSetSessionRecorder(path, CoreSettings.Config.DefaultOscPort);
            Log.Debug($"IO data recording started: {path}");
            return path;
        }
        catch (Exception e)
        {
            Log.Warning($"Failed to start IO data recording at '{path}': {e.Message}");
            return null;
        }
    }

    /// <summary>
    /// Stops the active recording started by <see cref="BeginRecording"/>, writes the
    /// captured <see cref="DataSet"/> to disk, and returns the file path. Returns
    /// <c>null</c> if no session was active or the write failed.
    /// </summary>
    public static string? EndRecording()
    {
        var active = _active;
        if (active == null)
        {
            Log.Warning("EndRecording called but no IO data recording is active.");
            return null;
        }

        _active = null;
        try
        {
            active.Dispose();
            active.DataSet.WriteToFile(active.Path);
            Log.Debug($"IO data recording stopped: {active.Path} ({active.DataSet.Channels.Count} channel(s))");
            return active.Path;
        }
        catch (Exception e)
        {
            Log.Warning($"Failed to finalise IO data recording '{active.Path}': {e.Message}");
            return null;
        }
    }

    public static bool IsRecording => _active != null;
    public static string? ActiveRecordingPath => _active?.Path;

    public string Path { get; }
    public DataSet DataSet { get; } = new();

    private DataSetSessionRecorder(string path, int oscPort)
    {
        Path = path;
        _recordStartRunSecs = Playback.RunTimeInSecs;

        MidiConnectionManager.RegisterConsumer(this);

        if (oscPort >= 0 && oscPort <= 65535)
        {
            OscConnectionManager.RegisterConsumer(this, oscPort);
            _oscPort = oscPort;
        }
        else
        {
            // Match OscDataRecording's behaviour: silently skip if no port is configured.
            _oscPort = -1;
        }
    }

    public void Dispose()
    {
        MidiConnectionManager.UnregisterConsumer(this);
        if (_oscPort >= 0)
            OscConnectionManager.UnregisterConsumer(this);
    }

    // ---------------------------------------------------------------------------
    // MIDI capture — mirrors MidiDataRecording but with relative timestamps. Note-on /
    // note-off pair up as DataIntervalEvents so playback can render note durations cleanly.
    // ---------------------------------------------------------------------------

    void MidiConnectionManager.IMidiConsumer.MessageReceivedHandler(object sender, MidiInMessageEventArgs msg)
    {
        if (sender is not MidiIn midiIn || msg.MidiEvent == null)
            return;

        if (msg.MidiEvent.CommandCode == MidiCommandCode.AutoSensing)
            return;

        var device = MidiConnectionManager.GetDescriptionForMidiIn(midiIn);
        var deviceName = (device.ProductName
                          + (device.ProductId is not (0 or 65535)
                                 ? device.ProductId.ToString()
                                 : string.Empty)).Replace("/", "_");

        var t = Playback.RunTimeInSecs - _recordStartRunSecs;

        switch (msg.MidiEvent)
        {
            case NoteEvent noteEvent:
            {
                var channel = FindOrCreateNoteChannel(deviceName, noteEvent);
                var lastNote = channel.GetLastEvent() as DataIntervalEvent;

                switch (msg.MidiEvent.CommandCode)
                {
                    case MidiCommandCode.NoteOff:
                        lastNote?.Finish(t);
                        break;

                    case MidiCommandCode.NoteOn:
                        if (lastNote != null && lastNote.IsUnfinished)
                        {
                            lastNote.Finish(t);
                            if (noteEvent.Velocity == 0)
                                break;
                        }
                        channel.Events.Add(new DataIntervalEvent
                                               {
                                                   Time = t,
                                                   EndTime = double.PositiveInfinity,
                                                   TimeCode = t,
                                                   Value = (float)noteEvent.Velocity,
                                               });
                        break;
                }
                break;
            }

            case ControlChangeEvent cc:
                FindOrCreateControlChangeChannel(deviceName, cc).Events.Add(new DataEvent
                                                                                {
                                                                                    Time = t,
                                                                                    TimeCode = t,
                                                                                    Value = (float)cc.ControllerValue,
                                                                                });
                break;

            case PitchWheelChangeEvent pb:
                FindOrCreatePitchWheelChannel(deviceName, pb).Events.Add(new DataEvent
                                                                             {
                                                                                 Time = t,
                                                                                 TimeCode = t,
                                                                                 Value = (float)pb.Pitch,
                                                                             });
                break;

            case ChannelAfterTouchEvent cat:
                FindOrCreateChannelPressureChannel(deviceName, cat).Events.Add(new DataEvent
                                                                                   {
                                                                                       Time = t,
                                                                                       TimeCode = t,
                                                                                       Value = (float)cat.AfterTouchPressure,
                                                                                   });
                break;
        }
    }

    void MidiConnectionManager.IMidiConsumer.ErrorReceivedHandler(object sender, MidiInMessageEventArgs msg) { }
    void MidiConnectionManager.IMidiConsumer.OnSettingsChanged() { }

    // ---------------------------------------------------------------------------
    // OSC capture — one channel per address+arg-index, float-valued. Non-numeric arg
    // types are skipped (consistent with OscDataRecording's existing behaviour).
    // ---------------------------------------------------------------------------

    void OscConnectionManager.IOscConsumer.ProcessMessage(OscMessage msg)
    {
        if (msg.Count == 0)
            return;

        var t = Playback.RunTimeInSecs - _recordStartRunSecs;

        for (var i = 0; i < msg.Count; i++)
        {
            if (!OscConnectionManager.TryGetFloatFromMessagePart(msg[i], out var value))
                continue;

            var pathWithIndex = msg.Count == 1
                                    ? OscConnectionManager.BuildMessageComponentPath(msg)
                                    : OscConnectionManager.BuildMessageComponentPath(msg, i);

            var channel = FindOrCreateOscChannel(pathWithIndex);
            channel.Events.Add(new DataEvent
                                   {
                                       Time = t,
                                       TimeCode = t,
                                       Value = value,
                                   });
        }
    }

    // ---------------------------------------------------------------------------
    // Channel factories. Hash + dictionary lookup matches MidiDataRecording style;
    // first event with a given (device, channel, controller) tuple lazily creates a
    // DataChannel and appends it to the recorder's DataSet.
    // ---------------------------------------------------------------------------

    private DataChannel FindOrCreateNoteChannel(string deviceName, NoteEvent noteEvent)
    {
        var hash = HashCode.Combine(deviceName, "note", noteEvent.Channel, noteEvent.NoteNumber);
        return GetOrAddChannel(hash, () => new DataChannel(typeof(float))
                                              {
                                                  Path = new List<string>
                                                             {
                                                                 MidiNamespacePrefix,
                                                                 deviceName,
                                                                 ChannelPathPrefix + noteEvent.Channel,
                                                                 "N" + noteEvent.NoteNumber,
                                                             },
                                              });
    }

    private DataChannel FindOrCreateControlChangeChannel(string deviceName, ControlChangeEvent cc)
    {
        var hash = HashCode.Combine(deviceName, "cc", cc.Channel, (int)cc.Controller);
        return GetOrAddChannel(hash, () => new DataChannel(typeof(float))
                                              {
                                                  Path = new List<string>
                                                             {
                                                                 MidiNamespacePrefix,
                                                                 deviceName,
                                                                 ChannelPathPrefix + cc.Channel,
                                                                 "CC" + (int)cc.Controller,
                                                             },
                                              });
    }

    private DataChannel FindOrCreatePitchWheelChannel(string deviceName, PitchWheelChangeEvent pb)
    {
        var hash = HashCode.Combine(deviceName, "pb", pb.Channel);
        return GetOrAddChannel(hash, () => new DataChannel(typeof(float))
                                              {
                                                  Path = new List<string>
                                                             {
                                                                 MidiNamespacePrefix,
                                                                 deviceName,
                                                                 ChannelPathPrefix + pb.Channel,
                                                                 "PB",
                                                             },
                                              });
    }

    private DataChannel FindOrCreateChannelPressureChannel(string deviceName, ChannelAfterTouchEvent cat)
    {
        var hash = HashCode.Combine(deviceName, "cp", cat.Channel);
        return GetOrAddChannel(hash, () => new DataChannel(typeof(float))
                                              {
                                                  Path = new List<string>
                                                             {
                                                                 MidiNamespacePrefix,
                                                                 deviceName,
                                                                 ChannelPathPrefix + cat.Channel,
                                                                 "CP",
                                                             },
                                              });
    }

    private DataChannel FindOrCreateOscChannel(string path)
    {
        var hash = HashCode.Combine("osc", _oscPort, path);
        return GetOrAddChannel(hash, () =>
                                   {
                                       var segments = string.IsNullOrEmpty(path)
                                                          ? new List<string> { "/" }
                                                          : new List<string>(path.Split('/'));
                                       segments[0] = $"{OscNamespacePrefix}:{_oscPort}";
                                       return new DataChannel(typeof(float)) { Path = segments };
                                   });
    }

    private DataChannel GetOrAddChannel(int hash, Func<DataChannel> factory)
    {
        if (_channelsByHash.TryGetValue(hash, out var channel))
            return channel;

        var newChannel = factory();
        _channelsByHash[hash] = newChannel;
        DataSet.Channels.Add(newChannel);
        return newChannel;
    }

    private const string MidiNamespacePrefix = "Midi";
    private const string OscNamespacePrefix = "OSC";
    private const string ChannelPathPrefix = "Ch";

    private readonly Dictionary<int, DataChannel> _channelsByHash = new();
    private readonly double _recordStartRunSecs;
    private readonly int _oscPort;

    private static DataSetSessionRecorder? _active;
}
