#nullable enable
using System;

namespace T3.IoServices.Midi;

/// <summary>
/// An opened MIDI output port. Controllers use this for LED and display feedback.
/// </summary>
public abstract class MidiOutputDevice : IDisposable
{
    public abstract MidiDeviceInfo Info { get; }

    /// <summary>Sends one short message, packed as NAudio packs it: status | data1 &lt;&lt; 8 | data2 &lt;&lt; 16.</summary>
    public abstract void Send(int shortMessage);

    /// <summary>Sends a complete message, including sysex, which controllers use for RGB pad colours.</summary>
    public abstract void SendBuffer(byte[] message);

    public abstract void Close();
    public abstract void Dispose();
}
