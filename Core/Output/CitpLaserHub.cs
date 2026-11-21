#nullable enable
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using T3.Core.DataTypes;
using T3.Core.Logging;
using T3.Core.Operator.Interfaces;

namespace T3.Core.Output;

/// <summary>
/// One feed slot on a <see cref="CitpLaserHub"/>: a laser plug's output as the shared CITP source
/// exposes it. The feed index is the wire identity the receiver binds its laser to ("Physical
/// Output" in Capture), so it is stable while the feed stays registered.
/// </summary>
public sealed class CitpLaserFeed
{
    /// <summary>The feed name announced in the LaserFeedList - the plug's resolved feed name.</summary>
    public string Name { get; internal set; } = string.Empty;

    /// <summary>The wire feed index; assigned at registration, reassigned when a feed leaves.</summary>
    public int Index { get; internal set; }

    /// <summary>Set by the receiver's LaserFeedControl; the feed sends nothing before it.</summary>
    public volatile bool Active;
    public int RequiredFps;

    // Per-feed device audit, read at encode time so a plug edit applies without touching the session.
    public volatile float MaxBrightness = 1f;
    public volatile bool SwapColorChannels;
    public volatile bool Keepalive = true;

    internal readonly ConcurrentQueue<LaserPoint[]> Frames = new();
    internal LaserPoint[]? LastFrame;
    internal long NextSendTimestamp;
    internal uint SequenceNo;
    internal LaserPoint[] BlankScratch = Array.Empty<LaserPoint>();
}

/// <summary>
/// The machine's one CITP laser source per receiver: a single TCP control session (PNam "TiXL")
/// announcing every registered feed the way Capture groups sources - one peer, N feeds, "Physical
/// Output" numbered by feed index - plus CAEX LaserFeedFrame datagrams tagged with that index.
/// This is the MadMapper shape of the protocol; one peer per laser output made same-named peers
/// indistinguishable and every stream collapsed onto the first feed.
///
/// Per-plug <c>CitpStream</c> adapters (one laser output each) register a feed here; the hub owns
/// the session, the discovery, the pacing and the wire format. Feeds keep their own device
/// settings (brightness ceiling, channel order, keepalive), applied at encode time.
/// </summary>
public sealed class CitpLaserHub : IDisposable
{
    private const uint CitpCookie = 0x43495450; // "CITP"
    private const uint PinFCookie = 0x50494E46; // "PINF"
    private const uint CaexCookie = 0x43414558; // "CAEX"

    private enum CaexContentType : uint
    {
        GetLaserFeedList = 0x00030100,
        LaserFeedList = 0x00030101,
        LaserFeedControl = 0x00030102,
        LaserFeedFrame = 0x00030200,
        EnterShow = 0x00020100,
    }

    // Verified working with Capture: 224.0.0.180:4809
    private static readonly IPAddress MulticastAddress = IPAddress.Parse("224.0.0.180");
    private const int MulticastPort = 4809;
    private const int DefaultCapturePort = 51245;

    /// <summary>A LaserFeedFrame travels as a single UDP datagram, capped to stay inside the IPv4
    /// datagram limit (~65,507 bytes ≈ 13,000 points) — a larger feed would fail every send with
    /// WSAEMSGSIZE and overflow the ushort point count.</summary>
    private const int MaxPointsPerDatagram = 12000;

    private const int MaxQueuedFramesPerFeed = 5;
    private const int ReconnectDelayMs = 2000;
    private const int LocalhostFallbackDelayMs = 6000;
    private const int ConnectTimeoutMs = 3000;

    /// <summary>Closed-session retries before a pinned target yields to what discovery announces.</summary>
    private const int DiscoveryFallbackFailures = 3;

    /// <summary>Last successful TCP read or write; a silent cable pull must not read as connected.</summary>
    private const int PeerTimeoutMs = 15_000;

    /// <summary>How often the feed list is re-announced on the control session: Capture re-lists
    /// the feeds after restarts without waiting for its own GetLaserFeedList.</summary>
    private static readonly TimeSpan ReannounceInterval = TimeSpan.FromSeconds(5);

    private static readonly ConcurrentDictionary<string, CitpLaserHub> Hubs = new();

    /// <summary>The hub for a target: direct host/port, or discovery when both are unset. All
    /// plugs sharing a target share the session - the one-source-many-feeds shape the receivers
    /// group by.</summary>
    public static CitpLaserHub GetOrCreate(string directHost, int directPort, string localAddress)
    {
        var key = $"{directHost}|{directPort}|{localAddress}";
        return Hubs.GetOrAdd(key, _ => new CitpLaserHub(directHost, directPort, localAddress));
    }

    private CitpLaserHub(string directHost, int directPort, string localAddress)
    {
        _directHost = directHost;
        _directPort = directPort;
        _localAddress = localAddress;

        // One source key per sender lifetime: a reconnect keeps the identity, so the receiver
        // updates its existing source instead of accumulating one entry per session.
        _sourceKey = (uint)Random.Shared.Next();
        _captureIp = _directHost;
        _captureTcpPort = IsDirectConnect ? _directPort : 0;

        StartConnection();
    }

    private readonly string _directHost;
    private readonly int _directPort;
    private readonly string _localAddress;
    private readonly uint _sourceKey;

