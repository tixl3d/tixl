#nullable enable
using System.Diagnostics.CodeAnalysis;
using System.Buffers.Binary;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace T3.Editor.Collab;

/// <summary>
/// Wire protocol for TiXL collaboration sessions.
///
/// Transport framing:
/// - Reliable channel (TCP or relay): [int32 length][byte type][payload], length includes the type
///   byte, all multi-byte values little-endian. Frames larger than <see cref="MaxFrameSize"/> are a
///   protocol violation and drop the connection (guards against memory-exhaustion from bad peers).
/// - Unreliable channel (UDP, direct LAN/online connections only): raw presence datagrams without a
///   length prefix, same payload layout as the reliable presence frame.
///
/// Payload types:
/// - <see cref="FrameType.Json">: UTF-8 JSON document, first property "t" discriminates the message.
/// - <see cref="FrameType.Presence">: fixed-layout binary cursor telemetry (see <see cref="PresencePacket"/>).
/// - <see cref="FrameType.Heartbeat">: 8-byte Environment.TickCount64 ping. The receiver answers
///   with the same 8 bytes plus one marker byte; answers are never answered.
///
/// All graph mutations travel as JSON "op" messages and are applied on the UI thread only; presence
/// is display-only and never touches the graph.
/// </summary>
internal static class CollabProtocol
{
    /// <summary>2: host-sequenced ops (seq/from/cid, echo to the sender, opNack) and stateHash.
    /// Version 1 peers would apply their own echoed ops twice, so mixed sessions are refused.</summary>
    public const int ProtocolVersion = 2;
    public const int DefaultTcpPort = 44700;
    public const int DefaultRelayPort = 44710;

    /// <summary>Relay used when the relay address setting is empty. TiXLCollabRelay is expected to
    /// run there; hosts and joiners must point at the same relay to see each other's rooms.</summary>
    public const string DefaultRelayHost = "10.0.0.222";

    /// <summary>Hard upper bound for one reliable frame including the type byte.</summary>
    public const int MaxFrameSize = 4 * 1024 * 1024;

    /// <summary>Hard upper bound for one JSON payload. Ops are tiny; anything bigger is hostile or buggy.</summary>
    public const int MaxJsonPayload = 1024 * 1024;

    /// <summary>Expected size of one presence datagram with a full selection list (connId, flags, guid, 2 floats, count, 15 guids).</summary>
    public const int MaxPresencePayload = 2 + 16 + 8 + 1 + 15 * 16 + 1 + 3 + 1 + MaxPressedKeysPerPresence;
    public const int MaxPressedKeysPerPresence = 4;

    public const int MaxPeers = 16;
    public const int MaxSelectionIdsPerPresence = 15;
    /// <summary>Show-control heartbeats ride small TCP frames; 500 ms keeps loss detection fast
    /// while costing nothing measurable on a wired LAN.</summary>
    public const int HeartbeatIntervalMs = 500;
    public const int HeartbeatPingSize = 8;
    /// <summary>Silence for more than ~12 × heartbeat means the peer is gone. Heartbeats ride the
    /// UI thread, so a peer that momentarily stalls (a snapshot apply, a project compile) must not
    /// be declared dead: 6 s survives a multi-second stall while still feeling instant on a show
    /// LAN. TCP keepalive below catches truly dead links as well; relay peers get
    /// <see cref="RelayPeerTimeoutMs"/>.</summary>
    public const int PeerTimeoutMs = 6_000;
    /// <summary>Relay sessions ride the internet: a transient 3 s stall must not end a session,
    /// so relay-backed peers tolerate ~10 s of silence.</summary>
    public const int RelayPeerTimeoutMs = 10_000;
    public const int PresenceSendIntervalMs = 50;

    /// <summary>Writes run on the UI thread: a peer that stops reading stalls it at most this long before it is dropped.</summary>
    public const int WriteTimeoutMs = 5000;

    /// <summary>Joins dial from the UI thread; an unreachable host must not freeze it for the OS SYN-retry timeout.</summary>
    public const int ConnectTimeoutMs = 5000;

