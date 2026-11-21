#nullable enable
using System.Collections.Concurrent;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DataChannelDotnet;
using DataChannelDotnet.Bindings;
using DataChannelDotnet.Data;
using DataChannelDotnet.Impl;
using Newtonsoft.Json.Linq;
using T3.Core.Utils;
using T3.Editor.Gui.UiHelpers;

namespace T3.Editor.Collab;

/// <summary>
/// Peer-to-peer data path for relay sessions. The relay connection is opened exactly as before and
/// keeps carrying the handshake; in parallel both ends negotiate a WebRTC peer connection over
/// <see cref="CollabProtocol.FrameType.Signal"/> frames, which the relay routes without inspecting.
///
/// Once the session has authenticated a peer, its collab frames move to the data channel and the
/// relay stream stays open as a fallback. If the data channel drops, the peer silently returns to
/// the relay path; the session never notices a transport change.
/// </summary>
internal static class CollabWebRtc
{
    /// <summary>The data channel is binary and ordered: collab frames are a byte stream, not messages.</summary>
    public const string DataChannelLabel = "collab";

    /// <summary>Video is a negotiated H.264 media track, not a data channel.</summary>
    public const string VideoTrackMid = "video";

    /// <summary>Conservative SCTP payload per message; the negotiated maximum is at least 64 KiB everywhere.</summary>
    public const int DataChannelChunkSize = 48 * 1024;

    /// <summary>STUN is the only NAT traversal the client can use without a TURN server; ICE TCP is enabled for hostile UDP networks.</summary>
    public const string DefaultIceServer = "stun:stun.l.google.com:19302";

    /// <summary>Set false once native loading failed, so a broken install is reported once instead of per join.</summary>
    private static int _nativeFailureReported;

    public static RtcPeerConfiguration CreateConfiguration(IEnumerable<string>? iceServers)
    {
        var configuration = new RtcPeerConfiguration
                                {
                                    // The application calls SetLocalDescription instead: creating the track and the
                                    // data channel must both be in the first offer, not race an automatic renegotiation.
                                    DisableAutoNegotiation = true,
                                    EnableIceTcp = true,
                                };

        var servers = new List<string>();
        foreach (var server in iceServers ?? [])
        {
            if (!string.IsNullOrWhiteSpace(server))
                servers.Add(server.Trim());
        }

        if (servers.Count == 0)
            servers.Add(DefaultIceServer);

        configuration.IceServers = servers;
        return configuration;
    }

    /// <summary>
    /// Creates a peer connection, returning null after logging once when the native library is not
    /// available (macOS arm64 has no bundled binary). Callers then stay on the relay.
    /// </summary>
    public static RtcPeerConnection? TryCreatePeer(RtcPeerConfiguration configuration)
    {
        try
        {
            return new RtcPeerConnection(configuration);
        }
        catch (Exception e) when (e is DllNotFoundException or TypeInitializationException or FileNotFoundException)
        {
            if (Interlocked.Exchange(ref _nativeFailureReported, 1) == 0)
                Log.Warning($"Collab: WebRTC unavailable on this platform, staying on the relay ({e.Message})");

            return null;
        }
    }
}

/// <summary>
/// One peer's WebRTC connection: signaling, the data channel, and the optional H.264 send track.
/// All callbacks run on libdatachannel threads; the transport-facing state is reference assignments
/// and a lock, so the UI thread can read it concurrently.
/// </summary>
internal sealed class CollabRtcLink : IDisposable
{
    public CollabPeerConnection Peer { get; }

    /// <summary>True once the data channel is open and the peer is actually using it.</summary>
    public bool IsActive => Volatile.Read(ref _active) != 0;

    /// <summary>True while this peer has a negotiated video track that the host can write to.</summary>
    public bool HasVideoTrack => _videoTrack is { IsOpen: true };

    /// <summary>Raw RTP packets of the host's preview, raised on a libdatachannel thread (client side only).</summary>
    public event Action<byte[]>? VideoRtpReceived;