    private readonly object _feedsLock = new();
    private readonly List<CitpLaserFeed> _feeds = new();

    private readonly object _connectionLock = new();
    private CancellationTokenSource? _cts;
    private TcpClient? _tcpClient;
    private NetworkStream? _tcpStream;
    private int _connectInProgress;
    private int _disposed;
    private int _consecutiveConnectFailures;
    private long _lastConnectFailureLog;
    private long _lastPeerActivityTicks = Environment.TickCount64;
    private int _captureTcpPort; // staged before StartConnection; 0 = PLoc discovery
    private string _captureIp = string.Empty;
    private volatile StatusInfo _status = new("Disconnected", IStatusProvider.StatusLevel.Notice);

    private sealed record StatusInfo(string Message, IStatusProvider.StatusLevel Level);

    /// <summary>Number of feeds registered right now - 0 while the last adapter is between rebuilds.</summary>
    public int FeedCount
    {
        get
        {
            lock (_feedsLock)
                return _feeds.Count;
        }
    }

    public bool IsConnected
    {
        get
        {
            lock (_connectionLock)
            {
                return _tcpClient is { Connected: true }
                       && Environment.TickCount64 - Volatile.Read(ref _lastPeerActivityTicks) < PeerTimeoutMs;
            }
        }
    }

    public IStatusProvider.StatusLevel StatusLevel => _status.Level;
    public string StatusMessage => _status.Message;

    /// <summary>True when the live session talks to exactly this target: a plug re-pinned to the
    /// peer the source is already connected to is the same session - rebuilding its feed would
    /// only drop it from the list.</summary>
    public bool IsConnectedTo(string ip, int port)
    {
        lock (_connectionLock)
            return _tcpClient is { Connected: true } && _captureIp == ip && _captureTcpPort == port;
    }

    /// <summary>Registers a laser output as a feed. The returned slot's Index is the wire identity;
    /// the feed list is re-announced so the receiver picks the change up without a reconnect.</summary>
    public CitpLaserFeed RegisterFeed(string name)
    {
        var feed = new CitpLaserFeed { Name = name };
        lock (_feedsLock)
        {
            feed.Index = _feeds.Count;
            _feeds.Add(feed);
        }

        Log.Debug($"CITP: registered feed {feed.Index + 1} '{name}' - the source now lists {FeedCount} feeds");
        if (IsConnected)
            SendLaserFeedList();

        return feed;
    }

    /// <summary>Removes a feed and reindexes the rest; the last unregister disposes the hub.</summary>
    public void UnregisterFeed(CitpLaserFeed feed)
    {
        lock (_feedsLock)
        {
            if (!_feeds.Remove(feed))
                return;

            for (var i = 0; i < _feeds.Count; i++)
                _feeds[i].Index = i;
        }

        if (FeedCount == 0)
        {
            Dispose();
            return;
        }

        if (IsConnected)
            SendLaserFeedList();
    }

    /// <summary>Queues a frame for a feed; the send thread paces it at the receiver's requested fps.</summary>
    public void SubmitFrame(CitpLaserFeed feed, in LaserFrame frame)
    {
        if (Volatile.Read(ref _disposed) != 0 || !IsConnected || !feed.Active)
            return;

        var copy = LaserOutputStream.CopyForQueue(frame, feed.Frames, MaxQueuedFramesPerFeed);
        if (copy == null)
            return;

        feed.Frames.Enqueue(copy);
    }

    // ---------------------------------------------------------------- session

    private bool IsDirectConnect => _directPort > 0 && !string.IsNullOrWhiteSpace(_directHost);

    private void SetStatus(string message, IStatusProvider.StatusLevel level)
    {
        _status = new StatusInfo(message, level);
    }

    private void StartConnection()
    {
        if (Volatile.Read(ref _disposed) != 0)
            return;

        var cts = new CancellationTokenSource();
        lock (_connectionLock)
            _cts = cts;

        // The discovery listener runs for every hub, including a pinned host: Capture's control
        // port is dynamic and only announced over PLoc, so a pinned port goes stale the moment
        // Capture restarts. Listening lets the connect follow the live port (AdoptAnnouncedPort).
        CitpPeerDiscovery.EnsureStarted();
        CitpPeerDiscovery.PeerAnnounced += OnPeerAnnounced;

        SetStatus(IsDirectConnect
                      ? $"Connecting to {_captureIp}:{_captureTcpPort}"
                      : $"Listening for Capture on {MulticastAddress}:{MulticastPort}",
                  IStatusProvider.StatusLevel.Notice);

        // TcpClient.Connect blocks for seconds when unreachable - never on the caller's thread.
        var token = cts.Token;
        _ = Task.Run(async () =>
                        {
                            try
                            {
                                await ReconnectLoopAsync(token).ConfigureAwait(false);
                            }
                            finally
                            {
                                lock (_connectionLock)
                                {
                                    if (ReferenceEquals(_cts, cts))
                                        _cts = null;
                                }

                                cts.Dispose();
                            }
                        });
    }

