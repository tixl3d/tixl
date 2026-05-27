#nullable enable

using System;
using NAudio.Midi;

namespace T3.Core.IO;

/// <summary>
/// Parallel dispatch path for replaying recorded IO events through graph-side input
/// operators (<c>MidiInput</c>, <c>OscInput</c>) without going through the normal
/// hardware-bound <c>MidiConnectionManager</c> / <c>OscConnectionManager</c> routes.
/// </summary>
/// <remarks>
/// <para>
/// Lives next to the connection managers in <c>Core/IO/</c> but does <b>not</b>
/// reach <c>CompatibleMidiDevice</c> instances (surface controllers driving variations
/// / snapshots) — those filter by <c>sender == _midiInputConnection</c>, so they
/// always need a real <see cref="MidiIn"/>. Variation / snapshot replay is explicitly
/// out of scope for this bus (see <c>.agentic/Plans/Plan_LiveSessionRecording.md</c>
/// Phase 3c discussion). The bus's job is to make <c>MidiInput</c> / <c>OscInput</c>
/// fire identically whether their events come from a real device or from a recorded
/// <c>.data</c> file, identified by the same device name.
/// </para>
/// <para>
/// The <c>SimulateIoData</c> operator (Phase 3c) is the producer; <c>MidiInput</c>
/// and <c>OscInput</c> opt in as consumers. Real-time recording
/// (<see cref="IoDataSetRecorder"/>) only listens to the connection managers, so
/// simulated events do not feed back into the recorder — no recursion guard needed.
/// </para>
/// </remarks>
public static class SimulatedIoBus
{
    /// <summary>
    /// One replayed MIDI event. Identifies the source device by product name (the same
    /// string <c>MidiInput.Device</c> matches against) rather than by <see cref="MidiIn"/>
    /// instance, so simulation works without the original hardware connected.
    /// </summary>
    /// <param name="DeviceProductName">Source device name as recorded.</param>
    /// <param name="Event">Reconstructed NAudio MIDI event — typically <c>NoteEvent</c>,
    /// <c>ControlChangeEvent</c>, <c>PitchWheelChangeEvent</c>, or
    /// <c>ChannelAfterTouchEvent</c>.</param>
    public sealed record SimulatedMidiEvent(string DeviceProductName, MidiEvent Event);

    /// <summary>
    /// One replayed OSC event. The port matches the recorded source's port so an
    /// <c>OscInput</c> bound to that port receives the event identically to a real one.
    /// </summary>
    public sealed record SimulatedOscEvent(int Port, string Address, object Value);

    public static event Action<SimulatedMidiEvent>? MidiEventReceived;
    public static event Action<SimulatedOscEvent>? OscEventReceived;

    public static void DispatchMidi(SimulatedMidiEvent ev) => MidiEventReceived?.Invoke(ev);
    public static void DispatchOsc(SimulatedOscEvent ev) => OscEventReceived?.Invoke(ev);
}
