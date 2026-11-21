#nullable enable
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using T3.Core.Utils;
using Newtonsoft.Json.Linq;
using T3.Editor.Gui.Interaction;

namespace T3.Editor.Collab;

/// <summary>A message received from a peer, handed to the UI thread through the inbound queue.</summary>
internal readonly struct InboundMessage
{
    public InboundMessage(CollabPeerConnection peer, JObject json)
    {
        Peer = peer;
        Json = json;
    }

    public InboundMessage(CollabPeerConnection peer, PresencePacket presence)
    {
        Peer = peer;
        Presence = presence;
    }

    public readonly CollabPeerConnection Peer;
    public readonly JObject? Json;
    public readonly PresencePacket? Presence;

    public bool IsPresence => Presence != null;
}

/// <summary>
/// One remote participant: framed reader thread, write lock, and authentication state.
/// Instances are created on network threads but only ever listed/removed from the UI thread.
/// Direct transports back this with a TcpClient; the relay transport backs it with the relay
/// stream plus a slot id and a close callback.
/// </summary>
internal sealed class CollabPeerConnection
{
    public CollabPeerConnection(TcpClient client, int assignedId)
    {
        Client = client;
        AssignedId = assignedId;
        Stream = client.GetStream();
        RemoteAddress = (client.Client.RemoteEndPoint as IPEndPoint)?.Address;
        client.SendTimeout = CollabProtocol.WriteTimeoutMs;
    }

    /// <summary>Relay-backed peer: shares the relay stream; closing only detaches this peer.
    /// Relay paths ride the internet, so their silence timeout is looser than a show LAN's.</summary>
    public CollabPeerConnection(Stream sharedStream, int assignedId, Action? onDetach)
    {
        Client = null;
        AssignedId = assignedId;
        Stream = sharedStream;
        _onDetach = onDetach;
        TimeoutMs = CollabProtocol.RelayPeerTimeoutMs;
    }

    public readonly TcpClient? Client;
    public readonly Stream Stream;
    public readonly int AssignedId;

    /// <summary>TCP peer address, captured on connect: the socket throws once it is closed.</summary>
    public readonly IPAddress? RemoteAddress;
    private readonly Action? _onDetach;

    public volatile bool SessionClosed;
    public volatile bool IsAuthenticated;

    public Guid UserId { get; set; }
    public string Name { get; set; } = "";

    /// <summary>True when the relay confirmed this peer's Discord account. Name, language and
    /// account id then come from Discord, not from what the peer says about itself.</summary>
    public bool IsVerified { get; set; }

    public string VerifiedName { get; set; } = "";
    public string DiscordId { get; set; } = "";

    /// <summary>Language tag of the peer (Discord locale, e.g. "hu" or "en-US"); empty when unknown.</summary>
    public string Lang { get; set; } = "";

    /// <summary>Port this peer would host a direct session on; used when it takes over as host.</summary>
    public int ListenPort { get; set; }

    public byte PresencePeerId => (byte)AssignedId;

    /// <summary>Latest presence. Swapped atomically by reference; never mutated in place.</summary>
    public PresencePacket? LatestPresence;

    /// <summary>UDP endpoint the peer sends presence from; the reply path for host-to-peer presence. Null for relayed peers.</summary>
    public volatile IPEndPoint? UdpEndpoint;

    private long _lastHeardFromTicks = Environment.TickCount64;
    private readonly object _writeLock = new();

    /// <summary>Round trip of the last answered heartbeat in ms, or -1 when none has returned yet.</summary>
    public int LastRttMs = -1;

    /// <summary>How long this peer may stay silent before it is considered gone. Show LAN peers
    /// use <see cref="CollabProtocol.PeerTimeoutMs"/>; relay-backed peers a looser internet budget.</summary>
    public int TimeoutMs { get; init; } = CollabProtocol.PeerTimeoutMs;

    public void MarkHeardFrom() => Interlocked.Exchange(ref _lastHeardFromTicks, Environment.TickCount64);

    /// <summary>Last frame received on any path, for a transport that checks its own liveness.</summary>
    public long LastHeardFromTicks => Interlocked.Read(ref _lastHeardFromTicks);