    /// <summary>
    /// Keeps the control session up: connects to the current target and reconnects after the
    /// receiver restarts or drops the session. In discovery mode announcements re-target it; a
    /// receiver that announced before the hub started will not repeat immediately, so the default
    /// localhost Capture is tried when nothing announced within a few seconds.
    /// </summary>
    private async Task ReconnectLoopAsync(CancellationToken token)
    {
        var startedAt = Environment.TickCount64;
        try
        {
            while (!token.IsCancellationRequested && Volatile.Read(ref _disposed) == 0)
            {
                if (!IsConnected)
                {
                    // Before retrying a pinned port, take Capture's announced one: a stale port
                    // is the usual reason a source sits on "connecting" while Capture is up.
                    AdoptAnnouncedPort();

                    if (!IsDirectConnect && Environment.TickCount64 - startedAt > LocalhostFallbackDelayMs)
                    {
                        lock (_connectionLock)
                        {
                            if (_captureTcpPort == 0)
                            {
                                _captureIp = "127.0.0.1";
                                _captureTcpPort = DefaultCapturePort;
                            }
                        }
                    }

                    await ConnectToCaptureAsync();

                    if (IsConnected)
                    {
                        _consecutiveConnectFailures = 0;
                    }
                    else if (++_consecutiveConnectFailures >= DiscoveryFallbackFailures)
                    {
                        // Self-healing: a pinned host that stopped answering falls back to what
                        // discovery actually announces, so opening Capture reconnects on its own.
                        if (AdoptDiscoveredPeer())
                            _consecutiveConnectFailures = 0;
                    }
                }

                await Task.Delay(ReconnectDelayMs, token);
            }
        }
        catch (OperationCanceledException)
        {
            // Hub disposed
        }
    }

    /// <summary>
    /// Capture's control port is dynamic: it re-announces the port it currently listens on, and
    /// the port stored on a plug goes stale after any Capture restart. With a pinned host, adopt
    /// the announced port for that host - that keeps the host pinned (the venue's machine) while
    /// the port follows reality.
    /// </summary>
    private bool AdoptAnnouncedPort()
    {
        if (!IsDirectConnect)
            return false;

        foreach (var peer in CitpPeerDiscovery.Peers)
        {
            if (!string.Equals(peer.Ip, _captureIp, StringComparison.Ordinal) || peer.TcpPort <= 0)
                continue;

            if (peer.TcpPort == _captureTcpPort)
                return false;

            string? announcement = null;
            lock (_connectionLock)
            {
                if (Volatile.Read(ref _disposed) != 0 || _tcpClient is { Connected: true })
                    return false;

                announcement = $"CITP: Capture at {_captureIp} announces port {peer.TcpPort} "
                               + $"(target has {_captureTcpPort}) - following the announced port";
                _captureTcpPort = peer.TcpPort;
            }

            Log.Info(announcement);
            return true;
        }

        return false;
    }

    /// <summary>Retargets a pinned connection at the peer CITP discovery actually announces -
    /// same /24 first, then the first announced. Returns true when the target changed.</summary>
    private bool AdoptDiscoveredPeer()
    {
        if (!IPAddress.TryParse(_captureIp, out var pinned) || pinned.GetAddressBytes().Length != 4)
            return false;

        var pinnedBytes = pinned.GetAddressBytes();
        CitpPeerDiscovery.Peer? best = null;
        CitpPeerDiscovery.Peer? first = null;

        foreach (var peer in CitpPeerDiscovery.Peers)
        {
            if (peer.TcpPort <= 0 || !IPAddress.TryParse(peer.Ip, out var peerIp) || peerIp.GetAddressBytes().Length != 4)
                continue;

            first ??= peer;

            var bytes = peerIp.GetAddressBytes();
            if (bytes[0] != pinnedBytes[0] || bytes[1] != pinnedBytes[1] || bytes[2] != pinnedBytes[2])
                continue;

            best = peer;
            break;
        }

        best ??= first;
        if (best == null)
            return false;

        string? message = null;
        lock (_connectionLock)
        {
            if (Volatile.Read(ref _disposed) != 0 || _tcpClient is { Connected: true })
                return false;

            if (best.Ip == _captureIp && best.TcpPort == _captureTcpPort)
                return false;

            message = $"CITP: {_captureIp}:{_captureTcpPort} is not answering - using the announced Capture "
                      + $"{best.Ip}:{best.TcpPort} for the feed source";
            _captureIp = best.Ip;
            _captureTcpPort = best.TcpPort;
        }

        Log.Debug(message);
        return true;
    }

    private void OnPeerAnnounced(CitpPeerDiscovery.Peer peer)
    {
        if (Volatile.Read(ref _disposed) != 0 || IsConnected || IsDirectConnect)
            return;

        // With a local interface selected, peers announcing from another subnet are ignored
        // (virtual adapters announce on their own subnets). "0.0.0.0" is the picker's "any
        // interface" and has no subnet to prefer - treating it as one rejected every
        // non-loopback peer, so discovery silently fell back to localhost.
        if (!IPAddress.TryParse(peer.Ip, out var peerIp))
            return;
        if (IPAddress.TryParse(_localAddress, out var selectedLocal)
            && !selectedLocal.Equals(IPAddress.Any)
            && !IPAddress.IsLoopback(peerIp)
            && selectedLocal.GetAddressBytes().Length == 4
            && peerIp.GetAddressBytes().Length == 4
            && selectedLocal.GetAddressBytes()[0] != peerIp.GetAddressBytes()[0])
        {
            return;
        }

        lock (_connectionLock)
        {
            if (Volatile.Read(ref _disposed) != 0 || _tcpClient is { Connected: true })
                return;

            _captureIp = peer.Ip;
            _captureTcpPort = peer.TcpPort;
        }

        // Runs on the shared discovery thread - a blocking connect would stall discovery.
        _ = Task.Run(ConnectToCaptureAsync);
    }