    private readonly RtcPeerConnection _connection;
    private readonly bool _isOfferer;
    private readonly bool _sendVideo;
    private readonly bool _receiveVideo;
    private readonly Action<byte[]> _sendSignal;
    private readonly Action<CollabPeerConnection, Stream> _readFrames;
    private readonly Action<CollabRtcLink> _onPathFailed;
    private readonly object _sendLock = new();

    /** Video writes can block natively on a slow viewer; keeping them off _sendLock means presence and
        heartbeats never queue behind them on the UI thread. */
    private readonly object _videoSendLock = new();
    private readonly List<RtcCandidate> _pendingCandidates = [];
    private readonly uint _videoSsrc = (uint)Random.Shared.Next(1, int.MaxValue);

    private IRtcDataChannel? _channel;
    private CollabDataChannelStream? _stream;
    private IRtcTrack? _videoTrack;
    private bool _remoteDescriptionSet;
    private int _started;
    private int _activationRequested;
    private int _active;
    private int _failed;

    public CollabRtcLink(CollabPeerConnection peer,
                         bool isOfferer,
                         bool sendVideo,
                         bool receiveVideo,
                         Action<byte[]> sendSignal,
                         Action<CollabPeerConnection, Stream> readFrames,
                         Action<CollabRtcLink> onPathFailed)
    {
        Peer = peer;
        _isOfferer = isOfferer;
        _sendVideo = sendVideo;
        _receiveVideo = receiveVideo;
        _sendSignal = sendSignal;
        _readFrames = readFrames;
        _onPathFailed = onPathFailed;

        var configuration = CollabWebRtc.CreateConfiguration(UserSettings.Config.CollabWebRtcIceServers?
                                                                         .Split(',', StringSplitOptions.RemoveEmptyEntries));
        _connection = CollabWebRtc.TryCreatePeer(configuration)
                      ?? throw new InvalidOperationException("WebRTC is not available");

        _connection.OnLocalDescriptionSafe += OnLocalDescription;
        _connection.OnCandidateSafe += OnLocalCandidate;
        _connection.OnConnectionStateChange += OnConnectionStateChanged;
        _connection.OnDataChannel += (_, channel) => AttachChannel(channel);
        _connection.OnTrack += OnTrack;
    }

    /// <summary>Starts negotiation. The offerer includes the data channel and video track in its single offer.</summary>
    public void Start()
    {
        if (Interlocked.Exchange(ref _started, 1) != 0)
            return;

        try
        {
            if (_sendVideo)
                CreateVideoTrack();

            if (_isOfferer)
            {
                var channel = _connection.CreateDataChannel(new RtcCreateDataChannelArgs
                                                                {
                                                                    Label = CollabWebRtc.DataChannelLabel,
                                                                    Protocol = RtcDataChannelProtocol.Binary,
                                                                });
                AttachChannel(channel);
                _connection.SetLocalDescription(RtcDescriptionType.Offer);
            }
        }
        catch (Exception e)
        {
            Fail($"could not start the peer connection: {e.Message}");
        }
    }

    /// <summary>Called by the session after the handshake. The channel may still be opening here.</summary>
    public void EnablePeerToPeer()
    {
        Interlocked.Exchange(ref _activationRequested, 1);
        TryActivate();
    }