    /// <summary>
    /// One knob for every show-control socket: no Nagle (frames must leave this frame) and a short
    /// TCP keepalive (3 s idle, 1 s probe interval) so a half-open link — cable pulled, peer
    /// crashed without FIN — surfaces as a read failure within seconds instead of minutes.
    /// Best effort: keepalive tuning throws on some platforms, and a missing one only slows
    /// detection, never correctness.
    /// </summary>
    public static void ConfigureShowSocket(TcpClient client)
    {
        client.NoDelay = true;
        try
        {
            var socket = client.Client;
            socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);
            socket.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveTime, 3);
            socket.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveInterval, 1);
        }
        catch (Exception)
        {
            // Older stacks without the TCP-level tuning; plain keepalive is still on.
        }
    }

    public enum FrameType : byte
    {
        Json = 1,
        Presence = 2,
        Heartbeat = 3,
        Signal = 4,
    }

    /// <summary>Hard upper bound for one WebRTC signaling frame.</summary>
    public const int MaxSignalPayload = 64 * 1024;

    /// <summary>Opens a TCP connection, giving up after <see cref="ConnectTimeoutMs"/>. Throws on failure.</summary>
    public static TcpClient ConnectWithTimeout(IPEndPoint endPoint)
    {
        var client = new TcpClient();
        try
        {
            using var timeout = new CancellationTokenSource(ConnectTimeoutMs);
            client.ConnectAsync(endPoint, timeout.Token).AsTask().GetAwaiter().GetResult();
            return client;
        }
        catch (OperationCanceledException)
        {
            client.Dispose();
            throw new TimeoutException($"no answer from {endPoint} within {ConnectTimeoutMs / 1000} s");
        }
        catch
        {
            client.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Reads the length prefix and returns it, or false when the stream ended. The caller then reads
    /// <paramref name="length"/> payload bytes.
    /// </summary>
    public static bool TryReadFrameLength(Stream stream, out int length)
    {
        length = 0;
        Span<byte> buffer = stackalloc byte[4];
        if (!TryReadExact(stream, buffer))
            return false;

        length = BinaryPrimitives.ReadInt32LittleEndian(buffer);
        return true;
    }

    public static bool TryReadExact(Stream stream, Span<byte> buffer)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var read = stream.Read(buffer[offset..]);
            if (read <= 0)
                return false;

            offset += read;
        }

        return true;
    }

    public static async Task<bool> TryReadExactAsync(Stream stream, Memory<byte> buffer, CancellationToken cancellation)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer[offset..], cancellation);
            if (read <= 0)
                return false;

            offset += read;
        }

        return true;
    }

    /// <summary>Writes one framed message to a reliable stream. Single writer enforced by caller locking.</summary>
    public static void WriteFrame(Stream stream, FrameType type, ReadOnlySpan<byte> payload)
    {
        var total = payload.Length + 1;
        if (total > MaxFrameSize)
            throw new IOException($"Collab frame of {total} bytes exceeds the {MaxFrameSize} byte limit");

        Span<byte> prefix = stackalloc byte[5];
        BinaryPrimitives.WriteInt32LittleEndian(prefix, total);
        prefix[4] = (byte)type;
        stream.Write(prefix);
        stream.Write(payload);
        stream.Flush();
    }

    public static byte[] EncodeJson(object message)
    {
        var json = JsonConvert.SerializeObject(message);
        return Encoding.UTF8.GetBytes(json);
    }

    /// <summary>Whether a JSON frame is a kick — the one relay message that must survive an
    /// active peer-to-peer data channel (see the WebRTC client's dispatch).</summary>
    public static bool IsKickJson(ReadOnlySpan<byte> payload)
    {
        return TryDecodeJson(payload, out var json)
               && string.Equals(GetString(json, "t"), "kicked", StringComparison.Ordinal);
    }

    public static bool TryDecodeJson(ReadOnlySpan<byte> payload, out JObject json)
    {
        json = null!;
        if (payload.Length > MaxJsonPayload)
            return false;

        try
        {
            json = JObject.Parse(Encoding.UTF8.GetString(payload));
            return json.TryGetValue("t", out _);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>
    /// String read that tolerates the token kinds our own writers produce: a Guid field serialized
    /// as a Guid token (snapshot ops) used to throw InvalidCastException out of the frame loop;
    /// numbers and booleans read as text instead of failing.
    /// </summary>
    public static string GetString(JObject json, string property, string fallback = "")
    {
        if (!json.TryGetValue(property, out var value))
            return fallback;

        switch (value.Type)
        {
            case JTokenType.String:
                return value.Value<string>() ?? fallback;
            case JTokenType.Guid:
                return value.Value<Guid>().ToString();
            case JTokenType.Integer:
            case JTokenType.Float:
            case JTokenType.Boolean:
                return value.ToString();
            case JTokenType.Null:
            case JTokenType.Undefined:
                return fallback;
            default:
                return fallback;
        }
    }

    /// <summary>
    /// Guid read that accepts both representations: names we build by hand go out as strings, while
    /// <c>new { c = someGuid }</c> serializes as a Guid token. Value&lt;string&gt;() threw on the latter.
    /// </summary>
    public static bool TryGetGuid(JObject json, string property, out Guid guid)
    {
        guid = Guid.Empty;
        if (!json.TryGetValue(property, out var value))
            return false;

        switch (value.Type)
        {
            case JTokenType.Guid:
                guid = value.Value<Guid>();
                return true;
            case JTokenType.String:
                return Guid.TryParse(value.Value<string>(), out guid);
            default:
                return false;
        }
    }

    /// <summary>
    /// Round trip of a heartbeat this side sent: the echo carries our own TickCount64, so the age of
    /// that stamp is the trip there and back. Out-of-range values (an echo from a previous session,
    /// or a clock jump) are ignored instead of reported as a latency.
    /// </summary>
    public static int MeasureHeartbeatRtt(ReadOnlySpan<byte> echo)
    {
        if (echo.Length != HeartbeatPingSize + 1)
            return -1;

        var sent = BitConverter.ToInt64(echo);
        var rtt = Environment.TickCount64 - sent;
        return rtt is >= 0 and <= 10_000 ? (int)rtt : -1;
    }

    /// <summary>Long read for values that do not fit an int — TickCount64 stamps, for instance.</summary>
    public static bool TryGetLong(JObject json, string property, out long value)
    {
        value = 0;
        if (!json.TryGetValue(property, out var token))
            return false;

        switch (token.Type)
        {
            case JTokenType.Integer:
                value = token.Value<long>();
                return true;
            case JTokenType.Float:
                value = (long)token.Value<double>();
                return true;
            case JTokenType.String:
                return long.TryParse(token.Value<string>(), out value);
            default:
                return false;
        }
    }

    public static bool TryGetInt(JObject json, string property, out int value)
    {
        value = 0;
        if (!json.TryGetValue(property, out var token))
            return false;

        switch (token.Type)
        {
            case JTokenType.Integer:
                value = token.Value<int>();
                return true;
            case JTokenType.String:
                return int.TryParse(token.Value<string>(), out value);
            default:
                return false;
        }
    }

    public static bool TryGetDouble(JObject json, string property, out double value)
    {
        value = 0;
        if (!json.TryGetValue(property, out var token))
            return false;

        switch (token.Type)
        {
            case JTokenType.Integer:
                value = token.Value<long>();
                return true;
            case JTokenType.Float:
                value = token.Value<double>();
                return true;
            case JTokenType.String:
                return double.TryParse(token.Value<string>(), out value);
            default:
                return false;
        }
    }

    /// <summary>
    /// Type-tolerant bool read for fields from UNTRUSTED senders (LAN discovery announcements): a
    /// string like "x" or an object token must be a miss, not a FormatException out of the frame loop.
    /// </summary>
    public static bool TryGetBool(JObject json, string property, out bool value)
    {
        value = false;
        if (!json.TryGetValue(property, out var token))
            return false;

        switch (token.Type)
        {
            case JTokenType.Boolean:
                value = token.Value<bool>();
                return true;
            case JTokenType.Integer:
            {
                var number = token.Value<long>();
                if (number is < 0 or > 1)
                    return false;

                value = number == 1;
                return true;
            }
            case JTokenType.String:
                return bool.TryParse(token.Value<string>(), out value);
            default:
                return false;
        }
    }
}

/// <summary>
/// Immutable snapshot of one peer's editor presence. Written by the UI thread, shipped over the wire,
/// decoded on the receiving network thread and consumed on the receiving UI thread. Passed by
/// reference so a swap replaces the whole packet without locking.
/// </summary>
internal sealed class PresencePacket
{
    public byte PeerId;
    public byte Flags;
    public Guid CompositionId;
    public Vector2 CursorOnCanvas;
    public Guid[] SelectedChildIds = [];

    /// <summary>Decaying wheel-spin value (signed ticks), appended to the payload so peers can show
    /// scrolling. Zero for peers on an older build that doesn't send it.</summary>
    public sbyte WheelSpin;

    /// <summary>The participant's chosen cursor color as 0xRRGGBB; 0 lets receivers derive one from the user id.</summary>
    public int ColorRgb;

    /// <summary>Key codes held right now, as the interaction overlay labels them.</summary>
    public byte[] PressedKeys = [];

    public const byte FlagHoveringCanvas = 0x01;
    public const byte FlagLeftButton = 0x02;
    public const byte FlagRightButton = 0x04;
    public const byte FlagMiddleButton = 0x08;
    public const byte FlagDoubleClick = 0x10;
    public const byte FlagAddingOperator = 0x20;

    public bool IsHoveringCanvas => (Flags & FlagHoveringCanvas) != 0;
    public bool IsLeftButtonDown => (Flags & FlagLeftButton) != 0;
    public bool IsRightButtonDown => (Flags & FlagRightButton) != 0;
    public bool IsMiddleButtonDown => (Flags & FlagMiddleButton) != 0;
    public bool IsDoubleClick => (Flags & FlagDoubleClick) != 0;
    public bool IsAddingOperator => (Flags & FlagAddingOperator) != 0;

    public int Encode(Span<byte> target)
    {
        var selectionCount = Math.Min(SelectedChildIds.Length, CollabProtocol.MaxSelectionIdsPerPresence);
        var keyCount = Math.Min(PressedKeys.Length, CollabProtocol.MaxPressedKeysPerPresence);
        var size = 2 + 16 + 8 + 1 + selectionCount * 16 + 1 + 3 + 1 + keyCount;
        if (target.Length < size)
            return 0;

        target[0] = PeerId;
        target[1] = Flags;
        CompositionId.TryWriteBytes(target.Slice(2, 16));
        BinaryPrimitives.WriteSingleLittleEndian(target.Slice(18, 4), CursorOnCanvas.X);
        BinaryPrimitives.WriteSingleLittleEndian(target.Slice(22, 4), CursorOnCanvas.Y);
        target[26] = (byte)selectionCount;
        for (var i = 0; i < selectionCount; i++)
        {
            SelectedChildIds[i].TryWriteBytes(target.Slice(27 + i * 16, 16));
        }

        // Everything after the selection list is appended rather than inserted: an older peer reads
        // exactly the bytes it knows and ignores the rest, so mixed builds keep exchanging cursors.
        var tail = 27 + selectionCount * 16;
        target[tail] = unchecked((byte)WheelSpin);
        target[tail + 1] = (byte)(ColorRgb >> 16);
        target[tail + 2] = (byte)(ColorRgb >> 8);
        target[tail + 3] = (byte)ColorRgb;
        target[tail + 4] = (byte)keyCount;
        for (var i = 0; i < keyCount; i++)
            target[tail + 5 + i] = PressedKeys[i];

        return size;
    }

    public static bool TryDecode(ReadOnlySpan<byte> payload, [NotNullWhen(true)] out PresencePacket? packet)
    {
        packet = null;
        if (payload.Length < 27)
            return false;

        var selectionCount = payload[26];
        if (selectionCount > CollabProtocol.MaxSelectionIdsPerPresence)
            return false;

        if (payload.Length < 27 + selectionCount * 16)
            return false;

        packet = new PresencePacket
                     {
                         PeerId = payload[0],
                         Flags = payload[1],
                         CompositionId = new Guid(payload.Slice(2, 16)),
                         CursorOnCanvas = new Vector2(BinaryPrimitives.ReadSingleLittleEndian(payload.Slice(18, 4)),
                                                      BinaryPrimitives.ReadSingleLittleEndian(payload.Slice(22, 4))),
                         SelectedChildIds = new Guid[selectionCount],
                     };

        var wheelOffset = 27 + selectionCount * 16;
        packet.WheelSpin = payload.Length > wheelOffset ? unchecked((sbyte)payload[wheelOffset]) : (sbyte)0;

        if (payload.Length >= wheelOffset + 5)
        {
            packet.ColorRgb = (payload[wheelOffset + 1] << 16) | (payload[wheelOffset + 2] << 8) | payload[wheelOffset + 3];
            var keyCount = Math.Min((int)payload[wheelOffset + 4], Math.Min(CollabProtocol.MaxPressedKeysPerPresence, payload.Length - wheelOffset - 5));
            packet.PressedKeys = payload.Slice(wheelOffset + 5, keyCount).ToArray();
        }

        for (var i = 0; i < selectionCount; i++)
        {
            packet.SelectedChildIds[i] = new Guid(payload.Slice(27 + i * 16, 16));
        }

        return true;
    }
}
