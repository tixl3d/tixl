#nullable enable
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace T3.Editor.Collab;

/// <summary>
/// What a peer tells the relay about itself when it opens or joins a room. The token lets the relay
/// confirm the Discord account; the rest describes the room for the session list. The key is the
/// room's secret: whoever presents it may re-open the room after its host went silent.
/// </summary>
internal sealed record CollabRelayHello(string Token = "",
                                        string Name = "",
                                        string Lang = "",
                                        string Key = "",
                                        string Instance = "",
                                        string Title = "",
                                        string Purpose = "",
                                        string Project = "",
                                        bool Public = false,
                                        bool PasswordProtected = false);

internal static class CollabRelayWire
{
    public const byte BroadcastSlot = 0xFF;

    /// <summary>Slot of a relay-to-host frame that carries a JSON control message instead of member traffic.</summary>
    public const byte ControlSlot = 0xFE;

    /// <summary>Longest control line accepted from the relay (identity fields make them longer than a bare slot number).</summary>
    public const int ControlLineLimit = 4096;

    /// <summary>The relay's answer to re-opening a room whose host is still there.</summary>
    public const string RoomHasLiveHostError = "room has a live host";

    /// <summary>The relay's reply to the handshake, per connection: who the relay says we and the host are.</summary>
    public static readonly System.Runtime.CompilerServices.ConditionalWeakTable<TcpClient, JObject> HandshakeReplies = new();

    private static readonly Newtonsoft.Json.JsonSerializerSettings _asciiJson
        = new() { StringEscapeHandling = Newtonsoft.Json.StringEscapeHandling.EscapeNonAscii };

    /// <summary>The handshake line. "v":2 asks the relay for the longer replies; empty fields are left out to keep the line short.</summary>
    public static string BuildRequest(string role, string roomCode, CollabRelayHello? hello)
    {
        var request = new JObject
                          {
                              ["role"] = role,
                              ["room"] = (roomCode ?? "").Trim(),
                              ["v"] = 2,
                          };

        // A little-endian length prefix can start with '{', so bare control lines on the host stream
        // are ambiguous. Asking for framed control makes the relay send them as frames on ControlSlot;
        // a relay that does not know the field keeps sending lines and says nothing in its reply.
        if (role == "host")
            request["ctl"] = "frame";

        if (hello != null)
        {
            void Add(string key, string value)
            {
                if (!string.IsNullOrEmpty(value))
                    request[key] = value;
            }

            Add("token", hello.Token);
            Add("name", hello.Name);
            Add("lang", hello.Lang);
            Add("key", hello.Key);
            Add("inst", hello.Instance);
            Add("title", hello.Title);
            Add("purpose", hello.Purpose);
            Add("project", hello.Project);
            if (hello.Public)
                request["public"] = true;

            if (hello.PasswordProtected)
                request["pwd"] = true;
        }

        return Newtonsoft.Json.JsonConvert.SerializeObject(request, _asciiJson) + "\n";
    }

    /// <summary>
    /// Asks a relay for its listed rooms. Throws with a readable message when the relay cannot be
    /// reached or does not keep a list.
    /// </summary>
    public static async Task<JArray> FetchRoomListAsync(string relayHost, int relayPort, CancellationToken cancellation)
    {
        using var client = new TcpClient();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        timeout.CancelAfter(CollabProtocol.ConnectTimeoutMs);
        await client.ConnectAsync(relayHost, relayPort, timeout.Token);
        var stream = client.GetStream();
        await stream.WriteAsync(Encoding.UTF8.GetBytes(BuildRequest("list", "", null)), timeout.Token);

        // One line, but with up to a few hundred rooms on it.
        const int maxListBytes = 512 * 1024;
        using var received = new MemoryStream();
        var buffer = new byte[8192];
        while (received.Length < maxListBytes)
        {
            var read = await stream.ReadAsync(buffer, timeout.Token);
            if (read == 0)
                break;

            received.Write(buffer, 0, read);
            if (Array.IndexOf(buffer, (byte)'\n', 0, read) >= 0)
                break;
        }

        JObject reply;
        try
        {
            reply = JObject.Parse(Encoding.UTF8.GetString(received.ToArray()));
        }
        catch (Newtonsoft.Json.JsonException)
        {
            throw new Exception("the relay sent a malformed session list");
        }

        if (!(reply["ok"]?.Value<bool>() ?? false))
        {
            var detail = reply["error"]?.Value<string>() ?? "";
            throw new Exception(detail == "unknown role" ? "this relay is too old to list sessions" : detail);
        }

        return reply["rooms"] as JArray ?? new JArray();
    }