    public void HandleSignal(byte[] payload)
    {
        // A host link stays idle until its member authenticated; signals before that are not for us.
        if (Volatile.Read(ref _started) == 0)
            return;

        if (payload.Length == 0 || payload.Length > CollabProtocol.MaxSignalPayload)
            return;

        JObject json;
        try
        {
            json = JObject.Parse(Encoding.UTF8.GetString(payload));
        }
        catch (Newtonsoft.Json.JsonException)
        {
            Log.Debug("Collab: ignored a malformed WebRTC signal");
            return;
        }

        try
        {
            var type = json["t"]?.Value<string>() ?? "";
            switch (type)
            {
                case "offer" when !_isOfferer:
                    _connection.SetRemoteDescription(new RtcDescription
                                                          {
                                                              Sdp = json["sdp"]?.Value<string>() ?? "",
                                                              Type = RtcDescriptionType.Offer,
                                                          });
                    _remoteDescriptionSet = true;
                    FlushPendingCandidates();
                    _connection.SetLocalDescription(RtcDescriptionType.Answer);
                    break;

                case "answer" when _isOfferer:
                    _connection.SetRemoteDescription(new RtcDescription
                                                          {
                                                              Sdp = json["sdp"]?.Value<string>() ?? "",
                                                              Type = RtcDescriptionType.Answer,
                                                          });
                    _remoteDescriptionSet = true;
                    FlushPendingCandidates();
                    break;

                case "ice":
                {
                    var candidate = new RtcCandidate
                                        {
                                            Content = json["candidate"]?.Value<string>() ?? "",
                                            Mid = json["mid"]?.Value<string>() ?? "",
                                        };

                    if (!_remoteDescriptionSet)
                        _pendingCandidates.Add(candidate);
                    else
                        _connection.AddRemoteCandidate(candidate);
                    break;
                }
            }
        }
        catch (Exception e)
        {
            Fail($"signaling failed: {e.Message}");
        }
    }

    /// <summary>The data channel's fast path for collab frames; false lets the relay stream take over.</summary>
    public bool TrySend(CollabProtocol.FrameType type, ReadOnlySpan<byte> payload)
    {
        var stream = _stream;
        if (stream == null || !IsActive || _channel is not { IsOpen: true })
            return false;

        try
        {
            // Never wait for a data-channel write on the caller's (UI) thread. A short attempt is enough
            // for a healthy link; otherwise the caller falls back to the relay.
            if (!Monitor.TryEnter(_sendLock, 50))
                return false;

            try
            {
                CollabProtocol.WriteFrame(stream, type, payload);
            }
            finally
            {
                Monitor.Exit(_sendLock);
            }

            return true;
        }
        catch (Exception e) when (e is IOException or ObjectDisposedException)
        {
            Fail($"write failed: {e.Message}");
            return false;
        }
    }

    /// <summary>Writes one Annex-B access unit to every open video track. Host side only.</summary>
    public void SendVideo(byte[] annexB)
    {
        var track = _videoTrack;
        if (track == null || !track.IsOpen)
            return;

        try
        {
            lock (_videoSendLock)
            {
                track.Timestamp = (uint)(_videoClock.Elapsed.TotalSeconds * 90_000);
                track.Write(annexB);
            }
        }
        catch (Exception e)
        {
            Log.Debug($"Collab: video write to {Peer} failed: {e.Message}");
        }
    }

    /// <summary>Requests a keyframe on the next encoded frame after a new viewer joined.</summary>
    public void RequestKeyFrame() => Interlocked.Exchange(ref _keyFrameRequested, 1);

    public bool ConsumeKeyFrameRequest() => Interlocked.Exchange(ref _keyFrameRequested, 0) != 0;

    public string Describe()
    {
        if (IsActive)
            return "P2P";

        return Volatile.Read(ref _failed) != 0 ? "" : "P2P...";
    }

