#nullable enable
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using System.Net.Sockets;
using Newtonsoft.Json.Linq;
using T3.Core.Model;
using T3.Editor.Compilation;
using T3.Editor.Gui.UiHelpers;
using T3.Editor.Gui.Window;
using T3.Editor.Gui.Windows.Layouts;
using T3.Editor.Show;
using T3.Editor.UiModel;
using T3.Editor.UiModel.Commands;
using T3.Editor.UiModel.ProjectHandling;

namespace T3.Editor.Collab;

/// <summary>
/// Owns the collaboration session state machine and is the single entry point for the rest of the
/// editor. Everything mutating editor state (applying remote ops, updating peer presence) happens in
/// <see cref="Update"/> on the UI thread; network threads only produce queued inbound messages.
///
/// Topology: host-authoritative star. Clients send ops to the host; the host applies every op to its
/// own graph and relays it. Clients apply only host-relayed ops, so all participants converge in
/// send order without clocks or vector versions. Presence (cursors/selection) is fire-and-forget
/// display data on its own channel and never influences the graph.
/// </summary>
internal sealed class CollabManager
{
    public static CollabManager Instance { get; } = new();

    public enum SessionState
    {
        Off,
        Hosting,
        Connecting,
        Connected,
    }

    public SessionState State { get; private set; } = SessionState.Off;
    public string StatusText { get; private set; } = "Collaboration is off";
    public string ErrorText { get; private set; } = "";

    /// <summary>Room code when hosting through a relay; empty for direct sessions.</summary>
    public string RoomCode { get; private set; } = "";

    /// <summary>"host:port" of the relay backing the current session; empty for direct sessions.
    /// The invite text carries it so a joiner on another network reaches the same room.</summary>
    public string RelayEndpoint { get; private set; } = "";

    /// <summary>IP of the direct session host on a client; null when hosting, on a relay join, or unknown.</summary>
    public string? ConnectedHostAddress => HostLink?.RemoteAddress?.ToString();

    public bool IsSessionActive => State is SessionState.Hosting or SessionState.Connected;

    /// <summary>True when the user ended the last session on purpose; the auto-rejoin respects it.</summary>
    public bool LeftManually { get; private set; }

    /// <summary>Collaboration on/off — the settings switch gates every session start and join.</summary>
    public bool IsEnabled => UserSettings.Config.CollabEnabled;

    public CollabUserIdentity LocalIdentity => _localIdentity;

    /// <summary>Peers currently in the session (UI thread only).</summary>
    public IReadOnlyList<CollabPeerConnection> Peers => _transport?.Peers ?? [];

    /// <summary>How each peer is reached ("direct" data channel, relay fallback); empty when unknown.</summary>
    public IReadOnlyList<(int PeerId, string Link)> PeerLinks
    {
        get
        {
            var result = new List<(int, string)>();
            foreach (var peer in Peers)
                result.Add((peer.AssignedId, _transport?.DescribePeerLink(peer) ?? ""));

            return result;
        }
    }

    internal CollabWebRtcHostTransport? WebRtcHost => _transport as CollabWebRtcHostTransport;
    internal CollabWebRtcClientTransport? WebRtcClient => _transport as CollabWebRtcClientTransport;

    private CollabVideoHostStreamer? _videoStreamer;
    private CollabVideoReceiver? _videoReceiver;

    /// <summary>The host's preview on a joined session, or null when no video is arriving.</summary>
    public CollabVideoReceiver? VideoReceiver => _videoReceiver;

    /// <summary>True while the host is encoding its preview for members.</summary>
    public bool IsStreamingPreview => _videoStreamer is { IsEncoding: true };

    public string PreviewEncoderName => _videoStreamer?.EncoderName ?? "";

    internal CollabVideoHostStreamer? VideoStreamer => _videoStreamer;

    /// <summary>The machines this instance is linked to right now — the settings panel's network
    /// check compares them against the selected adapter.</summary>
    public void AddLinkedPeers(List<NetworkPeer> peers)
    {
        foreach (var peer in Peers)
        {
            if (peer.RemoteAddress != null)
                peers.Add(new NetworkPeer("Collaboration peer", peer.RemoteAddress));
        }
    }

    /// <summary>The link back to the session host on client transports, direct or relay.</summary>
    private CollabPeerConnection? HostLink => _transport?.HostLink;

    public IReadOnlyList<DiscoveredSession> DiscoveredSessions => _discovered;

    private CollabUserIdentity _localIdentity = new();

    private CollabTransport? _transport;
    private LanDiscoveryScanner? _discoveryScanner;
    private readonly List<DiscoveredSession> _discovered = [];

    /// <summary>Ops applied or received since session start; replayed to late joiners.</summary>
    private readonly List<JObject> _sessionLog = [];
    private const int MaxSessionLogEntries = 10_000;

    // -- Op ordering. The host stamps every op with a sequence number and sends it to everyone,
    //    the sender included, so that number is the one order all participants agree on. --

    /// <summary>Host: sequence number of the last op it ordered.</summary>
    private long _hostSeq;

    /// <summary>Client: last host sequence number seen; -1 until the first state sync arrived.</summary>
    private long _lastAppliedSeq = -1;

    private long _nextLocalOpId;

    private readonly record struct PendingLocalOp(long ClientOpId, JObject Op);

    /// <summary>Client: local ops already applied here that the host has not ordered yet.</summary>
    private readonly List<PendingLocalOp> _pendingLocalOps = [];

    private const int MaxPendingLocalOps = 2048;

    /// <summary>Compositions the session edited. A set rather than a scan of the capped
    /// <see cref="_sessionLog"/>, so a long session still syncs the ones it touched early on.</summary>
    private readonly HashSet<Guid> _touchedCompositions = [];

    private readonly HashSet<Guid> _scratchCompositions = [];

    // -- Automatic reconnect. A dropped link ends the transport, not the user's intent to be in
    //    the session: the same target is retried, and unconfirmed edits are sent again. --

    /// <summary>How a session is reached: enough to open or join it again.</summary>
    private sealed record SessionTarget(bool Hosting, bool Relay, string Host, int Port, string RoomCode, string Password, string RoomKey = "");

    /// <summary>The running session's target; null when off.</summary>
    private SessionTarget? _currentTarget;

    /// <summary>Set while a lost session is being re-established.</summary>
    private SessionTarget? _reconnectTarget;

    private int _reconnectAttempts;
    private long _nextReconnectTicks;
    private bool _reconnectAttemptInFlight;
    private bool _keepReconnectOnLeave;

    /// <summary>Direct joins dial on the UI thread and can block for the connect timeout, so a
    /// background probe checks that the host answers before a retry dials for real.</summary>
    private Task<bool>? _reconnectProbe;

    /// <summary>Edits the host never confirmed, plus edits made while the link was down.</summary>
    private readonly List<JObject> _opsToResend = [];

    /// <summary>Retries back off 2, 4, 8, 15, 15... seconds: about 90 s in total, long enough for a
    /// host to come back from a network blip and re-open its relay room.</summary>
    private const int MaxReconnectAttempts = 8;

    /// <summary>True while a dropped session is being re-established automatically.</summary>
    public bool IsReconnecting => _reconnectTarget != null;

    // -- Host handover. When the host is gone for good, the participant with the lowest session
    //    id opens the session again and the others join it there; the next in line follows if
    //    that one is gone too. The host can also hand the session to a participant on purpose. --

    private sealed record HandoverCandidate(int PeerId, string Address, int Port);

    private sealed class HandoverPlan
    {
        public List<HandoverCandidate> Candidates = new();

        /// <summary>This participant's place in line; -1 when it will not take over.</summary>
        public int MyRank = -1;

        public long LossTicks;
        public string ProjectName = "";

        /// <summary>The host named its successor: no waiting for the old host to come back.</summary>
        public bool Appointed;

        public int TakeoverRetriesLeft = 3;
    }

    private HandoverPlan? _handover;

    /// <summary>Direct sessions: the address the running probe checks, dialled when it answers.</summary>
    private SessionTarget? _reconnectDial;

    /// <summary>
    /// Set after taking over a direct session while nobody has joined yet. A direct session has no
    /// relay to say whether the old host is really gone - it may have been this machine's own
    /// network that dropped. So the old host keeps being checked, and if it answers while this
    /// takeover is still alone, this editor goes back to it.
    /// </summary>
    private SessionTarget? _loneTakeoverOrigin;

    private Task<bool>? _loneTakeoverProbe;
    private long _nextLoneTakeoverProbeTicks;
    private const int LoneTakeoverProbeIntervalMs = 10_000;

    /// <summary>How long the first in line waits for the old host before taking over, and how much
    /// longer each next one waits. Long enough for a host to return from a network drop first.</summary>
    private const int HandoverBaseDelayMs = 20_000;
    private const int HandoverStepMs = 20_000;
    private const int AppointedTakeoverDelayMs = 1500;

    /// <summary>Host: a handover to this participant leaves once the announcement is on the wire.</summary>
    private (long AtTicks, SessionTarget JoinTarget, string Name)? _pendingHandoff;

    /// <summary>Secret of a relay room. The host makes it up and tells authenticated participants;
    /// the relay lets its holder re-open the room once the host has gone silent.</summary>
    private string _roomKey = "";

    /// <summary>Identifies this editor instance to the relay, so a host coming back for its own
    /// room is told apart from a participant taking it over.</summary>
    private static readonly string _instanceId = Guid.NewGuid().ToString("N");

    /// <summary>What is known about a participant besides the name.</summary>
    public readonly record struct ParticipantInfo(string Lang, bool Verified, string Address, int Port);

    private readonly Dictionary<int, ParticipantInfo> _participants = new();

    /// <summary>The relay confirmed this editor's Discord account for the running session.</summary>
    private bool _localVerified;

    /// <summary>Language tag and whether the relay confirmed the Discord account, for one participant.</summary>
    public ParticipantInfo GetParticipantInfo(int peerId)
    {
        if (peerId == MyPeerId)
            return new ParticipantInfo(_localIdentity.Lang, _localVerified, "", 0);

        return _participants.TryGetValue(peerId, out var info) ? info : new ParticipantInfo("", false, "", 0);
    }

    private static bool IsVerifiedByRelay(JObject? relayReply, string who)
    {
        return relayReply?[who] is JObject identity
               && CollabProtocol.TryGetBool(identity, "verified", out var verified)
               && verified;
    }

    private JObject BuildHello()
    {
        return new JObject
                   {
                       ["t"] = "hello",
                       ["proto"] = CollabProtocol.ProtocolVersion,
                       ["name"] = _localIdentity.DisplayName,
                       ["userId"] = _localIdentity.UserId,
                       ["lang"] = _localIdentity.Lang,
                       ["avatar"] = TruncateAvatar(_localIdentity.AvatarUrl),
                       ["port"] = UserSettings.Config.CollabPort,
                   };
    }

    private static string TruncateAvatar(string url) => string.IsNullOrEmpty(url) ? "" : url.Length <= 220 ? url : url[..220];

    /// <summary>What the relay is told when opening or joining a room. Hosting sends the Discord
    /// token (the host operates the relay); a join does not — the relay link is plaintext, so a
    /// joiner's token would end up with the relay operator. UI thread: reads the settings.</summary>
    private CollabRelayHello BuildRelayHello(bool hosting, string roomKey, string password, bool sendDiscordToken = true)
    {
        var config = UserSettings.Config;
        var token = sendDiscordToken ? CollabDiscordAuth.GetValidAccessToken() : "";
        if (!hosting)
            return new CollabRelayHello(Token: token, Name: _localIdentity.DisplayName, Lang: _localIdentity.Lang);

        var title = string.IsNullOrWhiteSpace(config.CollabSessionTitle)
                        ? $"{_localIdentity.DisplayName}'s session"
                        : config.CollabSessionTitle.Trim();
        return new CollabRelayHello(Token: token,
                                    Name: _localIdentity.DisplayName,
                                    Lang: _localIdentity.Lang,
                                    Key: roomKey,
                                    Instance: _instanceId,
                                    Title: title,
                                    Purpose: (config.CollabSessionPurpose ?? "").Trim(),
                                    Project: ProjectView.Focused?.OpenedProject.Package.DisplayName ?? "",
                                    Public: config.CollabListSession,
                                    PasswordProtected: !string.IsNullOrEmpty(password));
    }

    // -- Drift detection: the host announces a hash per composition, clients compare. --
    private const int StateHashIntervalMs = 5000;
    private const int MaxHashedCompositions = 32;
    private const int MismatchesBeforeHeal = 2;
    private const int MaxSyncRequestIds = 64;
    private const int ResyncRequestMinIntervalMs = 2000;
    private long _nextStateHashTicks;
    private long _nextFullResyncAllowedTicks;
    private int _hashRotation;
    private bool _syncInProgress;
    private readonly Dictionary<Guid, int> _hashMismatchStreak = new();

    /// <summary>Requested from the host, snapshot not applied yet — hashes that crossed the request are ignored.</summary>
    private readonly HashSet<Guid> _healRequested = [];

    /// <summary>Snapshot applied; the next hash tells whether the repair worked.</summary>
    private readonly HashSet<Guid> _healAwaitingVerification = [];

    /// <summary>A snapshot did not make these match. Repairing again would only replace the
    /// composition every few seconds, so they are left alone for the rest of the session.</summary>
    private readonly HashSet<Guid> _unhealable = [];

    private long _nextHeartbeatTicks;
    private long _nextRttProbeTicks;
    private const int RttProbeIntervalMs = 2000;
    private long _nextPresenceSendTicks;
    private long _nextRateResetTicks;

    private string _password = "";

    /// <summary>Inbound op counter per peer for rate limiting, keyed by peer id. UI thread only.</summary>
    private readonly Dictionary<int, int> _opsReceivedThisSecond = new();

    private PresencePacket _localPresence = new();

    /// <summary>
    /// Completions of background connects, applied on the UI thread so a Leave() that races them
    /// wins instead of being overwritten by a session nobody asked for.
    /// </summary>
    private readonly ConcurrentQueue<Action> _uiThreadActions = new();

    /// <summary>Runs an action on the UI thread at the next manager update. For pool-thread
    /// paths (e.g. the remote-management heartbeat) that must touch session state.</summary>
    internal void RunOnUi(Action action) => _uiThreadActions.Enqueue(action);

    /// <summary>Bumped by every session start and Leave; stale asynchronous completions check it.</summary>
    private volatile int _sessionGeneration;

    // ---------------------------------------------------------------------------------------
    // Lifecycle
    // ---------------------------------------------------------------------------------------

    private CollabManager()
    {
        RefreshLocalIdentity();
    }

    /// <summary>Rebuilds the local identity from user settings (after a Discord login or name change).</summary>
    public void RefreshLocalIdentity()
    {
        var config = UserSettings.Config;
        var userIdText = config.CollabUserId;
        if (!Guid.TryParse(userIdText, out var userId) || userId == Guid.Empty)
        {
            userId = Guid.NewGuid();
            config.CollabUserId = userId.ToString();
            UserSettings.Save();
        }

        // A Discord account is the same participant on every machine.
        if (!string.IsNullOrEmpty(config.CollabDiscordUserId))
            userId = CollabUserIdentity.UserIdForDiscordAccount(config.CollabDiscordUserId);

        // Discord identity wins once logged in; the nickname is the local fallback.
        var displayName = !string.IsNullOrWhiteSpace(config.CollabDiscordUserName)
                              ? config.CollabDiscordUserName
                              : (!string.IsNullOrWhiteSpace(config.CollabDisplayName)
                                     ? config.CollabDisplayName
                                     : Environment.UserName);

        _localIdentity = new CollabUserIdentity
                             {
                                 UserId = userId,
                                 DisplayName = TruncateName(displayName),
                                 Source = string.IsNullOrEmpty(config.CollabDiscordUserId) ? "local" : "discord",
                                 Lang = string.IsNullOrEmpty(config.CollabDiscordUserId) ? "" : (config.CollabDiscordLocale ?? ""),
                                 AvatarUrl = BuildAvatarUrl(config.CollabDiscordUserId, config.CollabDiscordUserAvatar),
                             };
    }

    private static string TruncateName(string name)
    {
        name = name?.Trim() ?? "";
        return name.Length <= 48 ? name : name[..48];
    }

    /// <summary>The Discord CDN avatar: the custom image when the account has one, otherwise one
    /// of the default avatars Discord assigns by user id.</summary>
    private static string BuildAvatarUrl(string userId, string avatarHash)
    {
        if (string.IsNullOrEmpty(userId))
            return "";

        if (string.IsNullOrEmpty(avatarHash))
            return ulong.TryParse(userId, out var id) ? $"https://cdn.discordapp.com/embed/avatars/{id % 5}.png" : "";

        return $"https://cdn.discordapp.com/avatars/{userId}/{avatarHash}.png";
    }

    public void StartHost(int tcpPort, string password, bool enableLanDiscovery, string? projectName = null)
    {
        if (!_reconnectAttemptInFlight)
            CancelReconnect();

        LeftManually = false;

        if (!IsEnabled)
        {
            ErrorText = "Collaboration is turned off in the settings.";
            return;
        }

        // A join from a still-Connecting session (the previous host never answered) must not leak
        // its transport: tear down whatever session object still exists, whatever its state.
        if (State != SessionState.Off)
            Leave();

        ++_sessionGeneration;
        ErrorText = "";
        try
        {
            _password = password ?? "";
            _sessionLog.Clear();
            var hostName = TruncateName(_localIdentity.DisplayName);
            _sessionProjectName = projectName
                                  ?? ProjectView.Focused?.OpenedProject.Package.DisplayName
                                  ?? "";
            _transport = new CollabHostTransport(tcpPort,
                                                 enableLanDiscovery,
                                                 () => new JObject
                                                           {
                                                               ["collab"] = true,
                                                               ["name"] = hostName,
                                                               ["host"] = Environment.MachineName,
                                                               ["port"] = tcpPort,
                                                               ["pwd"] = !string.IsNullOrEmpty(_password),
                                                               ["project"] = projectName
                                                                             ?? ProjectView.Focused?.OpenedProject.Package.DisplayName
                                                                             ?? "",
                                                           },
                                                 interfaceName: UserSettings.Config.CollabInterface);
            _transport.PeerDisconnected += OnPeerDisconnectedTransport;
            State = SessionState.Hosting;
            ParticipantsCanEdit = UserSettings.Config.CollabPartyControllable;
            _readOnlyNoticeShownFor.Clear();
            StatusText = $"Hosting on port {tcpPort}";
            Log.Info($"Collab: session started (host, port {tcpPort}, {(string.IsNullOrEmpty(_password) ? "open" : "password protected")})");
        }
        catch (SocketException e)
        {
            ErrorText = $"Could not open port {tcpPort}: {e.Message}";
            StatusText = "Failed to host";
            _transport = null;
            State = SessionState.Off;
        }
    }

    /// <summary>
    /// Hosts through a relay so participants behind NATs can join by room code without port
    /// forwarding. Runs async: state goes Connecting until the relay confirms the room.
    /// </summary>
    /// <param name="requestedRoomCode">Room code to ask the relay for; empty lets it pick one.
    /// Re-opening a room after the relay link dropped asks for the old code so participants
    /// find it again.</param>
    /// <param name="password">Session password; null takes it from the settings.</param>
    /// <param name="roomKey">Key of the room being re-opened; empty makes a new one for a new room.</param>
    public void StartHostViaRelay(string relayHost, int relayPort, string requestedRoomCode = "", string? password = null, string roomKey = "")
    {
        if (!_reconnectAttemptInFlight)
            CancelReconnect();

        LeftManually = false;

        if (!IsEnabled)
        {
            ErrorText = "Collaboration is turned off in the settings.";
            return;
        }

        // A join from a still-Connecting session (the previous host never answered) must not leak
        // its transport: tear down whatever session object still exists, whatever its state.
        if (State != SessionState.Off)
            Leave();

        ErrorText = "";
        State = SessionState.Connecting;
        StatusText = $"Registering room at {relayHost}:{relayPort}...";

        var generation = ++_sessionGeneration;
        var sessionPassword = password ?? UserSettings.Config.CollabPassword ?? "";
        var key = string.IsNullOrEmpty(roomKey) ? Guid.NewGuid().ToString("N") : roomKey;
        var hello = BuildRelayHello(hosting: true, key, sessionPassword);
        var useWebRtc = UserSettings.Config.CollabWebRtcEnabled;
        _ = Task.Run(async () =>
                {
                    try
                    {
                        var transport = useWebRtc
                                            ? await CollabWebRtcHostTransport.ConnectAsync(relayHost, relayPort, requestedRoomCode, CancellationToken.None, hello)
                                            : await CollabRelayHostTransport.ConnectAsync(relayHost, relayPort, requestedRoomCode, CancellationToken.None, hello);
                        _uiThreadActions.Enqueue(() => CompleteRelayHostConnect(transport, generation, sessionPassword, relayHost, relayPort, key));
                    }
                    catch (Exception e)
                    {
                        ReportRelayFailure(generation, "Relay hosting failed", "Failed to host", e);
                    }
                });
    }