    public const int SlotOffsetInHostFrame = 4; // [int32 length][byte slot][byte type][payload]

    public static void WriteHostFrame(Stream stream, object writeLock, byte slot, CollabProtocol.FrameType type, ReadOnlySpan<byte> payload)
    {
        var total = payload.Length + 2;
        if (total > CollabProtocol.MaxFrameSize)
            throw new IOException($"Relay frame of {total} bytes exceeds the limit");

        Span<byte> prefix = stackalloc byte[6];
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(prefix, total);
        prefix[4] = slot;
        prefix[5] = (byte)type;

        lock (writeLock)
        {
            stream.Write(prefix);
            stream.Write(payload);
            stream.Flush();
        }
    }

    public static async Task<(bool Ok, string Detail, byte Slot, JObject Reply)> ReadControlReplyAsync(NetworkStream stream, CancellationToken cancellation)
    {
        var line = await ReadLineAsync(stream, cancellation);
        JObject json;
        try
        {
            json = JObject.Parse(line);
        }
        catch (Newtonsoft.Json.JsonException)
        {
            return (false, "relay sent a malformed reply", 0, new JObject());
        }

        var ok = json["ok"]?.Value<bool>() ?? false;
        var detail = json["error"]?.Value<string>() ?? "";
        var slot = json["slot"]?.Value<byte>() ?? 0;
        return (ok, detail, slot, json);
    }

    public static async Task<string> ReadLineAsync(NetworkStream stream, CancellationToken cancellation)
    {
        var sb = new StringBuilder(64);
        var buffer = new byte[1];
        while (sb.Length < ControlLineLimit)
        {
            var read = await stream.ReadAsync(buffer, cancellation);
            if (read == 0)
                throw new IOException("relay closed the connection during handshake");

            if (buffer[0] == '\n')
                return sb.ToString().TrimEnd('\r');

            sb.Append((char)buffer[0]);
        }

        throw new IOException("relay handshake line too long");
    }

    /// <summary>
    /// Updates a hosted session's listing while it runs (Change entry / Delete entry). The room
    /// key authenticates the call. Returns null on success, else a readable reason.
    /// </summary>
    public static async Task<string?> UpdateListingAsync(string relayHost, int relayPort, string roomCode, string roomKey,
                                                         string title, string purpose, string project, bool listed,
                                                         CancellationToken cancellation)
    {
        using var client = new TcpClient();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        timeout.CancelAfter(CollabProtocol.ConnectTimeoutMs);
        await client.ConnectAsync(relayHost, relayPort, timeout.Token);
        var stream = client.GetStream();
        var request = new JObject
                          {
                              ["role"] = "update",
                              ["room"] = (roomCode ?? "").Trim(),
                              ["key"] = roomKey ?? "",
                              ["v"] = 2,
                          };
        if (!string.IsNullOrEmpty(title))
            request["title"] = title;
        if (!string.IsNullOrEmpty(purpose))
            request["purpose"] = purpose;
        if (!string.IsNullOrEmpty(project))
            request["project"] = project;
        if (listed)
            request["public"] = true;

        await stream.WriteAsync(Encoding.UTF8.GetBytes(Newtonsoft.Json.JsonConvert.SerializeObject(request, _asciiJson) + "\n"), timeout.Token);
        var (ok, detail, _, _) = await ReadControlReplyAsync(stream, timeout.Token);
        return ok ? null : (detail.Length > 0 ? detail : "the relay refused the update");
    }