    public bool IsTimedOut => Environment.TickCount64 - Interlocked.Read(ref _lastHeardFromTicks) > TimeoutMs;

    /// <summary>
    /// A path that can carry frames without the stream, e.g. a WebRTC data channel. The transport
    /// offers every frame here first; false means "not available", and the stream is used instead.
    /// </summary>
    public delegate bool FrameSender(CollabProtocol.FrameType type, ReadOnlySpan<byte> payload);

    /// <summary>
    /// Set while a peer has a lower-latency path than its relay stream. Cleared when that path dies,
    /// so the peer silently falls back to the relay instead of losing the session.
    /// </summary>
    public volatile FrameSender? PreferredSender;

    public bool TrySendPreferred(CollabProtocol.FrameType type, ReadOnlySpan<byte> payload)
    {
        var sender = PreferredSender;
        if (sender == null)
            return false;

        try
        {
            return sender(type, payload);
        }
        catch
        {
            // A dying data channel must not take the peer down; the relay path still works.
            return false;
        }
    }

    /// <summary>Thread-safe framed write. Callers must pass a payload already encoded for the frame type.</summary>
    public void Write(CollabProtocol.FrameType type, ReadOnlySpan<byte> payload)
    {
        if (SessionClosed)
            return;

        try
        {
            lock (_writeLock)
            {
                if (SessionClosed)
                    return;

                CollabProtocol.WriteFrame(Stream, type, payload);
            }
        }
        catch (Exception e) when (e is IOException or SocketException or ObjectDisposedException)
        {
            // Closing ends the reader too, so the peer is reported as gone instead of lingering
            // half-open with every further write silently dropped.
            Close();
        }
    }

    public void Close()
    {
        SessionClosed = true;
        try
        {
            Client?.Close();
        }
        catch
        {
            // Closing a dying socket can throw; nothing to do about it.
        }

        try
        {
            _onDetach?.Invoke();
        }
        catch
        {
            // Detach callbacks must not take the connection down with them.
        }
    }

    public override string ToString() => $"{Name}#{AssignedId}";
}

/// <summary>
/// Owns the raw sockets of a collaboration session. All socket I/O runs on dedicated background
/// threads; the only cross-thread artifacts are a ConcurrentQueue of inbound messages and locked
/// synchronous writes. Nothing in here touches editor state.
/// </summary>
internal abstract class CollabTransport : IDisposable
{
    /// <summary>Drained by <see cref="CollabManager"/> on the UI thread.</summary>
    public readonly ConcurrentQueue<InboundMessage> Inbound = new();

    /// <summary>Raised on a network thread when a peer disconnected. The manager removes it on the UI thread.</summary>
    public event Action<CollabPeerConnection>? PeerDisconnected;

    /// <summary>Raised on a network thread when a lobby whisper for the host arrives on a relay
    /// control link. The manager queues it for the UI thread.</summary>
    public event Action<string, string>? LobbyWhisperReceived;

    public event Action<string>? StatusMessage;

    /// <summary>Connected peers. Client transports expose only their host link so heartbeat and
    /// timeout handling covers it; the list may be read from the UI thread at any time.</summary>
    public abstract IReadOnlyList<CollabPeerConnection> Peers { get; }

    /// <summary>
    /// The single link back to the session host, for client transports (direct and relay). Null on
    /// hosts. Session messages that go "to the host" must use this instead of testing for one
    /// concrete client transport, or a relay client silently stops sending.
    /// </summary>
    public virtual CollabPeerConnection? HostLink => null;

    /// <summary>Called for peers that may take a direct data-channel path instead of the session's
    /// relay link. The relay-based transports override this to start WebRTC negotiation; the
    /// default does nothing, keeping every frame on the session link.</summary>
    public virtual void EnablePeerToPeer(CollabPeerConnection peer)
    {
    }

    /// <summary>Human-readable name of the link a peer uses ("relay", "direct", "p2p").</summary>
    public virtual string DescribePeerLink(CollabPeerConnection peer) => "";

    /// <summary>Writes a JSON message on the session link even when the peer prefers a data channel
    /// (control messages must survive a data channel that is about to close).</summary>
    public virtual void SendJsonOverRelay(CollabPeerConnection peer, object message)
        => SendJson(peer, message);