    /// <summary>
    /// UI thread: adopts a relay host link. The connect runs on a pool thread, so the user may have
    /// left (or started another session) while it was in flight; a stale completion is discarded
    /// and its room registration released instead of resurrecting the session.
    /// </summary>
    private void CompleteRelayHostConnect(CollabRelayHostTransport transport, int generation, string password, string relayHost, int relayPort,
                                          string roomKey)
    {
        if (generation != _sessionGeneration)
        {
            transport.Dispose();
            return;
        }

        _password = password;
        _sessionLog.Clear();
        transport.PeerDisconnected += OnPeerDisconnectedTransport;
        transport.LobbyWhisperReceived += OnLobbyWhisperTransport;
        transport.StartReading();
        _transport = transport;
        RoomCode = transport.AssignedRoomCode;
        RelayEndpoint = $"{relayHost}:{relayPort}";
        _roomKey = roomKey;
        _currentTarget = new SessionTarget(Hosting: true, Relay: true, relayHost, relayPort, RoomCode, password, roomKey);
        _localVerified = IsVerifiedByRelay(transport.RelayReply, "you");

        // List the session in the web backend's browser too, when one is configured. It carries
        // the relay address so the browser can hand a joiner straight to the room.
        if (CollabWebLobby.IsConfigured)
        {
            var config = UserSettings.Config;
            CollabWebLobby.Register(RoomCode, roomKey, $"{relayHost}:{relayPort}",
                                    string.IsNullOrWhiteSpace(config.CollabSessionTitle)
                                        ? $"{LocalIdentity.DisplayName}'s session"
                                        : config.CollabSessionTitle.Trim(),
                                    config.CollabSessionPurpose ?? "",
                                    ProjectView.Focused?.OpenedProject.Package.DisplayName ?? "",
                                    LocalIdentity.DisplayName, LocalIdentity.Lang,
                                    !string.IsNullOrEmpty(password),
                                    config.CollabListSession,
                                    ParticipantsCanEdit);
        }

        if (_reconnectTarget is { Hosting: true } reopened)
        {
            // Only a relay without room keys answers a taken code with a different one.
            if (!string.Equals(reopened.RoomCode, RoomCode, StringComparison.OrdinalIgnoreCase))
            {
                if (_handover is { } failedTakeover)
                {
                    // A participant taking over must end up in the room the others look for.
                    // Someone else holds it, so go back to joining it.
                    Log.Info("Collab: the session's room is still held by someone else; joining it instead of hosting");
                    failedTakeover.MyRank = -1;
                    _keepReconnectOnLeave = true;
                    try
                    {
                        Leave();
                    }
                    finally
                    {
                        _keepReconnectOnLeave = false;
                    }

                    _reconnectTarget = reopened with { Hosting = false };
                    _nextReconnectTicks = Environment.TickCount64 + 1000;
                    return;
                }

                Log.Warning($"Collab: the relay re-opened the session as room {RoomCode} instead of {reopened.RoomCode}; "
                            + "participants need the new code");
            }
            else
            {
                Log.Info($"Collab: room {RoomCode} is open again");
            }

            if (_handover != null)
                AppendChat(null, "The previous host is gone - you are the host now");

            CancelReconnect();
        }

        if (transport is CollabWebRtcHostTransport rtcHost)
            _videoStreamer = new CollabVideoHostStreamer(rtcHost, SharedFeedTexture);

        State = SessionState.Hosting;
        StatusText = $"Hosting via relay, room {RoomCode}";
        Log.Info($"Collab: hosting via relay {relayHost}:{relayPort}, room {RoomCode}");
    }

    /// <summary>Joins a relay-hosted room by code. The Discord token is not sent: a join hands no
    /// credential to a relay the user does not operate, and the relay link is plaintext. Hosting
    /// still sends it — the host runs the relay — so relay-side verification and the
    /// --require-discord gate keep working there.</summary>
    public void JoinViaRelay(string relayHost, int relayPort, string roomCode, string password, bool sendDiscordToken = false)
    {
        if (!_reconnectAttemptInFlight)
            CancelReconnect();

        LeftManually = false;

        if (!IsEnabled)
        {
            ErrorText = "Collaboration is turned off in the settings.";
            return;
        }

        // A join from a still-Connecting session (the previous host never answered) must not leak
        // its transport: tear down whatever session object still exists, whatever its state.
        if (State != SessionState.Off)
            Leave();

        if (string.IsNullOrWhiteSpace(roomCode))
        {
            ErrorText = "Enter the room code shared by the host";
            return;
        }

        ErrorText = "";
        State = SessionState.Connecting;
        StatusText = $"Joining room {roomCode} via {relayHost}:{relayPort}...";

        var generation = ++_sessionGeneration;
        var passwordToUse = password ?? "";
        var hello = BuildRelayHello(hosting: false, "", passwordToUse, sendDiscordToken);
        var useWebRtc = UserSettings.Config.CollabWebRtcEnabled;
        _ = Task.Run(async () =>
                {
                    try
                    {
                        var transport = useWebRtc
                                            ? await CollabWebRtcClientTransport.ConnectAsync(relayHost, relayPort, roomCode, CancellationToken.None, hello)
                                            : await CollabRelayClientTransport.ConnectAsync(relayHost, relayPort, roomCode, CancellationToken.None, hello);
                        _uiThreadActions.Enqueue(() => CompleteRelayClientConnect(transport, generation, passwordToUse, relayHost, relayPort, roomCode));
                    }
                    catch (Exception e)
                    {
                        ReportRelayFailure(generation, "Relay join failed", "Connection failed", e);
                    }
                });
    }

    private void RequestResyncAfterFallback(int generation)
    {
        if (generation != _sessionGeneration || State != SessionState.Connected || HostLink is not { } host)
            return;

        Log.Info("Collab: data channel lost, asking the host for a fresh state");
        SendJson(host, new { t = "requestSync" });
    }

    /// <summary>UI thread: adopts a joined relay link, unless that join was abandoned meanwhile.</summary>
    private void CompleteRelayClientConnect(CollabRelayClientTransport transport, int generation, string password, string relayHost, int relayPort, string roomCode)
    {
        if (generation != _sessionGeneration)
        {
            transport.Dispose();
            return;
        }

        // Persist the room anchor BEFORE anything can restart us: the project-download restart
        // must come back through the same room, or the client sits idle after its relaunch.
        UserSettings.Config.CollabLastRelayHost = $"{relayHost}:{relayPort}";
        UserSettings.Config.CollabLastRelayRoom = roomCode;
        UserSettings.Config.CollabLastSessionHost = "";
        UserSettings.Save();

        _password = password;
        _sessionLog.Clear();
        _transport = transport;
        _currentTarget = new SessionTarget(Hosting: false, Relay: true, relayHost, relayPort, roomCode, password);
        RelayEndpoint = $"{relayHost}:{relayPort}";
        transport.PeerDisconnected += OnPeerDisconnectedTransport;
        transport.StartReading();
        if (transport is CollabWebRtcClientTransport rtcClient)
        {
            _videoReceiver = new CollabVideoReceiver(rtcClient);
            rtcClient.FellBackToRelay += () => _uiThreadActions.Enqueue(() => RequestResyncAfterFallback(generation));
        }

        SendJson(transport.HostPeer, BuildHello());
        State = SessionState.Connecting;
        StatusText = "Connected to relay, waiting for host...";
    }

    /// <summary>UI thread: surfaces an asynchronous relay failure only for the session that asked.</summary>
    private void ReportRelayFailure(int generation, string errorPrefix, string statusText, Exception exception)
    {
        var message = exception.Message;
        _uiThreadActions.Enqueue(() =>
                                     {
                                         if (generation != _sessionGeneration)
                                             return;

                                         ErrorText = $"{errorPrefix}: {message}";
                                         StatusText = statusText;
                                         State = SessionState.Off;
                                         Log.Warning($"Collab: {errorPrefix.ToLowerInvariant()}: {message}");
                                         OnRelayAttemptFailed(message);
                                     });
    }

    public void JoinDirect(string hostAddress, int tcpPort, string password)
    {
        if (!_reconnectAttemptInFlight)
            CancelReconnect();

        LeftManually = false;

        if (!IsEnabled)
        {
            ErrorText = "Collaboration is turned off in the settings.";
            return;
        }

        // A join from a still-Connecting session (the previous host never answered) must not leak
        // its transport: tear down whatever session object still exists, whatever its state.
        if (State != SessionState.Off)
            Leave();

        ++_sessionGeneration;
        ErrorText = "";
        _joinedHost = $"{hostAddress}:{tcpPort}";
        State = SessionState.Connecting;
        StatusText = $"Connecting to {hostAddress}:{tcpPort}...";

        try
        {
            if (!IPAddress.TryParse(hostAddress, out var address))
                address = ResolveHostName(hostAddress);

            if (address == null)
                throw new ArgumentException($"'{hostAddress}' is not a valid address");

            _password = password ?? "";
            _sessionLog.Clear();
            _currentTarget = new SessionTarget(Hosting: false, Relay: false, hostAddress, tcpPort, "", password ?? "");
            var transport = new CollabClientTransport(new IPEndPoint(address, tcpPort));
            _transport = transport;
            _transport.PeerDisconnected += OnPeerDisconnectedTransport;
            transport.Connect();
            transport.SendJson(transport.HostPeer, BuildHello());
            State = SessionState.Connecting;
        }
        catch (Exception e)
        {
            ErrorText = $"Connection failed: {e.Message}";
            StatusText = "Connection failed";
            _transport?.Dispose();
            _transport = null;
            State = SessionState.Off;
        }
    }

