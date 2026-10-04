#nullable enable
using System;
using NAudio.Midi;

namespace T3.IoServices.Midi;

/// <summary>
/// An opened MIDI input port. Backends raise <see cref="MessageReceived"/> from their own threads.
/// </summary>
/// <remarks>
/// Messages are carried in NAudio's <see cref="MidiInMessageEventArgs"/> because its event model is
/// pure managed parsing and works on every platform - only NAudio's device layer is WinMM-bound.
/// Consumers identify the source device by the <c>sender</c>, so a backend must pass itself.
/// </remarks>
public abstract class MidiInputDevice : IDisposable
{
    public event EventHandler<MidiInMessageEventArgs>? MessageReceived;
    public event EventHandler<MidiInMessageEventArgs>? ErrorReceived;

    public abstract MidiDeviceInfo Info { get; }

    public abstract void Start();
    public abstract void Stop();
    public abstract void Close();
    public abstract void Dispose();

    protected void RaiseMessageReceived(int rawMessage, int timestamp)
    {
        MessageReceived?.Invoke(this, new MidiInMessageEventArgs(rawMessage, timestamp));
    }

    protected void RaiseErrorReceived(int rawMessage, int timestamp)
    {
        ErrorReceived?.Invoke(this, new MidiInMessageEventArgs(rawMessage, timestamp));
    }
}