    private void CreateVideoTrack()
    {
        _videoTrack = _connection.CreateTrack(new RtcCreateTrackArgs
                                                  {
                                                      Direction = rtcDirection.RTC_DIRECTION_SENDONLY,
                                                      Codec = rtcCodec.RTC_CODEC_H264,
                                                      PayloadType = 96,
                                                      Ssrc = _videoSsrc,
                                                      Mid = CollabWebRtc.VideoTrackMid,
                                                      Name = CollabWebRtc.VideoTrackMid,
                                                      Msid = "tixl",
                                                      TrackId = CollabWebRtc.VideoTrackMid,
                                                  });

        _videoTrack.AddH264Packetizer(new RtcPacketizerInitArgs
                                          {
                                              Ssrc = (int)_videoSsrc,
                                              Cname = $"tixl-{Guid.NewGuid():N}"[..16],
                                              PayloadType = 96,
                                              Clockrate = 90_000,
                                              MaxFragmentSize = 1200,
                                              NalUnitSeparator = rtcNalUnitSeparator.RTC_NAL_SEPARATOR_LONG_START_SEQUENCE,
                                          });

        // A viewer that lost its stream asks for a fresh IDR; the encoder honors it on the next frame.
        _videoTrack.SetPliHandler(RequestKeyFrame);
        _videoTrack.OnOpen += _ => RequestKeyFrame();
    }

    private void AttachChannel(IRtcDataChannel channel)
    {
        if (Interlocked.Exchange(ref _channelAttached, 1) != 0)
            return;

        _channel = channel;
        _stream = new CollabDataChannelStream(channel, CollabWebRtc.DataChannelChunkSize,
                                              () => Fail("the peer sent more data than the session can buffer"));
        channel.OnClose += _ => Fail("the data channel closed");
        channel.OnError += (_, error) => Fail($"data channel error: {error}");
        channel.OnOpen += _ => TryActivate();

        TryActivate();
    }

    private void TryActivate()
    {
        if (Volatile.Read(ref _activationRequested) == 0 || Volatile.Read(ref _failed) != 0)
            return;

        if (_channel is not { IsOpen: true } || _stream == null)
            return;

        if (Interlocked.Exchange(ref _active, 1) != 0)
            return;

        Peer.PreferredSender = TrySend;
        _stallTimer ??= new Timer(_ => CheckForStall(), null, StallCheckIntervalMs, StallCheckIntervalMs);
        _ = Task.Run(ReadRtcLoop);
    }

    /// <summary>
    /// A data channel can go silent without closing (a suspended laptop, a dead NAT mapping). Collab
    /// heartbeats arrive every second, so a longer gap means the path is dead; falling back here avoids
    /// the session timeout that would otherwise drop the peer before the relay could take over.
    /// </summary>
    private void CheckForStall()
    {
        if (!IsActive)
            return;

        if (Environment.TickCount64 - Peer.LastHeardFromTicks > StallTimeoutMs)
            Fail("the data channel stalled");
    }

    private void ReadRtcLoop()
    {
        var stream = _stream;
        if (stream == null)
            return;

        _readFrames(Peer, stream);
        Fail("the data channel ended");
    }

    private void OnLocalDescription(IRtcPeerConnection _, RtcDescription description)
    {
        if (_isOfferer && description.Type == RtcDescriptionType.Offer)
            _sendSignal(Signal("offer", description.Sdp));
        else if (!_isOfferer && description.Type == RtcDescriptionType.Answer)
            _sendSignal(Signal("answer", description.Sdp));
    }

    private void OnLocalCandidate(IRtcPeerConnection _, RtcCandidate candidate)
    {
        if (string.IsNullOrEmpty(candidate.Content))
            return;

        var json = new JObject
                       {
                           ["t"] = "ice",
                           ["candidate"] = candidate.Content,
                           ["mid"] = candidate.Mid ?? "",
                       };

        _sendSignal(Encoding.UTF8.GetBytes(json.ToString(Newtonsoft.Json.Formatting.None)));
    }

    private void OnConnectionStateChanged(IRtcPeerConnection _, rtcState state)
    {
        if (state is rtcState.RTC_FAILED or rtcState.RTC_CLOSED)
            Fail($"peer connection {state}");
    }

    private void OnTrack(IRtcPeerConnection _, IRtcTrack track)
    {
        if (!_receiveVideo)
            return;

        // No depacketizer is exposed through the binding: the track delivers RTP packets.
        track.OnBinaryReceivedSafe += (_, message) => VideoRtpReceived?.Invoke(message.Data.ToArray());
    }