    /// <summary>
    /// Sends a whisper to a hosted session's host. With <paramref name="onReply"/> the connection
    /// stays open and the host's replies ("to" the sender's name) arrive through it; without it
    /// this is the host's key-authenticated reply path.
    /// </summary>
    public static async Task<string?> SendWhisperAsync(string relayHost, int relayPort, string roomCode,
                                                       string fromName, string lang, string text,
                                                       string roomKey = "", string replyTo = "",
                                                       Action<string, string>? onReply = null,
                                                       CancellationToken cancellation = default)
    {
        var reply = roomKey.Length > 0 && replyTo.Length > 0;
        using var client = new TcpClient();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        timeout.CancelAfter(CollabProtocol.ConnectTimeoutMs);
        await client.ConnectAsync(relayHost, relayPort, timeout.Token);
        var stream = client.GetStream();
        var request = new JObject
                          {
                              ["role"] = "whisper",
                              ["room"] = (roomCode ?? "").Trim(),
                              ["v"] = 2,
                              ["name"] = fromName ?? "",
                              ["text"] = text ?? "",
                          };
        if (fromName.Length > 0)
            request["lang"] = lang ?? "";
        if (reply)
        {
            request["key"] = roomKey;
            request["to"] = replyTo;
        }
        else if (onReply != null)
        {
            request["listen"] = true;
        }

        await stream.WriteAsync(Encoding.UTF8.GetBytes(Newtonsoft.Json.JsonConvert.SerializeObject(request, _asciiJson) + "\n"), timeout.Token);
        var (ok, detail, _, _) = await ReadControlReplyAsync(stream, timeout.Token);
        if (!ok)
            return detail.Length > 0 ? detail : "the relay refused the whisper";

        if (!reply && onReply == null)
            return null;

        // Keep reading: replies arrive as {"t":"whisper","from":...,"text":...} control lines.
        var readForever = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        while (true)
        {
            string line;
            try
            {
                line = await ReadLineAsync(stream, readForever.Token);
            }
            catch (Exception e) when (e is IOException or OperationCanceledException or ObjectDisposedException)
            {
                return null;
            }

            JObject json;
            try
            {
                json = JObject.Parse(line);
            }
            catch (Newtonsoft.Json.JsonException)
            {
                continue;
            }

            var type = CollabProtocol.GetString(json, "t");
            if (type != "whisper")
                continue;

            onReply!(CollabProtocol.GetString(json, "from"), CollabProtocol.GetString(json, "text"));
        }
    }
}

/// <summary>
/// Host side: one connection to the relay carries all members. Virtual
/// <see cref="CollabTransport.CollabPeerConnection"/>s are created per relay slot; the rest of the
/// session machinery (handshake, ops, presence, sync) works unchanged against them.
/// </summary>
internal class CollabRelayHostTransport : CollabTransport
{
    public string AssignedRoomCode { get; private set; } = "";

    /// <summary>The relay's handshake reply: carries what the relay verified about this host.</summary>
    public JObject? RelayReply => CollabRelayWire.HandshakeReplies.TryGetValue(_relayClient, out var reply) ? reply : null;

    public override IReadOnlyList<CollabPeerConnection> Peers => _peerSnapshot;

    private readonly List<CollabPeerConnection> _peers = [];

    /** Copy of _peers republished on every change, so the UI thread enumerates without locking. */
    private volatile CollabPeerConnection[] _peerSnapshot = [];

    private readonly TcpClient _relayClient;
    private readonly NetworkStream _relayStream;
    private readonly object _relayWriteLock = new();
    private readonly CancellationTokenSource _cancellation = new();
    private readonly Timer _keepAliveTimer;

    /// <summary>The relay agreed to send control messages as frames, so no byte value on the stream is a line marker.</summary>
    private readonly bool _framedControl;

    /** The relay drops connections idle for 90 s; a host waiting for its first member sends nothing else. */
    private const int KeepAliveIntervalMs = 15_000;

    /** Never assigned to a member: the relay discards frames for it after counting them as activity. */
    private const byte KeepAliveSlot = 0;

    protected CollabRelayHostTransport(TcpClient relayClient, string assignedRoomCode)
    {
        _relayClient = relayClient;
        _relayStream = relayClient.GetStream();
        AssignedRoomCode = assignedRoomCode;
        _framedControl = CollabRelayWire.HandshakeReplies.TryGetValue(relayClient, out var reply)
                         && reply["ctl"]?.Value<string>() == "frame";
        _keepAliveTimer = new Timer(_ => SendKeepAlive(), null, KeepAliveIntervalMs, KeepAliveIntervalMs);
    }