    public abstract void SendToPeer(CollabPeerConnection peer, CollabProtocol.FrameType type, ReadOnlySpan<byte> payload);

    public void SendJson(CollabPeerConnection peer, object message)
    {
        SendToPeer(peer, CollabProtocol.FrameType.Json, CollabProtocol.EncodeJson(message));
    }

    public void BroadcastJson(IEnumerable<CollabPeerConnection> peers, object message, CollabPeerConnection? except = null)
    {
        var payload = CollabProtocol.EncodeJson(message);
        foreach (var peer in peers)
        {
            if (peer == except || !peer.IsAuthenticated)
                continue;

            SendToPeer(peer, CollabProtocol.FrameType.Json, payload);
        }
    }

    protected void OnPeerDisconnected(CollabPeerConnection peer) => PeerDisconnected?.Invoke(peer);

    protected void OnStatus(string message) => StatusMessage?.Invoke(message);

    protected void OnLobbyWhisper(string from, string text) => LobbyWhisperReceived?.Invoke(from, text);

    public abstract void Dispose();

    /// <summary>
    /// Shared per-connection receive loop. Reads frames until the socket dies or the session ends.
    /// Frame-size violations kill the connection instead of being logged — a peer sending oversized
    /// frames is either broken or attacking us.
    /// </summary>
    protected void ReadLoop(CollabPeerConnection peer)
    {
        ReadFrameLoop(peer, peer.Stream);

        // A transport that keeps the peer on another path (the WebRTC data channel) can keep it
        // alive after its original stream dies.
        if (!ShouldReportLostPeer(peer))
            return;

        peer.Close();
        CleanupPeer(peer);
        OnPeerDisconnected(peer);
    }

    /// <summary>
    /// Reads and dispatches frames from one stream until it ends. Shared by the socket transports
    /// and a peer's WebRTC data channel; the caller decides what a dead stream means.
    /// </summary>
    protected void ReadFrameLoop(CollabPeerConnection peer, Stream stream)
    {
        var buffer = new byte[CollabProtocol.MaxFrameSize];
        try
        {
            while (!peer.SessionClosed)
            {
                if (!CollabProtocol.TryReadFrameLength(stream, out var length))
                    break;

                if (length < 1 || length > CollabProtocol.MaxFrameSize)
                {
                    Log.Warning($"Collab: peer {peer} sent illegal frame length {length}, disconnecting");
                    break;
                }

                if (!CollabProtocol.TryReadExact(stream, buffer.AsSpan(0, length)))
                    break;

                // Any frame counts as a life sign, not just heartbeats.
                peer.MarkHeardFrom();
                DispatchFrame(peer, buffer.AsSpan(0, length), stream);
            }
        }
        catch (Exception e) when (e is IOException or SocketException or ObjectDisposedException)
        {
            // Expected during teardown or when a peer drops. Logged at debug level to keep the
            // session log readable when the wifi hiccups.
            Log.Debug($"Collab: connection to {peer} ended: {e.Message}");
        }
        catch (Exception e)
        {
            Log.Warning($"Collab: unexpected error reading from {peer}: {e.Message}");
        }
    }

    /// <summary>Whether losing this peer ends the session (a host loss on a client does; relayed
    /// members of other clients do not).</summary>
    protected virtual bool ShouldReportLostPeer(CollabPeerConnection peer) => true;

    /// <summary>Removes a dead peer from this transport's list. Called from the network thread.</summary>
    protected virtual void CleanupPeer(CollabPeerConnection peer)
    {
    }

    /// <summary>The stream a frame arrived on: the WebRTC client distinguishes relay-kept-alive
    /// traffic from data-channel traffic by it.</summary>
    protected virtual void DispatchFrame(CollabPeerConnection peer, ReadOnlySpan<byte> frame, Stream source)
    {
        DispatchFrame(peer, frame);
    }