    private void FlushPendingCandidates()
    {
        foreach (var candidate in _pendingCandidates)
        {
            try
            {
                _connection.AddRemoteCandidate(candidate);
            }
            catch (Exception e)
            {
                Log.Debug($"Collab: ignored a stale ICE candidate: {e.Message}");
            }
        }

        _pendingCandidates.Clear();
    }

    private void Fail(string reason)
    {
        if (Interlocked.Exchange(ref _failed, 1) != 0)
            return;

        Interlocked.Exchange(ref _active, 0);
        Peer.PreferredSender = null;

        // Unblock the data-channel reader: without this a failed link left one thread parked in
        // TryTake(Timeout.Infinite) until the peer was removed.
        _stream?.Close();

        if (Volatile.Read(ref _disposing) != 0)
            return;

        Log.Info($"Collab: WebRTC path to {Peer} failed ({reason}), using the relay");
        _onPathFailed(this);
    }

    private static byte[] Signal(string type, string sdp) =>
        Encoding.UTF8.GetBytes(new JObject { ["t"] = type, ["sdp"] = sdp }.ToString(Newtonsoft.Json.Formatting.None));

    private const int StallCheckIntervalMs = 1000;
    private const int StallTimeoutMs = 3000;

    private readonly System.Diagnostics.Stopwatch _videoClock = System.Diagnostics.Stopwatch.StartNew();
    private int _channelAttached;
    private int _keyFrameRequested;
    private int _disposing;
    private Timer? _stallTimer;

    public void Dispose()
    {
        // A planned shutdown is not a path failure: no log line, and no fallback handling.
        Interlocked.Exchange(ref _disposing, 1);
        Fail("disposed");
        _stallTimer?.Dispose();
        _stallTimer = null;
        try
        {
            _connection.Dispose();
        }
        catch (Exception e)
        {
            Log.Debug($"Collab: disposing a peer connection failed: {e.Message}");
        }
    }
}

/// <summary>
/// A <see cref="Stream"/> over a reliable ordered data channel. The collab protocol frames are a byte
/// stream, so writes are split into SCTP-sized binary messages and reads concatenate them again.
/// </summary>
internal sealed class CollabDataChannelStream : Stream
{
    /** Several maximum-size frames; a peer past this is flooding, not sending a session. */
    private const int MaxQueuedBytes = 4 * CollabProtocol.MaxFrameSize;

    private readonly IRtcDataChannel _channel;
    private readonly int _chunkSize;
    private readonly Action _onOverflow;
    private readonly BlockingCollection<byte[]> _incoming = new(new ConcurrentQueue<byte[]>());

    private byte[] _current = [];
    private int _currentOffset;
    private long _queuedBytes;
    private volatile bool _closed;

    /// <param name="onOverflow">Raised once when the unread data exceeds the limit; the stream is closed first.
    /// Data arrives before the session starts reading, so the queue is bounded rather than the sender trusted.</param>
    public CollabDataChannelStream(IRtcDataChannel channel, int chunkSize, Action onOverflow)
    {
        _channel = channel;
        _chunkSize = Math.Max(1024, chunkSize);
        _onOverflow = onOverflow;
        channel.OnBinaryReceivedSafe += OnBinaryReceived;
        channel.OnClose += _ => CloseStream();
        if (channel.IsClosed)
            CloseStream();
    }

    private void OnBinaryReceived(IRtcDataChannel _, DataChannelDotnet.Events.RtcBinaryReceivedEventSafe message)
    {
        if (_closed || message.Data.Length == 0)
            return;

        var length = message.Data.Length;
        if (Interlocked.Add(ref _queuedBytes, length) > MaxQueuedBytes)
        {
            Interlocked.Add(ref _queuedBytes, -length);
            CloseStream();
            _onOverflow();
            return;
        }

        try
        {
            _incoming.Add(message.Data.ToArray());
        }
        catch (InvalidOperationException)
        {
            // The consumer was disposed between the check and the add.
            Interlocked.Add(ref _queuedBytes, -length);
        }
    }

