#nullable enable

using System;
using NAudio.Midi;

namespace T3.IoServices;

/// <summary>
/// Parallel dispatch path for replaying recorded IO events through graph-side input
/// operators (<c>MidiInput</c>, <c>OscInput</c>) without going through the normal
/// hardware-bound <c>MidiConnectionManager</c> / <c>OscConnectionManager</c> routes.
/// Makes those ops fire identically whether their events come from a real device or
/// from a recorded <c>.data</c> file, identified by the same device name.
/// </summary>
/// <remarks>
/// Does <b>not</b> reach <c>CompatibleMidiDevice</c> instances (the surface controllers
/// driving variations / snapshots) — those filter by <c>sender == _midiInputConnection</c>
/// and always need a real <see cref="MidiIn"/>. Variation replay is out of scope for
/// this bus by design.
/// <para>
/// The producer side is the <c>SimulateIoData</c> operator; consumers subscribe in
/// their constructor and unsubscribe on dispose. Real-time recording
/// (<see cref="IoDataSetRecorder"/>) only listens to the connection managers, so
/// simulated events never feed back into a concurrent recording.
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