    private void DispatchFrame(CollabPeerConnection peer, ReadOnlySpan<byte> frame)
    {
        var type = (CollabProtocol.FrameType)frame[0];
        var payload = frame[1..];
        switch (type)
        {
            case CollabProtocol.FrameType.Json:
                if (CollabProtocol.TryDecodeJson(payload, out var json))
                    Inbound.Enqueue(new InboundMessage(peer, json));
                else
                    Log.Warning($"Collab: dropped malformed JSON frame from {peer}");
                break;

            case CollabProtocol.FrameType.Presence:
                // Presence is advisory display data; silently drop garbage instead of spamming the log.
                if (PresencePacket.TryDecode(payload, out var presence))
                    Inbound.Enqueue(new InboundMessage(peer, presence));
                break;

            case CollabProtocol.FrameType.Heartbeat:
                peer.MarkHeardFrom();
                if (payload.Length == CollabProtocol.HeartbeatPingSize)
                {
                    // The answer is one byte longer so it is never answered itself; echoing every
                    // heartbeat would bounce each one between both ends forever.
                    Span<byte> answer = stackalloc byte[CollabProtocol.HeartbeatPingSize + 1];
                    payload.CopyTo(answer);
                    answer[CollabProtocol.HeartbeatPingSize] = 0; // explicit: stackalloc is not guaranteed zeroed
                    EchoHeartbeat(peer, answer);
                }
                else if (payload.Length == CollabProtocol.HeartbeatPingSize + 1)
                {
                    // An echo of our own ping: its payload is still our TickCount64, so its age is
                    // the round trip. Shown per participant in the Collaboration panel.
                    var rtt = CollabProtocol.MeasureHeartbeatRtt(payload);
                    if (rtt >= 0)
                        peer.LastRttMs = rtt;
                }

                break;

            default:
                Log.Warning($"Collab: peer {peer} sent unknown frame type {type}, disconnecting");
                peer.SessionClosed = true;
                break;
        }
    }

    protected abstract void EchoHeartbeat(CollabPeerConnection peer, ReadOnlySpan<byte> timestamp);
}

/// <summary>
/// Host transport for direct LAN/online sessions: a TCP listener plus a UDP socket used for presence
/// telemetry and (optionally) LAN discovery replies. One reader thread per accepted peer.
/// </summary>
internal sealed class CollabHostTransport : CollabTransport
{
    private readonly List<CollabPeerConnection> _peers = [];

    /** Copy of _peers republished on every change, so the UI and UDP threads enumerate without locking. */
    private volatile CollabPeerConnection[] _peerSnapshot = [];

    private readonly TcpListener _listener;
    private readonly UdpClient _udp;
    private readonly CancellationTokenSource _cancellation = new();
    private int _nextPeerId = 1;

    public LanDiscoveryResponder? DiscoveryResponder { get; private set; }

    public override IReadOnlyList<CollabPeerConnection> Peers => _peerSnapshot;

    protected override void CleanupPeer(CollabPeerConnection peer)
    {
        lock (_peers)
        {
            _peers.Remove(peer);
            _peerSnapshot = _peers.ToArray();
        }
    }

    public CollabHostTransport(int tcpPort, bool enableLanDiscovery, Func<JObject> announcementFactory, string? interfaceName = null)
    {
        // A pinned adapter (Settings → Network) must receive the session; everything else keeps
        // the previous all-interface bind. Clients discover the host by the address they reach it on.
        var bindAddress = LanInterfaces.ResolveAddress(interfaceName) ?? IPAddress.Any;
        _listener = new TcpListener(bindAddress, tcpPort);
        _listener.Start();

        UdpClient? udp = null;
        try
        {
            udp = bindAddress != IPAddress.Any ? new UdpClient(new IPEndPoint(bindAddress, tcpPort)) : new UdpClient(tcpPort);
            udp.Client.EnableBroadcast = false;

            if (enableLanDiscovery)
                DiscoveryResponder = new LanDiscoveryResponder(tcpPort, announcementFactory,
                                                               bindAddress == IPAddress.Any ? null : bindAddress);

            _udp = udp;
        }
        catch
        {
            // The TCP listener (and possibly the discovery socket) is already bound. Without
            // releasing them here, starting a session again in this process fails with "address
            // already in use" until the editor exits.
            udp?.Close();
            DiscoveryResponder?.Dispose();
            try
            {
                _listener.Stop();
            }
            catch
            {
                // Listener already stopped.
            }

            throw;
        }

        _ = Task.Run(() => AcceptLoopAsync(_cancellation.Token));
        _ = Task.Run(() => UdpLoopAsync(_cancellation.Token));
        Log.Info($"Collab: hosting on port {tcpPort}");
    }