    private void CloseStream()
    {
        _closed = true;
        try
        {
            _incoming.CompleteAdding();
        }
        catch (ObjectDisposedException)
        {
            // Already completed by Dispose.
        }
    }

    public override int Read(Span<byte> buffer)
    {
        while (true)
        {
            if (_currentOffset < _current.Length)
            {
                var count = Math.Min(buffer.Length, _current.Length - _currentOffset);
                _current.AsSpan(_currentOffset, count).CopyTo(buffer);
                _currentOffset += count;
                return count;
            }

            try
            {
                // Chunks received before the close are still delivered; TryTake only fails once the
                // queue is completed and empty.
                if (!_incoming.TryTake(out var next, Timeout.Infinite))
                    return 0;

                Interlocked.Add(ref _queuedBytes, -next.Length);
                _current = next;
                _currentOffset = 0;
            }
            catch (Exception e) when (e is InvalidOperationException or ObjectDisposedException)
            {
                return 0;
            }
        }
    }

    public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

    public override void Write(ReadOnlySpan<byte> buffer)
    {
        while (buffer.Length > 0)
        {
            var count = Math.Min(_chunkSize, buffer.Length);
            _channel.Send(buffer[..count]);
            buffer = buffer[count..];
        }
    }

    public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));

    public override void Flush()
    {
    }

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => true;
    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();

    /// <summary>Wakes a reader parked in <see cref="Read(Span{byte})"/> without disposing the queue under it.</summary>
    public void Close() => CloseStream();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            CloseStream();
            _incoming.Dispose();
        }

        base.Dispose(disposing);
    }
}

/// <summary>
/// Host transport: the relay host link plus one WebRTC peer connection per member. Members join on
/// the relay, negotiate over it, and switch their frames to the data channel after authentication.
/// </summary>
internal sealed class CollabWebRtcHostTransport : CollabRelayHostTransport, ICollabVideoSink
{
    /// <summary>The video sink's view of <see cref="VideoViewerCount"/>.</summary>
    public int ViewerCount => VideoViewerCount;

    /// <summary>Opened video tracks across all peers, so the encoder knows whether anyone is watching.</summary>
    public int VideoViewerCount
    {
        get
        {
            var count = 0;
            foreach (var link in _links.Values)
            {
                if (link.HasVideoTrack && link.IsActive)
                    count++;
            }

            return count;
        }
    }

    private readonly ConcurrentDictionary<CollabPeerConnection, CollabRtcLink> _links = new();

    private CollabWebRtcHostTransport(TcpClient relayClient, string roomCode)
        : base(relayClient, roomCode)
    {
    }

    public static new async Task<CollabWebRtcHostTransport> ConnectAsync(string relayHost, int relayPort, string roomCode,
                                                                     CancellationToken cancellation,
                                                                     CollabRelayHello? hello = null)
    {
        var (client, room) = await OpenRoomAsync(relayHost, relayPort, roomCode, cancellation, hello);
        try
        {
            return new CollabWebRtcHostTransport(client, room);
        }
        catch
        {
            client.Dispose();
            throw;
        }
    }

    protected override void OnMemberJoined(CollabPeerConnection peer)
    {
        try
        {
            var link = new CollabRtcLink(peer,
                                         isOfferer: true,
                                         sendVideo: UserSettings.Config.CollabSharePreview,
                                         receiveVideo: false,
                                         sendSignal: payload => SendSignalOverRelay(peer, payload),
                                         readFrames: ReadFrameLoop,
                                         onPathFailed: _ => { });
            _links[peer] = link;
        }
        catch (Exception e)
        {
            // Without WebRTC the member still joins over the relay; only the faster path is lost.
            Log.Debug($"Collab: WebRTC unavailable for {peer}: {e.Message}");
        }
    }