    /// <summary>
    /// Starts reading relay traffic. Must run after <see cref="CollabTransport.PeerDisconnected"/> is
    /// wired, or a link that dies during the connect hand-off reports nothing.
    /// </summary>
    public void StartReading() => _ = Task.Run(() => ReadRelayLoopAsync(_cancellation.Token));

    /// <summary>Connects to the relay and registers as a room owner. Throws with a readable message on failure.</summary>
    public static async Task<CollabRelayHostTransport> ConnectAsync(string relayHost, int relayPort, string roomCode, CancellationToken cancellation,
                                                                    CollabRelayHello? hello = null)
    {
        var (client, room) = await OpenRoomAsync(relayHost, relayPort, roomCode, cancellation, hello);
        try
        {
            return new CollabRelayHostTransport(client, room);
        }
        catch
        {
            client.Dispose();
            throw;
        }
    }

    /// <summary>Shared by the relay and WebRTC host transports: both need a registered room first.</summary>
    protected static async Task<(TcpClient Client, string Room)> OpenRoomAsync(string relayHost, int relayPort, string roomCode,
                                                                               CancellationToken cancellation,
                                                                               CollabRelayHello? hello = null)
    {
        var client = new TcpClient();
        try
        {
            // A relay that accepts but never answers must not leave the session stuck in Connecting.
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
            timeout.CancelAfter(CollabProtocol.ConnectTimeoutMs);
            await client.ConnectAsync(relayHost, relayPort, timeout.Token);
            CollabProtocol.ConfigureShowSocket(client);
            client.SendTimeout = CollabProtocol.WriteTimeoutMs;
            var stream = client.GetStream();

            var bytes = Encoding.UTF8.GetBytes(CollabRelayWire.BuildRequest("host", roomCode, hello));
            await stream.WriteAsync(bytes, timeout.Token);

            var (ok, detail, _, reply) = await CollabRelayWire.ReadControlReplyAsync(stream, timeout.Token);
            if (!ok)
                throw new Exception(string.IsNullOrEmpty(detail) ? "relay refused the room" : detail);

            CollabRelayWire.HandshakeReplies.AddOrUpdate(client, reply);
            return (client, reply["room"]?.Value<string>() ?? "");
        }
        catch
        {
            client.Dispose();
            throw;
        }
    }

    public override void SendToPeer(CollabPeerConnection peer, CollabProtocol.FrameType type, ReadOnlySpan<byte> payload)
    {
        if (peer.SessionClosed)
            return;

        // A peer with a live data channel takes the frame directly; the relay path would be stale.
        if (peer.TrySendPreferred(type, payload))
            return;

        WriteToRelay(peer.PresencePeerId, type, payload);
    }

    /// <summary>Writes on the relay even when the peer prefers a data channel (signaling and keepalives).</summary>
    protected void SendOverRelay(CollabPeerConnection peer, CollabProtocol.FrameType type, ReadOnlySpan<byte> payload)
    {
        if (peer.SessionClosed)
            return;

        WriteToRelay(peer.PresencePeerId, type, payload);
    }

    /// <summary>
    /// Writes run on the UI thread and on the keep-alive timer; a dead relay link must not throw
    /// into them. Closing the socket ends the read loop, which reports the lost relay.
    /// </summary>
    private void WriteToRelay(byte slot, CollabProtocol.FrameType type, ReadOnlySpan<byte> payload)
    {
        try
        {
            CollabRelayWire.WriteHostFrame(_relayStream, _relayWriteLock, slot, type, payload);
        }
        catch (Exception e) when (e is IOException or SocketException or ObjectDisposedException)
        {
            Log.Debug($"Collab relay: write failed: {e.Message}");
            CloseRelayLink();
        }
    }

    private void SendKeepAlive()
    {
        if (_cancellation.IsCancellationRequested)
            return;

        Span<byte> timestamp = stackalloc byte[CollabProtocol.HeartbeatPingSize];
        System.Buffers.Binary.BinaryPrimitives.WriteInt64LittleEndian(timestamp, Environment.TickCount64);
        WriteToRelay(KeepAliveSlot, CollabProtocol.FrameType.Heartbeat, timestamp);
    }

    private void CloseRelayLink()
    {
        try
        {
            _relayClient.Close();
        }
        catch
        {
            // Relay link already gone.
        }
    }

