#nullable enable
using System.Collections.Concurrent;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DataChannelDotnet;
using DataChannelDotnet.Bindings;
using DataChannelDotnet.Data;
using DataChannelDotnet.Impl;
using Newtonsoft.Json.Linq;
using T3.Editor.Gui.UiHelpers;

namespace T3.Editor.Collab;

/// <summary>
/// Relay-free sessions: discovery and WebRTC signaling go through the self-hosted backend
/// (CollabBackend/api.php) and the whole session runs on the WebRTC data channels, so no relay
/// and no port forwarding is needed. The backend transports behave like the direct (LAN)
/// transports - authentication and every frame flow over the data channel - only the link under
/// the peer is a WebRTC data channel instead of a TCP socket, and the rendezvous is the backend's
/// join-ticket mailbox instead of a TCP connect.
/// </summary>
internal static class CollabBackendWire
{
    public const string DataChannelLabel = "tixl-collab";

    /// <summary>Purpose-built WebRTC link: no video track, no relay assumptions. Signals are the
    /// editor's standard signal JSON ({t:"offer"|"answer"|"ice", ...}) posted into a join ticket.</summary>
    internal sealed class BackendRtcLink : IDisposable
    {
        public bool IsActive => Volatile.Read(ref _active) != 0;
        public bool IsFailed => Volatile.Read(ref _failed) != 0;

        private readonly RtcPeerConnection _connection;
        private readonly bool _isOfferer;
        private readonly Action<byte[]> _sendSignal;
        private readonly Action<IRtcDataChannel> _onChannelOpen;
        private readonly Action<string> _onFailed;
        private IRtcDataChannel? _channel;
        private bool _remoteDescriptionSet;
        private readonly List<RtcCandidate> _pendingCandidates = [];
        private int _failed;
        private int _active;
        private int _channelAttached;
        private int _channelOpenNotified;

        // Optional outgoing video: the host publishes a feed (a webcam, an NDI input, ...) as an
        // H.264 track alongside the data channel.
        private readonly bool _sendVideo;
        private IRtcTrack? _videoTrack;
        private readonly object _videoSendLock = new();
        private readonly System.Diagnostics.Stopwatch _videoClock = System.Diagnostics.Stopwatch.StartNew();
        private readonly uint _videoSsrc = (uint)Random.Shared.Next(1, int.MaxValue);
        private int _keyFrameRequested;

        /// <summary>RTP packets of the host's shared video track (guest side).</summary>
        public event Action<byte[]>? VideoRtpReceived;

        public BackendRtcLink(bool isOfferer,
                              Action<byte[]> sendSignal,
                              Action<IRtcDataChannel> onChannelOpen,
                              Action<string> onFailed,
                              bool sendVideo = false)
        {
            _isOfferer = isOfferer;
            _sendVideo = sendVideo;
            _sendSignal = sendSignal;
            _onChannelOpen = onChannelOpen;
            _onFailed = onFailed;

            var connection = CollabWebRtc.TryCreatePeer(CollabWebRtc.CreateConfiguration(
                                                            UserSettings.Config.CollabWebRtcIceServers?
                                                                .Split(',', StringSplitOptions.RemoveEmptyEntries)));
            if (connection == null)
                throw new InvalidOperationException("WebRTC is not available on this platform");

            _connection = connection;
            _connection.OnDataChannel += (_, channel) => AttachChannel(channel);
            _connection.OnTrack += (_, track) =>
                                   {
                                       // The binding delivers RTP packets; the receiver depacketizes.
                                       track.OnBinaryReceivedSafe += (_, message) => VideoRtpReceived?.Invoke(message.Data.ToArray());
                                   };
            _connection.OnLocalDescriptionSafe += OnLocalDescription;
            _connection.OnCandidateSafe += OnLocalCandidate;
            _connection.OnConnectionStateChange += OnStateChanged;
        }