    protected override void DispatchRelayFrame(ReadOnlySpan<byte> frame)
    {
        if (frame.Length < 2)
            return;

        var type = (CollabProtocol.FrameType)frame[1];
        if (type == CollabProtocol.FrameType.Signal)
        {
            var signalPeer = FindPeer(frame[0]);
            if (signalPeer != null && _links.TryGetValue(signalPeer, out var link))
                link.HandleSignal(frame[2..].ToArray());
            return;
        }

        // Once a member is on its own channel, its relay slot only carries keepalives. Anything else
        // would be a duplicate that could overwrite newer state.
        var peer = FindPeer(frame[0]);
        if (peer != null && _links.TryGetValue(peer, out var activeLink) && activeLink.IsActive
            && type != CollabProtocol.FrameType.Heartbeat)
            return;

        base.DispatchRelayFrame(frame);
    }

    protected override void OnMemberRemoved(CollabPeerConnection peer)
    {
        if (_links.TryRemove(peer, out var link))
            link.Dispose();
    }

    /// <summary>
    /// The offer carries this machine's local and public addresses, so it only goes out once the member
    /// passed authentication; before that, anyone holding the room code would learn them.
    /// </summary>
    public override void EnablePeerToPeer(CollabPeerConnection peer)
    {
        if (!_links.TryGetValue(peer, out var link))
            return;

        link.Start();
        link.EnablePeerToPeer();
    }

    public override string DescribePeerLink(CollabPeerConnection peer) =>
        _links.TryGetValue(peer, out var link) ? link.Describe() : "";

    /// <summary>Offers one encoded Annex-B access unit to every member with an open video track.</summary>
    public void SendVideo(byte[] annexB)
    {
        foreach (var link in _links.Values)
            link.SendVideo(annexB);
    }

    /// <summary>True when the next frame must be an IDR (a viewer just joined or asked for one).</summary>
    public bool ConsumeKeyFrameRequest()
    {
        var requested = false;
        foreach (var link in _links.Values)
            requested |= link.ConsumeKeyFrameRequest();

        return requested;
    }

    public override void Dispose()
    {
        foreach (var link in _links.Values)
            link.Dispose();
        _links.Clear();
        base.Dispose();
    }

    private void SendSignalOverRelay(CollabPeerConnection peer, byte[] payload) =>
        SendOverRelay(peer, CollabProtocol.FrameType.Signal, payload);
}

/// <summary>
/// Client transport: the relay join plus one WebRTC peer connection to the host. The relay stays the
/// signaling channel and the fallback; the data channel carries the session once authenticated.
/// </summary>
internal sealed class CollabWebRtcClientTransport : CollabRelayClientTransport, ICollabVideoSource
{
    /// <summary>Raised on a libdatachannel thread with raw RTP packets of the host's preview.</summary>
    public event Action<byte[]>? VideoRtpReceived;

    /// <summary>
    /// Raised on a network thread when the data channel died but the relay still carries the session.
    /// Frames written to the channel just before it was noticed were never delivered, so the session
    /// asks the host for a fresh state.
    /// </summary>
    public event Action? FellBackToRelay;

    /// <summary>The relay drops an idle connection after 90 s; this keeps the fallback warm meanwhile.</summary>
    private const int RelayKeepAliveIntervalMs = 20_000;

    private CollabRtcLink? _link;
    private Timer? _keepAliveTimer;
    private volatile bool _relayAvailable = true;

    private CollabWebRtcClientTransport(TcpClient relayClient)
        : base(relayClient)
    {
    }

    public static new async Task<CollabWebRtcClientTransport> ConnectAsync(string relayHost, int relayPort, string roomCode,
                                                                       CancellationToken cancellation,
                                                                       CollabRelayHello? hello = null)
    {
        var client = await JoinRoomAsync(relayHost, relayPort, roomCode, cancellation, hello);
        try
        {
            return new CollabWebRtcClientTransport(client);
        }
        catch
        {
            client.Dispose();
            throw;
        }
    }