    public override void SendToPeer(CollabPeerConnection peer, CollabProtocol.FrameType type, ReadOnlySpan<byte> payload)
    {
        peer.Write(type, payload);
    }

    /// <summary>Relays a presence datagram from one peer to all other peers over UDP.</summary>
    public void RelayPresence(CollabPeerConnection source, ReadOnlySpan<byte> payload)
    {
        foreach (var peer in _peerSnapshot)
        {
            if (peer == source || !peer.IsAuthenticated)
                continue;

            SendPresenceTo(peer, payload);
        }
    }

    public void SendPresenceTo(CollabPeerConnection peer, ReadOnlySpan<byte> payload)
    {
        var endpoint = peer.UdpEndpoint;
        if (endpoint == null)
        {
            // No UDP pinhole (e.g. symmetric NAT or a relayed session) — fall back to the reliable
            // channel. Slightly higher latency, but correctness first.
            peer.Write(CollabProtocol.FrameType.Presence, payload);
            return;
        }

        try
        {
            _udp.Send(payload.ToArray(), payload.Length, endpoint);
        }
        catch (SocketException)
        {
            // ICMP unreachable etc. Presence is best-effort; the next tick retries.
        }
    }

    private async Task AcceptLoopAsync(CancellationToken cancellation)
    {
        while (!cancellation.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await _listener.AcceptTcpClientAsync(cancellation);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (SocketException e)
            {
                Log.Warning($"Collab: accept failed: {e.Message}");
                await Task.Delay(500, cancellation).ContinueWith(_ => { });
                continue;
            }

            if (_peerSnapshot.Count(p => !p.SessionClosed) >= CollabProtocol.MaxPeers)
            {
                Log.Warning("Collab: refused connection, peer limit reached");
                client.Close();
                continue;
            }

            CollabProtocol.ConfigureShowSocket(client);
            CollabPeerConnection peer;
            try
            {
                peer = new CollabPeerConnection(client, Interlocked.Increment(ref _nextPeerId));
            }
            catch (Exception e) when (e is SocketException or ObjectDisposedException or InvalidOperationException)
            {
                // Peer reset the connection between accept and setup.
                client.Close();
                continue;
            }

            lock (_peers)
            {
                _peers.Add(peer);
                _peerSnapshot = _peers.ToArray();
            }

            OnStatus($"Peer connecting from {(client.Client.RemoteEndPoint as IPEndPoint)?.Address}");
            _ = Task.Run(() => ReadLoop(peer), cancellation);
        }
    }

    private async Task UdpLoopAsync(CancellationToken cancellation)
    {
        while (!cancellation.IsCancellationRequested)
        {
            UdpReceiveResult result;
            try
            {
                result = await _udp.ReceiveAsync(cancellation);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (SocketException)
            {
                continue;
            }

            var payload = result.Buffer;
            if (payload.Length < 27 || payload.Length > CollabProtocol.MaxPresencePayload)
                continue;

            var peerId = payload[0];
            CollabPeerConnection? peer;
            lock (_peers)
            {
                peer = _peers.FirstOrDefault(p => p.AssignedId == peerId);
            }

            if (peer == null || !peer.IsAuthenticated)
                continue;

            // Learn the peer's UDP path: datagrams must originate from the address we already know
            // from TCP to prevent third parties from injecting presence into a session.
            var remoteIp = (result.RemoteEndPoint as IPEndPoint)?.Address;
            var tcpIp = peer.RemoteAddress;
            if (remoteIp == null || tcpIp == null || !remoteIp.Equals(tcpIp))
                continue;

            peer.UdpEndpoint = result.RemoteEndPoint;
            peer.MarkHeardFrom();

            var packet = PresencePacket.TryDecode(payload, out var presence) ? presence : null;
            if (packet == null)
                continue;

            Inbound.Enqueue(new InboundMessage(peer, packet));
            RelayPresence(peer, payload);
        }
    }

    public override void Dispose()
    {
        _cancellation.Cancel();
        lock (_peers)
        {
            foreach (var peer in _peers)
                peer.Close();

            _peers.Clear();
            _peerSnapshot = [];
        }

        DiscoveryResponder?.Dispose();
        try
        {
            _listener.Stop();
            _udp.Close();
        }
        catch
        {
            // Listener already dead.
        }
    }

    protected override void EchoHeartbeat(CollabPeerConnection peer, ReadOnlySpan<byte> timestamp)
    {
        peer.Write(CollabProtocol.FrameType.Heartbeat, timestamp);
    }
}

/// <summary>
/// Client transport for direct sessions: one TCP connection to the host plus a bound UDP socket for
/// presence. The host's replies keep the NAT mapping open.
/// </summary>
internal sealed class CollabClientTransport : CollabTransport
{
    /// <summary>The link to the host. Only valid after <see cref="Connect"/> succeeded.</summary>
    public CollabPeerConnection HostPeer => _hostPeer ?? throw new InvalidOperationException("Collab client transport is not connected");

