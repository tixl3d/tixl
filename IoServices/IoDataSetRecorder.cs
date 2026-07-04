#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using NAudio.Midi;
using Rug.Osc;
using T3.Core.Animation;
using T3.Core.DataTypes.DataSet;
using T3.Core.IO;
using T3.Core.Logging;
using T3.Core.Settings;

namespace T3.IoServices;

/// <summary>
/// Session-scoped MIDI / OSC recorder. A single static façade starts a session, captures
/// incoming MIDI and OSC events into a fresh <see cref="DataSet"/> with timestamps
/// relative to record-start, and serialises the result to a <c>.data</c> file on stop.
/// Mirrors <see cref="T3.Core.Audio.WasapiAudioInput.BeginRecording"/> on the data side.
/// </summary>
/// <remarks>
/// <para>
/// Coexists with the always-on <see cref="MidiDataRecording"/> / <see cref="OscDataRecording"/>
/// singletons that feed the IO window's live view — events go to both. The always-on path
/// stamps events with absolute <see cref="Playback.RunTimeInSecs"/> for a continuously
/// scrolling visualisation; this session path uses elapsed seconds since record-start, so
/// the saved file's events start at <c>t = 0</c>.
/// </para>
/// <para>
/// Allocation behaviour: per-event allocations (<see cref="DataEvent"/>, occasionally a
/// new channel on first encounter) are unavoidable; registration / un-registration only
/// happens at <see cref="BeginRecording"/> / <see cref="EndRecording"/>, not per frame.
/// </para>
/// </remarks>
public sealed class IoDataSetRecorder : MidiConnectionManager.IMidiConsumer, OscConnectionManager.IOscConsumer, IDisposable
{
    /// <summary>
    /// Starts a new IO recording session. Returns the destination file path that will be
    /// written when <see cref="EndRecording"/> is called, or <c>null</c> if a session is
    /// already active or the destination cannot be prepared.
    /// </summary>
    /// <param name="suffix">
    /// Optional filename suffix. Audio sessions use suffixes per source (e.g. <c>mic1</c>);
    /// data sessions merge all selected MIDI / OSC sources into a single file so this is
    /// normally null. Kept for symmetry with
    /// <see cref="T3.Core.Audio.WasapiAudioInput.BeginRecording"/>.
    /// </param>
    public static string? BeginRecording(int sessionIndex = -1, string? suffix = null, bool captureMidi = true, bool captureOsc = true)
    {
        if (_active != null)
        {
            Log.Warning($"IO data recording already active at '{_active.Path}'. Call EndRecording first.");
            return null;
        }

        if (!captureMidi && !captureOsc)
        {
            Log.Warning("IoDataSetRecorder.BeginRecording called with both MIDI and OSC disabled — nothing to capture.");
            return null;
        }

        var directory = T3.Core.Audio.RecordingPaths.TempRecordingsDirectory;
        try
        {
            Directory.CreateDirectory(directory);
        }
        catch (Exception e)
        {
            Log.Warning($"Cannot create recordings directory '{directory}': {e.Message}");
            return null;
        }

        // -1 = compute independently; the recording session passes a shared index so the audio + data
        // files of one session line up (AudioRec-007 / DataRec-007).
        if (sessionIndex < 0)
            sessionIndex = T3.Core.Audio.RecordingPaths.NextSessionIndex(directory);
        var fileName = T3.Core.Audio.RecordingPaths.BuildFileName(T3.Core.Audio.RecordingPaths.DataRecordingPrefix, sessionIndex, ".data", suffix);
        var path = System.IO.Path.Combine(directory, fileName);

        try
        {
            // Pass -1 for the OSC port when OSC is disabled — the constructor's existing
            // out-of-range branch already skips OSC registration on negative ports, so we
            // don't need a separate flag here.
            var oscPort = captureOsc ? CoreSettings.Config.DefaultOscPort : -1;
            _active = new IoDataSetRecorder(path, oscPort, captureMidi);
            Log.Debug($"IO data recording started: {path} (midi={captureMidi}, osc={captureOsc})");
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
            StampRecordingMetadata(active.DataSet, active.SessionStartUtc, active.RecordStartBpm);
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

    /// <summary>
    /// The live <see cref="DataSet"/> being written during an active session, or
    /// <c>null</c> when no session is in progress. Consumers iterating the channels'
    /// event lists must be tolerant of mutation: events arrive on MIDI / OSC callback
    /// threads and the list may be appended to mid-read.
    /// </summary>
    public static DataSet? ActiveDataSet => _active?.DataSet;

    public string Path { get; }
    public DataSet DataSet { get; } = new();

    /// <summary>Wall-clock UTC moment the session started. Stamped into file metadata on stop.</summary>
    public DateTime SessionStartUtc { get; }

    /// <summary>
    /// Playback BPM at record-start. Event times are stored in absolute seconds, but a clip's
    /// TimeRange / SourceRange are in bars — so the bars↔seconds conversion needs the tempo
    /// the recording was made at. Captured here (rather than read from live playback at replay
    /// time) so a later project-tempo change can't silently re-time the recording.
    /// </summary>
    public double RecordStartBpm { get; }

    private IoDataSetRecorder(string path, int oscPort, bool registerMidi = true)
    {
        Path = path;
        _recordStartRunSecs = Playback.RunTimeInSecs;
        SessionStartUtc = DateTime.UtcNow;
        RecordStartBpm = Playback.Current?.Bpm ?? CompositionSettings.Defaults.Playback.Bpm;

        if (registerMidi)
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
        // Use ProductName verbatim so it matches what MidiInput.Device sees during live
        // capture. SimulateIoData hands this same name to MidiInput on replay; any
        // divergence here breaks the round-trip. MidiDataRecording (always-on, IO Window
        // display only) appends ProductId for visual disambiguation; we don't, because
        // MidiInput itself can't distinguish same-name devices either.
        var deviceName = device.ProductName;

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
                                                                                    Value = (float)cc.ControllerValue,
                                                                                });
                break;

            case PitchWheelChangeEvent pb:
                FindOrCreatePitchWheelChannel(deviceName, pb).Events.Add(new DataEvent
                                                                             {
                                                                                 Time = t,
                                                                                 Value = (float)pb.Pitch,
                                                                             });
                break;

            case ChannelAfterTouchEvent cat:
                FindOrCreateChannelPressureChannel(deviceName, cat).Events.Add(new DataEvent
                                                                                   {
                                                                                       Time = t,
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
        return GetOrAddChannel(hash, () => new DataChannel(typeof(float), ChannelDurationTypes.Interval)
                                              {
                                                  Path = new List<string>
                                                             {
                                                                 ChannelPaths.MidiNamespacePrefix,
                                                                 deviceName,
                                                                 ChannelPaths.ChannelPathPrefix + noteEvent.Channel,
                                                                 ChannelPaths.MidiNoteTag + noteEvent.NoteNumber,
                                                             },
                                              });
    }

    private DataChannel FindOrCreateControlChangeChannel(string deviceName, ControlChangeEvent cc)
    {
        var hash = HashCode.Combine(deviceName, "cc", cc.Channel, (int)cc.Controller);
        return GetOrAddChannel(hash, () => new DataChannel(typeof(float), ChannelDurationTypes.Tick)
                                              {
                                                  Path = new List<string>
                                                             {
                                                                 ChannelPaths.MidiNamespacePrefix,
                                                                 deviceName,
                                                                 ChannelPaths.ChannelPathPrefix + cc.Channel,
                                                                 ChannelPaths.MidiControlChangeTag + (int)cc.Controller,
                                                             },
                                              });
    }

    private DataChannel FindOrCreatePitchWheelChannel(string deviceName, PitchWheelChangeEvent pb)
    {
        var hash = HashCode.Combine(deviceName, "pb", pb.Channel);
        return GetOrAddChannel(hash, () => new DataChannel(typeof(float), ChannelDurationTypes.Tick)
                                              {
                                                  Path = new List<string>
                                                             {
                                                                 ChannelPaths.MidiNamespacePrefix,
                                                                 deviceName,
                                                                 ChannelPaths.ChannelPathPrefix + pb.Channel,
                                                                 ChannelPaths.MidiPitchBendTag,
                                                             },
                                              });
    }

    private DataChannel FindOrCreateChannelPressureChannel(string deviceName, ChannelAfterTouchEvent cat)
    {
        var hash = HashCode.Combine(deviceName, "cp", cat.Channel);
        return GetOrAddChannel(hash, () => new DataChannel(typeof(float), ChannelDurationTypes.Tick)
                                              {
                                                  Path = new List<string>
                                                             {
                                                                 ChannelPaths.MidiNamespacePrefix,
                                                                 deviceName,
                                                                 ChannelPaths.ChannelPathPrefix + cat.Channel,
                                                                 ChannelPaths.MidiChannelPressureTag,
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
                                       segments[0] = $"{ChannelPaths.OscNamespacePrefix}:{_oscPort}";
                                       return new DataChannel(typeof(float), ChannelDurationTypes.Tick) { Path = segments };
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

    /// <summary>
    /// Stamps session-level provenance onto <see cref="DataSet.Metadata"/> right before
    /// the file is written: the TiXL build that recorded it, the wall-clock UTC moment the
    /// session started, the playback BPM at record-start (the tempo anchor for the clip's
    /// bars↔seconds mapping), and the event time unit (always seconds today). Lets the user
    /// tell at a glance which build / when a clip is from when re-opening it months later.
    /// </summary>
    private static void StampRecordingMetadata(DataSet dataSet, DateTime sessionStartUtc, double recordStartBpm)
    {
        var metadata = dataSet.Metadata ?? new Newtonsoft.Json.Linq.JObject();
        metadata[DataSet.MetadataKeys.TixlVersion] = FileLocations.TixlVersion;
        metadata[DataSet.MetadataKeys.RecordedAtUtc] = sessionStartUtc.ToString("o");  // ISO 8601 round-trip
        metadata[DataSet.MetadataKeys.Bpm] = recordStartBpm;
        metadata[DataSet.MetadataKeys.TimeUnits] = DataSet.MetadataKeys.TimeUnitSeconds;
        dataSet.Metadata = metadata;
    }

    /// <summary>
    /// Wire-format constants for <see cref="DataChannel.Path"/> segments produced by this
    /// recorder. Forms the read / write contract between the recorder (here, in
    /// <c>T3.Core</c>) and the <c>SimulateIoData</c> operator (in <c>Operators/Lib</c>)
    /// that decodes the segments back into MIDI / OSC events for replay.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Centralised here so renaming a segment is safe-by-construction — both writer and
    /// reader pick up the change atomically. Adding a new MIDI event family (e.g.
    /// program change) means adding a new tag here, a writer call site below, and a
    /// matching decoder branch in <c>SimulateIoData.DispatchMidiChannel</c>.
    /// </para>
    /// <para>
    /// Layouts:
    /// <list type="bullet">
    /// <item>MIDI: <c>["Midi", &lt;device&gt;, "Ch&lt;n&gt;", "&lt;tag&gt;&lt;param?&gt;"]</c>
    ///       where <c>tag</c> is one of <see cref="MidiNoteTag"/>, <see cref="MidiControlChangeTag"/>,
    ///       <see cref="MidiPitchBendTag"/>, <see cref="MidiChannelPressureTag"/>.</item>
    /// <item>OSC: <c>["&lt;OscNamespacePrefix&gt;:&lt;port&gt;", ...address segments]</c>.</item>
    /// </list>
    /// </para>
    /// <para>
    /// <b>Reserved top-level prefixes</b> — the first <c>Path[0]</c> segment identifies
    /// the source family. Decoders early-return on unknown prefixes, so a new family can
    /// be added freely without breaking existing consumers. Currently reserved:
    /// <list type="bullet">
    /// <item><c>"Midi"</c> — MIDI capture (this recorder).</item>
    /// <item><c>"OSC:&lt;port&gt;"</c> — OSC capture (this recorder).</item>
    /// <item><c>"Text"</c> — reserved for transcription / SRT subtitle channels.</item>
    /// <item><c>"Audio"</c> — reserved for audio-analysis channels (FFT bands, RMS, etc.).</item>
    /// <item><c>"Animation"</c> — reserved for keyframe-driven streams.</item>
    /// <item><c>"Annotation"</c> — reserved for markers, regions, user comments.</item>
    /// <item><c>"Debug"</c> — debug telemetry from <c>DebugDataRecording</c>.</item>
    /// </list>
    /// Picking a fresh prefix and writing the decoder is all that's needed for a new
    /// source type; existing files and ops keep working unchanged.
    /// </para>
    /// </remarks>
    public static class ChannelPaths
    {
        /// <summary>First segment for every recorded MIDI channel.</summary>
        public const string MidiNamespacePrefix = "Midi";

        /// <summary>First-segment prefix for OSC channels — followed by <c>:&lt;port&gt;</c>.</summary>
        public const string OscNamespacePrefix = "OSC";

        /// <summary>Prefix for the channel-number segment, e.g. <c>"Ch1"</c>.</summary>
        public const string ChannelPathPrefix = "Ch";

        /// <summary>MIDI note channel tag, followed by note number, e.g. <c>"N60"</c>.</summary>
        public const string MidiNoteTag = "N";

        /// <summary>MIDI control-change channel tag, followed by controller number, e.g. <c>"CC74"</c>.</summary>
        public const string MidiControlChangeTag = "CC";

        /// <summary>MIDI pitch-bend channel tag — no parameter; one channel per (device, midiChannel).</summary>
        public const string MidiPitchBendTag = "PB";

        /// <summary>MIDI channel-pressure (aftertouch) tag — no parameter.</summary>
        public const string MidiChannelPressureTag = "CP";
    }

    private readonly Dictionary<int, DataChannel> _channelsByHash = new();
    private readonly double _recordStartRunSecs;
    private readonly int _oscPort;

    private static IoDataSetRecorder? _active;
}