    /// <summary>Presence for relayed peers always rides the reliable channel.</summary>
    public void SendPresenceTo(CollabPeerConnection peer, ReadOnlySpan<byte> payload)
    {
        SendToPeer(peer, CollabProtocol.FrameType.Presence, payload);
    }

    private async Task ReadRelayLoopAsync(CancellationToken cancellation)
    {
        var buffer = new byte[CollabProtocol.MaxFrameSize];
        try
        {
            while (!cancellation.IsCancellationRequested)
            {
                // Without framed control, control lines and frames share the stream and '{' marks a
                // line. That is ambiguous (a little-endian length can start with 0x7B), which is why
                // a relay that understands framed control sends its control messages as frames.
                var first = _relayStream.ReadByte();
                if (first < 0)
                    break;

                if (!_framedControl && first == '{')
                {
                    var line = await ReadRestOfLineAsync((byte)first, cancellation);
                    HandleControlLine(line);
                    continue;
                }

                var lengthBytes = new byte[4];
                lengthBytes[0] = (byte)first;
                if (!await CollabProtocol.TryReadExactAsync(_relayStream, lengthBytes.AsMemory(1), cancellation))
                    break;

                var length = System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(lengthBytes);
                if (length < 2 || length > CollabProtocol.MaxFrameSize)
                {
                    Log.Warning($"Collab relay: illegal frame length {length}, disconnecting");
                    break;
                }

                if (!await CollabProtocol.TryReadExactAsync(_relayStream, buffer.AsMemory(0, length), cancellation))
                    break;

                if (_framedControl && buffer[0] == CollabRelayWire.ControlSlot)
                {
                    HandleControlLine(Encoding.UTF8.GetString(buffer, 2, length - 2));
                    continue;
                }

                DispatchRelayFrame(buffer.AsSpan(0, length));
            }
        }
        catch (Exception e) when (e is IOException or SocketException or ObjectDisposedException or OperationCanceledException)
        {
            Log.Debug($"Collab relay: host link ended: {e.Message}");
        }

        _cancellation.Cancel();
        _keepAliveTimer.Dispose();
        lock (_peers)
        {
            foreach (var peer in _peers)
                peer.SessionClosed = true;

            _peers.Clear();
            _peerSnapshot = [];
        }

        OnPeerDisconnected(new CollabPeerConnection(Stream.Null, int.MaxValue, null));
    }

    private async Task<string> ReadRestOfLineAsync(byte firstByte, CancellationToken cancellation)
    {
        var sb = new StringBuilder(64);
        sb.Append((char)firstByte);
        var buffer = new byte[1];
        while (sb.Length < CollabRelayWire.ControlLineLimit)
        {
            var read = await _relayStream.ReadAsync(buffer, cancellation);
            if (read == 0)
                throw new IOException("relay closed during control line");

            if (buffer[0] == '\n')
                return sb.ToString();

            sb.Append((char)buffer[0]);
        }

        throw new IOException("relay control line too long");
    }

    private void HandleControlLine(string line)
    {
        try
        {
            var json = JObject.Parse(line);
            var type = CollabProtocol.GetString(json, "t");
            var slot = 0;

            // Whispers carry no slot: they are lobby chatter, not session members.
            if (type != "whisper"
                && (!CollabProtocol.TryGetInt(json, "slot", out slot) || slot <= 0 || slot >= CollabRelayWire.BroadcastSlot))
                return;

            if (type == "member")
            {
                // A slot the relay hands out again belongs to a new connection: the previous
                // holder is gone, and its authentication must not carry over.
                RemovePeer(FindPeer(slot));

                CollabPeerConnection? member = null;
                member = CreateMemberPeer(slot, () => RemovePeer(member));

                // The relay checked the member's Discord token; what it reports here cannot be
                // forged by the member, unlike the name in the member's own hello.
                CollabProtocol.TryGetBool(json, "verified", out var verified);
                if (verified)
                {
                    member.IsVerified = true;
                    member.VerifiedName = CollabProtocol.GetString(json, "name");
                    member.DiscordId = CollabProtocol.GetString(json, "did");
                    member.Lang = CollabProtocol.GetString(json, "lang");
                }

                lock (_peers)
                {
                    _peers.Add(member);
                    _peerSnapshot = _peers.ToArray();
                }

                OnStatus($"Peer joining via relay (slot {slot})");

                // The WebRTC host builds its peer link here; the relay transport ignores it.
                OnMemberJoined(member);
            }
            else if (type == "left")
            {
                var peer = FindPeer(slot);
                if (peer != null)
                {
                    peer.SessionClosed = true;
                    RemovePeer(peer);
                }
            }
            else if (type == "whisper")
            {
                // A lobby whisper for the host. The relay cleaned and length-capped the fields.
                OnLobbyWhisper(CollabProtocol.GetString(json, "from"), CollabProtocol.GetString(json, "text"));
            }
        }
        catch (Newtonsoft.Json.JsonException)
        {
            Log.Debug($"Collab relay: ignoring malformed control line '{line}'");
        }
    }

