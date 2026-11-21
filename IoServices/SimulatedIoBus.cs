#nullable enable

using System;
using NAudio.Midi;
using Rug.Osc;
using T3.Core.IO;
using T3.IoServices.Midi;

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
/// and always need a real <see cref="MidiInputDevice"/>. Variation replay is out of scope for
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
    /// string <c>MidiInput.Device</c> matches against) rather than by <see cref="MidiInputDevice"/>
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

    /// <summary>
    /// A complete replayed OSC message. Used for boygroup input replication, where the master sends the
    /// packet's original bytes so every argument (and its type) reaches the render node unchanged.
    /// </summary>
    public sealed record SimulatedOscPacket(int Port, OscMessage Message);

    public static event Action<SimulatedMidiEvent>? MidiEventReceived;
    public static event Action<SimulatedOscEvent>? OscEventReceived;
    public static event Action<SimulatedOscPacket>? OscPacketReceived;

    public static void DispatchMidi(SimulatedMidiEvent ev)
    {
        // A simulated event on a boygroup master reaches the render nodes like a hardware one. On a
        // client this is a no-op, because capture is only enabled on the master.
        var rawMessage = ToRawMessage(ev.Event);
        if (rawMessage != 0)
            RemoteInputBus.CaptureMidi(ev.DeviceProductName, rawMessage);

        MidiEventReceived?.Invoke(ev);
    }

    public static void DispatchOsc(SimulatedOscEvent ev)
    {
        RemoteInputBus.CaptureOsc(ev.Port, new OscMessage(ev.Address, ev.Value).ToByteArray());
        OscEventReceived?.Invoke(ev);
    }

    /// <summary>Delivers a full OSC packet received from a boygroup master.</summary>
    public static void DispatchOscPacket(SimulatedOscPacket ev) => OscPacketReceived?.Invoke(ev);

    /// <summary>Packs the NAudio event subclasses the replay path produces back into a raw MIDI message.</summary>
    private static int ToRawMessage(MidiEvent midiEvent)
    {
        var channel = Math.Clamp(midiEvent.Channel - 1, 0, 15);

        return midiEvent switch
                   {
                       NoteOnEvent note => 0x90 | channel | (note.NoteNumber & 0x7F) << 8 | (note.Velocity & 0x7F) << 16,
                       NoteEvent note => 0x80 | channel | (note.NoteNumber & 0x7F) << 8,
                       ControlChangeEvent control => 0xB0 | channel | ((int)control.Controller & 0x7F) << 8 | (control.ControllerValue & 0x7F) << 16,
                       PitchWheelChangeEvent pitch => 0xE0 | channel | (pitch.Pitch & 0x7F) << 8 | ((pitch.Pitch >> 7) & 0x7F) << 16,
                       ChannelAfterTouchEvent afterTouch => 0xD0 | channel | (afterTouch.AfterTouchPressure & 0x7F) << 8,
                       PatchChangeEvent patch => 0xC0 | channel | (patch.Patch & 0x7F) << 8,
                       _ => 0,
                   };
    }
}