    private readonly UdpClient _udp;
    private readonly IPEndPoint _hostEndPoint;
    private CollabPeerConnection? _hostPeer;
    private readonly CancellationTokenSource _cancellation = new();

    public CollabClientTransport(IPEndPoint hostEndPoint)
    {
        _hostEndPoint = hostEndPoint;
        _udp = new UdpClient();
        _udp.Client.EnableBroadcast = false;
    }

    /// <summary>Connects synchronously; throws on failure so the manager can surface a clean error.</summary>
    public void Connect()
    {
        var client = CollabProtocol.ConnectWithTimeout(_hostEndPoint);
        CollabProtocol.ConfigureShowSocket(client);
        _hostPeer = new CollabPeerConnection(client, 0);
        _ = Task.Run(() => ReadLoop(_hostPeer));
        _ = Task.Run(() => UdpLoopAsync(_cancellation.Token));
        Log.Info($"Collab: connected to {_hostEndPoint}");
    }

    public override IReadOnlyList<CollabPeerConnection> Peers => _hostPeer == null ? [] : [_hostPeer];

    public override CollabPeerConnection? HostLink => _hostPeer;

    public void SendPresence(ReadOnlySpan<byte> payload)
    {
        try
        {
            _udp.Send(payload.ToArray(), payload.Length, _hostEndPoint);
        }
        catch (SocketException)
        {
            // Best-effort channel.
        }
    }

    public override void SendToPeer(CollabPeerConnection peer, CollabProtocol.FrameType type, ReadOnlySpan<byte> payload)
    {
        peer.Write(type, payload);
    }

    private async Task UdpLoopAsync(CancellationToken cancellation)
    {
        while (!cancellation.IsCancellationRequested)
        {
            UdpReceiveResult result;
            try
            {
                result = await _udp.ReceiveAsync(cancellation);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (SocketException)
            {
                continue;
            }

            var payload = result.Buffer;
            if (payload.Length < 27 || payload.Length > CollabProtocol.MaxPresencePayload)
                continue;

            // Accept only datagrams that come back from the address we connected to.
            var remoteIp = (result.RemoteEndPoint as IPEndPoint)?.Address;
            if (remoteIp == null || !remoteIp.Equals(_hostEndPoint.Address))
                continue;

            if (_hostPeer != null && PresencePacket.TryDecode(payload, out var presence))
                Inbound.Enqueue(new InboundMessage(_hostPeer, presence));
        }
    }

    public override void Dispose()
    {
        _cancellation.Cancel();
        _hostPeer?.Close();
        _udp.Close();
    }

    protected override void EchoHeartbeat(CollabPeerConnection peer, ReadOnlySpan<byte> timestamp)
    {
        peer.Write(CollabProtocol.FrameType.Heartbeat, timestamp);
    }
}

/// <summary>
/// LAN discovery: answers UDP broadcast probes with a small JSON announcement. Runs on its own
/// socket (broadcast port = session TCP port + 1) so it can be enabled and disabled independently.
/// </summary>
internal sealed class LanDiscoveryResponder : IDisposable
{
    public const string ProbePayload = "TIXL-COLLAB-DISCOVER";
    public const int DiscoveryPortOffset = 1;

    private readonly UdpClient _udp;
    private readonly CancellationTokenSource _cancellation = new();
    private readonly Func<JObject> _announcementFactory;

