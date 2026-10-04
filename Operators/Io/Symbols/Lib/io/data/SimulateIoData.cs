#nullable enable

using NAudio.Midi;
using T3.Core.Animation;
using T3.Core.DataTypes.DataSet;
using T3.Core.IO;
using T3.IoServices;
using T3.Core.Utils;

namespace Lib.io.data;

/// <summary>
/// Replays one or more recorded <see cref="DataClip"/>s by dispatching MIDI / OSC events
/// through <see cref="SimulatedIoBus"/> as the playhead moves through each source.
/// Downstream <c>MidiInput</c> and <c>OscInput</c> ops receive the simulated events
/// identically to live hardware — same device names, same address patterns, same channels.
/// </summary>
/// <remarks>
/// <para>
/// Multi-input: every connection to <see cref="Clips"/> contributes its own event stream
/// and tracks an independent source-time cursor, so multiple <c>LoadDataClip</c>s
/// (different files, different layers, different start times) can feed a single
/// dispatcher. Pattern mirrors <c>Execute</c> / <c>Group</c>.
/// </para>
/// <para>
/// Scope: this op intentionally bypasses <see cref="MidiConnectionManager"/> and
/// <c>CompatibleMidiDevice</c>. Variation / snapshot recalls do not replay through
/// this op — they require the active composition's state to be reproducible across
/// the session.
/// </para>
/// <para>
/// Time model: events in the underlying <see cref="DataSet"/> are stored in
/// seconds-from-record-start (see <see cref="IoDataSetRecorder"/>). Each frame, per clip,
/// the op queries the clip's <see cref="TimeRangeMapping"/> to convert the playhead's
/// current <see cref="EvaluationContext.LocalTime"/> into source seconds, then fires
/// every event whose <see cref="DataEvent.Time"/> falls in <c>(last, current]</c>. For
/// <see cref="DataIntervalEvent"/>s (MIDI notes) the start fires
/// <see cref="MidiCommandCode.NoteOn"/> and the end fires <see cref="MidiCommandCode.NoteOff"/>.
/// </para>
/// <para>
/// Scrubbing backward: when the source time jumps backward, no events fire for that
/// frame and the per-clip cursor resets — held notes are not auto-released because
/// the op doesn't track per-note state (a follow-up if needed for noticeable hung
/// notes during edit-time scrub).
/// </para>
/// </remarks>
[Guid("7e1c4a82-9c5d-4f8e-bd6f-1a2b3c4d5e60")]
internal sealed class SimulateIoData : Instance<SimulateIoData>
{
    [Output(Guid = "58F26C1B-B49B-4626-ACF2-F757182B8EC7", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<Command> Execute = new();

    public SimulateIoData()
    {
        Execute.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        if (!MathUtils.HasChanged(ref _lastUpdateFrame, Playback.FrameCount))
            return;

        var enabled = Enabled.GetValue(context);
        var connections = Clips.CollectedInputs;

        if (!enabled || connections.Count == 0)
        {
            // Reset every per-slot cursor so re-enabling doesn't replay a giant
            // accumulated window. Cheap; bounded by the number of currently wired clips.
            _lastSourceTimeByClipSlot.Clear();
            Clips.DirtyFlag.Clear();
            return;
        }

        // Track which slots we touched this frame; entries for slots that have been
        // disconnected get evicted at the end so the dictionary doesn't leak across
        // wiring changes.
        _touchedSlotsScratch.Clear();

        foreach (var slot in connections)
        {
            DispatchOneClip(slot, context);
            _touchedSlotsScratch.Add(slot);
        }

        if (_lastSourceTimeByClipSlot.Count != _touchedSlotsScratch.Count)
        {
            // Remove stale entries from disconnected slots.
            _staleKeysScratch.Clear();
            foreach (var key in _lastSourceTimeByClipSlot.Keys)
            {
                if (!_touchedSlotsScratch.Contains(key))
                    _staleKeysScratch.Add(key);
            }
            foreach (var key in _staleKeysScratch)
                _lastSourceTimeByClipSlot.Remove(key);
        }

        Clips.DirtyFlag.Clear();
    }

    private void DispatchOneClip(Slot<DataClip?> slot, EvaluationContext context)
    {
        var clip = slot.GetValue(context);

        if (clip == null || clip.Mapping is not { } mapping)
        {
            // Reset this slot's cursor — if the clip comes back later we want to start
            // from t=0 rather than picking up where we left off (which could be far in
            // the past and replay the whole recording).
            _lastSourceTimeByClipSlot[slot] = 0;
            return;
        }

        // Source-bar bounds the dispatch is allowed to fire across. Anything outside this
        // belongs to a sibling clip (after a Cut) or a future trim — without clamping, a
        // single playhead pass would re-dispatch the other half's events. Bars → seconds
        // via the BPM baked into the mapping at construction.
        var sourceMinSecs = mapping.SourceRange.Start * 240.0 / mapping.Bpm;
        var sourceMaxSecs = mapping.SourceRange.End * 240.0 / mapping.Bpm;

        // Skip dispatch entirely when the playhead sits outside the clip's TimeRange.
        // Snap the cursor to the clip's start so re-entry fires from the leading edge of
        // the source slice instead of replaying everything between 0 and the entry point.
        if (!mapping.IsActive(context.LocalTime))
        {
            _lastSourceTimeByClipSlot[slot] = sourceMinSecs;
            return;
        }

        if (!_lastSourceTimeByClipSlot.TryGetValue(slot, out var lastSourceTime))
            lastSourceTime = sourceMinSecs;

        // Clamp both cursors to the source slice. Past the trailing edge the dispatch
        // is a no-op; before the leading edge the clamp keeps a cold-start (cursor = 0)
        // from replaying events that belong to an earlier sibling slice.
        if (lastSourceTime < sourceMinSecs) lastSourceTime = sourceMinSecs;

        var currentSourceTime = mapping.LocalBarsToSourceSecs(context.LocalTime);
        if (currentSourceTime > sourceMaxSecs) currentSourceTime = sourceMaxSecs;

        // Backward scrub or no movement: snap cursor and skip dispatch.
        if (currentSourceTime <= lastSourceTime)
        {
            _lastSourceTimeByClipSlot[slot] = currentSourceTime;
            return;
        }

        foreach (var channel in clip.Set.Channels)
        {
            DispatchChannel(channel, lastSourceTime, currentSourceTime);
        }

        _lastSourceTimeByClipSlot[slot] = currentSourceTime;
    }

    // ---------------------------------------------------------------------------
    // Channel-path routing. Path layouts mirror what IoDataSetRecorder writes:
    //   ["Midi", deviceName, "Ch<n>", "<type><param>"]   — MIDI capture
    //   ["OSC:<port>", ...address segments]              — OSC capture
    // ---------------------------------------------------------------------------
    private void DispatchChannel(DataChannel channel, double from, double to)
    {
        if (channel.Path.Count == 0)
            return;

        var prefix = channel.Path[0];

        if (prefix == IoDataSetRecorder.ChannelPaths.MidiNamespacePrefix && channel.Path.Count >= 4)
        {
            DispatchMidiChannel(channel, from, to);
        }
        else if (prefix.StartsWith(_oscPrefixWithColon) && channel.Path.Count >= 2)
        {
            DispatchOscChannel(channel, prefix, from, to);
        }
    }

    private void DispatchMidiChannel(DataChannel channel, double from, double to)
    {
        var deviceName = channel.Path[1];
        var channelSegment = channel.Path[2]; // "Ch<n>"
        var typeSegment = channel.Path[3];    // "N<note>" | "CC<num>" | "PB" | "CP"

        if (!TryParseChannelNumber(channelSegment, out var midiChannel))
            return;

        foreach (var evRef in channel.Events)
        {
            if (evRef is null)
                continue;

            // Note events are intervals — fire NoteOn at Time, NoteOff at EndTime. Other
            // event kinds are point-in-time and fire once at Time.
            if (evRef is DataIntervalEvent interval && typeSegment.StartsWith(IoDataSetRecorder.ChannelPaths.MidiNoteTag))
            {
                if (!int.TryParse(typeSegment.AsSpan(IoDataSetRecorder.ChannelPaths.MidiNoteTag.Length), out var noteNumber))
                    continue;

                var velocity = (int)Math.Clamp((float)evRef.Value, 0f, 127f);
                if (IsInside(interval.Time, from, to))
                    DispatchMidi(deviceName, new NoteEvent(0, midiChannel, MidiCommandCode.NoteOn, noteNumber, velocity));

                if (!double.IsInfinity(interval.EndTime) && IsInside(interval.EndTime, from, to))
                    DispatchMidi(deviceName, new NoteEvent(0, midiChannel, MidiCommandCode.NoteOff, noteNumber, 0));

                continue;
            }

            if (!IsInside(evRef.Time, from, to))
                continue;

            if (typeSegment.StartsWith(IoDataSetRecorder.ChannelPaths.MidiControlChangeTag))
            {
                if (!int.TryParse(typeSegment.AsSpan(IoDataSetRecorder.ChannelPaths.MidiControlChangeTag.Length), out var controller))
                    continue;
                var value = (int)Math.Clamp((float)evRef.Value, 0f, 127f);
                DispatchMidi(deviceName, new ControlChangeEvent(0, midiChannel, (MidiController)controller, value));
            }
            else if (typeSegment == IoDataSetRecorder.ChannelPaths.MidiPitchBendTag)
            {
                var pitch = (int)Math.Clamp((float)evRef.Value, 0f, 16383f);
                DispatchMidi(deviceName, new PitchWheelChangeEvent(0, midiChannel, pitch));
            }
            else if (typeSegment == IoDataSetRecorder.ChannelPaths.MidiChannelPressureTag)
            {
                var pressure = (int)Math.Clamp((float)evRef.Value, 0f, 127f);
                DispatchMidi(deviceName, new ChannelAfterTouchEvent(0, midiChannel, pressure));
            }
        }
    }

    private void DispatchOscChannel(DataChannel channel, string prefix, double from, double to)
    {
        // prefix == "<OscNamespacePrefix>:<port>"
        if (!int.TryParse(prefix.AsSpan(_oscPrefixWithColon.Length), out var port))
            return;

        // Recorded address path is everything after the prefix, re-joined with '/'.
        // The recorder stored it that way via IoDataSetRecorder.FindOrCreateOscChannel.
        var address = "/" + string.Join('/', channel.Path.GetRange(1, channel.Path.Count - 1));

        foreach (var evRef in channel.Events)
        {
            if (evRef is not DataEvent ev)
                continue;
            if (!IsInside(ev.Time, from, to))
                continue;

            SimulatedIoBus.DispatchOsc(new SimulatedIoBus.SimulatedOscEvent(port, address, ev.Value));
        }
    }

    // Composite of the OSC namespace prefix + ':' separator, computed once because the
    // recorder writes "<OscNamespacePrefix>:<port>" as the first path segment.
    private static readonly string _oscPrefixWithColon = IoDataSetRecorder.ChannelPaths.OscNamespacePrefix + ":";

    private static void DispatchMidi(string deviceName, MidiEvent ev)
        => SimulatedIoBus.DispatchMidi(new SimulatedIoBus.SimulatedMidiEvent(deviceName, ev));

    private static bool IsInside(double t, double from, double to) => t > from && t <= to;

    private static bool TryParseChannelNumber(string segment, out int channelNumber)
    {
        // "Ch<n>" — strip the prefix (see IoDataSetRecorder.ChannelPaths.ChannelPathPrefix)
        // and parse the integer.
        channelNumber = 0;
        var prefix = IoDataSetRecorder.ChannelPaths.ChannelPathPrefix;
        return segment.StartsWith(prefix) && int.TryParse(segment.AsSpan(prefix.Length), out channelNumber);
    }

    private int _lastUpdateFrame;

    // Per-clip source-time cursor, keyed by the source slot (stable across frames).
    // Stale entries from disconnected slots get evicted each frame.
    private readonly Dictionary<Slot<DataClip?>, double> _lastSourceTimeByClipSlot = new();
    private readonly HashSet<Slot<DataClip?>> _touchedSlotsScratch = new();
    private readonly List<Slot<DataClip?>> _staleKeysScratch = new();

    [Input(Guid = "9a3e6cb4-be7f-4fb0-df81-3c4d5e6f7182")]
    public readonly MultiInputSlot<DataClip?> Clips = new();

    [Input(Guid = "bc508ed6-d091-4fc2-fa03-5e6f718293a4")]
    public readonly InputSlot<bool> Enabled = new();
}
