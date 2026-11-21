#nullable enable
using System;
using System.Threading;

namespace T3.Core.IO;

/// <summary>One MIDI message as a device raised it, with the raw status/data bytes NAudio parsed.</summary>
public readonly record struct RemoteMidiEvent(string DeviceProductName, int RawMessage);

/// <summary>
/// One OSC packet exactly as it arrived on the wire, so the receiving side can parse it with the same
/// Rug.Osc reader and keep every argument type (float, int, string, blob, ...).
/// </summary>
public readonly record struct RemoteOscPacket(int Port, byte[] Packet);

/// <summary>
/// Mirrors hardware input from a boygroup master to its render nodes.
/// <para>
/// The master taps its MIDI and OSC managers here; a render node replays the events through the same
/// graph-side input operators that a local device would drive (<c>SimulatedIoBus</c>), so filters,
/// ranges and persisted values behave identically. Video input is carried separately by
/// <see cref="T3.Core.Video.RemoteVideoInputBus"/> and the video relay that shares cameras, because
/// this bus moves events rather than textures.
/// </para>
/// <para>
/// Capture is a no-op unless a master has at least one connected render node, so a normal session
/// never pays for serializing input traffic.
/// </para>
/// </summary>
public static class RemoteInputBus
{
    /// <summary>Set by the boygroup master while at least one render node is connected.</summary>
    public static bool IsCapturing
    {
        get => _isCapturing;
        set => _isCapturing = value;
    }

    private static volatile bool _isCapturing;

    /// <summary>Raised on the thread that received the input (MIDI backend or OSC socket).</summary>
    public static event Action<RemoteMidiEvent>? MidiCaptured;

    /// <inheritdoc cref="MidiCaptured"/>
    public static event Action<RemoteOscPacket>? OscCaptured;

    /// <summary>
    /// Optional diagnostic sink for a subscriber that throws. Kept as a delegate so this file stays free of
    /// the logging stack and can be compiled into the headless network tests.
    /// </summary>
    public static event Action<string>? ErrorReported;

    /// <summary>Reports one live MIDI message; ignored unless the master is currently replicating.</summary>
    public static void CaptureMidi(string deviceProductName, int rawMessage)
    {
        if (!_isCapturing)
            return;

        try
        {
            MidiCaptured?.Invoke(new RemoteMidiEvent(deviceProductName ?? "", rawMessage));
        }
        catch (Exception e)
        {
            ReportError($"Replicating a MIDI event failed: {e.Message}");
        }
    }

    /// <summary>Reports one live OSC packet; ignored unless the master is currently replicating.</summary>
    public static void CaptureOsc(int port, byte[] packet)
    {
        if (!_isCapturing || packet.Length == 0)
            return;

        try
        {
            OscCaptured?.Invoke(new RemoteOscPacket(port, packet));
        }
        catch (Exception e)
        {
            ReportError($"Replicating an OSC packet failed: {e.Message}");
        }
    }

    private static void ReportError(string message)
    {
        try
        {
            ErrorReported?.Invoke(message);
        }
        catch
        {
            // A broken diagnostic sink must not break input handling.
        }
    }

    // ---------------------------------------------------------------------------------------
    // Replay activity

    /// <summary>
    /// How long a received remote event keeps local hardware input on this machine muted. A render node
    /// normally has no controllers of its own; this stops one that does from fighting the master, while
    /// still letting it fall back to local input if the master sends nothing at all.
    /// </summary>
    private const int ActivityWindowMs = 3000;

    private static long _lastRemoteMidiTicks;
    private static long _lastRemoteOscTicks;

    /// <summary>True while MIDI is arriving from the master, so a local device must not also drive the graph.</summary>
    public static bool IsMidiReplayActive => IsRecent(ref _lastRemoteMidiTicks);

    /// <inheritdoc cref="IsMidiReplayActive"/>
    public static bool IsOscReplayActive => IsRecent(ref _lastRemoteOscTicks);

    public static void MarkRemoteMidiReceived() => Interlocked.Exchange(ref _lastRemoteMidiTicks, Environment.TickCount64);

    public static void MarkRemoteOscReceived() => Interlocked.Exchange(ref _lastRemoteOscTicks, Environment.TickCount64);

    /// <summary>Called when the session ends so local hardware takes over again immediately.</summary>
    public static void ResetRemoteActivity()
    {
        Interlocked.Exchange(ref _lastRemoteMidiTicks, 0);
        Interlocked.Exchange(ref _lastRemoteOscTicks, 0);
    }

    private static bool IsRecent(ref long ticksField)
    {
        var ticks = Interlocked.Read(ref ticksField);
        return ticks != 0 && Environment.TickCount64 - ticks < ActivityWindowMs;
    }
}