    private static IPAddress? ResolveHostName(string host)
    {
        try
        {
            var addresses = Dns.GetHostAddresses(host);
            return addresses.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork);
        }
        catch (SocketException)
        {
            return null;
        }
    }

    // ---------------------------------------------------------------------------------------
    // Relay-free backend sessions: discovery and WebRTC signaling via the web backend, the
    // session itself rides WebRTC data channels. No relay, no port forwarding.
    // ---------------------------------------------------------------------------------------

    /// <summary>The room-code alphabet the relay uses, so backend codes look the same.</summary>
    private static string NewRoomCode()
    {
        const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        Span<char> code = stackalloc char[6];
        for (var i = 0; i < code.Length; i++)
            code[i] = alphabet[Random.Shared.Next(0, alphabet.Length)];

        return new string(code);
    }

    /// <summary>
    /// Hosts a relay-free session: the web backend lists the room and carries the WebRTC
    /// signaling, the session itself rides data channels. Requires a configured backend.
    /// </summary>
    public void StartHostViaBackend(string autoApproveClaim = "", bool listInBrowser = true)
    {
        if (!_reconnectAttemptInFlight)
            CancelReconnect();

        LeftManually = false;
        ShowControlMode.Activate(ShowControlMode.Collab);

        if (!IsEnabled)
        {
            ErrorText = "Collaboration is turned off in the settings.";
            return;
        }

        if (!CollabWebLobby.IsConfigured)
        {
            ErrorText = "No party backend configured (Settings > Collaboration > Advanced).";
            return;
        }

        if (State != SessionState.Off)
            Leave();

        ++_sessionGeneration;
        ErrorText = "";

        var room = NewRoomCode();
        var key = Guid.NewGuid().ToString("N");
        _password = UserSettings.Config.CollabPassword ?? "";
        _sessionLog.Clear();
        _sessionProjectName = ProjectView.Focused?.OpenedProject.Package.DisplayName ?? "";

        try
        {
            var transport = new CollabBackendWire.CollabBackendHostTransport(room, key, autoApproveClaim);
            transport.JoinRequested += OnJoinRequested;
            _transport = transport;
            _transport.PeerDisconnected += OnPeerDisconnectedTransport;
            RoomCode = room;
            _roomKey = key;
            RelayEndpoint = CollabWebLobby.BackendUrl;
            _currentTarget = new SessionTarget(Hosting: true, Relay: false, CollabWebLobby.BackendUrl, 0, room, _password, key);
            State = SessionState.Hosting;
            ParticipantsCanEdit = UserSettings.Config.CollabPartyControllable;
            IsRemoteManagedSession = autoApproveClaim.Length > 0;

            // Share the selected input operator's texture (a webcam, an NDI or Spout feed) with the
            // party when the preview sharing is on; without a selected texture the streamer falls
            // back to the main output.
            if (transport is ICollabVideoSink sink && UserSettings.Config.CollabSharePreview)
                _videoStreamer = new CollabVideoHostStreamer(sink, SharedFeedTexture);
            _pendingJoinRequests.Clear();
            _readOnlyNoticeShownFor.Clear();
            StatusText = IsRemoteManagedSession
                             ? $"Hosting a managed session, room {room}"
                             : $"Hosting via the backend, room {room}";
            Log.Info($"Collab: backend session started (room {room}{(IsRemoteManagedSession ? ", remote-managed" : "")})");

            var config = UserSettings.Config;
            CollabWebLobby.Register(room, key, "",
                                    string.IsNullOrWhiteSpace(config.CollabSessionTitle)
                                        ? $"{LocalIdentity.DisplayName}'s session"
                                        : config.CollabSessionTitle.Trim(),
                                    config.CollabSessionPurpose ?? "",
                                    _sessionProjectName,
                                    LocalIdentity.DisplayName, LocalIdentity.Lang,
                                    !string.IsNullOrEmpty(_password),
                                    listInBrowser && config.CollabListSession,
                                    ParticipantsCanEdit);

        }
        catch (Exception e)
        {
            ErrorText = $"Backend hosting failed: {e.Message}";
            CollabRemote.ReportFailure(e.Message);
            StatusText = "Failed to host";
            _transport?.Dispose();
            _transport = null;
            State = SessionState.Off;
        }
    }

    /// <summary>Joins a backend-hosted room: a join ticket, a WebRTC handshake through the
    /// backend, then the session runs on the data channel. Async; state Connecting meanwhile.</summary>
    public void JoinViaBackend(string room, string claim = "")
    {
        if (!_reconnectAttemptInFlight)
            CancelReconnect();

        LeftManually = false;

        if (!IsEnabled)
        {
            ErrorText = "Collaboration is turned off in the settings.";
            return;
        }

        if (!CollabWebLobby.IsConfigured)
        {
            ErrorText = "No party backend configured (Settings > Collaboration > Advanced).";
            return;
        }

        if (State != SessionState.Off)
            Leave();

        var generation = ++_sessionGeneration;
        ErrorText = "";
        _joinedHost = CollabWebLobby.BackendUrl;
        State = SessionState.Connecting;
        StatusText = $"Joining room {room} via the backend...";

        _password = UserSettings.Config.CollabPassword ?? "";
        _joinClaim = claim ?? "";
        _sessionLog.Clear();
        _currentTarget = new SessionTarget(Hosting: false, Relay: false, CollabWebLobby.BackendUrl, 0, room, _password, "");

        _ = Task.Run(async () =>
                         {
                             try
                             {
                                 var transport = new CollabBackendWire.CollabBackendClientTransport(room, TruncateName(LocalIdentity.DisplayName), LocalIdentity.Lang, _joinClaim);
                                 await transport.ConnectAsync(CancellationToken.None);
                                 _uiThreadActions.Enqueue(() => CompleteBackendJoin(transport, generation, room));
                             }
                             catch (Exception e)
                             {
                                 var message = e.Message;
                                 _uiThreadActions.Enqueue(() =>
                                                          {
                                                              if (generation != _sessionGeneration)
                                                                  return;

                                                              ErrorText = $"Join failed: {message}";
                                                              StatusText = "Join failed";
                                                              _transport?.Dispose();
                                                              _transport = null;
                                                              State = SessionState.Off;
                                                          });
                             }
                         });
    }

    /// <summary>
    /// The project a party should share. The focused view is unreliable right after a programmatic
    /// open, so this also looks at opened projects and, last, every graph window.
    /// </summary>
    private static SymbolPackage? FindAnyOpenedProject()
    {
        if (ProjectView.Focused?.OpenedProject?.Package is { } focused)
            return focused;

        if (OpenedProject.OpenedProjects.Values.FirstOrDefault()?.Package is { } opened)
            return opened;

        return WindowManager.GetAllWindows()
                            .OfType<T3.Editor.Gui.Window.GraphWindow>()
                            .Select(w => w.ProjectView?.OpenedProject?.Package)
                            .FirstOrDefault(p => p != null);
    }

    /// <summary>
    /// The operator whose video the party streams (a webcam, NDI or Spout input). Chosen
    /// explicitly - the graph selection is transient - and resolved by child id each frame.
    /// </summary>
    public string SharedFeedName { get; private set; } = "";

    private Guid _sharedFeedChildId;

    /// <summary>Host: shares the currently selected operator's texture output with the party.</summary>
    public bool ShareSelectedOperatorAsFeed()
    {
        var view = FindFocusedOrAnyView();
        var childId = view?.NodeSelection?.GetSelectionSymbolChildId();
        if (view?.CompositionInstance == null || childId == null)
            return false;

        if (!view.CompositionInstance.Children.TryGetChildInstance(childId.Value, out var instance) || instance == null)
            return false;

        _sharedFeedChildId = childId.Value;
        SharedFeedName = instance.Symbol.Name;
        UserSettings.Config.CollabSharePreview = true;
        UserSettings.Save();
        AppendChat(null, $"Sharing {SharedFeedName} as the party video feed");
        Log.Info($"Collab: sharing '{SharedFeedName}' ({childId}) as the video feed");
        return true;
    }

    /// <summary>Host: shares one operator by child id (used by the debug bridge).</summary>
    public bool ShareOperatorAsFeed(Guid childId)
    {
        var view = FindFocusedOrAnyView();
        if (view?.CompositionInstance == null)
            return false;

        if (!view.CompositionInstance.Children.TryGetChildInstance(childId, out var instance) || instance == null)
            return false;

        _sharedFeedChildId = childId;
        SharedFeedName = instance.Symbol.Name;
        UserSettings.Config.CollabSharePreview = true;
        UserSettings.Save();
        AppendChat(null, $"Sharing {SharedFeedName} as the party video feed");
        return true;
    }

    public void StopSharingFeed()
    {
        _sharedFeedChildId = Guid.Empty;
        SharedFeedName = "";
        AppendChat(null, "The party video feed is stopped");
    }

    private static ProjectView? FindFocusedOrAnyView()
    {
        var view = ProjectView.Focused;
        if (view?.CompositionInstance != null)
            return view;

        return WindowManager.GetAllWindows()
                            .OfType<T3.Editor.Gui.Window.GraphWindow>()
                            .Select(w => w.ProjectView)
                            .FirstOrDefault(v => v?.CompositionInstance != null);
    }

    /// <summary>The texture the party streams: the chosen operator's first Texture2D output.</summary>
    private T3.Core.DataTypes.Texture2D? SharedFeedTexture()
    {
        if (_sharedFeedChildId == Guid.Empty)
            return null;

        var view = FindFocusedOrAnyView();
        if (view?.CompositionInstance == null)
            return null;

        if (!view.CompositionInstance.Children.TryGetChildInstance(_sharedFeedChildId, out var instance) || instance == null)
        {
            DiagFeed($"child {_sharedFeedChildId} not in the focused instance");
            return null;
        }

        foreach (var slot in instance.Outputs)
        {
            if (slot is T3.Core.Operator.Slots.Slot<T3.Core.DataTypes.Texture2D> textureSlot)
            {
                if (textureSlot.Value == null)
                    DiagFeed($"{instance.Symbol.Name} texture output is null");
                return textureSlot.Value;
            }
        }

        DiagFeed($"{instance.Symbol.Name} outputs: {string.Join(",", instance.Outputs.Select(o => o.ValueType.Name))}");
        return null;
    }

    private static long _lastFeedDiagTicks;

    private static void DiagFeed(string message)
    {
        if (Environment.TickCount64 - _lastFeedDiagTicks < 3000)
            return;

        _lastFeedDiagTicks = Environment.TickCount64;
        Log.Debug($"Collab feed: {message}");
    }


    private void CompleteBackendJoin(CollabBackendWire.CollabBackendClientTransport transport, int generation, string room)
    {
        // The user may have left or started another session while the handshake ran.
        if (generation != _sessionGeneration)
        {
            transport.Dispose();
            return;
        }

        _sessionLog.Clear();
        _transport = transport;
        _transport.PeerDisconnected += OnPeerDisconnectedTransport;
        RoomCode = room;
        RelayEndpoint = CollabWebLobby.BackendUrl;
        _roomKey = "";
        IsRemoteManagedSession = _joinClaim.Length > 0;

        // The data channel is open: the hello goes out now and authentication runs on it.
        // The state must stay Connecting until authOk (HandleClientAuthOk flips it to Connected);
        // jumping ahead here made the guest drop its own authOk and stay unauthenticated.
        if (transport is ICollabVideoSource videoSource)
            _videoReceiver = new CollabVideoReceiver(videoSource);

        transport.SendJson(transport.HostLink!, BuildHello());
        State = SessionState.Connecting;
        StatusText = "Connected, authenticating...";
        Log.Info($"Collab: joined backend room {room} - authenticating");

        // Remember the room so a restart (e.g. after a project download) rejoins it.
        UserSettings.Config.CollabLastBackendRoom = room;
        UserSettings.Save();
    }

    /// <summary>
    /// Ends the session. <paramref name="manual"/> marks a leave the user asked for: the launch
    /// auto-rejoin then stays quiet instead of dragging the session back, which made Leave look
    /// broken. Starting or joining a session clears the mark again.
    /// </summary>
    public void Leave(bool manual = false)
    {
        LeftManually = manual;
        IsRemoteManagedSession = false;
        _joinClaim = "";

        // Every leave except the one a dropped link causes ends the wish to be in the session.
        if (!_keepReconnectOnLeave)
        {
            CancelReconnect();

            // Tell participants this is the end, so they do not spend a minute trying to come
            // back to a session that is gone for good.
            if (State == SessionState.Hosting)
                BroadcastSystemMessage(new { t = "sessionEnded" }, except: null);

            // Delete entry: the listing goes away even though the room key is about to be cleared.
            if (State == SessionState.Hosting && RoomCode.Length > 0 && _roomKey.Length > 0)
                CollabWebLobby.Unregister(RoomCode, _roomKey);
            _whisperSince = 0;
            _pendingJoinRequests.Clear();
            WindowManager.CollabJoinRequestWindow.Config.Visible = false;
            ParticipantsCanEdit = true;
            ReadOnlyForParticipants = false;
            UserSettings.Config.CollabLastBackendRoom = "";
        }

        _currentTarget = null;
        _loneTakeoverOrigin = null;
        _loneTakeoverProbe = null;

        // Anything still connecting becomes stale; its completion is discarded.
        ++_sessionGeneration;

        _videoStreamer?.Dispose();
        _videoStreamer = null;
        _videoReceiver?.Dispose();
        _videoReceiver = null;

        if (_transport != null)
        {
            _transport.PeerDisconnected -= OnPeerDisconnectedTransport;
            _transport.Dispose();
            _transport = null;
        }

        _sessionLog.Clear();
        _hostSeq = 0;
        _lastAppliedSeq = -1;
        _nextLocalOpId = 0;
        _pendingLocalOps.Clear();
        _touchedCompositions.Clear();
        _nextStateHashTicks = 0;
        _nextFullResyncAllowedTicks = 0;
        _syncInProgress = false;
        _hashMismatchStreak.Clear();
        _healRequested.Clear();
        _healAwaitingVerification.Clear();
        _unhealable.Clear();
        _opsReceivedThisSecond.Clear();
        _pendingAuth.Clear();
        _chatLog.Clear();
        foreach (var upload in _projectUploads.Values)
            upload.Sender?.Dispose();

        _projectUploads.Clear();
        _projectDownload?.Cancel();
        _projectDownload = null;
        _hostProjectName = "";
        _sessionProjectName = "";
        UserSettings.Config.CollabLastSessionHost = "";
        UserSettings.Config.CollabLastRelayHost = "";
        UserSettings.Config.CollabLastRelayRoom = "";
        UserSettings.Save();
        _pendingKicks.Clear();
        while (_pendingDisconnects.TryDequeue(out _))
        {
            // Drop disconnect events from the dead session; replaying them into a future
            // session would remove peers it never had.
        }
        _localPresence = new PresencePacket();
        _password = "";
        RoomCode = "";
        RelayEndpoint = "";
        _undoStacks.Clear();
        _redoStacks.Clear();
        _activeLocks.Clear();
        UndoNotice = "";
        // A session's participants must not survive into the next one. Per-peer disconnects do not
        // run for every peer on teardown, so clear the roster here or stale cursors/participants
        // reappear as ghosts after reconnecting.
        _roster.Clear();
        _rosterUserIds.Clear();
        _participants.Clear();
        _rosterAvatars.Clear();
        _peerPingMs.Clear();
        _localVerified = false;
        _roomKey = "";
        _latestPresenceByPeer.Clear();
        MyPeerId = 0;

        // The local undo stack only contains this machine's edits; after a session it no longer
        // matches the shared graph state, so replaying it would corrupt the project.
        T3.Editor.UiModel.Commands.UndoRedoStack.Clear();

        State = SessionState.Off;
        StatusText = "Collaboration is off";
        Log.Info("Collab: session ended");
    }

    // ---------------------------------------------------------------------------------------
    // Automatic reconnect
    // ---------------------------------------------------------------------------------------

    private void CancelReconnect()
    {
        _reconnectTarget = null;
        _reconnectAttempts = 0;
        _reconnectProbe = null;
        _reconnectDial = null;
        _handover = null;
        _pendingHandoff = null;
        _opsToResend.Clear();
    }

    /// <summary>
    /// Ends the dead transport but keeps the intent: the session's target is retried from
    /// <see cref="UpdateReconnect"/>. Returns false when there is nothing to come back to.
    /// Also called for a failed attempt, which only schedules the next one.
    /// </summary>
    private bool TryBeginReconnect(string statusText)
    {
        var firstLoss = _reconnectTarget == null;
        var target = _reconnectTarget ?? _currentTarget;
        if (target == null || !IsEnabled)
            return false;

        // A backend session has no dial target to retry (the WebRTC handshake ran through the
        // backend's tickets); a fresh join is the only way back. Ends cleanly instead.
        if (target is { Relay: false, Port: 0 })
            return false;

        List<Guid>? editedCompositions = null;
        if (firstLoss)
        {
            // A re-opened room is a new session for the transport, but participants coming back
            // still need every composition edited so far - from the old host, or from the
            // participant who takes the session over.
            editedCompositions = _touchedCompositions.ToList();
            if (!target.Hosting)
            {
                foreach (var pending in _pendingLocalOps)
                    _opsToResend.Add(pending.Op);

                // The roster is gone after Leave(); who is next in line has to be settled now.
                _handover ??= BuildHandoverPlan();
            }
        }

        _keepReconnectOnLeave = true;
        try
        {
            Leave();
        }
        finally
        {
            _keepReconnectOnLeave = false;
        }

        if (firstLoss)
        {
            _reconnectTarget = target;
            _reconnectAttempts = 0;
            _nextReconnectTicks = Environment.TickCount64 + 2000;
            if (editedCompositions != null)
            {
                foreach (var compositionId in editedCompositions)
                    _touchedCompositions.Add(compositionId);
            }

            Log.Warning($"Collab: {statusText}");
        }

        StatusText = statusText;
        return true;
    }

    private void UpdateReconnect()
    {
        if (_reconnectTarget is not { } target || State != SessionState.Off)
            return;

        if (!IsEnabled)
        {
            CancelReconnect();
            return;
        }

        if (_reconnectProbe is { } probe)
        {
            if (!probe.IsCompleted)
                return;

            _reconnectProbe = null;
            if (probe.IsCompletedSuccessfully && probe.Result)
                StartReconnectAttempt(_reconnectDial ?? target);

            return;
        }

        var now = Environment.TickCount64;

        // This participant's turn to open the session again.
        if (!target.Hosting && _handover is { MyRank: >= 0 } plan && now >= TakeoverTicks(plan))
        {
            TakeOverAsHost(target, plan);
            return;
        }

        if (now < _nextReconnectTicks)
            return;

        if (_reconnectAttempts >= MaxReconnectAttempts)
        {
            CancelReconnect();
            ErrorText = target.Hosting
                            ? "Lost the connection to the relay and could not re-open the room"
                            : "Lost the connection to the session and could not reconnect";
            StatusText = "Collaboration is off";
            Log.Warning($"Collab: {ErrorText}");
            return;
        }

        _reconnectAttempts++;
        _nextReconnectTicks = now + Math.Min(1000L << Math.Min(_reconnectAttempts, 4), 15_000);
        StatusText = $"Connection lost - reconnecting ({_reconnectAttempts}/{MaxReconnectAttempts})...";

        if (target is { Hosting: false, Relay: false })
        {
            var dial = ChooseDirectDialTarget(target, now);
            _reconnectDial = dial;
            var host = dial.Host;
            var port = dial.Port;
            _reconnectProbe = Task.Run(() => ProbeDirectHost(host, port));
            return;
        }

        StartReconnectAttempt(target);
    }

    private static long TakeoverTicks(HandoverPlan plan)
    {
        return plan.LossTicks + (plan.Appointed ? AppointedTakeoverDelayMs : HandoverBaseDelayMs + plan.MyRank * (long)HandoverStepMs);
    }

    /// <summary>Client, at the moment the host is lost: who takes over, in which order, and where
    /// to find them in a direct session.</summary>
    private HandoverPlan BuildHandoverPlan()
    {
        var plan = new HandoverPlan
                       {
                           LossTicks = Environment.TickCount64,
                           ProjectName = _hostProjectName,
                       };

        var ids = new List<int> { MyPeerId };
        foreach (var peerId in _roster.Keys)
        {
            // Session id 0 is the host that was just lost.
            if (peerId != 0 && peerId != MyPeerId)
                ids.Add(peerId);
        }

        ids.Sort();
        foreach (var peerId in ids)
        {
            var info = GetParticipantInfo(peerId);
            plan.Candidates.Add(new HandoverCandidate(peerId, info.Address ?? "", info.Port));
        }

        plan.MyRank = MyPeerId == 0 ? -1 : ids.IndexOf(MyPeerId);
        return plan;
    }

    /// <summary>
    /// Direct sessions have no shared meeting point: the old host is tried first, and once a
    /// participant ahead in line had time to take over, its address too, in turn.
    /// </summary>
    private SessionTarget ChooseDirectDialTarget(SessionTarget target, long now)
    {
        if (_handover is not { Appointed: false } plan)
            return target;

        var options = new List<SessionTarget> { target };
        for (var index = 0; index < plan.Candidates.Count; index++)
        {
            var candidate = plan.Candidates[index];
            if (candidate.PeerId == MyPeerId || string.IsNullOrEmpty(candidate.Address) || candidate.Port <= 0)
                continue;

            var opensAt = plan.LossTicks + HandoverBaseDelayMs + index * (long)HandoverStepMs + 3000;
            if (now >= opensAt)
                options.Add(target with { Host = candidate.Address, Port = candidate.Port });
        }

        return options[_reconnectAttempts % options.Count];
    }

    private void TakeOverAsHost(SessionTarget target, HandoverPlan plan)
    {
        Log.Warning("Collab: the host is gone - opening the session again as its new host");
        if (target.Relay)
        {
            // The answer arrives in CompleteRelayHostConnect or OnRelayAttemptFailed.
            _reconnectTarget = target with { Hosting = true };
            _reconnectAttemptInFlight = true;
            try
            {
                StartHostViaRelay(target.Host, target.Port, target.RoomCode, target.Password, target.RoomKey);
            }
            finally
            {
                _reconnectAttemptInFlight = false;
            }

            if (State != SessionState.Off)
                StatusText = "Taking over the session as host...";

            return;
        }

        _reconnectAttemptInFlight = true;
        try
        {
            StartHost(UserSettings.Config.CollabPort,
                      target.Password,
                      UserSettings.Config.CollabLanDiscovery,
                      plan.ProjectName.Length > 0 ? plan.ProjectName : null);
        }
        finally
        {
            _reconnectAttemptInFlight = false;
        }

        if (State == SessionState.Hosting)
        {
            CancelReconnect();
            AppendChat(null, "The previous host is gone - you are the host now");
            if (!plan.Appointed)
            {
                _loneTakeoverOrigin = target;
                _nextLoneTakeoverProbeTicks = Environment.TickCount64 + LoneTakeoverProbeIntervalMs;
            }
        }
        else
        {
            // The port could not be opened here; stay in line as a participant.
            plan.MyRank = -1;
        }
    }

    /// <summary>See <see cref="_loneTakeoverOrigin"/>.</summary>
    private void UpdateLoneTakeoverWatch()
    {
        if (_loneTakeoverOrigin is not { } origin || State != SessionState.Hosting)
            return;

        var now = Environment.TickCount64;
        if (_loneTakeoverProbe is { } probe)
        {
            if (!probe.IsCompleted)
                return;

            _loneTakeoverProbe = null;
            _nextLoneTakeoverProbeTicks = now + LoneTakeoverProbeIntervalMs;
            if (!probe.IsCompletedSuccessfully || !probe.Result || ActivePeerCount > 0)
                return;

            Log.Info("Collab: the original host answers again; going back to its session");
            _keepReconnectOnLeave = true;
            try
            {
                Leave();
            }
            finally
            {
                _keepReconnectOnLeave = false;
            }

            CancelReconnect();
            _reconnectTarget = origin;
            _nextReconnectTicks = now;
            StatusText = "The original host is back - rejoining...";
            return;
        }

        if (now < _nextLoneTakeoverProbeTicks)
            return;

        var host = origin.Host;
        var port = origin.Port;
        _loneTakeoverProbe = Task.Run(() => ProbeDirectHost(host, port));
    }

    /// <summary>UI thread: a relay attempt of the reconnect failed with this message.</summary>
    private void OnRelayAttemptFailed(string message)
    {
        if (_reconnectTarget is not { Hosting: true } hostTarget)
            return;

        var roomHasHost = message.Contains(CollabRelayWire.RoomHasLiveHostError, StringComparison.OrdinalIgnoreCase);
        var now = Environment.TickCount64;

        if (_handover is { } plan)
        {
            // A participant tried to take over.
            _reconnectTarget = hostTarget with { Hosting = false };
            if (roomHasHost && plan is { Appointed: true, TakeoverRetriesLeft: > 0 })
            {
                // The old host announced the handover but has not let go of the room yet.
                plan.TakeoverRetriesLeft--;
                plan.LossTicks = now - AppointedTakeoverDelayMs + 1000;
            }
            else if (roomHasHost)
            {
                // The host is back, or someone ahead in line was faster: join them.
                ErrorText = "";
                plan.MyRank = -1;
                _nextReconnectTicks = now + 500;
            }
            else
            {
                // The relay did not answer; try again shortly.
                plan.LossTicks += 5000 + Math.Max(0, now - TakeoverTicks(plan));
            }

            return;
        }

        if (roomHasHost)
        {
            // This was the host, and while it was away a participant took the session over.
            Log.Info("Collab: a participant took over the session meanwhile; joining it as a participant");
            ErrorText = "";
            _reconnectTarget = hostTarget with { Hosting = false };
            _reconnectAttempts = 0;
            _nextReconnectTicks = now + 500;
        }
    }

    /// <summary>
    /// Host: hands the session to a participant. Everyone is told who the new host is; this
    /// editor then leaves as host and joins the new host as a participant.
    /// </summary>
    public bool TransferHostTo(int peerId)
    {
        if (State != SessionState.Hosting || _pendingHandoff != null)
            return false;

        CollabPeerConnection? successor = null;
        foreach (var peer in Peers)
        {
            if (peer.AssignedId == peerId && peer.IsAuthenticated)
                successor = peer;
        }

        if (successor == null)
            return false;

        SessionTarget joinTarget;
        var address = "";
        var port = 0;
        if (_currentTarget is { Relay: true } relayTarget)
        {
            joinTarget = relayTarget with { Hosting = false };
        }
        else
        {
            // A direct session moves to the successor's machine; everyone needs its address.
            if (successor.RemoteAddress == null)
                return false;

            address = successor.RemoteAddress.ToString();
            port = successor.ListenPort > 0 ? successor.ListenPort : CollabProtocol.DefaultTcpPort;
            joinTarget = new SessionTarget(Hosting: false, Relay: false, address, port, "", _password);
        }

        BroadcastSystemMessage(new JObject
                                   {
                                       ["t"] = "hostTransfer",
                                       ["to"] = peerId,
                                       ["addr"] = address,
                                       ["port"] = port,
                                   }, except: null);

        // Leave a moment later, so the announcement is out before the links close.
        _pendingHandoff = (Environment.TickCount64 + 500, joinTarget, successor.Name);
        StatusText = $"Handing the session over to {successor.Name}...";
        Log.Info($"Collab: handing the session over to {successor.Name}");
        return true;
    }

    /// <summary>Host: completes a handover - stop hosting without ending the session, then join the new host.</summary>
    private void UpdatePendingHandoff()
    {
        if (_pendingHandoff is not { } handoff || Environment.TickCount64 < handoff.AtTicks)
            return;

        _keepReconnectOnLeave = true;
        try
        {
            Leave();
        }
        finally
        {
            _keepReconnectOnLeave = false;
        }

        CancelReconnect();
        _reconnectTarget = handoff.JoinTarget;
        _nextReconnectTicks = Environment.TickCount64 + 3000;
        StatusText = $"Joining {handoff.Name}'s session...";
    }

    /// <summary>Participant: the host named its successor. The successor opens the session
    /// right away; everyone else goes looking for it there.</summary>
    private void HandleHostTransfer(JObject json)
    {
        if (_currentTarget is not { Hosting: false } target || !CollabProtocol.TryGetInt(json, "to", out var successorId))
            return;

        var iAmSuccessor = successorId == MyPeerId;
        var successorName = _roster.TryGetValue(successorId, out var name) ? name : "another participant";
        if (!target.Relay && !iAmSuccessor)
        {
            var address = CollabProtocol.GetString(json, "addr");
            if (string.IsNullOrEmpty(address) || !CollabProtocol.TryGetInt(json, "port", out var port) || port <= 0)
                return;

            _currentTarget = target with { Host = address, Port = port };
        }

        _handover = new HandoverPlan
                        {
                            Appointed = true,
                            LossTicks = Environment.TickCount64,
                            ProjectName = _hostProjectName,
                            MyRank = iAmSuccessor ? 0 : -1,
                        };

        var status = iAmSuccessor ? "The host handed the session to you..." : $"The session is moving to {successorName}...";
        if (!TryBeginReconnect(status))
            _handover = null;
    }

    private void StartReconnectAttempt(SessionTarget target)
    {
        Log.Info($"Collab: reconnect attempt {_reconnectAttempts}/{MaxReconnectAttempts}");
        _reconnectAttemptInFlight = true;
        try
        {
            if (target.Hosting)
            {
                StartHostViaRelay(target.Host, target.Port, target.RoomCode, target.Password, target.RoomKey);
            }
            else if (target.Relay)
            {
                JoinViaRelay(target.Host, target.Port, target.RoomCode, target.Password);
            }
            else
            {
                JoinDirect(target.Host, target.Port, target.Password);
            }
        }
        finally
        {
            _reconnectAttemptInFlight = false;
        }
    }

    /// <summary>Pool thread: whether the host accepts a connection at all.</summary>
    private static bool ProbeDirectHost(string host, int port)
    {
        try
        {
            if (!IPAddress.TryParse(host, out var address))
                address = ResolveHostName(host);

            if (address == null)
                return false;

            using var client = CollabProtocol.ConnectWithTimeout(new IPEndPoint(address, port));
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>An edit made while the link is down is applied locally only. Queue it for the
    /// host; a host re-opening its room just notes the composition for the next state sync.</summary>
    private void KeepEditForReconnect(ICommand command)
    {
        if (_reconnectTarget is not { } target)
            return;

        var op = CollabCommandWire.EncodeCommand(command);
        if (op == null)
            return;

        if (target.Hosting)
            CollectCompositionIds(op, _touchedCompositions);
        else if (_opsToResend.Count < MaxPendingLocalOps)
            _opsToResend.Add(op);
    }

    /// <summary>Client, right after rejoining: sends the edits the host never confirmed. Ops are
    /// safe to apply twice, so one the host did receive before the link died does no harm.</summary>
    private void ResendOpsAfterReconnect(CollabPeerConnection host)
    {
        if (_reconnectTarget == null)
            return;

        Log.Info($"Collab: reconnected, sending {_opsToResend.Count} unconfirmed edit(s) again");
        foreach (var op in _opsToResend)
        {
            var clientOpId = ++_nextLocalOpId;
            _pendingLocalOps.Add(new PendingLocalOp(clientOpId, op));
            SendJson(host, new JObject
                               {
                                   ["t"] = "op",
                                   ["cid"] = clientOpId,
                                   ["op"] = op.DeepClone(),
                               });
        }

        CancelReconnect();
    }

    private void OnPeerDisconnectedTransport(CollabPeerConnection peer)
    {
        // Queued so the manager handles it on the UI thread with everything else.
        _pendingDisconnects.Enqueue(peer);
    }

    private readonly ConcurrentQueue<CollabPeerConnection> _pendingDisconnects = new();

    /// <summary>A kicked peer is told first and closed a moment later so the message flushes.</summary>
    private readonly List<(CollabPeerConnection Peer, long CloseAtTicks)> _pendingKicks = [];
    private const int KickCloseDelayMs = 600;

    /// <summary>Session participant names by session id, for display on hosts and clients alike.</summary>
    private readonly Dictionary<int, string> _roster = new();
    public IReadOnlyDictionary<int, string> Roster => _roster;

    /// <summary>Discord avatar URLs by session id (empty when the participant is not logged in).</summary>
    private readonly Dictionary<int, string> _rosterAvatars = new();

    /// <summary>Last measured round-trip time to a peer, in milliseconds (0 = not measured yet).</summary>
    private readonly Dictionary<int, int> _peerPingMs = new();
    private long _nextPingTicks;

    /// <summary>The participant's Discord avatar URL, or "" when they have none.</summary>
    public string GetPeerAvatarUrl(int peerId)
        => _rosterAvatars.TryGetValue(peerId, out var url) ? url : "";

    /// <summary>My own Discord avatar URL, or "" when not logged in.</summary>
    public string LocalAvatarUrl => _localIdentity.AvatarUrl;

    /// <summary>The participant's Discord locale ("en-US"), or "" when unknown.</summary>
    public string GetPeerLang(int peerId)
        => _participants.TryGetValue(peerId, out var info) ? info.Lang : "";

    /// <summary>Last measured round-trip time to a peer in milliseconds, or 0 when not measured yet.</summary>
    public int GetPeerPingMs(int peerId)
        => _peerPingMs.TryGetValue(peerId, out var ms) ? ms : 0;

    /// <summary>
    /// Pins a participant's work into my output window, rendering it locally from the synced
    /// project — no video stream involved. Shows the composition they are working in, or their
    /// selected operator when they have one.
    /// </summary>
    public bool PinPeerWork(int peerId)
    {
        if (GetPresence(peerId) is not { } presence || presence.CompositionId == Guid.Empty)
        {
            Log.Debug($"Collab pin: peer {peerId} has no presence with a composition");
            return false;
        }

        if (!TryFindCompositionInstance(presence.CompositionId, out var compositionInstance, out var view))
        {
            Log.Debug($"Collab pin: composition {presence.CompositionId} not found in the local project");
            return false;
        }

        var pinned = compositionInstance;
        foreach (var selectedChildId in presence.SelectedChildIds)
        {
            if (compositionInstance.Children.TryGetChildInstance(selectedChildId, out var child, allowCreate: false) && child != null)
            {
                pinned = child;
                break;
            }
        }

        if (!T3.Editor.Gui.Windows.Output.OutputWindow.TryGetPrimaryOutputWindow(out var outputWindow))
        {
            Log.Debug("Collab pin: no output window is open to pin into");
            return false;
        }

        outputWindow.Pinning.PinInstance(pinned, view);
        Log.Debug($"Collab pin: pinned {pinned.Symbol.Name} for peer {peerId}");
        return true;
    }

    /// <summary>Finds an instance of the given composition symbol among the open graphs, breadth-first.</summary>
    private static bool TryFindCompositionInstance(Guid compositionId, out T3.Core.Operator.Instance found, out ProjectView? view)
    {
        found = null!;
        view = null;
        var queue = new Queue<(T3.Core.Operator.Instance Instance, ProjectView? View)>();
        var visited = new HashSet<Guid>();

        foreach (var window in WindowManager.GetAllWindows().OfType<T3.Editor.Gui.Window.GraphWindow>())
        {
            if (window.ProjectView?.CompositionInstance is { } compositionInstance)
                queue.Enqueue((compositionInstance, window.ProjectView));
        }

        if (queue.Count == 0 && ProjectView.Focused?.CompositionInstance is { } focused)
            queue.Enqueue((focused, ProjectView.Focused));

        while (queue.Count > 0)
        {
            var (instance, projectView) = queue.Dequeue();
            if (instance.Symbol.Id == compositionId)
            {
                found = instance;
                view = projectView;
                return true;
            }

            if (!visited.Add(instance.Symbol.Id))
                continue; // recursive graphs would loop forever

            foreach (var child in ((IReadOnlyDictionary<Guid, T3.Core.Operator.Instance>)instance.Children).Values)
                queue.Enqueue((child, projectView));
        }

        return false;
    }

    public string DescribePeerLink(CollabPeerConnection peer) => _transport?.DescribePeerLink(peer) ?? "";

    public string DescribePeerLink(int peerId)
    {
        foreach (var peer in Peers)
        {
            if (peer.AssignedId == peerId)
                return DescribePeerLink(peer);
        }

        return "";
    }

    /// <summary>
    /// Round trip to one participant in ms, or -1 while none of our heartbeats has come back yet.
    /// Relay-backed peers include the relay hop; peers on a data channel measure the direct path.
    /// </summary>
    public int GetPeerRttMs(int peerId)
    {
        foreach (var peer in Peers)
        {
            if (peer.AssignedId == peerId)
                return peer.LastRttMs;
        }

        return -1;
    }

    /// <summary>
    /// Host-side removal of one participant. The peer is told first so a current client can leave
    /// cleanly; closing the connection then enforces it for clients that ignore the message.
    /// There is no ban: the participant can rejoin unless the session password keeps them out.
    /// </summary>
    public bool KickParticipant(int peerId)
    {
        if (State != SessionState.Hosting || peerId == 0)
            return false;

        CollabPeerConnection? target = null;
        foreach (var peer in Peers)
        {
            if (peer.AssignedId != peerId)
                continue;

            target = peer;
            break;
        }

        if (target is not { IsAuthenticated: true })
            return false;

        // Ride the relay/socket rather than a preferred data channel: that write is flushed before
        // the close below, and the client accepts this one relay message even while P2P is active.
        _transport?.SendJsonOverRelay(target, new { t = "kicked", reason = "Removed by the host" });
        Log.Info($"Collab: removed {target.Name}#{peerId} from the session");
        _pendingKicks.Add((target, Environment.TickCount64 + KickCloseDelayMs));
        return true;
    }



    /// <summary>User ids by session id; seeds participant colors on the client side too.</summary>
    private readonly Dictionary<int, Guid> _rosterUserIds = new();

    /// <summary>Latest presence per session id, kept separately so clients can render peers they
    /// only know from relayed presence frames.</summary>
    private readonly Dictionary<int, PresencePacket> _latestPresenceByPeer = new();

    /// <summary>Display info about a session participant, or null when nothing is known yet.</summary>
    public CollabPeerDisplay? GetPresence(int peerId)
    {
        PresencePacket? presence = null;
        var userId = Guid.Empty;
        var foundInTransport = false;

        for (var i = 0; i < Peers.Count; i++)
        {
            if (Peers[i].AssignedId != peerId)
                continue;

            presence = Peers[i].LatestPresence;
            userId = Peers[i].UserId;
            foundInTransport = true;
            break;
        }

        if (!foundInTransport)
        {
            presence = _latestPresenceByPeer.TryGetValue(peerId, out var stored) ? stored : null;
            userId = _rosterUserIds.TryGetValue(peerId, out var id) ? id : Guid.Empty;
        }

        if (userId == Guid.Empty)
            return null;

        return new CollabPeerDisplay
                   {
                       UserId = userId,
                       CompositionId = presence?.CompositionId ?? Guid.Empty,
                       IsHoveringCanvas = presence?.IsHoveringCanvas ?? false,
                       CursorOnCanvas = presence?.CursorOnCanvas ?? Vector2.Zero,
                       AccentColor = CollabColors.FromPacked(presence?.ColorRgb ?? 0, userId),
                       LeftButtonDown = presence?.IsLeftButtonDown ?? false,
                       RightButtonDown = presence?.IsRightButtonDown ?? false,
                       MiddleButtonDown = presence?.IsMiddleButtonDown ?? false,
                       IsDoubleClick = presence?.IsDoubleClick ?? false,
                       IsAddingOperator = presence?.IsAddingOperator ?? false,
                       WheelSpin = presence?.WheelSpin ?? 0,
                       PressedKeys = presence?.PressedKeys ?? [],
                       SelectedChildIds = presence?.SelectedChildIds ?? [],
                   };
    }

    /// <summary>Session id assigned to this editor by the host (0 on the host itself).</summary>
    public int MyPeerId { get; private set; }

    /// <summary>All other participants currently known to be inside the given composition.</summary>
    public IEnumerable<(string Name, CollabPeerDisplay Display)> GetPeersInComposition(Guid compositionId)
    {
        foreach (var (peerId, name) in _roster)
        {
            if (peerId == MyPeerId)
                continue;

            var display = GetPresence(peerId);
            if (display == null || display.CompositionId != compositionId)
                continue;

            yield return (name, display);
        }
    }

    internal sealed class CollabPeerDisplay
    {
        public Guid UserId;
        public Guid CompositionId;
        public bool IsHoveringCanvas;
        public bool IsAddingOperator;
        public bool LeftButtonDown;
        public bool RightButtonDown;
        public bool MiddleButtonDown;
        public bool IsDoubleClick;
        public sbyte WheelSpin;
        public byte[] PressedKeys = [];
        public Vector2 CursorOnCanvas;
        public Vector4 AccentColor;
        public Guid[] SelectedChildIds = [];
    }

    // ---------------------------------------------------------------------------------------
    // Per-frame update (UI thread)
    // ---------------------------------------------------------------------------------------

    /// <summary>Upper bound of inbound messages processed per frame so a burst can't stall rendering.</summary>
    private const int MaxInboundPerFrame = 512;

    public void Update()
    {
        // Apply background connect results first: they may install or (when stale) release a
        // transport, and everything below depends on the current session state.
        while (_uiThreadActions.TryDequeue(out var uiThreadAction))
        {
            try
            {
                uiThreadAction();
            }
            catch (Exception e)
            {
                Log.Warning($"Collab: applying a session update failed: {e.Message}");
            }
        }

        // The settings switch ends a running session: no session survives collaboration being off.
        if (!UserSettings.Config.CollabEnabled && IsSessionActive)
            Leave();

        // Auto-rejoin shortly after launch (e.g. after a project download restarted us).
        if (State == SessionState.Off)
        {
            if (_rejoinCheckTicks == 0)
            {
                _rejoinCheckTicks = Environment.TickCount64 + 8000;
            }
            else if (Environment.TickCount64 > _rejoinCheckTicks && !_rejoinAttempted)
            {
                _rejoinAttempted = true;
                TryAutoRejoin();
            }
        }

        if (_restartAtTicks > 0 && Environment.TickCount64 > _restartAtTicks)
        {
            _restartAtTicks = 0;
            EditorRestart.TryRestart();
            return;
        }

        UpdatePendingHandoff();
        UpdateReconnect();

        // The installation heartbeat runs in every state: an idle machine must still receive
        // connect commands from its owner.
        CollabRemote.Tick();

        if (State == SessionState.Off)
            return;

        ProcessPendingKicks();
        ProcessPendingDisconnects();

        // Measure latency to every peer so the roster can show it.
        if (Environment.TickCount64 > _nextPingTicks)
        {
            _nextPingTicks = Environment.TickCount64 + 2000;
            foreach (var pingPeer in Peers)
            {
                if (!pingPeer.IsAuthenticated)
                    continue;

                SendJson(pingPeer, new { t = "ping", ts = Environment.TickCount64 });
            }
        }

        // A disconnect handled above can end the session (host loss, relay link death), and
        // Leave() disposes the transport — nothing below may run against it.
        if (State == SessionState.Off)
            return;

        _videoStreamer?.Update();
        _videoReceiver?.Update();

        DrainInboundQueue();

        // A message handler can also end the session (auth failure, host bye) — the transport
        // is gone at that point, so the remaining steps must not run.
        if (State == SessionState.Off)
            return;

        PumpProjectUploads();
        SendHeartbeatsAndPrune();
        UpdateLoneTakeoverWatch();
        if (State == SessionState.Off)
            return;

        SendStateHashes();
        SendLocalPresence();
        RefreshDiscoveredSessions();
        UpdateBackendListing();
    }

    /// <summary>
    /// Heartbeats the web-backend listing and polls the whisper thread while hosting. Both run
    /// on pool threads; results land on the UI thread through the usual queues.
    /// </summary>
    private void UpdateBackendListing()
    {
        if (State != SessionState.Hosting || !CollabWebLobby.IsConfigured || RoomCode.Length == 0 || _roomKey.Length == 0)
            return;

        var now = Environment.TickCount64;
        if (now >= _nextBackendHeartbeatTicks)
        {
            _nextBackendHeartbeatTicks = now + BackendHeartbeatIntervalMs;
            CollabWebLobby.Heartbeat(RoomCode, _roomKey, Peers.Count + 1, ParticipantsCanEdit);
        }

        if (now >= _nextWhisperPollTicks)
        {
            _nextWhisperPollTicks = now + WhisperPollIntervalMs;
            var room = RoomCode;
            var key = _roomKey;
            var since = _whisperSince;
            _ = Task.Run(async () =>
                             {
                                 try
                                 {
                                     var whispers = await CollabWebLobby.PollWhispersAsync(room, key, "", since, CancellationToken.None);
                                     if (whispers.Count == 0)
                                         return;

                                     _uiThreadActions.Enqueue(() =>
                                                              {
                                                                  foreach (var (from, text, isHostReply, at) in whispers)
                                                                  {
                                                                      if (isHostReply)
                                                                          continue; // own replies are appended at send time

                                                                      _lobbyWhispers.Add(new LobbyWhisperEntry { From = from, Text = text });
                                                                      AppendChat(null, $"{from} whispers: {text}");
                                                                      _whisperSince = Math.Max(_whisperSince, at);
                                                                  }
                                                              });
                                 }
                                 catch (Exception e)
                                 {
                                     Log.Debug($"Collab backend: whisper poll failed: {e.Message}");
                                 }
                             });
        }
    }

    private const int BackendHeartbeatIntervalMs = 30_000;
    private const int WhisperPollIntervalMs = 5_000;
    private long _nextBackendHeartbeatTicks;
    private long _nextWhisperPollTicks;
    private long _whisperSince;

    private long _rejoinCheckTicks;
    private bool _rejoinAttempted;

    private void TryAutoRejoin()
    {
        // The user ended the last session on purpose; reconnecting would override that choice.
        if (LeftManually)
            return;

        // A direct join restarts into the host's address...
        var host = UserSettings.Config.CollabLastSessionHost;
        if (!string.IsNullOrEmpty(host))
        {
            var separator = host.LastIndexOf(':');
            if (separator > 0 && int.TryParse(host[(separator + 1)..], out var port))
            {
                Log.Info($"Collab: auto-rejoining last session {host}");
                JoinDirect(host[..separator], port, UserSettings.Config.CollabPassword ?? "");
                return;
            }
        }

        // ...a relay join that restarted for a project download comes back through the same room.
        var relayHost = UserSettings.Config.CollabLastRelayHost;
        var roomCode = UserSettings.Config.CollabLastRelayRoom;
        if (!string.IsNullOrEmpty(relayHost) && !string.IsNullOrEmpty(roomCode))
        {
            var relaySeparator = relayHost.LastIndexOf(':');
            if (relaySeparator > 0 && int.TryParse(relayHost[(relaySeparator + 1)..], out var relayPort))
            {
                Log.Info($"Collab: auto-rejoining relay room {roomCode} at {relayHost}");
                JoinViaRelay(relayHost[..relaySeparator], relayPort, roomCode, UserSettings.Config.CollabPassword ?? "");
            }
        }

        // ...and a backend party rejoins its room (the backend still knows it).
        var backendRoom = UserSettings.Config.CollabLastBackendRoom;
        if (!string.IsNullOrEmpty(backendRoom))
        {
            Log.Info($"Collab: auto-rejoining backend room {backendRoom}");
            JoinViaBackend(backendRoom);
        }
    }

    // -- Password-request dialog: a join turned away by a locked session asks for the password --
    private string? _passwordRequestMessage;
    private string? _passwordRequestTarget;
    private (bool relay, string host, int port, string roomCode)? _pendingPasswordJoin;

    /// <summary>True while a join is waiting for the session password (drives the password dialog).</summary>
    public bool IsPasswordRequested => _passwordRequestMessage != null;

    /// <summary>The message the rejecting side sent along with the password request.</summary>
    public string PasswordRequestMessage => _passwordRequestMessage ?? "";

    /// <summary>The session being joined, for the dialog's subtitle.</summary>
    public string? PasswordRequestTarget => _passwordRequestTarget;

    /// <summary>Called by the transport when a join was rejected because the session is locked.</summary>
    public void RequestSessionPassword(string message, string target, bool relay, string host, int port, string roomCode)
    {
        _passwordRequestMessage = string.IsNullOrEmpty(message) ? "This session is password protected." : message;
        _passwordRequestTarget = target;
        _pendingPasswordJoin = (relay, host, port, roomCode);
    }

    /// <summary>Retries the pending join with the password the user entered.</summary>
    public void SubmitRequestedPassword(string password)
    {
        var pending = _pendingPasswordJoin;
        _passwordRequestMessage = null;
        _passwordRequestTarget = null;
        _pendingPasswordJoin = null;
        if (pending == null)
            return;

        if (pending.Value.relay)
            JoinViaRelay(pending.Value.host, pending.Value.port, pending.Value.roomCode, password);
        else
            JoinDirect(pending.Value.host, pending.Value.port, password);
    }

    /// <summary>Abandons the pending password join.</summary>
    public void CancelPasswordRequest()
    {
        _passwordRequestMessage = null;
        _passwordRequestTarget = null;
        _pendingPasswordJoin = null;
    }

    /// <summary>Closes the focused project when it is the one the session shared.</summary>
    private static void CloseSharedProject(string projectName)
    {
        if (string.IsNullOrEmpty(projectName) || ProjectView.Focused is not { } view)
            return;

        var displayName = view.OpenedProject.Package?.DisplayName ?? "";
        if (string.Equals(displayName, projectName, StringComparison.OrdinalIgnoreCase)
            || displayName.StartsWith(projectName + " (", StringComparison.OrdinalIgnoreCase))
            view.Close();
    }

    private void ProcessPendingKicks()
    {
        if (_pendingKicks.Count == 0)
            return;

        var now = Environment.TickCount64;
        for (var i = _pendingKicks.Count - 1; i >= 0; i--)
        {
            if (now < _pendingKicks[i].CloseAtTicks)
                continue;

            var peer = _pendingKicks[i].Peer;
            _pendingKicks.RemoveAt(i);
            peer.SessionClosed = true;
            peer.Close();
        }
    }

    private void ProcessPendingDisconnects()
    {
        while (_pendingDisconnects.TryDequeue(out var peer))
        {
            // Sentinel emitted by the relay host transport when the relay link itself died.
            if (peer.AssignedId == int.MaxValue)
            {
                if (!TryBeginReconnect("Relay connection lost - re-opening the room..."))
                {
                    ErrorText = "Lost connection to the relay server";
                    Leave();
                }

                return;
            }

            // Connections that never joined (a failed password, a reachability probe) were never
            // announced, so their end is not announced either.
            var wasParticipant = _roster.Remove(peer.AssignedId);
            _participants.Remove(peer.AssignedId);
            _rosterUserIds.Remove(peer.AssignedId);
            _latestPresenceByPeer.Remove(peer.AssignedId);
            _pendingAuth.Remove(peer);
            DropLocksOwnedBy(peer.AssignedId);
            DropProjectUpload(peer);

            if (State == SessionState.Hosting)
            {
                if (wasParticipant)
                    Log.Info($"Collab: peer left: {peer}");

                _opsReceivedThisSecond.Remove(peer.AssignedId);
                if (wasParticipant)
                {
                    BroadcastSystemMessage(new
                                               {
                                                   t = "peerLeft",
                                                   id = peer.AssignedId,
                                                   name = peer.Name,
                                               }, except: null);
                }

                StatusText = $"Hosting{(!string.IsNullOrEmpty(RoomCode) ? $" (room {RoomCode})" : "")}, {_roster.Count} participant(s)";
            }
            else if (State == SessionState.Connected && ReferenceEquals(peer, (_transport as CollabClientTransport)?.HostPeer))
            {
                if (!TryBeginReconnect("Connection to the host lost - reconnecting..."))
                {
                    ErrorText = "Disconnected from host";
                    Leave();
                }
            }
            else if (State == SessionState.Connecting && _transport != null
                     && (ReferenceEquals(peer, (_transport as CollabClientTransport)?.HostPeer)
                         || _transport is CollabRelayClientTransport))
            {
                // The host/relay link died while the handshake was still running. Without this the
                // state stays "Connecting" forever with no error, and the next Join click builds a
                // second transport without disposing the first.
                // During an automatic reconnect this is one failed attempt; the next is scheduled.
                if (_reconnectTarget == null || !TryBeginReconnect(StatusText))
                {
                    ErrorText = "Could not connect to host";
                    Leave();
                }
            }
            else if (State == SessionState.Connected && _transport is CollabRelayClientTransport)
            {
                if (!TryBeginReconnect("Connection to the session lost - reconnecting..."))
                {
                    ErrorText = "Disconnected from relay session";
                    Leave();
                }
            }
        }
    }

    private int ActivePeerCount
    {
        get
        {
            var count = 0;
            foreach (var peer in Peers)
            {
                if (peer.IsAuthenticated)
                    count++;
            }

            return count;
        }
    }

    private void DrainInboundQueue()
    {
        var processed = 0;
        // A message handler may end the session (auth failure, host bye), nulling the transport
        // mid-loop — the null-conditional keeps the loop condition safe on the next iteration.
        while (processed < MaxInboundPerFrame && _transport?.Inbound.TryDequeue(out var message) == true)
        {
            processed++;
            if (message.Peer.SessionClosed && message.IsPresence)
                continue;

            if (message is { IsPresence: true, Presence: { } presence })
                HandlePresence(message.Peer, presence);
            else if (message.Json is { } json)
                HandleJson(message.Peer, json);
        }
    }

    private void HandlePresence(CollabPeerConnection peer, PresencePacket presence)
    {
        // On a client the only transport peer is the host link, but the host relays other clients'
        // presence frames through it. Only the packet that belongs to this peer may replace its
        // own presence, or another participant's cursor/selection overwrites the host's.
        if (presence.PeerId == peer.PresencePeerId)
            peer.LatestPresence = presence;
        _latestPresenceByPeer[presence.PeerId] = presence;

        // The host relays presence between clients on the reliable path when UDP isn't available
        // yet. The UDP path already relays on the network thread; relaying here as well would
        // double-send, so only fall back for peers with no known UDP endpoint.
        if (State != SessionState.Hosting)
            return;

        foreach (var other in Peers)
        {
            if (other == peer || !other.IsAuthenticated)
                continue;

            if (other.UdpEndpoint != null)
                continue;

            var buffer = new byte[CollabProtocol.MaxPresencePayload];
            var size = presence.Encode(buffer);
            if (size > 0)
                _transport?.SendToPeer(other, CollabProtocol.FrameType.Presence, buffer.AsSpan(0, size));
        }
    }

    private void HandleJson(CollabPeerConnection peer, JObject json)
    {
        var type = CollabProtocol.GetString(json, "t");

        // Only the handshake may arrive before a peer authenticated. Everything else either
        // mutates the shared graph, locks parameters or streams project files, and a
        // password-protected host must not let a connection that never passed the challenge
        // do any of those.
        if (!peer.IsAuthenticated && type is not ("hello" or "auth" or "welcome" or "authOk" or "authFail"))
        {
            Log.Debug($"Collab: ignoring '{type}' from unauthenticated {peer}");
            return;
        }

        switch (type)
        {
            case "hello" when State == SessionState.Hosting:
                HandleHostHello(peer, json);
                break;

            case "welcome" when State == SessionState.Connecting:
                HandleClientWelcome(json);
                break;

            case "auth" when State == SessionState.Hosting:
                HandleHostAuth(peer, json);
                break;

            case "authOk" when State == SessionState.Connecting:
                HandleClientAuthOk(peer, json);
                break;

            case "authFail" when State == SessionState.Connecting:
                ErrorText = "Rejected by host: " + CollabProtocol.GetString(json, "reason", "authentication failed");
                Leave();
                break;

            case "canEdit":
                HandleCanEdit(peer, json);
                break;

            case "op" when IsSessionActive:
                HandleOp(peer, json);
                break;

            case "undoRequest" when State == SessionState.Hosting:
                PerformUserUndo(peer.UserId, peer);
                break;

            case "redoRequest" when State == SessionState.Hosting:
                PerformUserRedo(peer.UserId, peer);
                break;

            case "undoNotice" when State == SessionState.Connected:
                UndoNotice = CollabProtocol.GetString(json, "msg");
                break;

            case "lock" when IsSessionActive:
                HandleLock(peer, json, true);
                break;

            case "unlock" when IsSessionActive:
                HandleLock(peer, json, false);
                break;

            case "hostTransfer" when State == SessionState.Connected:
                HandleHostTransfer(json);
                break;

            case "sessionEnded" when State == SessionState.Connected:
                ErrorText = "The host ended the session";
                Log.Info("Collab: the host ended the session");
                Leave();
                break;

            case "opNack" when State == SessionState.Connected:
                HandleOpNack(json);
                break;

            case "stateHash" when State == SessionState.Connected:
                HandleStateHash(json);
                break;

            case "requestSync" when State == SessionState.Hosting:
                SendSyncLog(peer, json["c"] as JArray);
                break;

            case "syncStart" when State == SessionState.Connected:
                _syncBatch = 0;
                _syncInProgress = true;
                // The snapshots that follow contain every op up to this number.
                if (CollabProtocol.TryGetLong(json, "seq", out var syncSeq))
                    _lastAppliedSeq = syncSeq;

                Log.Debug("Collab: applying host composition snapshots...");
                break;

            case "syncOp" when State == SessionState.Connected:
                if (json["op"] is JObject op)
                {
                    CollabCommandWire.ApplyOp(op);
                    CollectCompositionIds(op, _touchedCompositions);
                    _syncBatch++;
                }

                break;

            case "syncEnd" when State == SessionState.Connected:
                _syncInProgress = false;
                foreach (var healedId in _healRequested)
                    _healAwaitingVerification.Add(healedId);

                _healRequested.Clear();

                // A snapshot replaces whole compositions, including local edits the host had not
                // ordered when it built the snapshot. Their echoes are still on the way, so put
                // the edits back; ones the snapshot already contains apply as no-ops.
                ReassertPendingLocalOps(null);
                Log.Info($"Collab: replay complete ({_syncBatch} ops)");
                break;

            case "rttPing":
                if (CollabProtocol.TryGetLong(json, "stamp", out var probeStamp))
                    SendJson(peer, new { t = "rttPong", stamp = probeStamp });
                break;

            case "rttPong":
                if (CollabProtocol.TryGetLong(json, "stamp", out var answered) && peer.AssignedId != int.MaxValue)
                {
                    var rtt = Environment.TickCount64 - answered;
                    if (rtt is >= 0 and <= 10_000)
                        peer.LastRttMs = (int)rtt;
                }

                break;

            case "peerJoined" when State == SessionState.Connected:
            {
                var name = CollabProtocol.GetString(json, "name", "?");
                if (CollabProtocol.TryGetInt(json, "id", out var joinedId))
                {
                    _roster[joinedId] = name;
                    if (CollabProtocol.TryGetGuid(json, "userId", out var joinedUserId))
                        _rosterUserIds[joinedId] = joinedUserId;

                    _rosterAvatars[joinedId] = CollabProtocol.GetString(json, "avatar");
                    _participants[joinedId] = ReadParticipantInfo(json);
                }

                Log.Info($"Collab: {name} joined");
                AppendChat(null, $"{name} joined");
                StatusText = State == SessionState.Hosting
                                 ? $"Hosting{(!string.IsNullOrEmpty(RoomCode) ? $" (room {RoomCode})" : "")}, {_roster.Count} participant(s)"
                                 : StatusText;
                break;
            }

            case "peerLeft" when State == SessionState.Connected:
            {
                var name = CollabProtocol.GetString(json, "name", "?");
                if (CollabProtocol.TryGetInt(json, "id", out var leftId))
                {
                    _roster.Remove(leftId);
                    _participants.Remove(leftId);
                }

                Log.Info($"Collab: {name} left");
                AppendChat(null, $"{name} left");
                break;
            }

            case "kicked" when State == SessionState.Connected:
            {
                var kickReason = CollabProtocol.GetString(json, "reason", "Removed by the host");
                ErrorText = kickReason;
                Log.Info($"Collab: removed by the host: {kickReason}");

                // A kicked participant lands in the project hub rather than inside the host's show.
                var sharedProject = _hostProjectName;
                Leave(manual: true);
                CloseSharedProject(sharedProject);
                break;
            }

            case "chat" when IsSessionActive:
            {
                // Hosts relay chat to everyone else; both sides show it in the session chat log.
                var text = CollabProtocol.GetString(json, "text");
                if (string.IsNullOrEmpty(text))
                    break;

                var sender = CollabProtocol.GetString(json, "name", "?");
                AppendChat(sender, text);
                if (State == SessionState.Hosting)
                    BroadcastSystemMessage(json, except: peer);
                break;
            }

            case "ping" when IsSessionActive:
                SendJson(peer, new { t = "pong", ts = json["ts"] });
                break;

            case "pong" when IsSessionActive:
            {
                if (CollabProtocol.TryGetLong(json, "ts", out var sentTs))
                    _peerPingMs[peer.AssignedId] = (int)Math.Clamp(Environment.TickCount64 - sentTs, 0, 60_000);

                break;
            }

            case "requestProject" when State == SessionState.Hosting:
                HandleRequestProject(peer, json);
                break;

            case "projectBegin":
            case "fileBegin":
            case "fileChunk":
            case "fileEnd":
            case "projectDone":
            case "projectFail":
                HandleProjectDownload(peer, json);
                break;

            case "notice":
                Log.Debug($"Collab host: {CollabProtocol.GetString(json, "msg")}");
                break;

            default:
                Log.Debug($"Collab: ignoring message type '{type}' in state {State}");
                break;
        }
    }

    private int _syncBatch;

    // ---------------------------------------------------------------------------------------
    // Handshake
    // ---------------------------------------------------------------------------------------

    private sealed class PendingHandshake
    {
        public byte[]? Salt;
        public byte[]? Nonce;
    }

    /// <summary>
    /// Challenge per peer. A single shared challenge made simultaneous joins clobber each other:
    /// the second hello overwrote the first nonce, so the first participant's correct answer was
    /// rejected, and completing one handshake discarded every other peer's pending challenge.
    /// </summary>
    private readonly Dictionary<CollabPeerConnection, PendingHandshake> _pendingAuth = new();

    private void HandleHostHello(CollabPeerConnection peer, JObject json)
    {
        peer.Name = TruncateName(CollabProtocol.GetString(json, "name", "peer"));
        peer.UserId = CollabProtocol.TryGetGuid(json, "userId", out var userId) ? userId : Guid.NewGuid();
        peer.MarkHeardFrom();

        // What the relay confirmed beats what the peer says about itself.
        if (peer.IsVerified)
        {
            if (!string.IsNullOrWhiteSpace(peer.VerifiedName))
                peer.Name = TruncateName(peer.VerifiedName);

            if (!string.IsNullOrEmpty(peer.DiscordId))
                peer.UserId = CollabUserIdentity.UserIdForDiscordAccount(peer.DiscordId);
        }
        else
        {
            var claimedLang = CollabProtocol.GetString(json, "lang").Trim();
            peer.Lang = claimedLang.Length <= 12 ? claimedLang : claimedLang[..12];
        }

        peer.ListenPort = CollabProtocol.TryGetInt(json, "port", out var listenPort) && listenPort is > 0 and < 65536
                              ? listenPort
                              : CollabProtocol.DefaultTcpPort;

        _rosterAvatars[peer.AssignedId] = TruncateAvatar(CollabProtocol.GetString(json, "avatar"));

        if (!CollabProtocol.TryGetInt(json, "proto", out var peerProtocol) || peerProtocol != CollabProtocol.ProtocolVersion)
        {
            RejectPeer(peer, $"TiXL version mismatch (host uses collab protocol {CollabProtocol.ProtocolVersion}, "
                             + $"you use {peerProtocol}) - update both sides to the same build");
            return;
        }

        if (string.IsNullOrEmpty(_password))
        {
            CompleteHostAuth(peer);
            return;
        }

        var salt = CollabAuth.CreateSalt();
        var nonce = CollabAuth.CreateNonce();
        _pendingAuth[peer] = new PendingHandshake { Salt = salt, Nonce = nonce };
        SendJson(peer, new
                           {
                               t = "welcome",
                               proto = CollabProtocol.ProtocolVersion,
                               pwd = true,
                               salt = CollabAuth.ToHexString(salt),
                               nonce = CollabAuth.ToHexString(nonce),
                           });
    }

    private void HandleHostAuth(CollabPeerConnection peer, JObject json)
    {
        if (!_pendingAuth.TryGetValue(peer, out var pending) || pending.Salt == null || pending.Nonce == null)
        {
            RejectPeer(peer, "no challenge pending");
            return;
        }

        if (!CollabAuth.TryFromHexString(CollabProtocol.GetString(json, "mac"), out var mac)
            || mac.Length != CollabAuth.MacSizeBytes)
        {
            RejectPeer(peer, "malformed authentication response");
            return;
        }

        var key = CollabAuth.DeriveKey(_password, pending.Salt);
        var expected = CollabAuth.ComputeMac(key, pending.Nonce);
        if (!CollabAuth.MacsEqual(expected, mac))
        {
            RejectPeer(peer, "wrong password");
            return;
        }

        CompleteHostAuth(peer);
    }

    private void CompleteHostAuth(CollabPeerConnection peer)
    {
        peer.IsAuthenticated = true;
        _pendingAuth.Remove(peer);
        _roster[peer.AssignedId] = peer.Name;
        _rosterUserIds[peer.AssignedId] = peer.UserId;
        _participants[peer.AssignedId] = new ParticipantInfo(peer.Lang, peer.IsVerified, peer.RemoteAddress?.ToString() ?? "", peer.ListenPort);

        // Someone followed the takeover here: this is the session now.
        _loneTakeoverOrigin = null;

        var peersPayload = new JArray();
        foreach (var other in Peers)
        {
            if (other == peer || !other.IsAuthenticated)
                continue;

            peersPayload.Add(new JObject
                                 {
                                     ["id"] = other.AssignedId,
                                     ["userId"] = other.UserId,
                                     ["name"] = other.Name,
                                     ["lang"] = other.Lang,
                                     ["verified"] = other.IsVerified,
                                     ["avatar"] = _rosterAvatars.TryGetValue(other.AssignedId, out var otherAvatar) ? otherAvatar : "",
                                     ["addr"] = other.RemoteAddress?.ToString() ?? "",
                                     ["port"] = other.ListenPort,
                                 });
        }

        SendJson(peer, new
                           {
                               t = "authOk",
                               proto = CollabProtocol.ProtocolVersion,
                               yourId = peer.AssignedId,
                               hostId = 0,
                               hostName = _localIdentity.DisplayName,
                               hostUserId = _localIdentity.UserId,
                               hostLang = _localIdentity.Lang,
                               hostVerified = _localVerified,
                               // The key is only for re-opening a relay room after the host
                               // disappears; backend and LAN participants must not receive it.
                               roomKey = _currentTarget is { Relay: true } ? _roomKey : null,
                               peers = peersPayload,
                               logCount = _sessionLog.Count,
                               // Live: the host may open or switch the project after hosting
                               // started, and joiners that came earlier than that still need it.
                               project = FindAnyOpenedProject()?.DisplayName ?? _sessionProjectName,
                           });

        // The handshake runs on the relay first; only a fully authenticated peer may switch paths.
        _transport?.EnablePeerToPeer(peer);

        BroadcastSystemMessage(new
                                   {
                                       t = "peerJoined",
                                       id = peer.AssignedId,
                                       userId = peer.UserId,
                                       name = peer.Name,
                                       lang = peer.Lang,
                                       verified = peer.IsVerified,
                                       avatar = _rosterAvatars.TryGetValue(peer.AssignedId, out var joinedAvatar) ? joinedAvatar : "",
                                       addr = peer.RemoteAddress?.ToString() ?? "",
                                       port = peer.ListenPort,
                                   }, except: peer);

        StatusText = $"Hosting, {ActivePeerCount} participant(s)";
        Log.Info($"Collab: peer authenticated: {peer}");
    }

    private static ParticipantInfo ReadParticipantInfo(JObject entry)
    {
        CollabProtocol.TryGetBool(entry, "verified", out var verified);
        CollabProtocol.TryGetInt(entry, "port", out var port);
        return new ParticipantInfo(CollabProtocol.GetString(entry, "lang"), verified, CollabProtocol.GetString(entry, "addr"), port);
    }

    private void RejectPeer(CollabPeerConnection peer, string reason)
    {
        Log.Info($"Collab: rejected peer {peer}: {reason}");
        _pendingAuth.Remove(peer);
        SendJson(peer, new { t = "authFail", reason });
        peer.SessionClosed = true;
        peer.Close();
    }

    private void HandleClientWelcome(JObject json)
    {
        if (State != SessionState.Connecting || HostLink is not { } host)
            return;

        CollabProtocol.TryGetBool(json, "pwd", out var passwordRequired);
        if (!passwordRequired)
            return;

        if (!CollabAuth.TryFromHexString(CollabProtocol.GetString(json, "salt"), out var salt)
            || !CollabAuth.TryFromHexString(CollabProtocol.GetString(json, "nonce"), out var nonce))
        {
            ErrorText = "Host sent a malformed challenge";
            Leave();
            return;
        }

        var key = CollabAuth.DeriveKey(_password, salt);
        var mac = CollabAuth.ComputeMac(key, nonce);
        SendJson(host, new { t = "auth", mac = CollabAuth.ToHexString(mac) });
    }

    private void HandleClientAuthOk(CollabPeerConnection peer, JObject json)
    {
        // An older host neither orders ops nor announces a version; editing against it would
        // leave every local op waiting for an echo that never comes.
        if (!CollabProtocol.TryGetInt(json, "proto", out var hostProtocol) || hostProtocol != CollabProtocol.ProtocolVersion)
        {
            ErrorText = $"The host runs a different TiXL build (collab protocol {hostProtocol}, this one uses "
                        + $"{CollabProtocol.ProtocolVersion}). Update both sides to the same build.";
            Leave();
            return;
        }

        peer.IsAuthenticated = true;
        CollabProtocol.TryGetInt(json, "yourId", out var assignedId);
        _localPresence = new PresencePacket { PeerId = (byte)assignedId };
        MyPeerId = assignedId;

        // The host introduces itself so clients can render its cursor and participant entry.
        if (CollabProtocol.TryGetInt(json, "hostId", out var hostId))
        {
            _roster[hostId] = CollabProtocol.GetString(json, "hostName", "host");
            if (CollabProtocol.TryGetGuid(json, "hostUserId", out var hostUserId))
            {
                _rosterUserIds[hostId] = hostUserId;

                // The backend client's only transport peer IS the host: without its identity
                // GetPresence treats the host as unknown and pinning has nothing to resolve.
                var hostPeer = Peers.FirstOrDefault(p => p.AssignedId == hostId);
                if (hostPeer != null)
                {
                    hostPeer.UserId = hostUserId;
                    hostPeer.Name = _roster[hostId];
                }
            }

            // In a relay session the relay says who the host is; that cannot be made up by the
            // host. Otherwise the host's own word is all there is, and it counts as unverified.
            var relayReply = (_transport as CollabRelayClientTransport)?.RelayReply;
            _localVerified = IsVerifiedByRelay(relayReply, "you");
            if (relayReply?["host"] is JObject relayHost && IsVerifiedByRelay(relayReply, "host"))
            {
                var verifiedName = CollabProtocol.GetString(relayHost, "name");
                if (!string.IsNullOrWhiteSpace(verifiedName))
                    _roster[hostId] = TruncateName(verifiedName);

                _participants[hostId] = new ParticipantInfo(CollabProtocol.GetString(relayHost, "lang"), true, "", 0);
            }
            else
            {
                _participants[hostId] = new ParticipantInfo(CollabProtocol.GetString(json, "hostLang"), false, "", 0);
            }
        }

        // The key lets a participant re-open the room should the host disappear.
        if (_currentTarget is { Relay: true } joined)
            _currentTarget = joined with { RoomKey = CollabProtocol.GetString(json, "roomKey") };

        foreach (var token in json["peers"] ?? new JArray())
        {
            if (token is not JObject entry)
                continue;

            var name = CollabProtocol.GetString(entry, "name", "?");
            var id = CollabProtocol.TryGetInt(entry, "id", out var parsedId) ? parsedId : 0;
            _roster[id] = name;
            if (CollabProtocol.TryGetGuid(entry, "userId", out var rosterUserId))
                _rosterUserIds[id] = rosterUserId;

            _rosterAvatars[id] = CollabProtocol.GetString(entry, "avatar");

            _participants[id] = ReadParticipantInfo(entry);
            Log.Info($"Collab: {name} is in this session");
        }

        State = SessionState.Connected;
        StatusText = $"Connected as {_localIdentity.DisplayName}";
        _transport?.EnablePeerToPeer(peer);

        // Before the sync request: the host applies these first, so the state it sends back
        // already contains them.
        ResendOpsAfterReconnect(peer);
        SendJson(peer, new { t = "requestSync" });

        // Remember the session so a restart (e.g. after a project download) can rejoin, then make
        // sure the host's project is open locally — download it when this machine doesn't have it.
        UserSettings.Config.CollabLastSessionHost = _joinedHost;
        UserSettings.Config.CollabLastRelayHost = "";
        UserSettings.Config.CollabLastRelayRoom = "";
        UserSettings.Save();
        _hostProjectName = CollabProtocol.GetString(json, "project");
        EnsureHostProjectAvailable();
    }

    /// <summary>host:port of the session this client dialed into (persisted for auto-rejoin).</summary>
    private string _joinedHost = "";

    /// <summary>Remote management: the claim this client presented when joining, empty otherwise.</summary>
    private string _joinClaim = "";

    private string _sessionProjectName = "";
    private string _hostProjectName = "";

    /// <summary>
    /// Opens the host's project locally, or asks the host to stream it over when it doesn't exist
    /// here. Once the files have landed the editor restarts and auto-rejoins; from then on the
    /// session only syncs deltas.
    /// </summary>
    private void EnsureHostProjectAvailable()
    {
        // Works over every client transport: the transfer rides the session's data channel, and
        // the editor restarts into the downloaded project, rejoining automatically.
        if (string.IsNullOrEmpty(_hostProjectName))
            return;

        if (TryOpenProjectByName(_hostProjectName))
            return;

        Log.Info($"Collab: requesting project '{_hostProjectName}' from host");
        StatusText = $"Downloading project '{ShortProjectName(_hostProjectName)}' from host...";
        SendJson(HostLink!, new { t = "requestProject", project = _hostProjectName });
    }

    internal static string ShortProjectName(string displayName)
    {
        // Created projects get a "<name> (<namespace>)" display name.
        var paren = displayName.IndexOf(" (", StringComparison.Ordinal);
        return paren > 0 ? displayName[..paren] : displayName;
    }

    /// <summary>Opens a local project by display name (short form accepted) into the visible graph window.</summary>
    internal static bool TryOpenProjectByName(string name)
    {
        if (string.IsNullOrEmpty(name))
            return false;

        // Shared with failover and boygrouping: every mode follows the active machine's project the
        // same way (in place, without a restart, when the project is installed).
        if (T3.Editor.Show.ProjectFollower.TrySwitchToName(name))
            return true;

        Log.Warning($"Collab: project '{name}' is not installed locally");
        return false;
    }

    // ---------------------------------------------------------------------------------------
    // Ops
    // ---------------------------------------------------------------------------------------

    /// <summary>Hooked from <see cref="T3.Editor.UiModel.Commands.UndoRedoStack"/> for every local edit.</summary>
    public void OnLocalCommandAdded(ICommand command)
    {
        if (!IsSessionActive)
        {
            KeepEditForReconnect(command);
            return;
        }

        var op = CollabCommandWire.EncodeCommand(command);
        if (op == null)
            return;

        AppendToLog(op);
        RecordForUndo(op, _localIdentity.UserId);

        if (State == SessionState.Hosting)
        {
            BroadcastOrderedOp(op, fromPeerId: 0, clientOpId: 0);
        }
        else if (State == SessionState.Connected && HostLink is { } host)
        {
            // Applied here already; it stays pending until the host's echo fixes its place in
            // the session order.
            CollectCompositionIds(op, _touchedCompositions);
            var clientOpId = ++_nextLocalOpId;
            _pendingLocalOps.Add(new PendingLocalOp(clientOpId, op));
            if (_pendingLocalOps.Count > MaxPendingLocalOps)
            {
                Log.Warning("Collab: the host stopped confirming edits; dropping the oldest unconfirmed one");
                _pendingLocalOps.RemoveAt(0);
            }

            SendJson(host, new JObject
                               {
                                   ["t"] = "op",
                                   ["cid"] = clientOpId,
                                   ["op"] = op,
                               });
        }
    }

    private void HandleOp(CollabPeerConnection peer, JObject json)
    {
        if (!peer.IsAuthenticated)
            return;

        // Spectator mode: the host drops every graph op from participants while the toggle is
        // off. Chat and presence are separate messages and keep flowing.
        if (State == SessionState.Hosting && !ParticipantsCanEdit)
        {
            if (!_readOnlyNoticeShownFor.Contains(peer.AssignedId))
            {
                _readOnlyNoticeShownFor.Add(peer.AssignedId);
                Log.Info($"Collab: dropped an op from {peer} (participants cannot edit)");
                SendJson(peer, new { t = "canEdit", value = false });
            }

            return;
        }

        if (json["op"] is not JObject op)
            return;

        if (State == SessionState.Hosting)
        {
            CollabProtocol.TryGetLong(json, "cid", out var clientOpId);

            // The sender applied this op already. Dropping it silently would leave its copy
            // different from everyone else's for good, so tell it; it then takes the host's state.
            if (!RateAllow(peer))
            {
                Log.Warning($"Collab: rate limiting {peer}");
                SendJson(peer, new { t = "opNack", cid = clientOpId, reason = "too many edits per second" });
                return;
            }

            // Host-authoritative: arrival order here is the session order.
            CollabCommandWire.ApplyOp(op);
            AppendToLog(op);
            RecordForUndo(op, peer.UserId);
            BroadcastOrderedOp(op, peer.AssignedId, clientOpId);
            return;
        }

        // Client. Everything from the host is already ordered and must be applied: no rate limit
        // here, the host relays the edits of all participants through this one link.
        var hasSeq = CollabProtocol.TryGetLong(json, "seq", out var seq);
        if (hasSeq && _lastAppliedSeq >= 0)
        {
            // Contained in a snapshot applied earlier, or delivered twice across a path switch.
            if (seq <= _lastAppliedSeq)
                return;

            if (seq != _lastAppliedSeq + 1)
            {
                Log.Warning($"Collab: missed {seq - _lastAppliedSeq - 1} edit(s) from the host, asking for its state");
                RequestFullResync();
            }

            _lastAppliedSeq = seq;
        }

        var isOwnEcho = hasSeq
                        && CollabProtocol.TryGetInt(json, "from", out var fromPeerId)
                        && fromPeerId == MyPeerId
                        && MyPeerId != 0;
        if (isOwnEcho
            && CollabProtocol.TryGetLong(json, "cid", out var echoedOpId)
            && TryConfirmLocalOp(echoedOpId))
        {
            return;
        }

        CollabCommandWire.ApplyOp(op);
        AppendToLog(op);

        // Every participant keeps the list: whoever takes over as host needs it for joiners.
        CollectCompositionIds(op, _touchedCompositions);

        // The host ordered this op before every edit of ours it has not confirmed yet, but here
        // ours ran first. Applying ours again on top yields what the host and the other
        // participants have: remote op first, then ours.
        ReassertPendingLocalOps(op);
    }

    /// <summary>Host: gives an applied op its place in the session order and sends it to every
    /// participant. The sender gets it too — that echo is its confirmation.</summary>
    private void BroadcastOrderedOp(JObject op, int fromPeerId, long clientOpId)
    {
        CollectCompositionIds(op, _touchedCompositions);
        var envelope = new JObject
                           {
                               ["t"] = "op",
                               ["seq"] = ++_hostSeq,
                               ["from"] = fromPeerId,
                               ["cid"] = clientOpId,
                               ["op"] = op,
                           };
        BroadcastSystemMessage(envelope, except: null);
    }

    /// <summary>Removes a local op the host echoed. Echoes arrive in send order, so anything
    /// still queued ahead of it was refused or lost and will never be confirmed.</summary>
    private bool TryConfirmLocalOp(long clientOpId)
    {
        for (var index = 0; index < _pendingLocalOps.Count; index++)
        {
            if (_pendingLocalOps[index].ClientOpId != clientOpId)
                continue;

            _pendingLocalOps.RemoveRange(0, index + 1);
            return true;
        }

        return false;
    }

    /// <summary>Applies unconfirmed local ops again. With a remote op given, only those sharing a
    /// composition with it. Ops address their targets by id and are no-ops when the target is
    /// gone or the change is already in place, so applying one twice is safe.</summary>
    private void ReassertPendingLocalOps(JObject? afterRemoteOp)
    {
        if (_pendingLocalOps.Count == 0)
            return;

        HashSet<Guid>? remoteCompositions = null;
        if (afterRemoteOp != null)
        {
            remoteCompositions = new HashSet<Guid>();
            CollectCompositionIds(afterRemoteOp, remoteCompositions);
            if (remoteCompositions.Count == 0)
                return;
        }

        foreach (var pending in _pendingLocalOps)
        {
            if (remoteCompositions != null)
            {
                _scratchCompositions.Clear();
                CollectCompositionIds(pending.Op, _scratchCompositions);
                if (!_scratchCompositions.Overlaps(remoteCompositions))
                    continue;
            }

            CollabCommandWire.ApplyOp(pending.Op);
        }
    }

    private static void CollectCompositionIds(JObject op, HashSet<Guid> into)
    {
        if (CollabProtocol.TryGetGuid(op, "c", out var compositionId) && compositionId != Guid.Empty)
            into.Add(compositionId);

        if (op["ops"] is not JArray subOps)
            return;

        foreach (var token in subOps)
        {
            if (token is JObject subOp)
                CollectCompositionIds(subOp, into);
        }
    }

    /// <summary>Client: the host refused one of our ops. It is applied here and nowhere else, so
    /// forget it and take the host's state for the compositions it touched.</summary>
    private void HandleOpNack(JObject json)
    {
        if (!CollabProtocol.TryGetLong(json, "cid", out var clientOpId))
            return;

        var index = _pendingLocalOps.FindIndex(pending => pending.ClientOpId == clientOpId);
        if (index < 0)
            return;

        var refused = _pendingLocalOps[index].Op;
        _pendingLocalOps.RemoveAt(index);

        var affected = new HashSet<Guid>();
        CollectCompositionIds(refused, affected);
        Log.Warning($"Collab: the host refused an edit ({CollabProtocol.GetString(json, "reason", "no reason given")}); "
                    + "restoring its state for the affected composition(s)");
        RequestCompositionResync(affected);
    }

    private void RequestCompositionResync(IEnumerable<Guid> compositionIds)
    {
        if (State != SessionState.Connected || HostLink is not { } host)
            return;

        var ids = new JArray();
        foreach (var compositionId in compositionIds)
        {
            if (ids.Count >= MaxSyncRequestIds)
                break;

            ids.Add(compositionId.ToString());
        }

        if (ids.Count == 0)
            return;

        SendJson(host, new JObject { ["t"] = "requestSync", ["c"] = ids });
    }

    private void RequestFullResync()
    {
        if (State != SessionState.Connected || HostLink is not { } host)
            return;

        // One gap usually shows up as several out-of-order ops in a row; one request covers them.
        var now = Environment.TickCount64;
        if (now < _nextFullResyncAllowedTicks)
            return;

        _nextFullResyncAllowedTicks = now + ResyncRequestMinIntervalMs;
        SendJson(host, new { t = "requestSync" });
    }

    // ---------------------------------------------------------------------------------------
    // Drift detection and repair
    // ---------------------------------------------------------------------------------------

    /// <summary>Host: every few seconds, announce a hash of each composition the session edited
    /// (plus the one on screen). The sequence number says which op the hashes include.</summary>
    private void SendStateHashes()
    {
        if (State != SessionState.Hosting)
            return;

        var now = Environment.TickCount64;
        if (now < _nextStateHashTicks)
            return;

        _nextStateHashTicks = now + StateHashIntervalMs;
        if (ActivePeerCount == 0)
            return;

        var compositionIds = GetSessionCompositionIds();

        // Hashing serializes the composition. With many edited compositions, cover a window per
        // round and move it, so the cost per round stays bounded and all get checked in turn.
        var count = Math.Min(compositionIds.Count, MaxHashedCompositions);
        if (count == 0)
            return;

        var start = compositionIds.Count > MaxHashedCompositions ? _hashRotation++ * MaxHashedCompositions % compositionIds.Count : 0;
        var items = new JArray();
        for (var offset = 0; offset < count; offset++)
        {
            var compositionId = compositionIds[(start + offset) % compositionIds.Count];
            if (!SymbolUiRegistry.TryGetSymbolUi(compositionId, out var compositionUi))
                continue;

            try
            {
                items.Add(new JObject
                              {
                                  ["c"] = compositionId.ToString(),
                                  ["h"] = CollabCommandWire.ComputeCompositionHash(compositionUi),
                              });
            }
            catch (Exception e)
            {
                Log.Debug($"Collab: could not hash composition {compositionId}: {e.Message}");
            }
        }

        if (items.Count == 0)
            return;

        BroadcastSystemMessage(new JObject
                                   {
                                       ["t"] = "stateHash",
                                       ["seq"] = _hostSeq,
                                       ["items"] = items,
                                   }, except: null);
    }

    /// <summary>Compositions a joiner or a hash round has to cover: the one on screen first,
    /// then everything the session edited.</summary>
    private List<Guid> GetSessionCompositionIds()
    {
        var compositionIds = new List<Guid>(_touchedCompositions.Count + 1);
        var currentCompositionId = ProjectView.Focused?.InstView?.Symbol.Id ?? Guid.Empty;
        if (currentCompositionId != Guid.Empty)
            compositionIds.Add(currentCompositionId);

        foreach (var compositionId in _touchedCompositions)
        {
            if (compositionId != currentCompositionId)
                compositionIds.Add(compositionId);
        }

        return compositionIds;
    }

    /// <summary>
    /// Client: compares the host's hashes with the local compositions and asks for a snapshot of
    /// those that differ. Only a comparison at the same point of the op order means anything, so
    /// a round is skipped while local edits are unconfirmed or a sync is running. A difference
    /// has to show twice in a row before it is repaired, and a composition a snapshot could not
    /// fix is not repaired again.
    /// </summary>
    private void HandleStateHash(JObject json)
    {
        if (_syncInProgress || _pendingLocalOps.Count > 0 || _lastAppliedSeq < 0)
            return;

        if (!CollabProtocol.TryGetLong(json, "seq", out var seq) || seq != _lastAppliedSeq)
            return;

        if (json["items"] is not JArray items)
            return;

        List<Guid>? toHeal = null;
        foreach (var token in items)
        {
            if (token is not JObject item
                || !CollabProtocol.TryGetGuid(item, "c", out var compositionId)
                || _healRequested.Contains(compositionId)
                || _unhealable.Contains(compositionId)
                || !SymbolUiRegistry.TryGetSymbolUi(compositionId, out var compositionUi))
                continue;

            string localHash;
            try
            {
                localHash = CollabCommandWire.ComputeCompositionHash(compositionUi);
            }
            catch (Exception e)
            {
                Log.Debug($"Collab: could not hash composition {compositionId}: {e.Message}");
                continue;
            }

            if (string.Equals(localHash, CollabProtocol.GetString(item, "h"), StringComparison.Ordinal))
            {
                _hashMismatchStreak.Remove(compositionId);
                _healAwaitingVerification.Remove(compositionId);
                continue;
            }

            if (_healAwaitingVerification.Remove(compositionId))
            {
                _unhealable.Add(compositionId);
                _hashMismatchStreak.Remove(compositionId);
                Log.Warning($"Collab: '{compositionUi.Symbol.Name}' still differs from the host after a resync; "
                            + "not repairing it again in this session. Rejoin to force a full sync.");
                continue;
            }

            _hashMismatchStreak.TryGetValue(compositionId, out var streak);
            streak++;
            if (streak < MismatchesBeforeHeal)
            {
                _hashMismatchStreak[compositionId] = streak;
                continue;
            }

            _hashMismatchStreak.Remove(compositionId);
            Log.Warning($"Collab: '{compositionUi.Symbol.Name}' drifted from the host, restoring the host's state");
            toHeal ??= new List<Guid>();
            toHeal.Add(compositionId);
        }

        if (toHeal == null)
            return;

        foreach (var compositionId in toHeal)
            _healRequested.Add(compositionId);

        RequestCompositionResync(toHeal);
    }

    private void AppendToLog(JObject op)
    {
        if (_sessionLog.Count >= MaxSessionLogEntries)
            _sessionLog.RemoveAt(0);

        _sessionLog.Add(op);
    }

    // ---------------------------------------------------------------------------------------
    // Session-wide undo/redo (host-authoritative)
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// Undo history per participant, in the order the host applied their edits. Undo reverts the
    /// asking participant's own last edit, never someone else's. Keyed by the persistent user id
    /// so a participant who reconnects keeps their history. The host owns the stacks; clients
    /// just send requests.
    /// </summary>
    private readonly Dictionary<Guid, List<JObject>> _undoStacks = new();
    private readonly Dictionary<Guid, List<JObject>> _redoStacks = new();

    private const int MaxUndoEntriesPerUser = 1000;

    /// <summary>Last transient undo/redo notice for the settings panel.</summary>
    public string UndoNotice { get; private set; } = "";

    /// <summary>
    /// Routes a local undo gesture into the session: hosting undoes the host's own last edit,
    /// connected clients ask the host to undo theirs. Returns false when no session is running
    /// (classic local undo).
    /// </summary>
    public bool TryRouteGlobalUndo()
    {
        // Edits made while reconnecting are queued for the host; undoing one locally would not
        // take it out of that queue. Swallow the gesture until the session is back.
        if (State == SessionState.Off && _reconnectTarget != null)
            return true;

        if (State == SessionState.Off)
            return false;

        if (State == SessionState.Hosting)
        {
            PerformUserUndo(_localIdentity.UserId, null);
            return true;
        }

        if (State == SessionState.Connected && HostLink is { } host)
        {
            UndoNotice = "";
            SendJson(host, new { t = "undoRequest" });
            return true;
        }

        return true; // mid-handshake: swallow the gesture
    }

    public bool TryRouteGlobalRedo()
    {
        if (State == SessionState.Off && _reconnectTarget != null)
            return true;

        if (State == SessionState.Off)
            return false;

        if (State == SessionState.Hosting)
        {
            PerformUserRedo(_localIdentity.UserId, null);
            return true;
        }

        if (State == SessionState.Connected && HostLink is { } host)
        {
            UndoNotice = "";
            SendJson(host, new { t = "redoRequest" });
            return true;
        }

        return true;
    }

    private void RecordForUndo(JObject op, Guid userId)
    {
        if (State != SessionState.Hosting)
            return;

        if (!_undoStacks.TryGetValue(userId, out var undoStack))
            _undoStacks[userId] = undoStack = new List<JObject>();

        undoStack.Add(op);
        if (undoStack.Count > MaxUndoEntriesPerUser)
            undoStack.RemoveAt(0);

        if (_redoStacks.TryGetValue(userId, out var redoStack))
            redoStack.Clear();
    }

    /// <summary>Shows an undo/redo outcome to whoever asked: the host's own panel, or the
    /// requesting participant's.</summary>
    private void ReportUndoNotice(CollabPeerConnection? requester, string notice)
    {
        if (requester == null)
            UndoNotice = notice;
        else
            SendJson(requester, new { t = "undoNotice", msg = notice });
    }

    /// <summary>
    /// Host: reverts the last edit of one participant that can still be reverted. An edit is
    /// skipped when it has no inverse, or when someone else changed its target afterwards —
    /// reverting it then would throw their change away.
    /// </summary>
    private void PerformUserUndo(Guid userId, CollabPeerConnection? requester)
    {
        if (State != SessionState.Hosting)
            return;

        ReportUndoNotice(requester, "");
        _undoStacks.TryGetValue(userId, out var undoStack);
        if (!_redoStacks.TryGetValue(userId, out var redoStack))
            _redoStacks[userId] = redoStack = new List<JObject>();

        var skippedReason = "";
        while (undoStack is { Count: > 0 })
        {
            var op = undoStack[^1];
            undoStack.RemoveAt(undoStack.Count - 1);

            var inverse = CollabCommandWire.InvertOp(op);
            if (inverse == null)
            {
                skippedReason = "An edit that can't be undone in a session was skipped";
                Log.Debug($"Collab: op type '{CollabProtocol.GetString(op, "type")}' is not invertible");
                redoStack.Clear(); // Can't keep a consistent redo chain past a skipped op.
                continue;
            }

            if (!CollabCommandWire.IsOpStillInEffect(op))
            {
                skippedReason = "An edit someone else has changed since was skipped";
                redoStack.Clear();
                continue;
            }

            CollabCommandWire.ApplyOp(inverse);
            AppendToLog(inverse);
            redoStack.Add(op);
            BroadcastOrderedOp(inverse, fromPeerId: 0, clientOpId: 0);
            Log.Debug($"Collab: undo of '{CollabProtocol.GetString(op, "type")}' for user {userId}");
            if (skippedReason.Length > 0)
                ReportUndoNotice(requester, skippedReason);

            return;
        }

        ReportUndoNotice(requester, skippedReason.Length > 0 ? skippedReason + "; nothing else to undo" : "Nothing left to undo");
    }

    private void PerformUserRedo(Guid userId, CollabPeerConnection? requester)
    {
        if (State != SessionState.Hosting)
            return;

        ReportUndoNotice(requester, "");
        if (!_redoStacks.TryGetValue(userId, out var redoStack) || redoStack.Count == 0)
        {
            ReportUndoNotice(requester, "Nothing to redo");
            return;
        }

        var op = redoStack[^1];
        redoStack.RemoveAt(redoStack.Count - 1);

        // Redo reverts the undo. When someone changed the target after the undo, leave it.
        var undone = CollabCommandWire.InvertOp(op);
        if (undone != null && !CollabCommandWire.IsOpStillInEffect(undone))
        {
            redoStack.Clear();
            ReportUndoNotice(requester, "Someone else changed this since; redo was dropped");
            return;
        }

        CollabCommandWire.ApplyOp(op);
        AppendToLog(op);
        if (!_undoStacks.TryGetValue(userId, out var undoStack))
            _undoStacks[userId] = undoStack = new List<JObject>();

        undoStack.Add(op);
        BroadcastOrderedOp(op, fromPeerId: 0, clientOpId: 0);
        Log.Debug($"Collab: redo of '{CollabProtocol.GetString(op, "type")}' for user {userId}");
    }

    // ---------------------------------------------------------------------------------------
    // Parameter locks (soft mutexes)
    // ---------------------------------------------------------------------------------------

    public readonly record struct LockKey(Guid CompositionId, Guid ChildId, Guid InputId);

    /// <summary>Active input locks: which participant session id holds which parameter.</summary>
    private readonly Dictionary<LockKey, int> _activeLocks = [];

    public void AcquireInputLock(Guid compositionId, Guid childId, Guid inputId)
    {
        if (!IsSessionActive)
            return;

        var key = new LockKey(compositionId, childId, inputId);
        _activeLocks[key] = MyPeerId;
        BroadcastSystemMessage(new
                                   {
                                       t = "lock",
                                       c = compositionId,
                                       ch = childId,
                                       i = inputId,
                                       id = MyPeerId,
                                   }, except: null);
    }

    public void ReleaseInputLock(Guid compositionId, Guid childId, Guid inputId)
    {
        if (!IsSessionActive)
            return;

        var key = new LockKey(compositionId, childId, inputId);
        if (!_activeLocks.TryGetValue(key, out var owner) || owner != MyPeerId)
        {
            // Someone else took over (e.g. after our reconnect) — don't release their lock.
            return;
        }

        _activeLocks.Remove(key);
        BroadcastSystemMessage(new
                                   {
                                       t = "unlock",
                                       c = compositionId,
                                       ch = childId,
                                       i = inputId,
                                       id = MyPeerId,
                                   }, except: null);
    }

    public bool IsInputLockedByOther(Guid compositionId, Guid childId, Guid inputId)
    {
        return _activeLocks.TryGetValue(new LockKey(compositionId, childId, inputId), out var owner)
               && owner != MyPeerId;
    }

    public sealed class InputLockInfo
    {
        public Guid ChildId;
        public int PeerId;
        public string HolderName = "";
        public Vector4 Color;
    }

    /// <summary>Locks inside the given composition, for the graph badge overlay.</summary>
    public IEnumerable<InputLockInfo> GetInputLocksForComposition(Guid compositionId)
    {
        foreach (var (key, peerId) in _activeLocks)
        {
            if (key.CompositionId != compositionId || peerId == MyPeerId)
                continue;

            var userId = _rosterUserIds.TryGetValue(peerId, out var id) ? id : Guid.Empty;
            if (userId == Guid.Empty)
                continue;

            yield return new InputLockInfo
                             {
                                 ChildId = key.ChildId,
                                 PeerId = peerId,
                                 HolderName = _roster.TryGetValue(peerId, out var name) ? name : "peer",
                                 Color = CollabColors.FromId(userId),
                             };
        }
    }

    private void HandleLock(CollabPeerConnection peer, JObject json, bool acquire)
    {
        if (!peer.IsAuthenticated || !RateAllow(peer))
            return;

        if (!CollabProtocol.TryGetGuid(json, "c", out var compositionId)
            || !CollabProtocol.TryGetGuid(json, "ch", out var childId)
            || !CollabProtocol.TryGetGuid(json, "i", out var inputId))
            return;

        var key = new LockKey(compositionId, childId, inputId);

        // On a host the sender owns the lock; an "id" in its message is only a claim. On a client
        // the message was relayed by the host and the transport peer is always the host (id 0),
        // so the id field is the owner the host recorded.
        var ownerPeerId = peer.AssignedId;
        if (State == SessionState.Connected && CollabProtocol.TryGetInt(json, "id", out var relayedOwnerId))
            ownerPeerId = relayedOwnerId;

        var relay = true;
        if (acquire)
        {
            _activeLocks[key] = ownerPeerId;
        }
        else
        {
            // Only the holder (or the host on their behalf) may clear a lock.
            if (_activeLocks.TryGetValue(key, out var currentOwner) && currentOwner == ownerPeerId)
                _activeLocks.Remove(key);
            else
                relay = false;
        }

        if (State == SessionState.Hosting && relay)
        {
            var envelope = (JObject)json.DeepClone();
            envelope["id"] = ownerPeerId;
            _transport?.BroadcastJson(Peers, envelope, except: peer);
        }
    }

    private void DropLocksOwnedBy(int peerId)
    {
        var released = new List<LockKey>();
        foreach (var (key, owner) in _activeLocks)
        {
            if (owner == peerId)
                released.Add(key);
        }

        foreach (var key in released)
        {
            _activeLocks.Remove(key);
            if (State == SessionState.Hosting)
            {
                BroadcastSystemMessage(new
                                           {
                                               t = "unlock",
                                               c = key.CompositionId,
                                               ch = key.ChildId,
                                               i = key.InputId,
                                               id = peerId,
                                           }, except: null);
            }
        }
    }

    /// <param name="requestedIds">Compositions a client found drifted; null or empty for the
    /// full session state (join, reconnect, missed ops).</param>
    private void SendSyncLog(CollabPeerConnection peer, JArray? requestedIds = null)
    {
        // State sync: instead of replaying the raw op log — which only converges when
        // both sides started from identical projects — send full snapshots of every composition the
        // session touched, plus the host's current composition. apply_snapshot replaces the client's
        // copy of those compositions with the host's state.
        List<Guid> compositionIds;
        if (requestedIds is { Count: > 0 })
        {
            compositionIds = new List<Guid>();
            foreach (var token in requestedIds)
            {
                if (compositionIds.Count >= MaxSyncRequestIds)
                    break;

                if (token.Type is JTokenType.String or JTokenType.Guid
                    && Guid.TryParse(token.ToString(), out var requestedId)
                    && requestedId != Guid.Empty
                    && !compositionIds.Contains(requestedId))
                {
                    compositionIds.Add(requestedId);
                }
            }
        }
        else
        {
            compositionIds = GetSessionCompositionIds();
        }

        // This runs between two ops on the UI thread, so the snapshots are exactly the state
        // after op _hostSeq; the client continues counting from there.
        var snapshotCount = 0;
        SendJson(peer, new { t = "syncStart", count = compositionIds.Count, seq = _hostSeq });
        foreach (var compositionId in compositionIds)
        {
            if (!SymbolUiRegistry.TryGetSymbolUi(compositionId, out var compositionUi))
                continue;

            // Large compositions travel in several messages; see BuildSnapshotOps.
            foreach (var snapshotOp in CollabCommandWire.BuildSnapshotOps(compositionUi))
                SendJson(peer, new { t = "syncOp", op = snapshotOp });

            snapshotCount++;
        }

        SendJson(peer, new { t = "syncEnd" });
        Log.Info($"Collab: sent {snapshotCount} composition snapshot(s) to {peer}");
    }

    private bool RateAllow(CollabPeerConnection peer)
    {
        _opsReceivedThisSecond.TryGetValue(peer.AssignedId, out var count);
        if (count >= MaxOpsPerPeerPerSecond)
            return false;

        _opsReceivedThisSecond[peer.AssignedId] = count + 1;
        return true;
    }

    private const int MaxOpsPerPeerPerSecond = 256;

    // ---------------------------------------------------------------------------------------
    // Presence
    // ---------------------------------------------------------------------------------------

    /// <summary>Called by the graph overlay each frame; only the latest snapshot is kept.</summary>
    public void SetLocalPresence(Guid compositionId, Vector2 cursorOnCanvas, bool hoveringCanvas, Guid[] selectedChildIds,
                                 bool leftButtonDown, bool rightButtonDown, bool middleButtonDown, float wheelSpin,
                                 bool doubleClick, byte[] pressedKeys, bool addingOperator)
    {
        _localPresence.CompositionId = compositionId;
        _localPresence.CursorOnCanvas = cursorOnCanvas;
        _localPresence.SelectedChildIds = selectedChildIds;

        byte flags = 0;
        if (hoveringCanvas)
            flags |= PresencePacket.FlagHoveringCanvas;
        if (leftButtonDown)
            flags |= PresencePacket.FlagLeftButton;
        if (rightButtonDown)
            flags |= PresencePacket.FlagRightButton;
        if (middleButtonDown)
            flags |= PresencePacket.FlagMiddleButton;
        if (doubleClick)
            flags |= PresencePacket.FlagDoubleClick;
        if (addingOperator)
            flags |= PresencePacket.FlagAddingOperator;
        _localPresence.Flags = flags;
        _localPresence.WheelSpin = (sbyte)Math.Clamp(wheelSpin, sbyte.MinValue, sbyte.MaxValue);
        _localPresence.ColorRgb = UserSettings.Config.CollabCursorColor;
        _localPresence.PressedKeys = pressedKeys;
    }

    private void SendLocalPresence()
    {
        var now = Environment.TickCount64;
        if (now < _nextPresenceSendTicks || !IsSessionActive)
            return;

        _nextPresenceSendTicks = now + CollabProtocol.PresenceSendIntervalMs;

        var buffer = new byte[CollabProtocol.MaxPresencePayload];
        var size = _localPresence.Encode(buffer);
        if (size <= 0)
            return;

        var payload = buffer.AsSpan(0, size);

        if (_transport is CollabClientTransport directClient)
        {
            directClient.SendPresence(payload);
        }
        else if (_transport is CollabRelayClientTransport relayClient)
        {
            relayClient.SendPresence(payload);
        }
        else if (_transport is CollabHostTransport host)
        {
            _localPresence.PeerId = 0;
            size = _localPresence.Encode(buffer);
            if (size <= 0)
                return;

            foreach (var peer in host.Peers)
            {
                if (peer.IsAuthenticated)
                    host.SendPresenceTo(peer, buffer.AsSpan(0, size));
            }
        }
        else if (_transport is CollabRelayHostTransport relayHost)
        {
            _localPresence.PeerId = 0;
            size = _localPresence.Encode(buffer);
            if (size <= 0)
                return;

            foreach (var peer in relayHost.Peers)
            {
                if (peer.IsAuthenticated)
                    relayHost.SendPresenceTo(peer, buffer.AsSpan(0, size));
            }
        }
        else if (_transport is CollabBackendWire.CollabBackendClientTransport backendClient)
        {
            if (backendClient.HostLink is { } hostLink)
                backendClient.SendToPeer(hostLink, CollabProtocol.FrameType.Presence, payload);
        }
        else if (_transport is CollabBackendWire.CollabBackendHostTransport backendHost)
        {
            _localPresence.PeerId = 0;
            size = _localPresence.Encode(buffer);
            if (size <= 0)
                return;

            foreach (var peer in backendHost.Peers)
            {
                if (peer.IsAuthenticated)
                    backendHost.SendToPeer(peer, CollabProtocol.FrameType.Presence, buffer.AsSpan(0, size));
            }
        }
    }

    // ---------------------------------------------------------------------------------------
    // Heartbeats, timeouts and discovery
    // ---------------------------------------------------------------------------------------

    private void SendHeartbeatsAndPrune()
    {
        var now = Environment.TickCount64;
        var rateWindowExpired = now - _nextRateResetTicks >= 1000;
        if (rateWindowExpired)
        {
            _nextRateResetTicks = now;
            _opsReceivedThisSecond.Clear();
        }

        if (now < _nextHeartbeatTicks)
            return;

        _nextHeartbeatTicks = now + CollabProtocol.HeartbeatIntervalMs;
        var timestamp = BitConverter.GetBytes(Environment.TickCount64);

        // Round-trip probe: the binary heartbeat echo only comes back on some transports (a data
        // channel answers on the relay, or not at all), so latency rides a JSON message instead —
        // the same path chat uses, which works on every transport.
        var sendRttProbe = now >= _nextRttProbeTicks;
        if (sendRttProbe)
            _nextRttProbeTicks = now + RttProbeIntervalMs;

        foreach (var peer in Peers)
        {
            if (peer.IsTimedOut)
            {
                // On a client the host link's death IS the session's death: a relay peer has no
                // socket of its own to close, so the generic path below would only mark it and
                // leave the session hanging Connected. End the session here instead.
                if (State is SessionState.Connected or SessionState.Connecting && _transport?.HostLink == peer)
                {
                    Log.Info($"Collab: the host link ({peer}) timed out — ending the session");
                    Leave();
                    ErrorText = "Lost connection to the session host";
                    return;
                }

                Log.Info($"Collab: peer {peer} timed out");
                peer.SessionClosed = true;
                peer.Close();
                continue;
            }

            if (peer.IsAuthenticated)
            {
                _transport?.SendToPeer(peer, CollabProtocol.FrameType.Heartbeat, timestamp);
                if (sendRttProbe)
                    SendJson(peer, new { t = "rttPing", stamp = Environment.TickCount64 });
            }
        }
    }

    public void StartDiscovery()
    {
        _discoveryScanner ??= new LanDiscoveryScanner();
        _discovered.Clear();
        _discoveryScanner.Start(DefaultDiscoveryPort);
    }

    public void StopDiscovery()
    {
        _discoveryScanner?.Stop();
    }

    private static int DefaultDiscoveryPort => UserSettings.Config.CollabPort + LanDiscoveryResponder.DiscoveryPortOffset;

    private LanDiscoveryScanner? _hubScanner;
    private long _lastHubScanTicks;

    /// <summary>
    /// Hub-facing discovery refresh: keeps scanning while no session is active so the project
    /// list can show live sessions. During a session the collab update loop refreshes instead,
    /// and the hub scanner shuts down.
    /// </summary>
    public void RefreshDiscoveryFromHub()
    {
        if (State != SessionState.Off)
        {
            if (_hubScanner != null)
            {
                _hubScanner.Stop();
                _hubScanner.Dispose();
                _hubScanner = null;
            }

            return;
        }

        _hubScanner ??= new LanDiscoveryScanner();
        if (Environment.TickCount64 - _lastHubScanTicks > 1000)
        {
            _lastHubScanTicks = Environment.TickCount64;
            _hubScanner.Start(DefaultDiscoveryPort); // re-probe; Start restarts its loop cleanly
        }

        RefreshDiscoveredFrom(_hubScanner);
    }

    private void RefreshDiscoveredSessions()
    {
        if (_discoveryScanner == null)
            return;

        RefreshDiscoveredFrom(_discoveryScanner);
    }

    private void RefreshDiscoveredFrom(LanDiscoveryScanner scanner)
    {
        var cutoff = DateTime.UtcNow - TimeSpan.FromSeconds(3);
        while (scanner.Results.TryDequeue(out var result))
        {
            var announcement = result.Announcement;
            var session = new DiscoveredSession
                              {
                                  Name = CollabProtocol.GetString(announcement, "name", "TiXL session"),
                                  HostName = CollabProtocol.GetString(announcement, "host", ""),
                                  Address = result.Source.Address.ToString(),
                                  Port = CollabProtocol.TryGetInt(announcement, "port", out var port)
                                             ? port
                                             : CollabProtocol.DefaultTcpPort,
                                  PasswordProtected = CollabProtocol.TryGetBool(announcement, "pwd", out var passwordProtected)
                                                          ? passwordProtected
                                                          : false,
                                  Project = CollabProtocol.GetString(announcement, "project", ""),
                              };

            if (_discovered.Any(d => d.Address == session.Address && d.Port == session.Port))
                continue;

            _discovered.Add(session);
            Log.Debug($"Collab: discovered session '{session.Name}' at {session.Address}:{session.Port}");
        }

        _discovered.RemoveAll(d => d.Seen < cutoff);
    }

    // ---------------------------------------------------------------------------------------
    // Helpers shared with transports
    // ---------------------------------------------------------------------------------------

    private void SendJson(CollabPeerConnection peer, object message) => _transport?.SendJson(peer, message);

    private void BroadcastSystemMessage(object message, CollabPeerConnection? except = null) => _transport?.BroadcastJson(Peers, message, except);

    /// <summary>LAN discovery announcement used in the settings tab.</summary>
    public sealed class DiscoveredSession
    {
        public string Name = "";
        public string HostName = "";
        public string Address = "";
        public int Port;
        public bool PasswordProtected;
        public string Project = "";
        public DateTime Seen = DateTime.UtcNow;

        public override string ToString() => $"{Name}  ({Address}:{Port})";
    }

    // ---------------------------------------------------------------------------------------
    // Session chat
    // ---------------------------------------------------------------------------------------

    /// <summary>One line of the session chat; system lines (join/left) carry a null name.</summary>
    public sealed class ChatEntry
    {
        public DateTime Time = DateTime.Now;
        public string? Name;
        public string Text = "";
    }

    private readonly List<ChatEntry> _chatLog = [];
    private const int MaxChatEntries = 200;

    /// <summary>Session chat history, newest last. UI thread only (handlers run on it).</summary>
    public IReadOnlyList<ChatEntry> ChatLog => _chatLog;

    // ---------------------------------------------------------------------------------------
    // Lobby whispers: pre-join messages a visitor sends from the session browser, and the
    // host's replies. Kept separate from the session chat: the visitor is not in the session.
    // ---------------------------------------------------------------------------------------

    /// <summary>One whisper line: a visitor's message to the host, or the host's reply.</summary>
    public sealed class LobbyWhisperEntry
    {
        public DateTime Time = DateTime.Now;
        public string From = "";
        public string Text = "";
        public bool FromHost;
    }

    private readonly List<LobbyWhisperEntry> _lobbyWhispers = [];
    private const int MaxLobbyWhispers = 100;

    /// <summary>Lobby whispers for the session being hosted, oldest first. UI thread only.</summary>
    public IReadOnlyList<LobbyWhisperEntry> LobbyWhispers => _lobbyWhispers;

    private void OnLobbyWhisperTransport(string from, string text)
    {
        _uiThreadActions.Enqueue(() =>
                                 {
                                     _lobbyWhispers.Add(new LobbyWhisperEntry { From = from, Text = text });
                                     if (_lobbyWhispers.Count > MaxLobbyWhispers)
                                         _lobbyWhispers.RemoveAt(0);

                                     AppendChat(null, $"{from} whispers: {text}");
                                 });
    }

    // ---------------------------------------------------------------------------------------
    // Spectator mode and join approval
    // ---------------------------------------------------------------------------------------

    /// <summary>Host toggle: when off, the host drops every graph op from participants (spectators
    /// can watch and chat but not change anything). Broadcast as canEdit so guests can show it.</summary>
    public bool ParticipantsCanEdit { get; private set; } = true;

    /// <summary>True while this session was opened for remote management (either role): its room
    /// is unlisted, the paired controller auto-approves, and the Remote tab shows it as active.</summary>
    public bool IsRemoteManagedSession { get; private set; }

    /// <summary>Guest view: the host runs the session in spectator mode.</summary>
    public bool ReadOnlyForParticipants { get; private set; }

    /// <summary>One waiting join request: who, which ticket, when.</summary>
    public sealed class PendingJoinRequest
    {
        public string TicketId = "";
        public string Name = "";
        public string Lang = "";
        public DateTime At = DateTime.Now;
    }

    private readonly List<PendingJoinRequest> _pendingJoinRequests = [];
    private readonly HashSet<int> _readOnlyNoticeShownFor = [];

    /// <summary>Join requests waiting for the host's decision. UI thread only.</summary>
    public IReadOnlyList<PendingJoinRequest> PendingJoinRequests => _pendingJoinRequests;

    /// <summary>Host: approves a join request; the negotiation starts right away.</summary>
    public void ApproveJoin(string ticketId)
    {
        var request = _pendingJoinRequests.FirstOrDefault(r => r.TicketId == ticketId);
        if (request != null)
            _pendingJoinRequests.Remove(request);

        AppendChat(null, $"{request?.Name ?? "A visitor"} was confirmed into the session");
        (_transport as CollabBackendWire.CollabBackendHostTransport)?.ApproveTicket(ticketId);
        WindowManager.CollabJoinRequestWindow.Config.Visible = false;
    }

    /// <summary>Host: declines a join request; blocked names are declined automatically from now on.</summary>
    public void RejectJoin(string ticketId, bool block)
    {
        var request = _pendingJoinRequests.FirstOrDefault(r => r.TicketId == ticketId);
        if (request != null)
            _pendingJoinRequests.Remove(request);

        if (request != null)
            AppendChat(null, $"{request.Name}'s join request was declined{(block ? " (blocked)" : "")}");
        (_transport as CollabBackendWire.CollabBackendHostTransport)?.RejectTicket(ticketId, block);
        if (_pendingJoinRequests.Count == 0)
            WindowManager.CollabJoinRequestWindow.Config.Visible = false;
    }

    /// <summary>Host: toggles spectator mode and tells everyone.</summary>
    public void SetParticipantsCanEdit(bool value)
    {
        if (State != SessionState.Hosting || ParticipantsCanEdit == value)
            return;

        ParticipantsCanEdit = value;
        _readOnlyNoticeShownFor.Clear();
        _transport?.BroadcastJson(Peers, new { t = "canEdit", value });
        AppendChat(null, value ? "Participants can edit again" : "Participants are spectators now (read-only)");
        Log.Info($"Collab: participants can edit = {value}");

        // Remember the choice so the next party starts in the same mode.
        UserSettings.Config.CollabPartyControllable = value;
        UserSettings.Save();

        // The matching list's "type" column follows immediately, not on the next heartbeat.
        if (State == SessionState.Hosting && RoomCode.Length > 0 && _roomKey.Length > 0 && CollabWebLobby.IsConfigured)
        {
            var config = UserSettings.Config;
            CollabWebLobby.Update(RoomCode, _roomKey,
                                  config.CollabSessionTitle, config.CollabSessionPurpose,
                                  ProjectView.Focused?.OpenedProject.Package.DisplayName ?? "",
                                  config.CollabListSession, value);
        }
    }

    private void OnJoinRequested(string name, string ticketId)
    {
        _uiThreadActions.Enqueue(() =>
                                 {
                                     _pendingJoinRequests.Add(new PendingJoinRequest
                                                                  {
                                                                      TicketId = ticketId,
                                                                      Name = name,
                                                                  });
                                     AppendChat(null, $"{name} requests to join the party");
                                     WindowManager.CollabJoinRequestWindow.Config.Visible = true;
                                     WindowManager.CollabJoinRequestWindow.RequestWindowFocus();
                                 });
    }

    private void HandleCanEdit(CollabPeerConnection peer, JObject json)
    {
        ReadOnlyForParticipants = !(CollabProtocol.TryGetBool(json, "value", out var canEdit) ? canEdit : true);
        AppendChat(null, ReadOnlyForParticipants
                             ? "The host made the session read-only: you can watch and chat"
                             : "The host allows editing again");
    }

    /// <summary>Host: answers a browser whisper. Fire-and-forget; failures land in the log.</summary>
    public void ReplyToLobbyWhisper(string to, string text)
    {
        if (State != SessionState.Hosting || text.Length == 0)
            return;

        _lobbyWhispers.Add(new LobbyWhisperEntry
                               {
                                   From = LocalIdentity.DisplayName,
                                   Text = text,
                                   FromHost = true,
                               });

        var room = RoomCode;
        var key = _roomKey;

        if (_currentTarget is { Relay: true } target)
        {
            _ = Task.Run(async () =>
                             {
                                 try
                                 {
                                     var error = await CollabRelayWire.SendWhisperAsync(target.Host, target.Port, room,
                                                                                        "", "", text, key, to);
                                     if (error != null)
                                         Log.Warning($"Collab: whisper reply failed: {error}");
                                 }
                                 catch (Exception e)
                                 {
                                     Log.Warning($"Collab: whisper reply failed: {e.Message}");
                                 }
                             });
        }

        if (CollabWebLobby.IsConfigured)
            CollabWebLobby.ReplyWhisper(room, key, to, text);
    }

    /// <summary>
    /// Host: updates the listing of the running session (Change entry, Delete entry) in the
    /// web backend and, when the relay keeps its own list, there too.
    /// </summary>
    public void UpdateSessionListing(string title, string purpose, string project, bool listed)
    {
        if (State != SessionState.Hosting)
            return;

        var room = RoomCode;
        var key = _roomKey;

        if (_currentTarget is { Relay: true } target)
        {
            _ = Task.Run(async () =>
                             {
                                 try
                                 {
                                     var error = await CollabRelayWire.UpdateListingAsync(target.Host, target.Port, room, key,
                                                                                          title, purpose, project, listed,
                                                                                          CancellationToken.None);
                                     if (error != null)
                                     {
                                         Log.Warning($"Collab: listing update failed: {error}");
                                         _uiThreadActions.Enqueue(() => ErrorText = $"Updating the session listing failed: {error}");
                                     }
                                 }
                                 catch (Exception e)
                                 {
                                     Log.Warning($"Collab: listing update failed: {e.Message}");
                                 }
                             });
        }

        if (CollabWebLobby.IsConfigured)
        {
            CollabWebLobby.Update(room, key, title, purpose, project, listed);
        }
    }

    public void AppendChat(string? name, string text)
    {
        _chatLog.Add(new ChatEntry { Name = name, Text = text });
        if (_chatLog.Count > MaxChatEntries)
            _chatLog.RemoveRange(0, _chatLog.Count - MaxChatEntries);
    }

    /// <summary>Sends a chat line to the session (to the host as a client, to everyone as host).</summary>
    public void SendChat(string text)
    {
        if (string.IsNullOrWhiteSpace(text) || !IsSessionActive)
            return;

        var message = new
                          {
                              t = "chat",
                              name = _localIdentity.DisplayName,
                              text,
                          };
        if (State == SessionState.Hosting)
        {
            AppendChat(_localIdentity.DisplayName, text);
            BroadcastSystemMessage(message, except: null);
        }
        else if (HostLink is { } host)
        {
            _transport?.SendJson(host, message);
            AppendChat(_localIdentity.DisplayName, text);
        }
    }

    // ---------------------------------------------------------------------------------------
    // Project download on join (host streams the whole project folder, client restarts into it)
    // ---------------------------------------------------------------------------------------

    private sealed class ProjectUpload
    {
        public required CollabPeerConnection Peer;
        public required string ProjectFolder;
        public required string ProjectName;
        public required Queue<string> PendingFiles;
        public BoygroupDeployer.FileSender? Sender;
        public int TotalFiles;
        public int DoneFiles;
        public bool HeaderSent;
    }

    private readonly Dictionary<CollabPeerConnection, ProjectUpload> _projectUploads = new();

    private void HandleRequestProject(CollabPeerConnection peer, JObject json)
    {
        var projectName = CollabProtocol.GetString(json, "project");
        var folder = FindProjectFolder(projectName);
        if (folder == null)
        {
            SendJson(peer, new { t = "projectFail", reason = $"host does not have project '{projectName}'" });
            return;
        }

        // A repeated request replaces the running transfer; its open file handle must go with it.
        DropProjectUpload(peer);

        // Walking the project and hashing every file can take seconds on a real show project;
        // the inventory runs on a worker so the render loop keeps up, and the upload state is
        // built back on the UI thread like every other background completion.
        var generation = _sessionGeneration;
        _ = Task.Run(() =>
                         {
                             var inventory = BoygroupDeployer.BuildInventory(folder);
                             _uiThreadActions.Enqueue(() =>
                                                          {
                                                              if (generation != _sessionGeneration)
                                                                  return;

                                                              _projectUploads[peer] = new ProjectUpload
                                                                                          {
                                                                                              Peer = peer,
                                                                                              ProjectFolder = folder,
                                                                                              ProjectName = ShortProjectName(projectName),
                                                                                              PendingFiles = new Queue<string>(inventory.Keys),
                                                                                              TotalFiles = inventory.Count,
                                                                                          };
                                                              Log.Info($"Collab: sending project '{projectName}' ({inventory.Count} files) to {peer.Name}");
                                                          });
                         });
    }

    /// <summary>Ends a pending upload for one peer and releases the file it had open.</summary>
    private void DropProjectUpload(CollabPeerConnection peer)
    {
        if (!_projectUploads.Remove(peer, out var upload))
            return;

        upload.Sender?.Dispose();
    }

    private static string? FindProjectFolder(string displayName)
    {
        foreach (var p in SymbolPackage.AllPackages)
        {
            if (p is EditableSymbolProject project
                && (string.Equals(project.DisplayName, displayName, StringComparison.OrdinalIgnoreCase)
                    || project.DisplayName.StartsWith(displayName + " (", StringComparison.OrdinalIgnoreCase)))
            {
                return project.CsProjectFile.Directory;
            }
        }

        return null;
    }

    /// <summary>Feeds pending project files into the connection, bounded per frame like the boygroup deploy.</summary>
    private void PumpProjectUploads()
    {
        if (_projectUploads.Count == 0)
            return;

        var budget = 32;
        List<CollabPeerConnection>? finished = null;
        foreach (var upload in _projectUploads.Values)
        {
            while (budget > 0)
            {
                if (!upload.HeaderSent)
                {
                    upload.HeaderSent = true;
                    SendJson(upload.Peer, new { t = "projectBegin", project = upload.ProjectName, files = upload.TotalFiles });
                    budget--;
                    continue;
                }

                if (upload.Sender == null)
                {
                    if (upload.PendingFiles.Count == 0)
                    {
                        SendJson(upload.Peer, new { t = "projectDone", project = upload.ProjectName });
                        (finished ??= []).Add(upload.Peer);
                        break;
                    }

                    var next = upload.PendingFiles.Dequeue();
                    BoygroupDeployer.FileSender sender;
                    try
                    {
                        sender = new BoygroupDeployer.FileSender(upload.ProjectFolder, next);
                    }
                    catch (Exception e)
                    {
                        // The file vanished or became unreadable after the inventory was taken.
                        Log.Warning($"Collab: cannot send {next} to {upload.Peer}: {e.Message}");
                        upload.DoneFiles++;
                        continue;
                    }

                    SendJson(upload.Peer, new { t = "fileBegin", path = next });
                    upload.Sender = sender;
                    budget--;
                    continue;
                }

                JObject? message;
                try
                {
                    message = upload.Sender.NextMessage();
                }
                catch (Exception e)
                {
                    // A read error must not escape into the frame loop mid-transfer.
                    Log.Warning($"Collab: cannot read {upload.Sender.RelativePath} for {upload.Peer}: {e.Message}");
                    upload.Sender.Dispose();
                    upload.Sender = null;
                    upload.DoneFiles++;
                    continue;
                }

                if (message == null)
                {
                    upload.Sender.Dispose();
                    upload.Sender = null;
                    upload.DoneFiles++;
                    continue;
                }

                SendJson(upload.Peer, message);
                budget--;
            }
        }

        if (finished != null)
        {
            foreach (var peer in finished)
                _projectUploads.Remove(peer);
        }
    }

    private BoygroupDeployer.FileReceiver? _projectDownload;
    private string _projectDownloadFolder = "";
    private long _restartAtTicks;

    private void HandleProjectDownload(CollabPeerConnection peer, JObject json)
    {
        // Only the host streams a project; a peer sending transfer frames must never create
        // directories or write files here (the path checks below are the second line of defense).
        if (State != SessionState.Connected || !ReferenceEquals(peer, HostLink))
            return;

        try
        {
            HandleProjectDownloadMessage(json);
        }
        catch (Exception e)
        {
            // Malformed base64, an unsafe path or an unwritable folder must fail this transfer,
            // not the render loop that drains the inbound queue.
            Log.Warning($"Collab: project download failed: {e.Message}");
            StatusText = "Project download failed";
            _projectDownload?.Cancel();
            _projectDownload = null;
        }
    }

    private void HandleProjectDownloadMessage(JObject json)
    {
        var type = CollabProtocol.GetString(json, "t");
        switch (type)
        {
            case "projectBegin":
            {
                var name = SafeProjectFolderName(CollabProtocol.GetString(json, "project"));
                if (name == null)
                    throw new IOException($"host offered a project with an unusable folder name");
                if (UserSettings.Config.ProjectDirectories.Count == 0)
                    throw new IOException("no project directory is configured");

                _projectDownload?.Cancel();
                _projectDownloadFolder = Path.Combine(UserSettings.Config.ProjectDirectories[0], name);
                Directory.CreateDirectory(_projectDownloadFolder);
                _projectDownload = new BoygroupDeployer.FileReceiver();
                CollabProtocol.TryGetInt(json, "files", out var fileCount);
                Log.Info($"Collab: receiving project '{name}' ({fileCount} files) into {_projectDownloadFolder}");
                break;
            }

            case "fileBegin":
                _projectDownload?.Begin(_projectDownloadFolder, json);
                break;

            case "fileChunk":
                _projectDownload?.Chunk(json);
                break;

            case "fileEnd":
            {
                var error = _projectDownload?.Finish(json);
                if (error != null)
                {
                    Log.Warning($"Collab: project download failed: {error}");
                    StatusText = "Project download failed";
                    _projectDownload?.Cancel();
                    _projectDownload = null;
                }

                break;
            }

            case "projectDone":
            {
                var name = CollabProtocol.GetString(json, "project");
                _projectDownload?.Cancel();
                _projectDownload = null;
                StatusText = $"Project '{name}' downloaded — restarting to load it...";
                Log.Info($"Collab: project '{name}' downloaded; restarting to load it (session will rejoin automatically)");

                // The restart must land IN the downloaded project. Without pinning it, the editor
                // reopened whatever the startup project was (the host's other project), rejoined,
                // saw the mismatch and downloaded again - an endless restart cycle.
                if (_projectDownloadFolder.Length > 0)
                {
                    UserSettings.Config.StartupProjectFolder = _projectDownloadFolder;
                    UserSettings.Config.LastOpenProjectFolder = _projectDownloadFolder;
                    NetworkProjectRegistry.Register(NetworkProjectRegistry.CollabKind, _projectDownloadFolder);
                    UserSettings.Save();
                }

                _restartAtTicks = Environment.TickCount64 + 1500;
                break;
            }

            case "projectFail":
                _projectDownload?.Cancel();
                _projectDownload = null;
                ErrorText = CollabProtocol.GetString(json, "reason");
                StatusText = "Project download failed";
                break;
        }
    }

    /// <summary>
    /// Validates the host-supplied project name, which becomes one folder below the local project
    /// directory. A rooted path or a ".." segment would put the downloaded files anywhere this
    /// machine can write.
    /// </summary>
    internal static string? SafeProjectFolderName(string name)
    {
        name = (name ?? "").Trim();
        if (name.Length == 0 || name is "." or ".." || name.Contains(".."))
            return null;

        if (name.Contains('/') || name.Contains('\\') || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || Path.IsPathRooted(name))
            return null;

        // Windows would silently strip these (and resolve to a sibling target).
        if (OperatingSystem.IsWindows() && (name[^1] == '.' || name[^1] == ' '))
            return null;

        return name;
    }
}