        /// <summary>Offerer: creates the video track (when sharing a feed) and the data channel, then
        /// starts gathering; the offer lands in the sendSignal callback once it is ready.</summary>
        public void Start()
        {
            if (_isOfferer)
            {
                // The track must exist before the offer so both travel in one negotiation.
                if (_sendVideo)
                    CreateVideoTrack();

                var channel = _connection.CreateDataChannel(new RtcCreateDataChannelArgs
                                                                {
                                                                    Label = DataChannelLabel,
                                                                    Protocol = RtcDataChannelProtocol.Binary,
                                                                });
                AttachChannel(channel);
                _connection.SetLocalDescription(RtcDescriptionType.Offer);
            }
        }

        /// <summary>True while the outgoing video track is open (host side).</summary>
        public bool HasVideoTrack => _videoTrack is { IsOpen: true };

        /// <summary>Writes one Annex-B access unit to the outgoing video track.</summary>
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
                Log.Debug($"Collab: video write failed: {e.Message}");
            }
        }

        /// <summary>A viewer asked for a fresh IDR (it joined or its stream broke).</summary>
        public void RequestKeyFrame() => Interlocked.Exchange(ref _keyFrameRequested, 1);

        public bool ConsumeKeyFrameRequest() => Interlocked.Exchange(ref _keyFrameRequested, 0) != 0;

        private void CreateVideoTrack()
        {
            try
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

                _videoTrack.SetPliHandler(RequestKeyFrame);
                _videoTrack.OnOpen += _ => RequestKeyFrame();
            }
            catch (Exception e)
            {
                // The party still works without the shared feed; only this feature is lost.
                Log.Warning($"Collab: could not create the video track: {e.Message}");
                _videoTrack = null;
            }
        }

        /// <summary>Feeds one remote signal into the connection.</summary>
        public void HandleSignal(byte[] payload)
        {
            JObject json;
            try
            {
                json = JObject.Parse(Encoding.UTF8.GetString(payload));
            }
            catch (Newtonsoft.Json.JsonException)
            {
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

    private void FlushPendingCandidates()
    {
        foreach (var candidate in _pendingCandidates)
            _connection.AddRemoteCandidate(candidate);

        _pendingCandidates.Clear();
    }

    private void AttachChannel(IRtcDataChannel channel)
    {
        if (Interlocked.Exchange(ref _channelAttached, 1) != 0)
            return;

        _channel = channel;
        channel.OnOpen += _ => OnChannelOpen();
        if (channel.IsOpen)
            OnChannelOpen();
    }

    /// <summary>The data channel opened: hand it to the transport exactly once. Without this the
    /// peer connection reaches CONNECTED but no session stream is ever created.</summary>
    private void OnChannelOpen()
    {
        Interlocked.Exchange(ref _active, 1);
        if (Interlocked.Exchange(ref _channelOpenNotified, 1) != 0 || _channel == null)
            return;

        Log.Debug("Collab backend link: data channel open");
        _onChannelOpen(_channel);
    }

    private void OnLocalDescription(IRtcPeerConnection _, RtcDescription description)
    {
        Log.Debug($"Collab backend link: local description {description.Type} (offerer={_isOfferer})");
        if (_isOfferer && description.Type == RtcDescriptionType.Offer)
        {
            SendJsonSignal(Signal("offer", description.Sdp));
        }
        else if (!_isOfferer && description.Type == RtcDescriptionType.Answer)
        {
            SendJsonSignal(Signal("answer", description.Sdp));
        }
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

        SendJsonSignal(Encoding.UTF8.GetBytes(json.ToString(Newtonsoft.Json.Formatting.None)));
    }

    private void OnStateChanged(IRtcPeerConnection _, rtcState state)
    {
        Log.Debug($"Collab backend link: state {state}");
        if (state is rtcState.RTC_FAILED or rtcState.RTC_CLOSED)
            Fail($"connection {state}");
    }

    private static byte[] Signal(string type, string sdp)
    {
        var json = new JObject
                       {
                           ["t"] = type,
                           ["sdp"] = sdp,
                       };
        return Encoding.UTF8.GetBytes(json.ToString(Newtonsoft.Json.Formatting.None));
    }

    private void SendJsonSignal(byte[] payload) 
    {
        Log.Debug($"Collab backend link: sending signal ({payload.Length} bytes)");
        try
        {
            _sendSignal(payload);
        }
        catch (Exception e)
        {
            Log.Warning($"Collab backend link: signal send threw: {e}");
        }
    }

    private void Fail(string reason)
    {
        if (Interlocked.Exchange(ref _failed, 1) != 0)
            return;

        Log.Warning($"Collab backend link failed: {reason}");
        _onFailed(reason);
    }

    public void Dispose()
    {
        _connection.Dispose();
    }
    }

    // ---------------------------------------------------------------------------------------
    // Host transport: polls the backend for join tickets, negotiates one WebRTC link per ticket
    // and runs the session (authentication included) on the data channels.
    // ---------------------------------------------------------------------------------------

    internal sealed class CollabBackendHostTransport : CollabTransport, ICollabVideoSink
    {
        /// <summary>Peers currently watching the shared feed.</summary>
        public int ViewerCount
        {
            get
            {
                lock (_gate)
                {
                    // Called once per render frame; keep it allocation-free like the sibling transport.
                    var count = 0;
                    foreach (var entry in _entries)
                    {
                        if (!entry.Dead && entry.Link is { HasVideoTrack: true })
                            count++;
                    }
                    return count;
                }
            }
        }

        /// <summary>One encoded access unit, fanned out to every open video track.</summary>
        public void SendVideo(byte[] annexB)
        {
            // Called once per encoded access unit on the encode thread; the scratch list
            // is reused so the fan-out does not allocate per frame.
            List<BackendRtcLink> links;
            lock (_gate)
            {
                links = _sendScratch;
                links.Clear();
                foreach (var entry in _entries)
                {
                    if (!entry.Dead && entry.Link is BackendRtcLink link)
                        links.Add(link);
                }
            }

            foreach (var link in links)
                link.SendVideo(annexB);
        }

        /// <summary>True when any viewer needs a fresh IDR.</summary>
        public bool ConsumeKeyFrameRequest()
        {
            var requested = false;
            lock (_gate)
            {
                // Just a flag flip per link; consuming under the gate avoids the snapshot list.
                foreach (var entry in _entries)
                {
                    if (entry.Dead)
                        continue;
                    if (entry.Link is BackendRtcLink link)
                        requested |= link.ConsumeKeyFrameRequest();
                }
            }

            return requested;
        }

        /// <summary>Raised on a pool thread when a visitor asks to join; the host approves in the UI.</summary>
        public event Action<string, string>? JoinRequested;

        /// <summary>Raised on a pool thread when an approved ticket's guest disconnects while negotiating.</summary>
        public event Action<string>? JoinCancelled;

        private readonly string _room;
        private readonly string _key;
        /// <summary>Remote management: the one claim this session auto-approves. Empty = every
        /// request needs the host's confirmation.</summary>
        private readonly string _autoApproveClaim;
        private readonly CancellationTokenSource _cancellation = new();
        private readonly object _gate = new();
        private readonly List<HostPeerEntry> _entries = [];
        private readonly List<BackendRtcLink> _sendScratch = [];
        private readonly HashSet<string> _blockedNames = new(StringComparer.OrdinalIgnoreCase);
        private volatile CollabPeerConnection[] _peerSnapshot = [];
        private int _nextPeerId = 1;

        private sealed class HostPeerEntry
        {
            public string TicketId = "";
            public string GuestName = "";
            public string State = "pending";
            public int PeerId;
            public long Since;
            public BackendRtcLink? Link;
            public CollabPeerConnection? Peer;
            public bool Dead;
        }

        public override IReadOnlyList<CollabPeerConnection> Peers => _peerSnapshot;

        public CollabBackendHostTransport(string room, string key, string autoApproveClaim = "")
        {
            _room = room;
            _key = key;
            _autoApproveClaim = autoApproveClaim ?? "";
            _ = Task.Run(() => PollLoopAsync(_cancellation.Token));
            Log.Info($"Collab: hosting via the backend, waiting for join tickets (room {_room})");
        }

        public override void SendToPeer(CollabPeerConnection peer, CollabProtocol.FrameType type, ReadOnlySpan<byte> payload)
        {
            peer.Write(type, payload);
        }

        protected override void EchoHeartbeat(CollabPeerConnection peer, ReadOnlySpan<byte> timestamp)
        {
            SendToPeer(peer, CollabProtocol.FrameType.Heartbeat, timestamp);
        }

        protected override void CleanupPeer(CollabPeerConnection peer)
        {
            lock (_gate)
            {
                foreach (var entry in _entries)
                {
                    if (entry.Peer != peer)
                        continue;

                    entry.Dead = true;
                    entry.Link?.Dispose();
                    break;
                }

                _peerSnapshot = _entries.Where(e => e.Peer != null && !e.Dead).Select(e => e.Peer!).ToArray();
            }
        }

        private async Task PollLoopAsync(CancellationToken cancellation)
        {
            while (!cancellation.IsCancellationRequested)
            {
                try
                {
                    await PollOnceAsync(cancellation);
                }
                catch (Exception e)
                {
                    Log.Debug($"Collab backend host: poll failed: {e.Message}");
                }

                try
                {
                    await Task.Delay(3000, cancellation);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }

        private async Task PollOnceAsync(CancellationToken cancellation)
        {
            var (tickets, error) = await CollabWebLobby.FetchTicketsAsync(_room, _key, cancellation);
            if (error != null)
            {
                Log.Debug($"Collab backend host: tickets poll failed: {error}");
            }
            else
            {
                lock (_gate)
                {
                    foreach (var ticket in tickets)
                    {
                        if (_entries.Any(e => e.TicketId == ticket.Id))
                            continue;

                        // Blocked names get an instant rejection without a popup.
                        if (_blockedNames.Contains(ticket.Name))
                        {
                            _ = CollabWebLobby.SetTicketStateAsync(_room, _key, ticket.Id, "rejected", cancellation);
                            continue;
                        }

                        var entry = new HostPeerEntry
                                        {
                                            TicketId = ticket.Id,
                                            GuestName = ticket.Name,
                                            State = ticket.State,
                                        };
                        _entries.Add(entry);

                        if (ticket.State == "approved")
                        {
                            StartLink(entry);
                        }
                        else if (ticket.State == "pending")
                        {
                            // A remote-management session accepts exactly the ticket that carries
                            // its claim; everyone else still needs the host's confirmation.
                            if (_autoApproveClaim.Length > 0 && ticket.Claim == _autoApproveClaim)
                            {
                                Log.Info($"Collab backend host: auto-approving the paired controller ({ticket.Name})");
                                entry.State = "approved";
                                _ = CollabWebLobby.SetTicketStateAsync(_room, _key, ticket.Id, "approved", cancellation);
                                StartLink(entry);
                            }
                            else
                            {
                                Log.Debug($"Collab backend host: join request from {ticket.Name}"
                                          + $" (ticket claim '{ticket.Claim}', expected '{_autoApproveClaim}')");
                                JoinRequested?.Invoke(ticket.Name, ticket.Id);
                            }
                        }
                    }
                }
            }

            // Approvals that landed between polls flip pending entries to negotiating.
            List<HostPeerEntry> entries;
            lock (_gate)
                entries = _entries.Where(e => !e.Dead).ToList();

            foreach (var entry in entries)
            {
                if (entry.Link == null && entry.State == "pending")
                {
                    // Re-check the ticket's state each poll: the host may have approved meanwhile.
                    var (messages, state, stateError) = await CollabWebLobby.FetchTicketMessagesAsync(_room, _key, entry.TicketId, 0, cancellation);
                    if (stateError == null && state == "approved")
                    {
                        entry.State = "approved";
                        StartLink(entry);
                    }
                    else if (stateError == null && state == "rejected")
                    {
                        entry.Dead = true;
                        JoinCancelled?.Invoke(entry.TicketId);
                        continue;
                    }
                }

                if (entry.Link == null)
                    continue;

                var (msgs, _, msgError) = await CollabWebLobby.FetchTicketMessagesAsync(_room, _key, entry.TicketId, entry.Since, cancellation);
                if (msgError != null)
                {
                    Log.Debug($"Collab backend host: signals poll failed: {msgError}");
                    continue;
                }

                // `since` is an offset into the ticket's message array, so it advances by the whole
                // slice - not by the guest-filtered count, which re-read the same lines forever.
                foreach (var message in msgs.Where(m => m.From == "guest"))
                    await ApplyMessageAsync(entry, message, cancellation);

                entry.Since += msgs.Count;
            }
        }

        private Task ApplyMessageAsync(HostPeerEntry entry, CollabWebLobby.TicketMessage message, CancellationToken cancellation)
        {
            var bytes = Encoding.UTF8.GetBytes(message.Sdp.Length > 0
                                                   ? $"{{\"t\":\"{message.Type}\",\"sdp\":{Newtonsoft.Json.JsonConvert.SerializeObject(message.Sdp)}}}"
                                                   : $"{{\"t\":\"{message.Type}\",\"candidate\":{Newtonsoft.Json.JsonConvert.SerializeObject(message.Candidate)},\"mid\":{Newtonsoft.Json.JsonConvert.SerializeObject(message.Mid)}}}");
            Log.Debug($"Collab backend host: applying {message.Type} from {message.From} (sdp {message.Sdp.Length} chars)");
            entry.Link?.HandleSignal(bytes);
            return Task.CompletedTask;
        }

        /// <summary>Host: approves a join request; the WebRTC negotiation starts on the next poll.</summary>
        public void ApproveTicket(string ticketId)
        {
            _ = Task.Run(async () =>
                             {
                                 var error = await CollabWebLobby.SetTicketStateAsync(_room, _key, ticketId, "approved", CancellationToken.None);
                                 if (error != null)
                                     Log.Debug($"Collab backend host: approve failed: {error}");
                             });
        }

        /// <summary>Host: declines a join request. <paramref name="block"/> also auto-declines
        /// further requests from the same name this session.</summary>
        public void RejectTicket(string ticketId, bool block)
        {
            lock (_gate)
            {
                var entry = _entries.FirstOrDefault(e => e.TicketId == ticketId);
                if (entry != null)
                    entry.Dead = true;

                if (block)
                {
                    var name = entry?.GuestName;
                    if (!string.IsNullOrEmpty(name))
                        _blockedNames.Add(name);
                }
            }

            _ = Task.Run(async () =>
                             {
                                 var error = await CollabWebLobby.SetTicketStateAsync(_room, _key, ticketId, "rejected", CancellationToken.None);
                                 if (error != null)
                                     Log.Debug($"Collab backend host: reject failed: {error}");
                             });
        }

        private void StartLink(HostPeerEntry entry)
        {
            try
            {
                var slot = _nextPeerId++;
                entry.PeerId = slot;
                var ticketId = entry.TicketId;
                entry.Link = new BackendRtcLink(isOfferer: true,
                                                sendSignal: payload => _ = PostSignalLoggedAsync(ticketId, payload),
                                                onChannelOpen: channel => OnChannelOpen(entry, channel, slot),
                                                onFailed: reason => OnLinkFailed(entry, reason),
                                                sendVideo: UserSettings.Config.CollabSharePreview);
                entry.Link.Start();
                OnStatus($"Join ticket from {entry.GuestName} - negotiating the peer connection...");
            }
            catch (Exception e)
            {
                Log.Warning($"Collab backend host: could not negotiate for {entry.GuestName}: {e.Message}");
                entry.Dead = true;
            }
        }

        private async Task PostSignalLoggedAsync(string ticketId, byte[] payload)
        {
            string type = "?";
            try { type = JObject.Parse(Encoding.UTF8.GetString(payload))["t"]?.Value<string>() ?? "?"; } catch { }
            var error = await CollabWebLobby.PostTicketMessageAsync(_room, _key, ticketId, payload, CancellationToken.None);
            if (error != null)
                Log.Warning($"Collab backend host: signal post ({type}) failed: {error}");
            else
                Log.Debug($"Collab backend host: signal post ({type}) ok, {payload.Length} bytes");
        }

        private void OnChannelOpen(HostPeerEntry entry, IRtcDataChannel channel, int slot)
        {
            var stream = new CollabDataChannelStream(channel, CollabWebRtc.DataChannelChunkSize,
                                                     () => OnLinkFailed(entry, "overflow"));
            var peer = new CollabPeerConnection(stream, slot, null);
            entry.Peer = peer;

            lock (_gate)
                _peerSnapshot = _entries.Where(e => e.Peer != null && !e.Dead).Select(e => e.Peer!).ToArray();

            OnStatus($"Peer connected via the backend: {entry.GuestName}");
            _ = Task.Run(() =>
                             {
                                 ReadFrameLoop(peer, stream);
                                 OnLinkFailed(entry, "the data channel ended");
                             });
        }

        private void OnLinkFailed(HostPeerEntry entry, string reason)
        {
            if (entry.Dead)
                return;

            entry.Dead = true;
            Log.Info($"Collab backend host: {entry.GuestName}: {reason}");
            if (entry.Peer != null)
            {
                entry.Peer.Close();
                CleanupPeer(entry.Peer);
                OnPeerDisconnected(entry.Peer);
            }
        }

        public override void Dispose()
        {
            _cancellation.Cancel();
            lock (_gate)
            {
                foreach (var entry in _entries)
                    entry.Link?.Dispose();

                _entries.Clear();
                _peerSnapshot = [];
            }
        }
    }

    // ---------------------------------------------------------------------------------------
    // Client transport: one join ticket, one WebRTC link, the session rides the data channel.
    // ---------------------------------------------------------------------------------------

    internal sealed class CollabBackendClientTransport : CollabTransport, ICollabVideoSource
    {
        /// <summary>Encoded access units of the host's shared feed, raised on a libdatachannel thread.</summary>
        public event Action<byte[]>? VideoRtpReceived;

        private readonly string _room;
        private readonly string _name;
        private readonly string _lang;
        /// <summary>Remote management: the claim this guest presents in its join ticket so the
        /// managed installation auto-approves it. Empty = a normal request.</summary>
        private readonly string _claim;
        private readonly CancellationTokenSource _cancellation = new();
        private string _ticket = "";
        private long _since;
        private int _pollFailures;
        private BackendRtcLink? _link;
        private CollabPeerConnection? _hostPeer;
        private CollabPeerConnection[] _peerList = [];
        private volatile bool _connected;

        public override IReadOnlyList<CollabPeerConnection> Peers => _peerList;

        public override CollabPeerConnection? HostLink => _hostPeer;

        public bool IsConnected => _connected;

        /// <summary>The join ticket this connection was approved through (used for the project download).</summary>
        public string TicketId => _ticket;

        public CollabBackendClientTransport(string room, string name, string lang, string claim = "")
        {
            _room = room;
            _name = name;
            _lang = lang;
            _claim = claim ?? "";
        }

        /// <summary>Opens the ticket, waits for the host's approval, runs <paramref name="prepare"/>
        /// (the project handoff), negotiates the WebRTC link and returns once the data channel is
        /// open. Throws with a readable message when anything fails.</summary>
        public async Task ConnectAsync(CancellationToken cancellation, Func<string, CancellationToken, Task>? prepare = null)
        {
            _ticket = await CollabWebLobby.JoinRequestAsync(_room, _name, _lang, _claim, cancellation);
            Log.Debug($"Collab backend client: join ticket opened (claim '{_claim}')");

            // The host confirms the request in a popup first; the negotiation waits for it.
            var approvalDeadline = Environment.TickCount64 + 90_000;
            while (true)
            {
                var (messages, state, error) = await CollabWebLobby.FetchTicketMessagesAsync(_room, "", _ticket, 0, cancellation);
                if (error == null)
                {
                    if (state == "approved")
                        break;

                    if (state == "rejected")
                        throw new Exception("the host declined your join request");
                }

                if (Environment.TickCount64 > approvalDeadline)
                    throw new Exception("the host did not answer your join request in time");

                await Task.Delay(2000, cancellation);
            }

            Log.Info("Collab: the host approved the join request - connecting...");

            // The project handoff runs before the negotiation: both sides must be on the same
            // snapshot, and the data channel carries the hello immediately after it opens.
            if (prepare != null)
                await prepare(_ticket, cancellation);

            var ticket = _ticket;
            _link = new BackendRtcLink(isOfferer: false,
                                       sendSignal: payload => _ = PostSignalLoggedAsync(ticket, payload),
                                       onChannelOpen: OnChannelOpen,
                                       onFailed: reason => OnFailed(reason));
            _link.VideoRtpReceived += rtp => VideoRtpReceived?.Invoke(rtp);

            _ = Task.Run(() => PollLoopAsync(cancellation));

            // The channel-open callback completes the handshake; wait for it.
            var deadline = Environment.TickCount64 + 30_000;
            while (!_connected && Environment.TickCount64 < deadline && !cancellation.IsCancellationRequested)
                await Task.Delay(200, cancellation);

            if (!_connected)
                throw new Exception("the peer connection did not open in time (NAT or ICE failure?)");
        }

        private async Task PostSignalLoggedAsync(string ticket, byte[] payload)
        {
            var error = await CollabWebLobby.PostTicketMessageAsync(_room, "", ticket, payload, CancellationToken.None);
            if (error != null)
                Log.Debug($"Collab backend client: signal post failed: {error}");
        }

        private async Task PollLoopAsync(CancellationToken cancellation)
        {
            while (!cancellation.IsCancellationRequested && !_connected)
            {
                try
                {
                    var (messages, _, error) = await CollabWebLobby.FetchTicketMessagesAsync(_room, "", _ticket, Interlocked.Read(ref _since), cancellation);
                    if (error == null)
                    {
                        Interlocked.Exchange(ref _pollFailures, 0);
                        foreach (var message in messages.Where(m => m.From == "host"))
                        {
                            Log.Debug($"Collab backend client: got {message.Type} from host");
                            var bytes = Encoding.UTF8.GetBytes(message.Sdp.Length > 0
                                                                   ? $"{{\"t\":\"{message.Type}\",\"sdp\":{Newtonsoft.Json.JsonConvert.SerializeObject(message.Sdp)}}}"
                                                                   : $"{{\"t\":\"{message.Type}\",\"candidate\":{Newtonsoft.Json.JsonConvert.SerializeObject(message.Candidate)},\"mid\":{Newtonsoft.Json.JsonConvert.SerializeObject(message.Mid)}}}");
                            _link?.HandleSignal(bytes);
                        }

                        // Offset semantics: advance past the whole slice, own lines included.
                        Interlocked.Add(ref _since, messages.Count);
                    }
                    else
                    {
                        // A rate-limited server answers with an error page; backing off keeps the
                        // block from extending.
                        var failures = Interlocked.Increment(ref _pollFailures);
                        Log.Debug($"Collab backend client: signals poll failed ({failures}): {error}");
                    }
                }
                catch (Exception e)
                {
                    Log.Debug($"Collab backend client: poll failed: {e.Message}");
                }

                try
                {
                    var failures = _pollFailures;
                    var delay = 2000 + Math.Min(28_000, failures * failures * 2000);
                    await Task.Delay(delay, cancellation);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }

        private void OnChannelOpen(IRtcDataChannel channel)
        {
            var stream = new CollabDataChannelStream(channel, CollabWebRtc.DataChannelChunkSize,
                                                     () => OnFailed("overflow"));
            _hostPeer = new CollabPeerConnection(stream, 0, null);
            _peerList = [_hostPeer];
            _connected = true;
            _ = Task.Run(() =>
                             {
                                 ReadFrameLoop(_hostPeer, stream);
                                 OnFailed("the data channel ended");
                             });
        }

        private void OnFailed(string reason)
        {
            if (_connected && _hostPeer != null)
            {
                _hostPeer.Close();
                OnPeerDisconnected(_hostPeer);
                _connected = false;
                return;
            }

            Log.Debug($"Collab backend client: {reason}");
        }

        public override void SendToPeer(CollabPeerConnection peer, CollabProtocol.FrameType type, ReadOnlySpan<byte> payload)
        {
            peer.Write(type, payload);
        }

        protected override void EchoHeartbeat(CollabPeerConnection peer, ReadOnlySpan<byte> timestamp)
        {
            SendToPeer(peer, CollabProtocol.FrameType.Heartbeat, timestamp);
        }

        public override void Dispose()
        {
            _cancellation.Cancel();
            _link?.Dispose();
        }
    }
}