    protected CollabPeerConnection? FindPeer(int slot)
    {
        foreach (var peer in _peerSnapshot)
        {
            if (peer.AssignedId == slot)
                return peer;
        }

        return null;
    }

    /// <summary>Drops a relayed member (left, timed out or rejected) and reports it once.</summary>
    private void RemovePeer(CollabPeerConnection? peer)
    {
        if (peer == null)
            return;

        lock (_peers)
        {
            if (!_peers.Remove(peer))
                return;

            _peerSnapshot = _peers.ToArray();
        }

        peer.SessionClosed = true;
        OnMemberRemoved(peer);
        OnPeerDisconnected(peer);
    }

    /// <summary>A relayed frame for one member slot. The WebRTC host forwards to peer links instead.</summary>
    protected virtual void DispatchRelayFrame(ReadOnlySpan<byte> frame)
    {
        if (frame.Length < 2)
            return;

        var slot = frame[0];
        var type = (CollabProtocol.FrameType)frame[1];
        var payload = frame[2..];

        var peer = FindPeer(slot);
        if (peer == null || peer.SessionClosed)
            return;

        peer.MarkHeardFrom();
        switch (type)
        {
            case CollabProtocol.FrameType.Json:
                if (CollabProtocol.TryDecodeJson(payload, out var json))
                    Inbound.Enqueue(new InboundMessage(peer, json));
                break;

            case CollabProtocol.FrameType.Presence:
                if (PresencePacket.TryDecode(payload, out var presence))
                    Inbound.Enqueue(new InboundMessage(peer, presence));
                break;

            case CollabProtocol.FrameType.Heartbeat:
                // Answer pings so the client's timeout logic sees life on the relay path; answers
                // are one byte longer and never answered, or heartbeats would bounce forever.
                if (payload.Length == CollabProtocol.HeartbeatPingSize)
                {
                    Span<byte> answer = stackalloc byte[CollabProtocol.HeartbeatPingSize + 1];
                    payload.CopyTo(answer);
                    answer[CollabProtocol.HeartbeatPingSize] = 0; // explicit: stackalloc is not guaranteed zeroed
                    WriteToRelay(slot, CollabProtocol.FrameType.Heartbeat, answer);
                }

                break;
        }
    }

    public override void Dispose()
    {
        _cancellation.Cancel();
        _keepAliveTimer.Dispose();
        lock (_peers)
        {
            foreach (var peer in _peers)
                peer.SessionClosed = true;

            _peers.Clear();
            _peerSnapshot = [];
        }

        CloseRelayLink();
    }

    protected override void EchoHeartbeat(CollabPeerConnection peer, ReadOnlySpan<byte> timestamp)
    {
        SendToPeer(peer, CollabProtocol.FrameType.Heartbeat, timestamp);
    }

    /// <summary>Factory for member peers; the WebRTC host creates peers whose stream is a data channel.</summary>
    protected virtual CollabPeerConnection CreateMemberPeer(int slot, Action? onDetach) =>
        new CollabPeerConnection(_relayStream, slot, onDetach);

    protected virtual void OnMemberJoined(CollabPeerConnection peer)
    {
    }

    protected virtual void OnMemberRemoved(CollabPeerConnection peer)
    {
    }

}

/// <summary>
/// Client side: behaves like the direct client transport, but the single stream goes through the
/// relay and presence rides the reliable channel.
/// </summary>
internal class CollabRelayClientTransport : CollabTransport
{
    public CollabPeerConnection HostPeer => _hostPeer;