    private async Task ConnectToCaptureAsync()
    {
        string targetIp;
        int targetPort;
        lock (_connectionLock)
        {
            targetIp = _captureIp;
            targetPort = _captureTcpPort;
        }

        if (string.IsNullOrEmpty(targetIp) || targetPort == 0)
            return;
        if (!IPAddress.TryParse(targetIp, out var ip))
            return;

        // Discovery and reconnects can reach this at the same moment - only one attempt may run.
        if (Interlocked.Exchange(ref _connectInProgress, 1) == 1)
            return;

        try
        {
            await ConnectToCaptureInnerAsync(ip, targetPort);
        }
        finally
        {
            Interlocked.Exchange(ref _connectInProgress, 0);
        }
    }

    private async Task ConnectToCaptureInnerAsync(IPAddress ip, int targetPort)
    {
        TcpClient? tcpClient = null;
        try
        {
            StopTcpConnection();

            tcpClient = new TcpClient(ip.AddressFamily)
                            {
                                // A peer that stops reading must not block the UDP feed thread
                                // forever. The receive side stays untimed (idle control sessions
                                // are normal); activity is tracked separately.
                                SendTimeout = 2000,
                            };
            tcpClient.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);

            // With a pinned interface, bind the socket to it so the session leaves on the chosen
            // NIC. A bind that fails falls back to OS routing rather than never connecting.
            if (IPAddress.TryParse(_localAddress, out var localIp)
                && !localIp.Equals(IPAddress.Any)
                && !IPAddress.IsLoopback(ip))
            {
                try
                {
                    tcpClient.Client.Bind(new IPEndPoint(localIp, 0));
                }
                catch (Exception bindEx)
                {
                    Log.Debug($"CITP: interface bind to {_localAddress} failed ({bindEx.Message}) - using OS routing");
                }
            }

            using var connectCts = new CancellationTokenSource(ConnectTimeoutMs);
            try
            {
                await tcpClient.ConnectAsync(ip, targetPort, connectCts.Token);
            }
            catch (OperationCanceledException)
            {
                SetStatus($"CITP: {ip}:{targetPort} did not answer within {ConnectTimeoutMs / 1000}s",
                          IStatusProvider.StatusLevel.Notice);
                StopTcpConnection();
                try { tcpClient.Close(); } catch { /* ignore */ }
                return;
            }

            var tcpStream = tcpClient.GetStream();

            lock (_connectionLock)
            {
                if (_cts == null || _cts.IsCancellationRequested)
                {
                    tcpStream.Close();
                    tcpClient.Close();
                    return;
                }

                _tcpClient = tcpClient;
                _tcpStream = tcpStream;
                Volatile.Write(ref _lastPeerActivityTicks, Environment.TickCount64);

                SetStatus($"Connected to Capture at {ip}:{targetPort}", IStatusProvider.StatusLevel.Success);

                var token = _cts.Token;
                var client = tcpClient;
                var stream = tcpStream;
                new Thread(() => TcpReceiveLoop(token, client, stream))
                    {
                        IsBackground = true,
                        Name = "CITP_TCP_Receiver",
                    }
                    .Start();
                new Thread(() => SendLoop(token, client))
                    {
                        IsBackground = true,
                        Name = "CITP_UDP_Sender",
                    }
                    .Start();
            }

            // Log outside the lock: a slow log writer must not stall SubmitFrame's IsConnected.
            Log.Debug($"CITP: control session established with {ip}:{targetPort} ({FeedCount} feeds)");

            // Handshake first
            SendPeerName();
            SendPeerType();
            SendLaserFeedList();
        }
        catch (Exception ex)
        {
            SetStatus($"Discovery: {ip}:{targetPort} refused connection ({ex.Message})", IStatusProvider.StatusLevel.Notice);

            var now = Environment.TickCount64;
            if (now - _lastConnectFailureLog > 10000)
            {
                _lastConnectFailureLog = now;
                Log.Debug($"CITP: cannot reach Capture at {ip}:{targetPort} ({ex.Message}) - retrying; "
                          + "the source follows Capture's announced port when one is known");
            }

            StopTcpConnection();
            try { tcpClient?.Close(); } catch { /* ignore */ }
        }
    }

    /// <summary>
    /// Drops the control session. A caller that still holds a session (a receive loop that just
    /// noticed its peer closing) passes it: teardown is only allowed while that is still the live
    /// session, or an old loop unwinding would close the replacement connection.
    /// </summary>
    private void StopTcpConnection(TcpClient? session = null)
    {
        lock (_connectionLock)
        {
            if (session != null && !ReferenceEquals(_tcpClient, session))
                return;

            try { _tcpStream?.Close(); } catch { /* ignore */ }
            _tcpStream = null;

            try { _tcpClient?.Close(); } catch { /* ignore */ }
            _tcpClient = null;

            // The receiver re-requests every feed it wants after a reconnect, so a dropped
            // session stops all feeds until then.
            lock (_feedsLock)
            {
                foreach (var feed in _feeds)
                {
                    feed.Active = false;
                    feed.RequiredFps = 0;
                    feed.Frames.Clear();
                }
            }

            if (_cts is { IsCancellationRequested: false })
                SetStatus($"Listening for Capture on {MulticastAddress}:{MulticastPort}", IStatusProvider.StatusLevel.Notice);
        }
    }

    private void TcpReceiveLoop(CancellationToken token, TcpClient client, NetworkStream stream)
    {
        // TCP carries no message boundaries: one read may hold several CITP messages or part of
        // one, so messages are cut from an accumulating buffer by their MessageSize. Anything
        // larger than the cap is skipped by its declared size instead of claiming memory.
        const int MaxControlMessageSize = 1 << 20;
        var buffer = new byte[4096];
        var buffered = 0;
        var skipRemaining = 0;
        while (!token.IsCancellationRequested && client.Connected)
        {
            try
            {
                if (buffered == buffer.Length)
                {
                    var grown = Math.Min(buffer.Length * 2, MaxControlMessageSize);
                    if (grown == buffer.Length)
                    {
                        buffered = 0;
                        skipRemaining = 0;
                    }
                    else
                    {
                        Array.Resize(ref buffer, grown);
                    }
                }

                var bytesRead = stream.Read(buffer, buffered, buffer.Length - buffered);
                if (bytesRead == 0)
                {
                    Log.Debug("CITP: receiver closed the control session - reconnecting");
                    StopTcpConnection(client);
                    break;
                }

                Volatile.Write(ref _lastPeerActivityTicks, Environment.TickCount64);
                buffered += bytesRead;
                var consumed = 0;
                while (consumed < buffered)
                {
                    if (skipRemaining > 0)
                    {
                        var skipped = Math.Min(skipRemaining, buffered - consumed);
                        skipRemaining -= skipped;
                        consumed += skipped;
                        continue;
                    }

                    var size = ReadCitpMessageSize(buffer, consumed, buffered - consumed);
                    if (size == 0)
                        break;

                    if (size < 0)
                    {
                        consumed = buffered;
                        break;
                    }

                    if (size > buffer.Length)
                    {
                        if (size > MaxControlMessageSize)
                        {
                            skipRemaining = size - (buffered - consumed);
                            consumed = buffered;
                            break;
                        }

                        Array.Resize(ref buffer, size);
                        break;
                    }

                    if (size > buffered - consumed)
                        break;

                    HandleControlMessage(buffer, consumed, size);
                    consumed += size;
                }

                if (consumed > 0)
                {
                    Array.Copy(buffer, consumed, buffer, 0, buffered - consumed);
                    buffered -= consumed;
                }
            }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException)
            {
                StopTcpConnection(client);
                break;
            }
            catch
            {
                if (!token.IsCancellationRequested)
                    StopTcpConnection(client);
                break;
            }
        }
    }

    /// <summary>Total size of the CITP message starting at <paramref name="offset"/>: 0 while its
    /// 20-byte header is incomplete, -1 when the bytes are not a CITP header.</summary>
    private static int ReadCitpMessageSize(byte[] buffer, int offset, int available)
    {
        if (available < 4)
            return 0;

        if (ReadBigEndianUint(buffer, offset) != CitpCookie)
            return -1;

        if (available < 20)
            return 0;

        var size = ReadLittleEndianUint(buffer, offset + 8);
        if (size < 20)
            return available;

        return size > int.MaxValue ? -1 : (int)size;
    }

    private void HandleControlMessage(byte[] buffer, int offset, int length)
    {
        if (length < 24)
            return; // 20-byte CITP header + 4-byte layer content type minimum

        var contentType = ReadBigEndianUint(buffer, offset + 16);
        if (contentType != CaexCookie)
            return;

        var caexType = ReadLittleEndianUint(buffer, offset + 20);
        switch ((CaexContentType)caexType)
        {
            case CaexContentType.GetLaserFeedList:
                SendLaserFeedList();
                break;
            case CaexContentType.LaserFeedControl:
                HandleLaserFeedControl(buffer, offset, length);
                break;
            case CaexContentType.EnterShow:
                SendEnterShow();
                break;
        }
    }

    // ---------------------------------------------------------------- CAEX messages

    private void SendEnterShow()
    {
        NetworkStream? stream;
        lock (_connectionLock) stream = _tcpStream;
        if (stream == null)
            return;

        try
        {
            using var ms = new MemoryStream();
            using var writer = new BinaryWriter(ms);

            WriteCitpHeader(writer, CaexCookie);
            writer.Write((uint)CaexContentType.EnterShow);

            // Show name, UCS-2 LE, null-terminated
            writer.Write(Encoding.Unicode.GetBytes("TiXL\0"));

            SendTcpMessage(stream, ms);
        }
        catch (Exception ex)
        {
            StopTcpConnection();
            SetStatus($"CITP send failed: {ex.Message}", IStatusProvider.StatusLevel.Warning);
        }
    }

    private void SendPeerName()
    {
        NetworkStream? stream;
        lock (_connectionLock) stream = _tcpStream;
        if (stream == null)
            return;

        try
        {
            using var ms = new MemoryStream();
            using var writer = new BinaryWriter(ms);

            WriteCitpHeader(writer, PinFCookie);
            WriteCookieBytes(writer, PnamCookie);
            writer.Write(Encoding.ASCII.GetBytes("TiXL\0"));

            SendTcpMessage(stream, ms);
        }
        catch (Exception ex)
        {
            StopTcpConnection();
            SetStatus($"CITP send failed: {ex.Message}", IStatusProvider.StatusLevel.Warning);
        }
    }

    private void SendPeerType()
    {
        NetworkStream? stream;
        lock (_connectionLock) stream = _tcpStream;
        if (stream == null)
            return;

        try
        {
            using var ms = new MemoryStream();
            using var writer = new BinaryWriter(ms);

            WriteCitpHeader(writer, PinFCookie);
            WriteCookieBytes(writer, PtypCookie);
            writer.Write(Encoding.ASCII.GetBytes("Laser\0"));

            SendTcpMessage(stream, ms);
        }
        catch (Exception ex)
        {
            StopTcpConnection();
            SetStatus($"CITP send failed: {ex.Message}", IStatusProvider.StatusLevel.Warning);
        }
    }

    private const uint PnamCookie = 0x504E616D; // "PNam"
    private const uint PtypCookie = 0x50547970; // "PTyp"

    /// <summary>
    /// The feed list: source key, feed count, then one UCS-2 name per feed. One source, N feeds -
    /// the receiver numbers its "Physical Output"s by the list position, so the names carry no
    /// index of their own.
    /// </summary>
    private void SendLaserFeedList()
    {
        NetworkStream? stream;
        lock (_connectionLock) stream = _tcpStream;
        if (stream == null)
            return;

        try
        {
            CitpLaserFeed[] feeds;
            lock (_feedsLock)
                feeds = _feeds.ToArray();

            using var ms = new MemoryStream();
            using var writer = new BinaryWriter(ms);

            WriteCitpHeader(writer, CaexCookie);
            writer.Write((uint)CaexContentType.LaserFeedList);

            writer.Write(_sourceKey);
            writer.Write((byte)feeds.Length);
            foreach (var feed in feeds)
                writer.Write(Encoding.Unicode.GetBytes($"{feed.Name}\0"));

            SendTcpMessage(stream, ms);
        }
        catch (Exception ex)
        {
            StopTcpConnection();
            SetStatus($"CITP send failed: {ex.Message}", IStatusProvider.StatusLevel.Warning);
        }
    }

    /// <summary>The capture asks a feed to start/stop sending frames: FeedIndex (1) + FrameRate (1)
    /// after the 24-byte CAEX header. FrameRate 0 stops the feed.</summary>
    private void HandleLaserFeedControl(byte[] buffer, int offset, int length)
    {
        if (length < 26)
            return;

        var feedIndex = buffer[offset + 24];
        var frameRate = buffer[offset + 25];

        CitpLaserFeed? feed;
        lock (_feedsLock)
            feed = feedIndex < _feeds.Count ? _feeds[feedIndex] : null;

        if (feed == null)
        {
            // The receiver asks for a feed the list no longer carries (an adapter rebuilt
            // mid-session): re-announce the current list so it re-binds.
            SendLaserFeedList();
            return;
        }

        feed.RequiredFps = frameRate;
        feed.Active = frameRate != 0;

        SetStatus($"Feed {feedIndex + 1}/{_feeds.Count} '{feed.Name}' "
                  + $"{(frameRate != 0 ? $"active at {frameRate} fps" : "stopped")}",
                  IStatusProvider.StatusLevel.Success);
        Log.Debug($"CITP: feed {feedIndex + 1} '{feed.Name}' {(frameRate != 0 ? $"active at {frameRate} fps" : "stopped")}");
    }

    private void SendTcpMessage(NetworkStream stream, MemoryStream ms)
    {
        var data = ms.ToArray();
        var length = (uint)data.Length;
        // Patch MessageSize into the CITP header (bytes 8-11, little-endian)
        data[8] = (byte)length;
        data[9] = (byte)(length >> 8);
        data[10] = (byte)(length >> 16);
        data[11] = (byte)(length >> 24);

        // The TCP receive thread and any handshake triggered on connect share this stream.
        lock (stream)
            stream.Write(data, 0, data.Length);

        Volatile.Write(ref _lastPeerActivityTicks, Environment.TickCount64);
    }

    // ---------------------------------------------------------------- frame sending

    /// <summary>Runs for the lifetime of one control session: a reconnect starts a new loop, so
    /// this one exits once <paramref name="client"/> is no longer the live connection. Paces every
    /// active feed at its requested fps; the newest queued frame wins - one that waited behind a
    /// newer frame is already out of date.</summary>
    private void SendLoop(CancellationToken token, TcpClient client)
    {
        try
        {
            using var udpSender = new UdpClient();
            var target = new IPEndPoint(MulticastAddress, MulticastPort);
            var lastAnnounceUtc = DateTime.UtcNow;

            while (!token.IsCancellationRequested && Volatile.Read(ref _disposed) == 0
                   && ReferenceEquals(Volatile.Read(ref _tcpClient), client))
            {
                CitpLaserFeed[] feeds;
                lock (_feedsLock)
                    feeds = _feeds.ToArray();

                var sentAny = false;
                foreach (var feed in feeds)
                {
                    // Keepalive: CITP is a previz path that never drives projectors, so with the
                    // feed's keepalive on it keeps streaming while the LASER master is off -
                    // settled blanking, geometry moving, zero light.
                    var masterOff = !LaserMasterEnable.Enabled && !feed.Keepalive;
                    if (masterOff)
                    {
                        feed.Frames.Clear();
                        if (feed.Active && feed.LastFrame is { Length: > 0 } lastFrame)
                        {
                            var blanked = LaserOutputStream.BlankFrame(lastFrame, ref feed.BlankScratch);
                            try
                            {
                                SendLaserFeedFrame(udpSender, feed, blanked, target);
                                sentAny = true;
                            }
                            catch { /* transient socket errors retry on the next tick */ }
                        }

                        continue;
                    }

                    if (!feed.Active || feed.Frames.IsEmpty)
                        continue;

                    var now = System.Diagnostics.Stopwatch.GetTimestamp();
                    if (now < feed.NextSendTimestamp)
                        continue;

                    LaserPoint[]? newest = null;
                    while (feed.Frames.TryDequeue(out var queued))
                        newest = queued;

                    if (newest == null)
                        continue;

                    feed.LastFrame = newest;
                    var interval = feed.RequiredFps > 0
                                       ? System.Diagnostics.Stopwatch.Frequency / feed.RequiredFps
                                       : 0;
                    feed.NextSendTimestamp = Math.Max(feed.NextSendTimestamp + interval, now);
                    try
                    {
                        SendLaserFeedFrame(udpSender, feed, newest, target);
                        sentAny = true;
                    }
                    catch (Exception ex)
                    {
                        SetStatus($"CITP frame send failed: {ex.Message}", IStatusProvider.StatusLevel.Warning);
                    }
                }

                if (!sentAny)
                    Thread.Sleep(1);

                // Re-announce the feed list: if Capture restarted (or joined late) it re-lists
                // the feeds without anyone pressing anything.
                if (IsConnected && DateTime.UtcNow - lastAnnounceUtc > ReannounceInterval)
                {
                    lastAnnounceUtc = DateTime.UtcNow;
                    SendLaserFeedList();
                }
            }
        }
        catch (Exception e) when (!token.IsCancellationRequested)
        {
            // A manually created thread must never let an exception escape: it would take the
            // whole editor down. Treat it as session loss and let the reconnect loop take over.
            if (Volatile.Read(ref _disposed) == 0)
            {
                SetStatus($"CITP send loop failed: {e.Message}", IStatusProvider.StatusLevel.Warning);
                Log.Warning($"CITP send loop failed - {e.Message}");
                StopTcpConnection();
            }
        }
    }

    /// <summary>Reusable datagram buffer, send thread only - the largest LaserFeedFrame this
    /// source can produce: 35-byte header block + 5 bytes per point at the datagram cap.</summary>
    private readonly byte[] _sendBuffer = new byte[35 + MaxPointsPerDatagram * 5];

    private void SendLaserFeedFrame(UdpClient udpSender, CitpLaserFeed feed, LaserPoint[] points, IPEndPoint target)
    {
        if (points.Length == 0)
            return;

        var pointCount = Math.Min(points.Length, MaxPointsPerDatagram);

        try
        {
            var data = _sendBuffer;

            // CITP header (20 bytes): cookie, version, request index, size placeholder,
            // part count/part, content type.
            var citpCookie = CitpCookie;
            var caexCookie = CaexCookie;
            data[0] = (byte)(citpCookie >> 24);
            data[1] = (byte)(citpCookie >> 16);
            data[2] = (byte)(citpCookie >> 8);
            data[3] = (byte)citpCookie;
            data[4] = 1;                    // VersionMajor
            data[5] = 0;                    // VersionMinor
            data[6] = 0;                    // RequestIndex
            data[7] = 0;
            // bytes 8-11: MessageSize, patched once the length is known
            data[12] = 1;                   // MessagePartCount
            data[13] = 0;
            data[14] = 0;                   // MessagePart
            data[15] = 0;
            data[16] = (byte)(caexCookie >> 24);
            data[17] = (byte)(caexCookie >> 16);
            data[18] = (byte)(caexCookie >> 8);
            data[19] = (byte)caexCookie;

            // Layer content code (little-endian)
            var contentCode = (uint)CaexContentType.LaserFeedFrame;
            data[20] = (byte)contentCode;
            data[21] = (byte)(contentCode >> 8);
            data[22] = (byte)(contentCode >> 16);
            data[23] = (byte)(contentCode >> 24);

            // LaserFeedFrame body: sourceKey + feedIndex + frameSequence + pointCount
            var sourceKey = _sourceKey;
            data[24] = (byte)sourceKey;
            data[25] = (byte)(sourceKey >> 8);
            data[26] = (byte)(sourceKey >> 16);
            data[27] = (byte)(sourceKey >> 24);
            data[28] = (byte)feed.Index;

            var sequence = feed.SequenceNo++;
            data[29] = (byte)sequence;
            data[30] = (byte)(sequence >> 8);
            data[31] = (byte)(sequence >> 16);
            data[32] = (byte)(sequence >> 24);
            data[33] = (byte)pointCount;
            data[34] = (byte)(pointCount >> 8);

            var maxBrightness = feed.MaxBrightness;
            var swapColor = feed.SwapColorChannels;
            var offset = 35;
            for (var i = 0; i < pointCount; i++)
            {
                // ConvertLaserPoint inlined: ILDA 16-bit signed -> 12-bit unsigned, RGB565 color
                var p = points[i];
                if (p.I <= 0)
                    p = LaserPoint.CreateBlanked(p.X, p.Y); // CITP has no intensity channel: I = 0 must mean no colour

                var xNorm = Math.Clamp(p.X / 32767f, -1f, 1f);
                var yNorm = Math.Clamp(p.Y / 32767f, -1f, 1f);
                var x = Math.Clamp((int)((xNorm + 1f) * 0.5f * 4095f), 0, 4095);
                var y = Math.Clamp((int)((yNorm + 1f) * 0.5f * 4095f), 0, 4095);

                var r = (byte)Math.Clamp((int)MathF.Round(Math.Max(swapColor ? p.B : p.R, 0) * maxBrightness) >> 8, 0, 255);
                var g = (byte)Math.Clamp((int)MathF.Round(Math.Max(p.G, 0) * maxBrightness) >> 8, 0, 255);
                var b = (byte)Math.Clamp((int)MathF.Round(Math.Max(swapColor ? p.R : p.B, 0) * maxBrightness) >> 8, 0, 255);
                var color = (ushort)(((r & 0xF8) << 8) | ((g & 0xFC) << 5) | ((b & 0xF8) >> 3));

                data[offset] = (byte)x;
                data[offset + 1] = (byte)y;
                data[offset + 2] = (byte)(((x >> 8) & 0x0F) | (((y >> 8) & 0x0F) << 4));
                data[offset + 3] = (byte)color;
                data[offset + 4] = (byte)(color >> 8);
                offset += 5;
            }

            var length = offset;
            data[8] = (byte)length;
            data[9] = (byte)(length >> 8);
            data[10] = (byte)(length >> 16);
            data[11] = (byte)(length >> 24);

            udpSender.Send(data, length, target);
        }
        catch (Exception ex)
        {
            SetStatus($"CITP frame send failed: {ex.Message}", IStatusProvider.StatusLevel.Warning);
        }
    }

    // ---------------------------------------------------------------- helpers

    #region Helpers - CITP is little-endian ("All fields use little endian byte order" per spec); cookies are ASCII
    private static void WriteCitpHeader(BinaryWriter writer, uint contentType)
    {
        // CITP header: 20 bytes total
        WriteCookieBytes(writer, CitpCookie);   // bytes 0-3: "CITP"
        writer.Write((byte)1);                  // byte 4: VersionMajor
        writer.Write((byte)0);                  // byte 5: VersionMinor
        writer.Write((ushort)0);                // bytes 6-7: RequestIndex
        writer.Write((uint)0);                  // bytes 8-11: MessageSize placeholder
        writer.Write((ushort)1);                // bytes 12-13: MessagePartCount
        writer.Write((ushort)0);                // bytes 14-15: MessagePart
        WriteCookieBytes(writer, contentType);  // bytes 16-19: ContentType
    }

    private static void WriteCookieBytes(BinaryWriter writer, uint cookie)
    {
        writer.Write((byte)((cookie >> 24) & 0xFF));
        writer.Write((byte)((cookie >> 16) & 0xFF));
        writer.Write((byte)((cookie >> 8) & 0xFF));
        writer.Write((byte)(cookie & 0xFF));
    }

    private static uint ReadLittleEndianUint(byte[] buffer, int offset)
    {
        return buffer[offset]
               | ((uint)buffer[offset + 1] << 8)
               | ((uint)buffer[offset + 2] << 16)
               | ((uint)buffer[offset + 3] << 24);
    }

    private static uint ReadBigEndianUint(byte[] buffer, int offset)
    {
        return ((uint)buffer[offset] << 24)
               | ((uint)buffer[offset + 1] << 16)
               | ((uint)buffer[offset + 2] << 8)
               | buffer[offset + 3];
    }
    #endregion

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        CitpPeerDiscovery.PeerAnnounced -= OnPeerAnnounced;
        CancellationTokenSource? cts;
        lock (_connectionLock)
            cts = _cts;

        try { cts?.Cancel(); }
        catch (ObjectDisposedException) { /* the reconnect task already finished */ }

        lock (_connectionLock)
        {
            try { _tcpStream?.Close(); } catch { /* ignore */ }
            _tcpStream = null;

            try { _tcpClient?.Close(); } catch { /* ignore */ }
            _tcpClient = null;

            _cts = null;
        }

        lock (_feedsLock)
        {
            foreach (var feed in _feeds)
            {
                feed.Active = false;
                feed.Frames.Clear();
            }

            _feeds.Clear();
        }

        SetStatus("Disconnected", IStatusProvider.StatusLevel.Notice);

        // Only the hub that still matches its registry key may remove itself: a rebuilt hub for
        // the same target could otherwise evict the live one.
        var key = $"{_directHost}|{_directPort}|{_localAddress}";
        ((ICollection<KeyValuePair<string, CitpLaserHub>>)Hubs).Remove(new KeyValuePair<string, CitpLaserHub>(key, this));
    }
}