    /// <summary><paramref name="bindAddress"/> pins the responder to one adapter (Settings → Network);
    /// null binds all interfaces.</summary>
    public LanDiscoveryResponder(int tcpPort, Func<JObject> announcementFactory, IPAddress? bindAddress = null)
    {
        _announcementFactory = announcementFactory;
        _udp = bindAddress != null ? new UdpClient(new IPEndPoint(bindAddress, tcpPort + DiscoveryPortOffset))
                       : new UdpClient(tcpPort + DiscoveryPortOffset);
        _udp.Client.EnableBroadcast = true;
        _ = Task.Run(LoopAsync);
    }

    private async Task LoopAsync()
    {
        while (!_cancellation.IsCancellationRequested)
        {
            UdpReceiveResult result;
            try
            {
                result = await _udp.ReceiveAsync(_cancellation.Token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (SocketException)
            {
                continue;
            }

            var text = Encoding.UTF8.GetString(result.Buffer);
            if (text != ProbePayload)
                continue;

            var announcement = _announcementFactory();
            var payload = CollabProtocol.EncodeJson(announcement);
            try
            {
                await _udp.SendAsync(payload, payload.Length, result.RemoteEndPoint);
            }
            catch (SocketException)
            {
                // The probing client may already be gone.
            }
        }
    }

    public void Dispose()
    {
        _cancellation.Cancel();
        _udp.Close();
    }
}

/// <summary>
/// LAN discovery scanner used by the join UI: broadcasts probes and collects announcements for a
/// couple of seconds. Fully independent of any running session.
/// </summary>
internal sealed class LanDiscoveryScanner : IDisposable
{
    public readonly ConcurrentQueue<(JObject Announcement, IPEndPoint Source, DateTime Received)> Results = new();

    private UdpClient? _udp;
    private CancellationTokenSource? _cancellation;
    private Task? _loopTask;
    private IReadOnlyList<IPAddress> _directTargets = [];
    private LanInterface? _pinnedInterface;

    /// <summary>
    /// Starts the scan. <paramref name="directTargets"/> are probed by unicast alongside the
    /// broadcasts — a master whose address is already known is re-found even on networks that
    /// block broadcast (Wi-Fi client isolation, guest VLANs). <paramref name="interfaceName"/>
    /// pins probe and socket to one adapter (Settings → Network); empty probes every route.
    /// </summary>
    public void Start(int discoveryPort, IReadOnlyList<IPAddress>? directTargets = null, string? interfaceName = null)
    {
        Stop();
        _directTargets = directTargets ?? [];
        _pinnedInterface = T3.Core.Utils.LanInterfaces.Find(interfaceName);
        var cancellation = new CancellationTokenSource();
        var udp = new UdpClient();
        udp.Client.EnableBroadcast = true;
        udp.Client.Bind(new IPEndPoint(_pinnedInterface?.Address ?? IPAddress.Any, 0));
        _cancellation = cancellation;
        _udp = udp;

        // The loop owns its socket and token directly: fields are swapped and nulled by Stop()
        // from the UI thread, and a loop that read them would race with its own teardown.
        _loopTask = Task.Run(() => LoopAsync(discoveryPort, udp, cancellation.Token));
    }

    private async Task LoopAsync(int discoveryPort, UdpClient udp, CancellationToken cancellation)
    {
        var probe = Encoding.UTF8.GetBytes(LanDiscoveryResponder.ProbePayload);
        var interval = TimeSpan.FromMilliseconds(500);

        // Probe every route out of this machine: the limited broadcast reaches the same subnet,
        // each interface's directed broadcast covers multi-adapter machines (Hyper-V, Wi-Fi plus
        // Ethernet) and routed subnets, the loopback covers rehearsing on one computer, and the
        // direct targets re-find a master whose address we already know on networks that block
        // broadcast entirely (Wi-Fi client isolation, guest VLANs). A pinned adapter (Settings →
        // Network) narrows all of that to that adapter's broadcast.
        var targets = new List<IPEndPoint>();
        void RebuildTargets()
        {
            targets.Clear();
            if (_pinnedInterface != null)
            {
                targets.Add(new IPEndPoint(_pinnedInterface.BroadcastAddress ?? IPAddress.Broadcast, discoveryPort));
            }
            else
            {
                targets.Add(new IPEndPoint(IPAddress.Broadcast, discoveryPort));
                try
                {
                    foreach (var @interface in NetworkInterface.GetAllNetworkInterfaces())
                    {
                        if (@interface.OperationalStatus != OperationalStatus.Up
                            || @interface.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                            continue;

                        foreach (var address in @interface.GetIPProperties().UnicastAddresses)
                        {
                            if (address.Address.AddressFamily != AddressFamily.InterNetwork
                                || address.PrefixLength is < 8 or > 32)
                                continue;

                            var local = address.Address.GetAddressBytes();
                            var mask = PrefixToMask(address.PrefixLength);
                            var broadcast = new byte[4];
                            for (var i = 0; i < 4; i++)
                                broadcast[i] = (byte)(local[i] | (byte)~mask[i]);

                            targets.Add(new IPEndPoint(new IPAddress(broadcast), discoveryPort));
                        }
                    }
                }
                catch (Exception)
                {
                    // Interface enumeration can fail on machines with exotic virtual adapters; the
                    // limited broadcast and the direct targets below still go out.
                }
            }

            targets.Add(new IPEndPoint(IPAddress.Loopback, discoveryPort));
            foreach (var direct in _directTargets)
                targets.Add(new IPEndPoint(direct, discoveryPort));
        }

        RebuildTargets();
        var nextTargetsRefresh = Environment.TickCount64 + 15_000; // adapters come and go

        while (!cancellation.IsCancellationRequested)
        {
            try
            {
                if (Environment.TickCount64 >= nextTargetsRefresh)
                {
                    RebuildTargets();
                    nextTargetsRefresh = Environment.TickCount64 + 15_000;
                }

                foreach (var target in targets)
                {
                    try
                    {
                        udp.Send(probe, probe.Length, target);
                    }
                    catch (SocketException)
                    {
                        // One unrouteable interface (broken virtual adapter, VPN tunnel) must not
                        // starve the remaining targets of this round.
                    }
                }

                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
                timeout.CancelAfter(interval);
                while (true)
                {
                    var result = await udp.ReceiveAsync(timeout.Token);
                    var text = Encoding.UTF8.GetString(result.Buffer);

                    // No prefix gate here: collab and boygroup announcements discriminate by
                    // content at the consumer, and junk replies throw JsonException below.
                    JObject json;
                    try
                    {
                        json = JObject.Parse(text);
                    }
                    catch (JsonException)
                    {
                        // Some other tool answered our probe; ignore.
                        continue;
                    }

                    var source = result.RemoteEndPoint as IPEndPoint ?? new IPEndPoint(IPAddress.None, 0);
                    Results.Enqueue((json, source, DateTime.UtcNow));
                }
            }
            catch (OperationCanceledException) when (!cancellation.IsCancellationRequested)
            {
                // Receive timeout for this interval — probe again.
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (SocketException)
            {
                try { await Task.Delay(200, cancellation); } catch (OperationCanceledException) { break; }
            }
            catch (ObjectDisposedException)
            {
                // Stop() closed the socket while a receive/send was in flight.
                break;
            }
            catch (Exception e)
            {
                // The scan must never die silently: report and keep going after a pause.
                Console.WriteLine($"[discovery-scanner] retrying after: {e.Message}");
                try { await Task.Delay(500, cancellation); } catch (OperationCanceledException) { break; }
            }
        }
    }

    /// <summary>Prefix length to the network mask's bytes; the host bits are set to form the directed broadcast address.</summary>
    private static byte[] PrefixToMask(int prefixLength)
    {
        var mask = new byte[4];
        for (var i = 0; i < 4; i++)
        {
            var bits = Math.Clamp(prefixLength - i * 8, 0, 8);
            mask[i] = bits == 0 ? (byte)0 : (byte)(0xFF << (8 - bits));
        }

        return mask;
    }

    public void Stop()
    {
        _cancellation?.Cancel();
        _udp?.Close();
        try { _loopTask?.Wait(200); } catch { /* teardown */ }
        _cancellation?.Dispose();
        _cancellation = null;
        _udp = null;
        _loopTask = null;
    }

    public void Dispose() => Stop();
}