    protected CollabPeerConnection _hostPeer;
    private readonly TcpClient _relayClient;
    private readonly CancellationTokenSource _cancellation = new();

    /// <summary>The relay's handshake reply: who the relay says we and the host are.</summary>
    public JObject? RelayReply => CollabRelayWire.HandshakeReplies.TryGetValue(_relayClient, out var reply) ? reply : null;

    protected CollabRelayClientTransport(TcpClient relayClient)
    {
        _relayClient = relayClient;
        _hostPeer = new CollabPeerConnection(relayClient.GetStream(), 0, null);
    }

    /// <summary>Connects to the relay and joins the room. Throws with a readable message on failure.</summary>
    public static async Task<CollabRelayClientTransport> ConnectAsync(string relayHost, int relayPort, string roomCode, CancellationToken cancellation,
                                                                      CollabRelayHello? hello = null)
    {
        var client = await JoinRoomAsync(relayHost, relayPort, roomCode, cancellation, hello);
        try
        {
            return new CollabRelayClientTransport(client);
        }
        catch
        {
            client.Dispose();
            throw;
        }
    }

    /// <summary>Shared by the relay and WebRTC client transports: both join the room the same way.</summary>
    protected static async Task<TcpClient> JoinRoomAsync(string relayHost, int relayPort, string roomCode, CancellationToken cancellation,
                                                         CollabRelayHello? hello = null)
    {
        var client = new TcpClient();
        try
        {
            // A relay that accepts but never answers must not leave the session stuck in Connecting.
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
            timeout.CancelAfter(CollabProtocol.ConnectTimeoutMs);
            await client.ConnectAsync(relayHost, relayPort, timeout.Token);
            CollabProtocol.ConfigureShowSocket(client);
            client.SendTimeout = CollabProtocol.WriteTimeoutMs;
            var stream = client.GetStream();

            var bytes = Encoding.UTF8.GetBytes(CollabRelayWire.BuildRequest("join", roomCode, hello));
            await stream.WriteAsync(bytes, timeout.Token);

            var (ok, detail, _, reply) = await CollabRelayWire.ReadControlReplyAsync(stream, timeout.Token);
            if (!ok)
                throw new Exception(string.IsNullOrEmpty(detail) ? "could not join room" : detail);

            CollabRelayWire.HandshakeReplies.AddOrUpdate(client, reply);
            return client;
        }
        catch
        {
            client.Dispose();
            throw;
        }
    }

    public override void SendToPeer(CollabPeerConnection peer, CollabProtocol.FrameType type, ReadOnlySpan<byte> payload)
    {
        // The host link may have a data channel; without this the client kept writing to the relay
        // while the host - whose own channel was up - dropped every relayed frame as stale, so
        // nothing the client sent (ops, chat, presence) reached the host.
        if (peer.TrySendPreferred(type, payload))
            return;

        peer.Write(type, payload);
    }

    /// <summary>Writes on the relay even when the host link prefers a data channel (signaling and keepalives).</summary>
    protected void SendOverRelay(CollabPeerConnection peer, CollabProtocol.FrameType type, ReadOnlySpan<byte> payload)
    {
        peer.Write(type, payload);
    }

    /// <summary>
    /// The relay stream carries plain framed traffic once joined — the standard read loop. Start it
    /// only after PeerDisconnected is wired, so an immediately dying link is reported.
    /// </summary>
    public virtual void StartReading() => _ = Task.Run(() => ReadLoop(_hostPeer));

    /// <summary>No UDP pinhole through the relay — presence rides the reliable channel.</summary>
    public void SendPresence(ReadOnlySpan<byte> payload)
    {
        SendToPeer(_hostPeer, CollabProtocol.FrameType.Presence, payload);
    }

    public override IReadOnlyList<CollabPeerConnection> Peers => _hostPeer == null ? [] : [_hostPeer];

    public override CollabPeerConnection? HostLink => _hostPeer;

    public override void Dispose()
    {
        _cancellation.Cancel();
        _hostPeer?.Close();
        _relayClient?.Close();
    }

    protected override void EchoHeartbeat(CollabPeerConnection peer, ReadOnlySpan<byte> timestamp)
    {
        peer.Write(CollabProtocol.FrameType.Heartbeat, timestamp);
    }
}