    public override void StartReading()
    {
        try
        {
            var link = new CollabRtcLink(_hostPeer,
                                         isOfferer: false,
                                         sendVideo: false,
                                         receiveVideo: true,
                                         sendSignal: payload => SendOverRelay(_hostPeer, CollabProtocol.FrameType.Signal, payload),
                                         readFrames: ReadFrameLoop,
                                         onPathFailed: OnLinkFailed);
            link.VideoRtpReceived += rtp => VideoRtpReceived?.Invoke(rtp);
            _link = link;
        }
        catch (Exception e)
        {
            // The session still works over the relay; only the faster path is lost.
            Log.Debug($"Collab: WebRTC unavailable: {e.Message}");
        }

        _keepAliveTimer = new Timer(_ => SendRelayKeepAlive(), null, RelayKeepAliveIntervalMs, RelayKeepAliveIntervalMs);
        base.StartReading();
        _link?.Start();
    }

    protected override void DispatchFrame(CollabPeerConnection peer, ReadOnlySpan<byte> frame, Stream source)
    {
        if (frame.Length < 1)
            return;

        var type = (CollabProtocol.FrameType)frame[0];
        if (type == CollabProtocol.FrameType.Signal)
        {
            _link?.HandleSignal(frame[1..].ToArray());
            return;
        }

        // The relay keeps carrying keepalives; session traffic on it while the data channel runs
        // would be stale by the time it arrived. A kick is the exception: the host sends it on the
        // relay so it survives the data channel being torn down, and dropping it would leave the
        // removed participant connected.
        if (_link is { IsActive: true } && ReferenceEquals(source, _hostPeer.Stream)
            && type != CollabProtocol.FrameType.Heartbeat
            && !(type == CollabProtocol.FrameType.Json && CollabProtocol.IsKickJson(frame[1..])))
            return;

        base.DispatchFrame(peer, frame, source);
    }

    public override void EnablePeerToPeer(CollabPeerConnection peer)
    {
        _link?.EnablePeerToPeer();
    }

    public override string DescribePeerLink(CollabPeerConnection peer) => _link?.Describe() ?? "";

    /// <summary>
    /// The relay is the fallback, not the primary path once the data channel runs. Its link dying
    /// must not end a session that is still alive peer to peer.
    /// </summary>
    protected override bool ShouldReportLostPeer(CollabPeerConnection peer)
    {
        if (_link is not { IsActive: true })
            return true;

        _relayAvailable = false;
        Log.Info("Collab: relay link ended, keeping the session on the data channel");
        return false;
    }

    /// <summary>Both paths are gone: report the host link gone so the session ends cleanly.</summary>
    private void OnLinkFailed(CollabRtcLink link)
    {
        if (_relayAvailable)
        {
            FellBackToRelay?.Invoke();
            return;
        }

        _hostPeer.Close();
        OnPeerDisconnected(_hostPeer);
    }

    /// <summary>A 9-byte heartbeat is never answered, but it keeps the relay connection from timing out.</summary>
    private void SendRelayKeepAlive()
    {
        Span<byte> timestamp = stackalloc byte[CollabProtocol.HeartbeatPingSize + 1];
        System.Buffers.Binary.BinaryPrimitives.WriteInt64LittleEndian(timestamp, Environment.TickCount64);
        timestamp[CollabProtocol.HeartbeatPingSize] = 0;

        try
        {
            // Must bypass the preferred path: this write exists to keep the relay from timing out.
            SendOverRelay(_hostPeer, CollabProtocol.FrameType.Heartbeat, timestamp);
        }
        catch
        {
            // The relay link is gone; the data channel still carries the session.
        }
    }

    public override void Dispose()
    {
        _keepAliveTimer?.Dispose();
        _keepAliveTimer = null;
        _link?.Dispose();
        _link = null;
        base.Dispose();
    }
}
