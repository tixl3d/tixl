#nullable enable
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using NAudio.Midi;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Rug.Osc;
using T3.Core.Animation;
using T3.Core.IO;
using T3.Core.Output;
using T3.Editor.Collab;
using T3.Editor.Compilation;
using T3.Editor.Gui.Windows.OutputSetup;
using T3.Editor.Gui.UiHelpers;
using T3.Editor.SystemUi;
using T3.Editor.UiModel;
using T3.Editor.UiModel.Commands;
using T3.Editor.UiModel.ProjectHandling;
using T3.IoServices;

namespace T3.Editor.Show;

/// <summary>
/// Boygrouping: one TiXL instance (server) distributes a show to render-node clients, vvvv-style.
/// The server deploys project files and live output state, drives playback, and each client renders
/// only the outputs assigned to it in the Output Setup (OutputDefinition.MachineName).
///
/// Threading contract matches CollabManager: network threads only enqueue inbound messages; every
/// state change and every graph/file write happens in <see cref="Update"/> on the UI thread. File
/// transfers run per client in small steps per frame so large deployments never stall rendering.
/// </summary>
internal sealed class BoygroupManager
{
    public static BoygroupManager Instance { get; } = new();

    public enum BoygroupState
    {
        Off,
        Server,
        Client,
    }

    public BoygroupState State { get; private set; } = BoygroupState.Off;

    /// <summary>Set by a manual Leave: the enabled-role automation must not rejoin on its own.</summary>
    private bool _leftManually;
    public string StatusText { get; private set; } = "Boygrouping is off";
    public string ErrorText { get; private set; } = "";
    public string GroupName { get; private set; } = "";
    /// <summary>IP of the master this node joined as a client; null when serving, off or unknown.</summary>
    public string? ConnectedMasterAddress => (_transport as BoygroupClientTransport)?.ServerPeer.RemoteAddress?.ToString();

    public string TimeFollowSummary { get; private set; } = "Time follow off";
    public string DeploySummary { get; private set; } = "";
    public bool RestartPending { get; private set; }

    /// <summary>A render node's display as it reported itself; the master offers it as a binding target.</summary>
    public sealed record RemoteScreen(int Index, string Name, int Width, int Height, int X = 0, int Y = 0, bool Primary = false);

    public sealed class ClientInfo
    {
        public required CollabPeerConnection Peer;
        public string Name = "";
        public string Address = "";
        public string Group = "";
        public string StatusText = "connected";
        public bool IsDeployed;

        /// <summary>The node's machine-local output bindings, so the master sees where it presents.</summary>
        public MachineConfig? MachineConfig;

        /// <summary>The node's displays as reported; ids for them come from <see cref="RemoteDisplayPlugId"/>.</summary>
        public List<RemoteScreen> Screens = [];

        /// <summary>The project name the node last reported. Empty until it answered an inventory request.</summary>
        public string KnownProject = "";

        // Pending auth challenge (server side)
        public byte[]? PendingSalt;
        public byte[]? PendingNonce;

        // Deploy pipeline state (server side)
        public Dictionary<string, BoygroupDeployer.InventoryEntry>? PendingInventory;
        public List<string>? PendingFiles;
        public int FilesDone;
        public BoygroupDeployer.FileSender? Sender;
        public bool Mirror;
    }

    private readonly List<ClientInfo> _clients = [];
    public IReadOnlyList<ClientInfo> Clients => _clients;

    public sealed class DiscoveredServer
    {
        public string ServerName = "";
        public string Address = "";
        public int Port;
        public List<string> Groups = [];
        public DateTime Seen = DateTime.UtcNow;
    }

    private readonly List<DiscoveredServer> _discoveredServers = [];
    public IReadOnlyList<DiscoveredServer> DiscoveredServers => _discoveredServers;

    private LanDiscoveryScanner? _discoveryScanner;
    private BoygroupTransport? _transport;
    private string _password = "";

    /// <summary>The active link while a session is running; null when boygrouping is off. The peer
    /// camera service tracks it so cameras flow between the machines of the session.</summary>
    public CollabTransport? PeerSessionTransport => _transport;

    public bool PeerSessionIsHost => State == BoygroupState.Server;

    private long _nextHeartbeatTicks;
    private long _nextTimeSyncTicks;
    private long _autoJoinCheckTicks;
    private long _nextServerRestartTicks;
    private long _nextSlaveJoinAttemptTicks;

    // Input replication. Hardware callbacks enqueue from their own threads; the UI thread batches them
    // into one message per frame.
    private const int MaxQueuedInputEvents = 8192;
    private readonly ConcurrentQueue<RemoteMidiEvent> _outgoingMidi = new();
    private readonly ConcurrentQueue<RemoteOscPacket> _outgoingOsc = new();
    private readonly List<RemoteMidiEvent> _outgoingMidiBatch = [];
    private readonly List<RemoteOscPacket> _outgoingOscBatch = [];

    // Client-side deploy receiver
    private readonly BoygroupDeployer.FileReceiver _fileReceiver = new();

    private BoygroupManager()
    {
        RemoteInputBus.MidiCaptured += OnMidiCaptured;
        RemoteInputBus.OscCaptured += OnOscCaptured;
        RemoteInputBus.ErrorReported += message => Log.Warning(message);
    }

    /// <summary>Adds the machines this instance is currently linked to: the render nodes on a
    /// master, the master on a render node — the settings panel's network check compares them
    /// against the selected adapter.</summary>
    public void AddLinkedPeers(List<NetworkPeer> peers)
    {
        if (State == BoygroupState.Client)
        {
            var address = (_transport as BoygroupClientTransport)?.ServerPeer.RemoteAddress;
            if (address != null)
                peers.Add(new NetworkPeer("Boygroup master", address));
        }
        else if (State == BoygroupState.Server)
        {
            foreach (var client in _clients)
            {
                var address = client.Peer.RemoteAddress;
                if (address != null)
                    peers.Add(new NetworkPeer($"Boygroup node {client.Name}", address));
            }
        }
    }

    /// <summary>What the input replication is doing, for the settings panel and the debug bridge.</summary>
    public string InputRelaySummary => State switch
                                           {
                                               BoygroupState.Server => "MIDI/OSC from this machine",
                                               BoygroupState.Client => "Receiving MIDI/OSC",
                                               _                    => "",
                                           };

    /// <summary>True while this master forwards its hardware input to at least one render node.</summary>
    public bool IsCapturingInputs => RemoteInputBus.IsCapturing;

    private void OnMidiCaptured(RemoteMidiEvent ev)
    {
        if (State != BoygroupState.Server)
            return;

        if (_outgoingMidi.Count >= MaxQueuedInputEvents)
        {
            Interlocked.Increment(ref _droppedInputEvents);
            return;
        }

        _outgoingMidi.Enqueue(ev);
    }

    private void OnOscCaptured(RemoteOscPacket packet)
    {
        if (State != BoygroupState.Server)
            return;

        if (_outgoingOsc.Count >= MaxQueuedInputEvents)
        {
            Interlocked.Increment(ref _droppedInputEvents);
            return;
        }

        _outgoingOsc.Enqueue(packet);
    }

    private long _droppedInputEvents;

    // ---------------------------------------------------------------------------------------
    // Lifecycle
    // ---------------------------------------------------------------------------------------

    public void StartServer(int port, string password, string group)
    {
        _leftManually = false;
        if (State != BoygroupState.Off)
            Leave();

        ErrorText = "";
        try
        {
            _password = password ?? "";
            GroupName = group;
            _transport = new BoygroupServerTransport(port, UserSettings.Config.BoygroupGroups.Distinct().ToArray(), UserSettings.Config.BoygroupInterface);
            _transport.PeerDisconnected += OnPeerDisconnected;
            State = BoygroupState.Server;
            StatusText = $"Serving group '{group}' on port {port}";
            Log.Info($"Boygroup: server started (group '{group}', port {port})");
        }
        catch (SocketException e)
        {
            ErrorText = $"Could not open port {port}: {e.Message}";
            _transport = null;
            State = BoygroupState.Off;
        }
    }

    public void JoinServer(string host, int port, string group, string password)
    {
        _leftManually = false;
        if (State != BoygroupState.Off)
            Leave();

        ErrorText = "";
        try
        {
            _password = password ?? "";
            GroupName = group;
            var address = ResolveHost(host);
            if (address == null)
                throw new ArgumentException($"'{host}' is not a valid address");

            var transport = BoygroupClientTransport.Connect(new IPEndPoint(address, port));
            _transport = transport;
            transport.PeerDisconnected += OnPeerDisconnected;
            State = BoygroupState.Client;
            StatusText = $"Joining '{group}' on {host}:{port}...";
            // Subscribe first, then start reading: a link that dies right now must report.
            transport.StartReading();
            SendJson(transport.ServerPeer,
                     new
                     {
                         t = "hello",
                         name = ClientDisplayName(),
                         group,
                     });
            PersistJoinTarget(host, group);
            StopDiscovery(); // joined: no need to keep probing (the wait loop restarts it if we drop)
        }
        catch (Exception e)
        {
            ErrorText = $"Boygroup join failed: {e.Message}";
            _transport?.Dispose();
            _transport = null;
            State = BoygroupState.Off;
        }
    }

    /// <summary>
    /// Ends the session. <paramref name="manual"/> marks a leave the user asked for: the enabled-role
    /// automation (auto-join for a slave, auto-restart for a master) then stays quiet instead of
    /// rejoining a second later, which made the Leave button look broken. Starting or joining a
    /// session, or switching into the mode, clears the mark.
    /// </summary>
    public void Leave(bool manual = false)
    {
        _leftManually = manual;

        if (_transport != null)
        {
            _transport.PeerDisconnected -= OnPeerDisconnected;
            _transport.Dispose();
            _transport = null;
        }

        foreach (var client in _clients)
        {
            client.Sender?.Dispose();
        }

        _clients.Clear();
        _fileReceiver.Cancel();
        while (_pendingDisconnects.TryDequeue(out _))
        {
            // Drop events from the dead session: replaying them into the next one would end it
            // immediately (the collab manager drains its queue for the same reason).
        }

        _password = "";
        ResetClockSync();
        GroupName = "";
        RestartPending = false;
        State = BoygroupState.Off;
        StatusText = manual ? "Boygrouping left" : "Boygrouping is off";
    }

    /// <summary>The name this machine is known by in output assignments (OutputDefinition.MachineName).</summary>
    public static string LocalNodeName =>
        string.IsNullOrWhiteSpace(UserSettings.Config.BoygroupClientName) ? Environment.MachineName : UserSettings.Config.BoygroupClientName;

    private static string ClientDisplayName() => LocalNodeName;

    private void PersistJoinTarget(string host, string group)
    {
        var config = UserSettings.Config;
        if (config.BoygroupLastServer == host && config.BoygroupLastGroup == group)
            return;

        config.BoygroupLastServer = host;
        config.BoygroupLastGroup = group;
        UserSettings.Save();
    }

    /// <summary>Auto-rejoin after a deployment restart (or normal launch) when enabled.</summary>
    private void TryAutoJoin()
    {
        var config = UserSettings.Config;
        if (!config.BoygroupAutoJoin || string.IsNullOrEmpty(config.BoygroupLastServer) || string.IsNullOrEmpty(config.BoygroupLastGroup))
            return;

        Log.Info($"Boygroup: auto-joining {config.BoygroupLastServer} group '{config.BoygroupLastGroup}'");
        var port = config.BoygroupPort;
        JoinServer(config.BoygroupLastServer, port, config.BoygroupLastGroup, config.CollabPassword);
    }

    /// <summary>
    /// Applies the enable switch from the settings: starts the server when this machine is a
    /// master, joins the last server when it is a slave, leaves when disabled. Called from the
    /// panel toggle and once per launch (after the settle delay), so a show machine comes up
    /// in its role without anyone clicking.
    /// </summary>
    public void ApplyEnabledSetting()
    {
        var config = UserSettings.Config;
        if (!config.BoygroupEnabled)
        {
            if (State != BoygroupState.Off)
                Leave();
            StopDiscovery();
            return;
        }

        // A role flip while running means leave + re-apply on the next call.
        if (State != BoygroupState.Off && (State == BoygroupState.Server) != config.BoygroupIsMaster)
            Leave();

        if (State != BoygroupState.Off)
            return; // Already running in the configured role.

        if (config.BoygroupIsMaster)
        {
            StartServer(config.BoygroupPort, config.CollabPassword, config.BoygroupGroups.FirstOrDefault() ?? "Main");
        }
        else
        {
            TryAutoJoin();
        }
    }

    /// <summary>
    /// An enabled slave that has never joined a master keeps scanning and joins the first master
    /// that appears, like the master role keeps serving: the pair finds each other after a reboot
    /// in any order. Join attempts are throttled so a dead candidate can never flap the frame
    /// loop; a failed attempt clears the candidate list and the scanner restarts on its own.
    /// </summary>
    private void WaitAndAutoJoinDiscoveredMaster()
    {
        if (_discoveryScanner == null)
            StartDiscovery();

        DrainDiscovery();

        if (Environment.TickCount64 < _nextSlaveJoinAttemptTicks)
            return;

        var config = UserSettings.Config;
        var preferredGroup = config.BoygroupLastGroup is { Length: > 0 } last ? last : null;

        // Prefer a master that hosts the group we want; otherwise take the only / first one.
        DiscoveredServer? candidate = null;
        if (preferredGroup != null)
        {
            foreach (var server in _discoveredServers)
            {
                if (server.Groups.Contains(preferredGroup))
                {
                    candidate = server;
                    break;
                }
            }
        }

        if (candidate == null && _discoveredServers.Count > 0)
            candidate = _discoveredServers[0];

        if (candidate == null)
            return;

        _nextSlaveJoinAttemptTicks = Environment.TickCount64 + 3_000;
        Log.Info($"Boygroup: enabled slave joining discovered master {candidate.ServerName} ({candidate.Address}:{candidate.Port})");
        StopDiscovery(); // clears the candidate list; the scanner restarts if this attempt fails
        JoinServer(candidate.Address, candidate.Port,
                   preferredGroup ?? config.BoygroupGroups.FirstOrDefault() ?? candidate.Groups.FirstOrDefault() ?? "Main",
                   config.CollabPassword);
    }

    private static IPAddress? ResolveHost(string host)
    {
        if (IPAddress.TryParse(host, out var parsed))
            return parsed;

        try
        {
            return Dns.GetHostAddresses(host).FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork);
        }
        catch (SocketException)
        {
            return null;
        }
    }

    // ---------------------------------------------------------------------------------------
    // Per-frame update (UI thread)
    // ---------------------------------------------------------------------------------------

    private const int MaxInboundPerFrame = 256;

    public void Update()
    {
        // Apply the enable switch shortly after startup (give the project time to load first):
        // an enabled master starts its server, an enabled slave auto-joins the last server.
        if (State == BoygroupState.Off)
        {
            if (_autoJoinCheckTicks == 0)
            {
                _autoJoinCheckTicks = Environment.TickCount64 + 8000;
            }
            else if (Environment.TickCount64 > _autoJoinCheckTicks && !_autoJoinAttempted)
            {
                _autoJoinAttempted = true;
                if (_leftManually)
                {
                    // The user left on purpose; the role automation waits for an explicit start.
                }
                else if (UserSettings.Config.BoygroupEnabled)
                {
                    ApplyEnabledSetting();
                }
                else
                {
                    // Auto-join alone must not claim the boygroup mode: joining anywhere else (the
                    // hub, a collab or failover session) pulled the machine into a group behind the
                    // operator's back, which made turning boygrouping off look ineffective.
                    Log.Info("Boygroup: disabled - not auto-joining (enable the mode to follow a master)");
                }
            }
            else if (_autoJoinAttempted && !_leftManually && UserSettings.Config.BoygroupEnabled && !UserSettings.Config.BoygroupIsMaster)
            {
                // Enabled slave still off: keep looking for a master.
                WaitAndAutoJoinDiscoveredMaster();
            }
            else if (_autoJoinAttempted && !_leftManually && UserSettings.Config.BoygroupEnabled && UserSettings.Config.BoygroupIsMaster
                     && Environment.TickCount64 >= _nextServerRestartTicks)
            {
                // Enabled master whose server dropped (port conflict, transport fault): bring it
                // back. Throttled so a persistent failure cannot spin the frame loop.
                _nextServerRestartTicks = Environment.TickCount64 + 5_000;
                ApplyEnabledSetting();
            }

            return;
        }

        ProcessPendingDisconnects();

        // Disconnects and inbound messages can both end the session (server loss, restart
        // request), disposing the transport — the remaining steps must not run against it.
        if (State == BoygroupState.Off)
            return;

        DrainInbound();

        if (State == BoygroupState.Off)
            return;

        PumpDeploy();
        RunScheduledAutoDeploy();
        SendHeartbeats();
        SendTimeSync();
        SendClockPings();
        ApplyPendingAnchor();
        MaybeSendBindings();
        ProcessPendingRestart();

        if (State == BoygroupState.Server)
        {
            PumpInputReplication();
            ReRequestPendingInventories();
            FollowSharedProject();
            SyncLiveValues();
            BroadcastPlayStateOnChange();
        }

        RefreshDiscoveredServers();
    }

    private static bool _autoJoinAttempted;

    private void DrainInbound()
    {
        var processed = 0;
        // A message handler may end the session (server loss, restart), nulling the transport
        // mid-loop — the null-conditional keeps the loop condition safe on the next iteration.
        while (processed < MaxInboundPerFrame && _transport?.Inbound.TryDequeue(out var message) == true)
        {
            processed++;
            if (message.IsPresence || message.Json == null)
                continue;

            HandleMessage(message.Peer, message.Json);
        }
    }

    private readonly ConcurrentQueue<CollabPeerConnection> _pendingDisconnects = [];

    private void OnPeerDisconnected(CollabPeerConnection peer)
    {
        _pendingDisconnects.Enqueue(peer);
    }

    private void ProcessPendingDisconnects()
    {
        while (_pendingDisconnects.TryDequeue(out var peer))
        {
            var client = _clients.FirstOrDefault(c => ReferenceEquals(c.Peer, peer));
            if (client != null)
            {
                client.Sender?.Dispose();
                _clients.Remove(client);
                Log.Info($"Boygroup: client left: {client.Name}");
                continue;
            }

            if (State == BoygroupState.Client)
            {
                ErrorText = "Disconnected from boygroup server";
                Leave();
            }
        }
    }

    // ---------------------------------------------------------------------------------------


    // ---------------------------------------------------------------------------------------
    // Input replication
    // ---------------------------------------------------------------------------------------

    private void PumpInputReplication()
    {
        if (_outgoingMidi.IsEmpty && _outgoingOsc.IsEmpty)
            return;

        _outgoingMidiBatch.Clear();
        _outgoingOscBatch.Clear();

        for (var i = 0; i < BoygroupInputWire.MaxEventsPerBatch && _outgoingMidi.TryDequeue(out var midi); i++)
            _outgoingMidiBatch.Add(midi);

        for (var i = 0; i < BoygroupInputWire.MaxEventsPerBatch && _outgoingOsc.TryDequeue(out var osc); i++)
            _outgoingOscBatch.Add(osc);

        if (_outgoingMidiBatch.Count == 0 && _outgoingOscBatch.Count == 0)
            return;

        BroadcastToGroup(BoygroupInputWire.Encode(_outgoingMidiBatch, _outgoingOscBatch));

        // A producer faster than the link must not make the queue grow without bound.
        var dropped = Interlocked.Exchange(ref _droppedInputEvents, 0);
        if (dropped > 0)
            Log.Warning($"Boygroup: dropped {dropped} input events because the replication queue overflowed");
    }

    /// <summary>Client side: replays the master's replicated MIDI and OSC events through the same
    /// graph-side input operators a local device would drive.</summary>
    private static void ApplyRemoteInput(JObject json)
    {
        if (!BoygroupInputWire.TryDecode(json, out var midi, out var osc))
            return;

        foreach (var remote in midi)
        {
            try
            {
                var midiEvent = MidiEvent.FromRawMessage(remote.RawMessage);
                if (midiEvent == null)
                    continue;

                SimulatedIoBus.DispatchMidi(new SimulatedIoBus.SimulatedMidiEvent(remote.DeviceProductName, midiEvent));
                RemoteInputBus.MarkRemoteMidiReceived();
            }
            catch (Exception e)
            {
                Log.Debug($"Boygroup: dropping a malformed MIDI event: {e.Message}");
            }
        }

        foreach (var remote in osc)
        {
            try
            {
                if (OscPacket.Read(remote.Packet, 0, remote.Packet.Length) is not OscMessage message)
                    continue;

                SimulatedIoBus.DispatchOscPacket(new SimulatedIoBus.SimulatedOscPacket(remote.Port, message));
                RemoteInputBus.MarkRemoteOscReceived();
            }
            catch (Exception e)
            {
                Log.Debug($"Boygroup: dropping a malformed OSC packet: {e.Message}");
            }
        }
    }

    // Messages
    // ---------------------------------------------------------------------------------------

    private void HandleMessage(CollabPeerConnection peer, JObject json)
    {
        var type = CollabProtocol.GetString(json, "t");

        // Only the handshake may arrive before a peer authenticated: everything else mutates
        // machine state, binds displays or feeds the deploy pump, and a connection that never
        // passed the master's password challenge must not drive any of that.
        if (!peer.IsAuthenticated && type is not ("hello" or "auth" or "welcome" or "authOk" or "authFail"))
        {
            Log.Debug($"Boygroup: ignoring '{type}' from unauthenticated {peer}");
            return;
        }

        switch (type)
        {
            case "hello" when State == BoygroupState.Server:
                HandleHello(peer, json);
                break;

            case "io" when State == BoygroupState.Client:
                ApplyRemoteInput(json);
                break;

            case "welcome":
                HandleWelcome(json);
                break;

            case "auth" when State == BoygroupState.Server:
                HandleAuth(peer, json);
                break;

            case "authOk":
                StatusText = "Joined group";

                // Mark the master connection as trusted so its messages pass the authentication gate.
                if (_transport is BoygroupClientTransport clientTransport)
                    clientTransport.ServerPeer.IsAuthenticated = true;

                // The first report can go out before this node is authenticated on the server, where
                // it is dropped; the dedup in MaybeSendBindings would then never send it again. Force
                // one report now that the master is ready to accept it.
                _lastBindingsSent = "";
                _nextBindingsSendTicks = 0;
                break;

            case "authFail":
                ErrorText = "Rejected by server: " + CollabProtocol.GetString(json, "reason", "authentication failed");
                Leave();
                break;

            case "noProject" when State == BoygroupState.Server:
                HandleNoProject(peer);
                break;

            case "inventory" when State == BoygroupState.Server:
            {
                // The client answered our requestInventory with what it has on disk.
                var requestingClient = _clients.FirstOrDefault(c => ReferenceEquals(c.Peer, peer));
                if (requestingClient != null)
                    HandleClientInventory(requestingClient, json);
                break;
            }

            case "requestInventory" when State == BoygroupState.Client:
                HandleServerInventory(json);
                break;

            case "projectName" when State == BoygroupState.Client:
                HandleBootstrapProject(json);
                break;

            case "deployStart" when State == BoygroupState.Client:
                _deployFilesTotal = CollabProtocol.TryGetInt(json, "count", out var filesTotal) ? filesTotal : 0;
                _deployFilesDone = 0;
                _deployErrors = 0;
                _deployedRelativePaths.Clear();
                DeploySummary = $"Receiving {_deployFilesTotal} file(s)...";
                break;

            case "fileBegin" when State == BoygroupState.Client:
                StartReceivingFile(json);
                break;

            case "fileChunk" when State == BoygroupState.Client:
                _fileReceiver.Chunk(json);
                break;

            case "fileEnd" when State == BoygroupState.Client:
                FinishReceivingFile(json);
                break;

            case "deleteFile" when State == BoygroupState.Client:
                DeleteClientFile(json);
                break;

            case "deployDone" when State == BoygroupState.Client:
                HandleDeployDone(json);
                break;

            case "setupJson" when State == BoygroupState.Client:
                WriteSetupFile(json);
                break;

            case "snapshot" when State == BoygroupState.Client:
                if (json["op"] is JObject op)
                    CollabCommandWire.ApplyOp(op);

                break;

            // Live values from the master. Applied without marking the project modified: they are
            // show state, not edits — a dirty project gets saved, differs from the master's file and
            // is deployed back with a restart.
            case "op" when State == BoygroupState.Client:
                if (json["op"] is JObject valueOp)
                    CollabCommandWire.ApplyOp(valueOp, markModified: false);

                break;

            case "bindings" when State == BoygroupState.Server:
                HandleClientBindings(json);
                break;

            case "bindOutput" when State == BoygroupState.Client:
                HandleRemoteBinding(json, bind: true);
                break;

            case "unbindOutput" when State == BoygroupState.Client:
                HandleRemoteBinding(json, bind: false);
                break;

            case "clockPing" when State == BoygroupState.Server:
                // Timestamp both ends of the master's handling so the client can subtract its own
                // processing; the two are the same instant here, but the shape keeps NTP semantics.
                CollabProtocol.TryGetLong(json, "id", out var pingId);
                CollabProtocol.TryGetLong(json, "c1", out var pingC1);
                SendJson(peer, new
                                   {
                                       t = "clockPong",
                                       id = pingId,
                                       c1 = pingC1,
                                       m2 = PreciseUtcNowTicks(),
                                       m3 = PreciseUtcNowTicks(),
                                   });
                break;

            case "clockPong" when State == BoygroupState.Client:
                HandleClockPong(json);
                break;

            case "play" when State == BoygroupState.Client:
                ApplyPlayControl(json);
                break;

            case "time" when State == BoygroupState.Client:
                ApplyTimeSync(json);
                break;

            default:
                Log.Debug($"Boygroup: ignoring message '{type}' in state {State}");
                break;
        }
    }

    private int _deployFilesTotal;
    private int _deployFilesDone;
    private readonly List<string> _deployedRelativePaths = [];
    private int _deployErrors;

    // ---------------------------------------------------------------------------------------
    // Server: handshake
    // ---------------------------------------------------------------------------------------

    private void HandleHello(CollabPeerConnection peer, JObject json)
    {
        // A hello from an unregistered connection is simply a new client: the transport accepts
        // peers on its own, so this is where the server first learns who connected.
        var client = _clients.FirstOrDefault(c => ReferenceEquals(c.Peer, peer));
        if (client == null)
        {
            client = new ClientInfo { Peer = peer };
            _clients.Add(client);
        }

        client.Name = CollabProtocol.GetString(json, "name", "client");
        client.Group = CollabProtocol.GetString(json, "group", GroupName);
        client.Address = (peer.Client?.Client.RemoteEndPoint as IPEndPoint)?.Address.ToString() ?? "?";

        if (string.IsNullOrEmpty(_password))
        {
            CompleteAuth(client);
            return;
        }

        var salt = CollabAuth.CreateSalt();
        var nonce = CollabAuth.CreateNonce();
        client.PendingSalt = salt;
        client.PendingNonce = nonce;
        SendJson(peer, new
                           {
                               t = "welcome",
                               pwd = true,
                               salt = CollabAuth.ToHexString(salt),
                               nonce = CollabAuth.ToHexString(nonce),
                           });
    }

    private void HandleAuth(CollabPeerConnection peer, JObject json)
    {
        var client = _clients.FirstOrDefault(c => ReferenceEquals(c.Peer, peer));
        if (client?.PendingSalt == null)
        {
            Reject(peer, "no challenge pending");
            return;
        }

        if (!CollabAuth.TryFromHexString(CollabProtocol.GetString(json, "mac"), out var mac)
            || mac.Length != CollabAuth.MacSizeBytes)
        {
            Reject(peer, "malformed authentication response");
            return;
        }

        var key = CollabAuth.DeriveKey(_password, client.PendingSalt);
        var expected = CollabAuth.ComputeMac(key, client.PendingNonce!);
        if (!CollabAuth.MacsEqual(expected, mac))
        {
            Reject(peer, "wrong password");
            return;
        }

        CompleteAuth(client);
    }

    private void CompleteAuth(ClientInfo client)
    {
        client.Peer.IsAuthenticated = true;
        SendJson(client.Peer, new { t = "authOk", serverName = Environment.MachineName });

        // Late joiners must see the show as it runs: the play broadcast only fires when the
        // master's playback state changes, so a client that joined mid-show stayed frozen.
        var playback = Playback.Current;
        SendJson(client.Peer, new
                               {
                                   t = "play",
                                   playing = (playback?.PlaybackSpeed ?? 0) > 0,
                                   bars = playback?.TimeInBars ?? 0,
                               });

        Log.Info($"Boygroup: client authenticated: {client.Name} ({client.Address}) group '{client.Group}'");

        // The new node starts from the values saved on disk. Forget the change baseline so the next
        // sync pass sends every live value, not just the ones that happen to change afterwards.
        _sentValues.Clear();

        // A node that appears is served: schedule a deploy shortly after the handshake (the delay
        // debounces a burst of joins into one pass; clients mid-deploy keep their own transfer).
        if (UserSettings.Config.BoygroupAutoDeployOnJoin)
            _autoDeployAtTicks = Environment.TickCount64 + 1500;
    }

    /// <summary>Due time for the deploy-on-join pass; 0 while nothing is scheduled.</summary>
    private long _autoDeployAtTicks;

    private void RunScheduledAutoDeploy()
    {
        if (_autoDeployAtTicks == 0 || Environment.TickCount64 < _autoDeployAtTicks)
            return;

        _autoDeployAtTicks = 0;
        _autoDeployOnlyUndeployed = true;
        if (State != BoygroupState.Server || string.IsNullOrEmpty(GetProjectFolder()))
            return; // No project open: a manual Deploy & Sync will report it properly.

        Log.Info("Boygroup: node joined - running deploy & sync for new nodes");
        DeployAndSync(onlyUndeployed: true);
    }

    private void Reject(CollabPeerConnection peer, string reason)
    {
        Log.Info($"Boygroup: rejected client: {reason}");
        SendJson(peer, new { t = "authFail", reason });
        peer.SessionClosed = true;
        peer.Close();
    }

    // ---------------------------------------------------------------------------------------
    // Client: handshake
    // ---------------------------------------------------------------------------------------

    private byte[]? _pendingSalt;
    private byte[]? _pendingNonce;

    private void HandleWelcome(JObject json)
    {
        if (_transport is not BoygroupClientTransport client)
            return;

        CollabProtocol.TryGetBool(json, "pwd", out var passwordRequired);
        if (!passwordRequired)
            return;

        if (!CollabAuth.TryFromHexString(CollabProtocol.GetString(json, "salt"), out var salt)
            || !CollabAuth.TryFromHexString(CollabProtocol.GetString(json, "nonce"), out var nonce))
        {
            ErrorText = "Server sent a malformed challenge";
            Leave();
            return;
        }

        _pendingSalt = salt;
        _pendingNonce = nonce;
        var key = CollabAuth.DeriveKey(_password, salt);
        var mac = CollabAuth.ComputeMac(key, nonce);
        SendJson(client.ServerPeer, new { t = "auth", mac = CollabAuth.ToHexString(mac) });
    }

    // ---------------------------------------------------------------------------------------
    // Deploy & Sync (server side)
    // ---------------------------------------------------------------------------------------

    /// <summary>Server action: push project files, the output setup, and live graph state to the group.</summary>
    public void DeployAndSync(bool onlyUndeployed = false)
    {
        if (State != BoygroupState.Server)
        {
            ErrorText = "Only the boygroup server can deploy";
            return;
        }

        var projectFolder = GetProjectFolder();
        if (string.IsNullOrEmpty(projectFolder))
        {
            ErrorText = "No project open — open a project before deploying";
            return;
        }

        var mirror = UserSettings.Config.BoygroupDeployMirror;
        // Remember what this pass is for: _autoDeployOnlyUndeployed leaked from an earlier
        // join-triggered pass and silently dropped manual deploys for already-deployed nodes.
        _autoDeployOnlyUndeployed = onlyUndeployed;
        var setupsFolderRelative = Path.GetRelativePath(projectFolder, SetupFiles.FolderIn(projectFolder));
        var inventory = BoygroupDeployer.BuildInventory(projectFolder, setupsFolderRelative);

        foreach (var client in _clients.Where(c => c.Peer.IsAuthenticated && c.Group == GroupName).ToList())
        {
            if (client.PendingFiles != null)
                continue; // A deploy is already in flight for this client.

            // The automatic pass serves only never-deployed nodes. A node that rewrites an
            // unchanged project file at launch (asset-path migration normalizes it) would
            // otherwise be re-sent, restart, rewrite, and loop forever; its updates ride the
            // explicit Deploy & Sync button instead.
            if (onlyUndeployed && client.IsDeployed)
                continue;

            client.Mirror = mirror;
            client.PendingInventory = inventory;
            // The client's own inventory arrives first (it reported it in its hello); request it via
            // deployStart and let the client answer with its inventory for diffing.
            client.StatusText = "requesting inventory";
            SendJson(client.Peer, new { t = "requestInventory" });
        }

        PushSetupToGroup();

        // No graph snapshot: the files below carry the structure, and SyncLiveValues keeps the node
        // on the master's live values. Rebuilding the node's composition here on every deploy was
        // what dropped its connections and made it restart in a loop.
        SendCurrentValuesToGroup();
    }

    /// <summary>A node joined without a project open: name ours so it can create the folder and
    /// receive the whole project (then restart into it).</summary>
    private void HandleNoProject(CollabPeerConnection peer)
    {
        var client = _clients.FirstOrDefault(c => ReferenceEquals(c.Peer, peer));
        if (client == null)
            return;

        var projectFolder = ProjectView.Focused?.OpenedProject.Package.Folder;
        if (string.IsNullOrEmpty(projectFolder))
            return; // No project open here either; nothing to serve.

        var name = Path.GetFileName(projectFolder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (_bootstrapSentTo.Add(peer.AssignedId))
            Log.Info($"Boygroup: {client.Name} joined without a project - sending '{name}'");

        client.StatusText = $"bootstrapping project '{name}'";
        SendJson(peer, new { t = "projectName", name });
    }

    private readonly HashSet<int> _bootstrapSentTo = [];

    private void HandleClientInventory(ClientInfo client, JObject json)
    {
        if (client.PendingInventory == null)
            return;

        var projectFolder = GetProjectFolder();
        if (string.IsNullOrEmpty(projectFolder))
            return;

        // A node showing another project must not receive this project's files on top of it
        // (two .csproj files in one folder break project loading). Re-bootstrap it instead.
        var masterName = Path.GetFileName(projectFolder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        var clientProject = CollabProtocol.GetString(json, "project", "");
        client.KnownProject = clientProject;
        if (clientProject.Length > 0 && !string.Equals(clientProject, masterName, StringComparison.OrdinalIgnoreCase))
        {
            client.PendingInventory = null;
            Log.Info($"Boygroup: {client.Name} shows project '{clientProject}' - switching it to '{masterName}'");
            SendJson(client.Peer, new { t = "projectName", name = masterName });
            return;
        }

        var serverInventory = client.PendingInventory;
        var clientInventory = BoygroupDeployer.ParseInventory(json);
        var missing = BoygroupDeployer.ComputeMissingFiles(serverInventory, clientInventory);
        var mirror = client.Mirror;

        if (mirror)
        {
            foreach (var extra in BoygroupDeployer.ComputeExtraFiles(serverInventory, clientInventory))
            {
                SendJson(client.Peer, new { t = "deleteFile", path = extra });
            }
        }

        client.PendingFiles = missing;
        client.FilesDone = 0;
        client.Sender = null;
        client.StatusText = missing.Count == 0 ? "up to date" : $"sending {missing.Count} file(s)...";
        client.IsDeployed = false;
        SendJson(client.Peer, new { t = "deployStart", count = missing.Count, mirror });

        if (missing.Count == 0)
        {
            FinishDeployToClient(client);
        }
    }

    /// <summary>
    /// Clients still loading their project stay silent when asked for their inventory (see
    /// <see cref="HandleServerInventory"/>), so a deploy request would otherwise dangle. Re-ask
    /// every few seconds until the client answers or the session ends.
    /// </summary>
    private bool _autoDeployOnlyUndeployed;

    private void ReRequestPendingInventories()
    {
        var now = Environment.TickCount64;
        foreach (var client in _clients)
        {
            if (!client.Peer.IsAuthenticated || client.PendingInventory == null)
                continue;

            if (_autoDeployOnlyUndeployed && client.IsDeployed)
            {
                client.PendingInventory = null;
                continue;
            }

            if (_inventoryRequestedAt.TryGetValue(client.Peer.AssignedId, out var requestedAt)
                && now - requestedAt < ReRequestInventoryIntervalMs)
                continue;

            _inventoryRequestedAt[client.Peer.AssignedId] = now;
            client.StatusText = "waiting for project to load...";
            SendJson(client.Peer, new { t = "requestInventory" });
        }
    }

    private const int ReRequestInventoryIntervalMs = 4000;
    private readonly Dictionary<int, long> _inventoryRequestedAt = [];

    /// <summary>
    /// The play broadcast must follow the master's playback however it is driven - timeline
    /// space bar, panel buttons or an external agent. Broadcast when the running state changes.
    /// </summary>
    private void BroadcastPlayStateOnChange()
    {
        var playing = (Playback.Current?.PlaybackSpeed ?? 0) > 0;
        if (playing == _lastBroadcastPlaying)
            return;

        _lastBroadcastPlaying = playing;
        BroadcastToGroup(new { t = "play", playing, bars = Playback.Current?.TimeInBars ?? 0 });
    }

    private bool _lastBroadcastPlaying;

    /// <summary>Runs one deploy step per client per frame; returns true when a frame budget is spent.</summary>
    private void PumpDeploy()
    {
        if (State != BoygroupState.Server)
            return;

        var projectFolder = GetProjectFolder();
        if (string.IsNullOrEmpty(projectFolder))
            return; // Nothing to deploy from; the deploy was already reported as failed on request.

        foreach (var client in _clients)
        {
            if (client.PendingFiles == null)
                continue;

            // A few chunk messages per client per frame keep transfers moving without hurting the
            // render frame budget.
            for (var step = 0; step < 24; step++)
            {
                if (client.Sender == null)
                {
                    if (client.FilesDone >= client.PendingFiles.Count)
                    {
                        FinishDeployToClient(client);
                        break;
                    }

                    var relativePath = client.PendingFiles[client.FilesDone];
                    try
                    {
                        client.Sender = new BoygroupDeployer.FileSender(projectFolder, relativePath);
                        SendJson(client.Peer, new { t = "fileBegin", path = relativePath });
                    }
                    catch (Exception e)
                    {
                        Log.Warning($"Boygroup: can't read {relativePath}: {e.Message}");
                        client.FilesDone++;
                        continue;
                    }
                }

                var message = client.Sender.NextMessage();
                if (message == null)
                    continue;

                SendJson(client.Peer, message);
                if (client.Sender.Completed)
                {
                    client.FilesDone++;
                    client.Sender.Dispose();
                    client.Sender = null;
                    client.StatusText = $"sending {client.FilesDone}/{client.PendingFiles.Count}";
                }
                else
                {
                    break; // Spend the rest of this frame's budget on the next client.
                }
            }
        }
    }

    private void FinishDeployToClient(ClientInfo client)
    {
        client.PendingFiles = null;
        client.PendingInventory = null;
        client.IsDeployed = true;
        client.StatusText = "deployed";
        // A deploy that transferred nothing (the node was already current) must not reboot it:
        // the auto-deploy on join runs against every join, up-to-date nodes stay up.
        var transferred = client.FilesDone > 0;
        SendJson(client.Peer, new { t = "deployDone", restart = transferred });
        Log.Info($"Boygroup: deployment to {client.Name} complete ({client.FilesDone} file(s){(transferred ? ", restart requested" : ", no changes")})");
    }

    private long _nextProjectCheckTicks;
    private const int ProjectCheckIntervalMs = 3000;

    /// <summary>
    /// Keeps the group on the master's project. A node that still shows another project (the master
    /// switched shows, or the node reopened its own) is asked for its inventory, and the mismatch
    /// branch in <see cref="HandleClientInventory"/> switches it to the shared project. Without this
    /// the master and its nodes can sit on different projects until someone presses Deploy & Sync.
    /// </summary>
    private void FollowSharedProject()
    {
        if (State != BoygroupState.Server || _clients.Count == 0)
            return;

        var now = Environment.TickCount64;
        if (now < _nextProjectCheckTicks)
            return;

        _nextProjectCheckTicks = now + ProjectCheckIntervalMs;

        var projectFolder = GetProjectFolder();
        if (string.IsNullOrEmpty(projectFolder))
            return;

        var masterName = Path.GetFileName(projectFolder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        foreach (var client in _clients)
        {
            if (!client.Peer.IsAuthenticated || client.PendingFiles != null || client.PendingInventory != null)
                continue;

            if (client.KnownProject.Length == 0
                || string.Equals(client.KnownProject, masterName, StringComparison.OrdinalIgnoreCase))
                continue;

            // Reuse the deploy diff: the inventory answer runs the same mismatch check.
            client.PendingInventory = BoygroupDeployer.BuildInventory(projectFolder,
                                                                      Path.GetRelativePath(projectFolder, SetupFiles.FolderIn(projectFolder)));
            client.Mirror = UserSettings.Config.BoygroupDeployMirror;
            client.StatusText = "checking project";
            SendJson(client.Peer, new { t = "requestInventory" });
        }
    }

    /// <summary>
    /// A boygroup server forwards setup edits to its render nodes as they are saved. A machine that
    /// isn't serving a group has no one to forward to, so this is a no-op there.
    /// </summary>
    public void OnSetupSaved()
    {
        if (State != BoygroupState.Server)
            return;

        PushSetupToGroup();
    }

    /// <summary>Pushes the active output setup file so clients render the master's output config.</summary>
    private void PushSetupToGroup()
    {
        var projectFolder = GetProjectFolder();
        if (string.IsNullOrEmpty(projectFolder))
            return;

        var setupsFolder = SetupFiles.FolderIn(projectFolder);
        if (!Directory.Exists(setupsFolder))
            return;

        foreach (var filePath in Directory.EnumerateFiles(setupsFolder, "*" + Setup.FileSuffix))
        {
            try
            {
                var json = File.ReadAllText(filePath);
                var name = Path.GetFileName(filePath);
                BroadcastToGroup(new { t = "setupJson", name, json });
            }
            catch (IOException e)
            {
                Log.Warning($"Boygroup: can't read setup {filePath}: {e.Message}");
            }
        }
    }

    /// <summary>
    /// Master side: sends only the input values that changed since the last pass. The project files
    /// (graph structure, operators, assets) are deployed once; everything after that is live show
    /// state. The old full graph snapshot deleted and rebuilt the node's whole composition on every
    /// deploy — which is what made nodes restart in a loop and crash while rendering.
    /// </summary>
    private void SyncLiveValues()
    {
        if (State != BoygroupState.Server || _clients.Count == 0)
            return;

        var now = Environment.TickCount64;
        if (now < _nextValueSyncTicks)
            return;

        _nextValueSyncTicks = now + ValueSyncIntervalMs;

        var compositionUi = ProjectView.Focused?.InstView?.Symbol.GetSymbolUi();
        if (compositionUi == null)
            return;

        // A different composition means a different set of keys; comparing against the old baseline
        // would suppress the first values of the new one.
        if (compositionUi.Symbol.Id != _valueSyncCompositionId)
        {
            _valueSyncCompositionId = compositionUi.Symbol.Id;
            _sentValues.Clear();
        }

        var ops = new List<JObject>();
        foreach (var childUi in compositionUi.ChildUis.Values)
        {
            var child = childUi.SymbolChild;
            foreach (var (inputId, input) in child.Inputs)
            {
                var token = CollabCommandWire.InputValueToToken(input.IsDefault ? input.DefaultValue : input.Value);
                if (token == null || token.Type == JTokenType.Null)
                    continue;

                var valueJson = token.ToString(Formatting.None);
                var key = $"{child.Id:N}/{inputId:N}";
                if (_sentValues.TryGetValue(key, out var previous) && previous == valueJson)
                    continue;

                _sentValues[key] = valueJson;
                ops.Add(new JObject
                            {
                                ["type"] = "set_input",
                                ["c"] = compositionUi.Symbol.Id,
                                ["ch"] = child.Id,
                                ["i"] = inputId,
                                ["v"] = token,
                            });
            }
        }

        BroadcastValueOps(ops);
    }

    /// <summary>
    /// Sends value ops in bounded batches. A big composition's first pass covers every input; one
    /// unbounded message would run into the session's JSON payload cap and kill the link.
    /// </summary>
    private void BroadcastValueOps(List<JObject> ops)
    {
        for (var offset = 0; offset < ops.Count; offset += MaxValueOpsPerMessage)
        {
            var batch = new JArray();
            for (var i = offset; i < Math.Min(offset + MaxValueOpsPerMessage, ops.Count); i++)
                batch.Add(ops[i]);

            BroadcastToGroup(new
                                 {
                                     t = "op",
                                     op = new JObject
                                              {
                                                  ["type"] = "batch",
                                                  ["ops"] = batch,
                                              },
                                 });
        }
    }

    private const int MaxValueOpsPerMessage = 400;

    /// <summary>
    /// Sends every current value of the focused composition, ignoring the change baseline. A node
    /// that just joined or received files starts from the values saved on disk; this puts it on the
    /// master's live values immediately instead of waiting for each one to change again.
    /// </summary>
    private void SendCurrentValuesToGroup()
    {
        var compositionUi = ProjectView.Focused?.InstView?.Symbol.GetSymbolUi();
        if (compositionUi == null)
            return;

        var ops = new List<JObject>();
        foreach (var childUi in compositionUi.ChildUis.Values)
        {
            var child = childUi.SymbolChild;
            foreach (var (inputId, input) in child.Inputs)
            {
                var token = CollabCommandWire.InputValueToToken(input.IsDefault ? input.DefaultValue : input.Value);
                if (token == null || token.Type == JTokenType.Null)
                    continue;

                _sentValues[$"{child.Id:N}/{inputId:N}"] = token.ToString(Formatting.None);
                ops.Add(new JObject
                            {
                                ["type"] = "set_input",
                                ["c"] = compositionUi.Symbol.Id,
                                ["ch"] = child.Id,
                                ["i"] = inputId,
                                ["v"] = token,
                            });
            }
        }

        // A full push covers every value, so the incremental baseline restarts from it.
        _valueSyncCompositionId = compositionUi.Symbol.Id;
        BroadcastValueOps(ops);
    }

    /// <summary>Values last broadcast per child/input, so only changes travel.</summary>
    private readonly Dictionary<string, string> _sentValues = [];

    private Guid _valueSyncCompositionId;
    private long _nextValueSyncTicks;
    private const int ValueSyncIntervalMs = 500;

    private static string? GetProjectFolder()
    {
        // A pending bootstrap (the master serves a different project) takes precedence: the files
        // must land in that folder, and the deploy restart opens it.
        if (Instance._bootstrapProjectFolder is { Length: > 0 } bootstrap)
            return bootstrap;

        return ProjectView.Focused?.OpenedProject.Package.Folder;
    }

    /// <summary>Whether a project is really open (not merely a pending bootstrap folder) — the debug
    /// bridge's status reports it, so a master that cannot deploy is diagnosable remotely.</summary>
    public static bool HasProjectFolder => ProjectView.Focused?.OpenedProject?.Package.Folder is { Length: > 0 };

    // ---------------------------------------------------------------------------------------
    // Client: deploy reception
    // ---------------------------------------------------------------------------------------

    private void HandleServerInventory(JObject json)
    {
        // Client answers the server's request with what it has on disk. The setups folder is
        // excluded on both sides: it travels as live setupJson, and mirror mode would otherwise
        // flag the client's applied setups as extra and delete them. While the project is still
        // loading there is nothing to answer with: staying silent makes the server re-request
        // instead of deploying the whole project into a folder that does not exist yet.
        var projectFolder = GetProjectFolder();
        if (string.IsNullOrEmpty(projectFolder))
        {
            // Nothing opened yet: tell the master, which answers with the project it deploys, and
            // this node creates that folder and receives the whole project (bootstrap).
            SendJson(((_transport as BoygroupClientTransport)!).ServerPeer, new { t = "noProject" });
            return;
        }

        var inventory = BoygroupDeployer.BuildInventory(projectFolder,
                                                        Path.GetRelativePath(projectFolder, SetupFiles.FolderIn(projectFolder)));
        SendInventoryToMaster(inventory);
    }

    /// <summary>Sends this machine's project inventory, answering the master's requestInventory.</summary>
    private void SendInventoryToMaster(Dictionary<string, BoygroupDeployer.InventoryEntry>? inventory = null)
    {
        var projectFolder = GetProjectFolder();
        if (string.IsNullOrEmpty(projectFolder) || _transport is not BoygroupClientTransport transport)
            return;

        inventory ??= BoygroupDeployer.BuildInventory(projectFolder,
                                                      Path.GetRelativePath(projectFolder, SetupFiles.FolderIn(projectFolder)));

        SendJson(transport.ServerPeer,
                 new JObject
                     {
                         ["t"] = "inventory",
                         ["project"] = Path.GetFileName(projectFolder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)),
                         ["files"] = BoygroupDeployer.BuildInventoryMessage(inventory)["files"],
                     });
    }

    /// <summary>Folder this node is bootstrap-receiving the master's project into (no project was
    /// open when it joined). Becomes the startup project so the deploy restart reopens it.</summary>
    private string? _bootstrapProjectFolder;

    private void HandleBootstrapProject(JObject json)
    {
        var name = CollabProtocol.GetString(json, "name", "");
        if (string.IsNullOrEmpty(name))
            return;

        // The master runs a project this machine already has: switch to it in place (a no-op when it
        // is already shown). Receiving a second copy into a bootstrap folder would restart the node
        // for a project that is right here — and the deploy then diffs against what it really shows.
        if (ProjectFollower.TrySwitchToName(name))
        {
            _bootstrapProjectFolder = null;
            DeploySummary = $"Following the master's project '{name}'";
            Log.Info($"Boygroup: following the master onto project '{name}' without a restart");

            // Answer the pending inventory request with the project we just opened, so the deploy
            // diffs against it instead of waiting for the next re-request.
            SendInventoryToMaster();
            return;
        }

        var root = UserSettings.Config.ProjectDirectories.FirstOrDefault();
        if (string.IsNullOrEmpty(root))
        {
            Log.Warning("Boygroup: no project directory configured - cannot receive the master's project");
            return;
        }

        try
        {
            var safeName = CollabManager.SafeProjectFolderName(name)
                           ?? throw new IOException($"the master offered a project with an unusable folder name '{name}'");
            var folder = Path.Combine(root, safeName);
            Directory.CreateDirectory(folder);
            if (!string.Equals(_bootstrapProjectFolder, folder, StringComparison.Ordinal))
            {
                Log.Info($"Boygroup: receiving the master's project into {folder}");
                _bootstrapProjectFolder = folder;

                // The received project shows up in the Hub's "Network projects" group.
                NetworkProjectRegistry.Register(NetworkProjectRegistry.BoygroupKind, folder);

                // Receiving a project is a one-time event per project, not a restart loop: the
                // flood guard from a previous project must not block opening this one.
                UserSettings.Config.BoygroupDeployRestartCount = 0;
                UserSettings.Config.BoygroupDeployRestartWindowStartTicks = 0;
                UserSettings.Config.BoygroupDeployRestartBlockedUntilTicks = 0;
            }
            SendJson(((_transport as BoygroupClientTransport)!).ServerPeer,
                     BoygroupDeployer.BuildInventoryMessage([]));
        }
        catch (Exception e)
        {
            Log.Warning($"Boygroup: cannot create the project folder for '{name}': {e.Message}");
        }
    }

    private void StartReceivingFile(JObject json)
    {
        var projectFolder = GetProjectFolder();
        if (string.IsNullOrEmpty(projectFolder))
        {
            ErrorText = "No project open — can't receive deployment";
            return;
        }

        try
        {
            _fileReceiver.Begin(projectFolder, json);
        }
        catch (Exception e)
        {
            ErrorText = $"Deploy: {e.Message}";
        }
    }

    private void FinishReceivingFile(JObject json)
    {
        var error = _fileReceiver.Finish(json);
        if (error == null && _fileReceiver.RelativePath.Length > 0)
            _deployedRelativePaths.Add(_fileReceiver.RelativePath);

        _deployFilesDone++;
        if (error != null)
        {
            _deployErrors++;
            Log.Warning($"Boygroup deploy: {error}");
        }

        DeploySummary = $"Receiving {_deployFilesDone}/{_deployFilesTotal} files" + (_deployErrors > 0 ? $" ({_deployErrors} failed)" : "");
    }

    private void DeleteClientFile(JObject json)
    {
        var projectFolder = GetProjectFolder();
        if (string.IsNullOrEmpty(projectFolder))
            return;

        try
        {
            var relativePath = json["path"]?.Value<string>() ?? "";
            var fullPath = BoygroupDeployer.SafeProjectPath(projectFolder, relativePath);
            if (File.Exists(fullPath))
            {
                File.SetAttributes(fullPath, FileAttributes.Normal);
                File.Delete(fullPath);
                Log.Debug($"Boygroup deploy: removed {relativePath}");
            }
        }
        catch (Exception e)
        {
            Log.Warning($"Boygroup deploy: can't delete {CollabProtocol.GetString(json, "path")}: {e.Message}");
        }
    }

    private void WriteSetupFile(JObject json)
    {
        var projectFolder = GetProjectFolder();
        if (string.IsNullOrEmpty(projectFolder))
            return;

        try
        {
            var name = json["name"]?.Value<string>() ?? "";
            if (CollabManager.SafeProjectFolderName(name) == null || !name.EndsWith(Setup.FileSuffix, StringComparison.Ordinal))
                throw new IOException($"unsafe setup file name '{name}'");

            var setupsFolder = SetupFiles.FolderIn(projectFolder);
            Directory.CreateDirectory(setupsFolder);
            var fullPath = Path.Combine(setupsFolder, name);
            File.WriteAllText(fullPath, json["json"]?.Value<string>() ?? "");
            Log.Info($"Boygroup: setup updated: {name}");
        }
        catch (Exception e)
        {
            Log.Warning($"Boygroup: can't write setup: {e.Message}");
        }
    }

    // ---------------------------------------------------------------------------------------
    // Remote displays: what the nodes present, offered as binding targets on the master
    // ---------------------------------------------------------------------------------------

    /// <summary>Master side: adopts a node's reported machine config and display list.</summary>
    private void HandleClientBindings(JObject json)
    {
        // The message carries the node's name (it also rides every hello); match on that.
        var name = CollabProtocol.GetString(json, "name", "");
        var client = _clients.FirstOrDefault(c => c.Peer.IsAuthenticated && c.Name == name);
        if (client == null)
            return;

        var configJson = CollabProtocol.GetString(json, "config");
        if (string.IsNullOrEmpty(configJson))
            return;

        try
        {
            client.MachineConfig = MachineConfig.ReadFromJson(JObject.Parse(configJson));
            client.Screens = ReadScreens(json["screens"]);
            Log.Info($"Boygroup: bindings from {client.Name}: {DescribeBindings(client.MachineConfig)}");
        }
        catch (JsonException e)
        {
            Log.Warning($"Boygroup: malformed bindings from {client.Name}: {e.Message}");
        }
    }

    private static List<RemoteScreen> ReadScreens(JToken? token)
    {
        var screens = new List<RemoteScreen>();
        if (token is not JArray array)
            return screens;

        foreach (var screen in array)
        {
            if (screen is not JObject screenObject)
                continue;

            var index = CollabProtocol.TryGetInt(screenObject, "index", out var parsedIndex) ? parsedIndex : screens.Count;
            CollabProtocol.TryGetBool(screenObject, "primary", out var primary);
            screens.Add(new RemoteScreen(index,
                                         CollabProtocol.GetString(screenObject, "name", $"Display {index + 1}"),
                                         CollabProtocol.TryGetInt(screenObject, "width", out var width) ? width : 0,
                                         CollabProtocol.TryGetInt(screenObject, "height", out var height) ? height : 0,
                                         CollabProtocol.TryGetInt(screenObject, "x", out var x) ? x : 0,
                                         CollabProtocol.TryGetInt(screenObject, "y", out var y) ? y : 0,
                                         primary));
        }

        return screens;
    }

    /// <summary>The master's list of a client's displays; the id is stable across reconnects and frames.</summary>
    public static Guid RemoteDisplayPlugId(ClientInfo client, int displayIndex)
    {
        var key = $"boygroup:{client.Address}:{client.Name}:{displayIndex}";
        var hash = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(key));
        return new Guid(hash.AsSpan(0, 16));
    }

    /// <summary>Resolves an outliner/board plug id back to the render node display it stands for.</summary>
    public bool TryGetRemoteDisplayPlug(Guid plugId, out ClientInfo client, out int displayIndex)
    {
        foreach (var candidate in _clients)
        {
            foreach (var screen in candidate.Screens)
            {
                if (RemoteDisplayPlugId(candidate, screen.Index) != plugId)
                    continue;

                client = candidate;
                displayIndex = screen.Index;
                return true;
            }
        }

        client = null!;
        displayIndex = -1;
        return false;
    }

    /// <summary>Master side: asks a render node to present an output on one of its own displays.</summary>
    public void BindRemoteOutput(ClientInfo client, Guid outputId, int displayIndex)
    {
        if (State != BoygroupState.Server || !client.Peer.IsAuthenticated)
            return;

        SendJson(client.Peer, new { t = "bindOutput", output = outputId.ToString(), display = displayIndex });
        StatusText = $"Binding {client.Name}'s Display {displayIndex + 1}...";
    }

    public void UnbindRemoteOutput(ClientInfo client, Guid outputId, int displayIndex)
    {
        if (State != BoygroupState.Server || !client.Peer.IsAuthenticated)
            return;

        SendJson(client.Peer, new { t = "unbindOutput", output = outputId.ToString(), display = displayIndex });
    }

    /// <summary>Output to plug pairs from one machine's config, for the log line.</summary>
    public static string DescribeBindings(MachineConfig? machineConfig)
    {
        if (machineConfig == null || machineConfig.Bindings.Count == 0)
            return "no display bindings";

        var parts = new List<string>();
        foreach (var binding in machineConfig.Bindings)
        {
            var plug = binding.IsStream ? "stream" : $"Display {binding.DisplayIndex + 1}";
            parts.Add($"output {binding.OutputId.ToString()[..8]} -> {plug}");
        }

        return string.Join(" - ", parts);
    }

    private long _nextBindingsSendTicks;
    private string _lastBindingsSent = "";

    /// <summary>Client side: sends this machine's machine config and displays when they changed.</summary>
    private void MaybeSendBindings()
    {
        if (State != BoygroupState.Client)
            return;

        var now = Environment.TickCount64;
        if (now < _nextBindingsSendTicks)
            return;

        _nextBindingsSendTicks = now + 2000;

        var projectFolder = GetProjectFolder();
        if (string.IsNullOrEmpty(projectFolder))
            return;

        var path = Path.Combine(SetupFiles.FolderIn(projectFolder), MachineConfig.FileName);
        try
        {
            // The master also needs this machine's displays before anything is bound, and a fresh
            // client may not have written a machine config yet.
            var json = File.Exists(path) ? File.ReadAllText(path) : new MachineConfig().ToJsonString();
            var screens = DescribeScreens();
            var key = json + "|" + screens.ToString(Formatting.None);
            if (key == _lastBindingsSent)
                return;

            _lastBindingsSent = key;
            Log.Debug($"Boygroup: reporting output bindings from '{path}'");
            if (_transport is BoygroupClientTransport clientTransport)
                SendJson(clientTransport.ServerPeer, new { t = "bindings", name = ClientDisplayName(), config = json, screens });
        }
        catch (IOException e)
        {
            Log.Debug($"Boygroup: can't read machine bindings: {e.Message}");
        }
    }

    /// <summary>This machine's displays, sent so a master can offer them as binding targets.</summary>
    private static JArray DescribeScreens()
    {
        var result = new JArray();
        var screens = EditorUi.Instance.AllScreens;
        for (var i = 0; i < screens.Count; i++)
        {
            result.Add(new JObject
                           {
                               ["index"] = i,
                               ["name"] = screens[i].DeviceName,
                               ["width"] = screens[i].Bounds.Width,
                               ["height"] = screens[i].Bounds.Height,
                               ["x"] = screens[i].Bounds.X,
                               ["y"] = screens[i].Bounds.Y,
                               ["primary"] = screens[i].Primary,
                           });
        }

        return result;
    }

    /// <summary>Client side: applies a binding the master routed to one of this machine's displays.</summary>
    private void HandleRemoteBinding(JObject json, bool bind)
    {
        if (!Guid.TryParse(CollabProtocol.GetString(json, "output"), out var outputId))
            return;

        var displayIndex = CollabProtocol.TryGetInt(json, "display", out var displayValue) ? displayValue : -1;
        if (displayIndex < 0 || displayIndex >= EditorUi.Instance.AllScreens.Count)
            return;

        if (!OutputSetupHandling.TryGetActiveSetup(out _, out var machineConfig))
            return;

        var plugId = Plugs.DisplayPlugId(displayIndex);
        if (bind)
            Plugs.BindOutput(machineConfig, outputId, plugId);
        else
            Plugs.UnbindOutput(machineConfig, outputId, plugId);

        // Report right away: the master's binding column is driven by what this machine says it presents.
        _lastBindingsSent = "";
        _nextBindingsSendTicks = 0;
        MaybeSendBindings();
    }

    private void HandleDeployDone(JObject json)
    {
        var shouldRestart = CollabProtocol.TryGetBool(json, "restart", out var restartFlag) ? restartFlag : true;
        DeploySummary = $"Deployment complete ({_deployFilesDone} files" + (_deployErrors > 0 ? $", {_deployErrors} failed" : "") + ")";

        // A deploy that transferred no files (the node was already current) leaves this machine up:
        // only a real update restarts into the deployed state.
        if (_deployFilesTotal == 0)
        {
            Log.Info("Boygroup: deploy had no file changes - staying up");
            return;
        }

        if (!shouldRestart || !UserSettings.Config.BoygroupAutoJoin)
            return;

        // Flood breaker, persisted across the restarts it guards: a file that perpetually diffs
        // (should be impossible now that setups are excluded) may never loop a show machine.
        var config = UserSettings.Config;
        var now = Environment.TickCount64;
        var isBootstrapRestart = _bootstrapProjectFolder is { Length: > 0 };
        if (!isBootstrapRestart && now < config.BoygroupDeployRestartBlockedUntilTicks)
        {
            Log.Warning($"Boygroup: deploy restart blocked for another {(config.BoygroupDeployRestartBlockedUntilTicks - now) / 1000}s (restart-loop guard) - staying up");
            return;
        }

        // A window of ten minutes, not two: restarts that follow each reboot by a few minutes (the
        // join -> deploy -> restart cycle) always fell outside the old window, so the counter reset
        // and the guard could never trip. Tripping it now really keeps the node up.
        if (config.BoygroupDeployRestartWindowStartTicks == 0 || now - config.BoygroupDeployRestartWindowStartTicks > DeployRestartWindowMs)
        {
            config.BoygroupDeployRestartWindowStartTicks = now;
            config.BoygroupDeployRestartCount = 1;
        }
        else
        {
            config.BoygroupDeployRestartCount++;
            if (config.BoygroupDeployRestartCount > MaxDeployRestartsPerWindow)
            {
                config.BoygroupDeployRestartBlockedUntilTicks = now + 300_000;
                config.BoygroupDeployRestartCount = 0;
                UserSettings.Save();
                Log.Warning($"Boygroup: more than {MaxDeployRestartsPerWindow} deploy restarts in ten minutes - blocking further deploy restarts for five minutes (restart-loop guard) - staying up");
                DeploySummary += " - restart blocked (loop guard)";
                return;
            }
        }

        // A render node that just received a bootstrapped project can open it in place - no restart
        // needed, because nothing was open that the new project would have to replace.
        if (_bootstrapProjectFolder is { Length: > 0 } bootstrapped
            && ProjectView.Focused?.OpenedProject == null
            && T3.Editor.App.Startup.StartupProjectLoader.TryOpen(bootstrapped))
        {
            Log.Info($"Boygroup: opened the received project {bootstrapped} without a restart");
            _bootstrapProjectFolder = null;
            DeploySummary += " - project opened";
            return;
        }

        // Deployed files the running editor can pick up live need no reload at all: assets are
        // invalidated by the resource watcher, and setups travel as live setupJson anyway. Only
        // symbol data (.t3/.t3ui) and operator code (.cs/.csproj) require a reload - and TiXL has
        // no runtime loader for those, so the node restarts into the updated show.
        // Everything this node received belongs to the deployed show project: it joins the
        // Hub's "Network projects" group.
        if (GetProjectFolder() is { Length: > 0 } receivedProject)
            NetworkProjectRegistry.Register(NetworkProjectRegistry.BoygroupKind, receivedProject);

        var needsReload = _deployedRelativePaths.Any(IsStructureFile);
        _deployedRelativePaths.Clear();
        if (!needsReload)
        {
            // Nothing the running editor can't hot-apply arrived — but the node may still be showing
            // a different project than the master. Follow it in place instead of restarting.
            if (GetProjectFolder() is { Length: > 0 } deployedFolder
                && !ProjectFollower.IsOpen(deployedFolder)
                && ProjectFollower.TrySwitchTo(deployedFolder))
            {
                DeploySummary += " - followed the master's project";
                return;
            }

            Log.Info($"Boygroup: deploy changed {_deployFilesDone} live-applicable file(s) - staying up without a restart");
            DeploySummary += " - applied live";
            return;
        }

        // A render node must come back into the show it was deployed, not into the Hub: pin the
        // deployed project for the restart. Both restart paths are covered — the reopen-last-session
        // loader (the default) and the startup-project one. Only for the slave role; a master's
        // editor restarts are the operator's business.
        if (State == BoygroupState.Client && config.BoygroupEnabled && !config.BoygroupIsMaster
            && GetProjectFolder() is { Length: > 0 } deployedProject)
        {
            ProjectFollower.PinForRestart(deployedProject);
        }
        else
        {
            UserSettings.Save();
        }

        RestartPending = true;
        // Brief delay so the panel shows what happened before the process exits.
        _restartAtTicks = Environment.TickCount64 + 1500;
    }

    /// <summary>Files that define operators or operator code - the editor cannot hot-apply them.</summary>
    private static bool IsStructureFile(string relativePath)
    {
        var extension = Path.GetExtension(relativePath);
        return extension is ".t3" or ".t3ui" or ".cs" or ".csproj";
    }

    private long _restartAtTicks;

    /// <summary>Restart-flood window and budget; see <see cref="HandleDeployDone"/>.</summary>
    private const long DeployRestartWindowMs = 10 * 60_000;
    private const int MaxDeployRestartsPerWindow = 4;

    private void ProcessPendingRestart()
    {
        if (!RestartPending || _restartAtTicks == 0 || Environment.TickCount64 < _restartAtTicks)
            return;

        _restartAtTicks = 0;
        Log.Info("Boygroup: restarting to apply deployed files");
        EditorRestart.TryRestart();
    }

    // ---------------------------------------------------------------------------------------
    // Playback control and time sync
    // ---------------------------------------------------------------------------------------

    public void SendPlayControl(bool play)
    {
        if (State != BoygroupState.Server)
            return;

        // An explicit play/pause pushes the next time sync immediately: the anchor carries the new
        // speed, so every node switches on the same beat instead of at each node's next 10 Hz tick.
        SendPlayControlBroadcast(play);
        SendTimeSyncNow();
    }

    private void SendPlayControlBroadcast(bool play)
    {
        BroadcastToGroup(new { t = "play", playing = play, bars = Playback.Current?.TimeInBars ?? 0 });
    }

    private void ApplyPlayControl(JObject json)
    {
        // The play state itself rides the time anchors (speed 0 vs 1); this only handles the case
        // where a node receives a play control without any anchor yet.
        var playing = json["playing"]?.Value<bool>() ?? false;
        if (Playback.Current != null && _lastSyncStopwatch < 0)
            Playback.Current.PlaybackSpeed = playing ? 1.0 : 0.0;
    }

    public void SendTimeSyncNow()
    {
        _nextTimeSyncTicks = 0; // Next Update() sends immediately.
    }

    private void SendHeartbeats()
    {
        var now = Environment.TickCount64;
        if (now < _nextHeartbeatTicks)
            return;

        _nextHeartbeatTicks = now + CollabProtocol.HeartbeatIntervalMs;

        if (_transport is BoygroupClientTransport clientTransport)
        {
            // The server pings every client every second, so silence means the master is gone
            // (or partitioned). Without this check the client kept dead-reckoning time forever
            // from the last sync and reported itself as connected.
            var serverPeer = clientTransport.ServerPeer;
            if (serverPeer.IsTimedOut)
            {
                Log.Info("Boygroup: server timed out");
                serverPeer.SessionClosed = true;
                serverPeer.Close();
            }

            return;
        }

        var timestamp = BitConverter.GetBytes(Environment.TickCount64);
        foreach (var client in _clients)
        {
            if (client.Peer.IsTimedOut)
            {
                Log.Info($"Boygroup: client {client.Name} timed out");
                client.Peer.SessionClosed = true;
                client.Peer.Close();
                continue;
            }

            client.Peer.Write(CollabProtocol.FrameType.Heartbeat, timestamp);
        }
    }

    private void SendTimeSync()
    {
        if (State != BoygroupState.Server)
            return;

        var now = Environment.TickCount64;
        if (now < _nextTimeSyncTicks)
            return;

        _nextTimeSyncTicks = now + 100; // 10 Hz
        var playback = Playback.Current;
        if (playback == null)
            return;

        // Remember where the master started, whether from this panel or its own transport keys,
        // so StopAll can reset to it when the timeline settings ask for that.
        if (Math.Abs(_lastMasterSpeed) < 1e-3 && Math.Abs(playback.PlaybackSpeed) > 1e-3)
            _playStartBars = playback.TimeInBars;
        _lastMasterSpeed = playback.PlaybackSpeed;

        // Play-at anchor: the bars the master will have at an instant slightly in the future, sent
        // with that instant on the master's UTC clock. Clients convert it with their measured clock
        // offset and switch when their own monotonic clock reaches it, so a late or jittery packet
        // does not shift the phase — the anchors line up on the timeline itself.
        var lead = TimeSpan.FromMilliseconds(AnchorLeadMs);
        var anchorBars = playback.TimeInBars + playback.PlaybackSpeed * lead.TotalSeconds * playback.Bpm / 240.0;
        BroadcastToGroup(new
                             {
                                 t = "time",
                                 bars = anchorBars,
                                 speed = playback.PlaybackSpeed,
                                 bpm = playback.Bpm,
                                 at = PreciseUtcNowTicks() + lead.Ticks,
                             });
    }

    /// <summary>How far ahead a play-at anchor is scheduled: enough for LAN jitter, shorter than the send cadence.</summary>
    private const int AnchorLeadMs = 80;

    // Clock offset (master UTC - local UTC), taken from the sample with the least round trip. A
    // render node follows the master's phase from its OWN monotonic clock between anchors, so the
    // timeline keeps advancing every frame with no per-packet jitter.
    private long _clockOffsetTicks;
    private long _clockBestRttTicks = long.MaxValue;
    private long _clockBestSampleUtc;
    private long _nextClockPingTicks;
    private long _clockPingId;
    private int _clockSamples;

    /// <summary>Whether playback anchors apply with the OS clock trusted (a PTP discipline daemon
    /// keeps it within microseconds of the show reference) or with TiXL's own NTP exchange.</summary>
    private static bool UsesOsClock => string.Equals(UserSettings.Config.BoygroupClockSync,
                                                     UserSettings.SyncPtp, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// UTC now, from the best clock this machine has. GetSystemTimePreciseAsFileTime is 100 ns
    /// resolution without the ~15 ms smoothing of DateTime.UtcNow; used for both the NTP stamps and
    /// (in PTP mode) the anchor instants.
    /// </summary>
    private static long PreciseUtcNowTicks()
    {
        if (OperatingSystem.IsWindows())
        {
            GetSystemTimePreciseAsFileTime(out var fileTime);
            return new DateTime(1601, 1, 1, 0, 0, 0, DateTimeKind.Utc).Ticks + fileTime;
        }

        return DateTime.UtcNow.Ticks; // Linux/macOS: DateTime.UtcNow already reads clock_gettime(CLOCK_REALTIME).
    }

    [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = false)]
    private static extern void GetSystemTimePreciseAsFileTime(out long fileTime);

    private void SendClockPings()
    {
        if (State != BoygroupState.Client)
            return;

        var now = Environment.TickCount64;
        if (now < _nextClockPingTicks)
            return;

        // A burst establishes the offset quickly; after that a slow refresh tracks clock drift.
        _nextClockPingTicks = now + (_clockSamples < 8 ? 1000 : 10_000);
        if (_transport is not BoygroupClientTransport clientTransport)
            return;

        // PTP mode trusts the OS clock: nothing to measure against the master.
        if (UsesOsClock)
            return;

        SendJson(clientTransport.ServerPeer,
                 new
                 {
                     t = "clockPing",
                     id = ++_clockPingId,
                     c1 = PreciseUtcNowTicks(),
                 });
    }

    private void HandleClockPong(JObject json)
    {
        if (!CollabProtocol.TryGetLong(json, "id", out var pongId) || pongId != _clockPingId)
            return;

        CollabProtocol.TryGetLong(json, "c1", out var c1);
        CollabProtocol.TryGetLong(json, "m2", out var m2);
        CollabProtocol.TryGetLong(json, "m3", out var m3);
        var c4 = PreciseUtcNowTicks();
        if (c1 == 0 || m2 == 0 || m3 == 0)
            return;

        var roundTrip = (c4 - c1) - (m3 - m2);
        if (roundTrip < 0)
            return;

        // NTP offset: the master's clock minus this machine's, at the middle of the round trip.
        var offset = ((m2 - c1) + (m3 - c4)) / 2;
        _clockSamples++;

        // The least-queueing sample is the most symmetric. A sample older than a minute is replaced
        // even when it is not better, so a clock that was adjusted does not pin the offset forever.
        if (roundTrip <= _clockBestRttTicks || c4 - _clockBestSampleUtc > TimeSpan.TicksPerMinute)
        {
            _clockOffsetTicks = offset;
            _clockBestRttTicks = roundTrip;
            _clockBestSampleUtc = c4;
        }
    }

    private void ResetClockSync()
    {
        _clockOffsetTicks = 0;
        _clockBestRttTicks = long.MaxValue;
        _clockBestSampleUtc = 0;
        _clockSamples = 0;
        _nextClockPingTicks = 0;
        _hasAnchor = false;
        _lastSyncStopwatch = -1;
        _syncErrorMs = -1;
    }

    private bool _hasAnchor;
    private double _anchorLocalStopwatch = -1;
    private double _anchorBars;
    private double _anchorSpeed;
    private double _anchorBpm = 120;

    /// <summary>Client side: converts the master's play-at instant into this machine's monotonic clock.</summary>
    private void ApplyTimeSync(JObject json)
    {
        var playback = Playback.Current;
        CollabProtocol.TryGetLong(json, "at", out var masterUtc);
        if (playback == null || masterUtc == 0)
            return;

        // PTP mode: the OS clock is the shared reference, no offset to subtract. Internal mode:
        // subtract the measured offset so the instant lands in this machine's clock domain.
        var localUtc = UsesOsClock ? masterUtc : masterUtc - _clockOffsetTicks;
        var deltaSecs = (localUtc - DateTime.UtcNow.Ticks) / (double)TimeSpan.TicksPerSecond;
        _anchorLocalStopwatch = Stopwatch.GetTimestamp() + deltaSecs * Stopwatch.Frequency;

        CollabProtocol.TryGetDouble(json, "bars", out _anchorBars);
        CollabProtocol.TryGetDouble(json, "speed", out _anchorSpeed);
        if (!CollabProtocol.TryGetDouble(json, "bpm", out _anchorBpm))
            _anchorBpm = 120;
        _hasAnchor = true;
    }

    /// <summary>Client side: switches to the anchor once its instant arrives, then dead-reckons from there.</summary>
    private void ApplyPendingAnchor()
    {
        if (!_hasAnchor || Stopwatch.GetTimestamp() < _anchorLocalStopwatch)
            return;

        var playback = Playback.Current;
        if (playback == null)
            return;

        // A late anchor still carries the right instant: advance its bars by the lateness rather
        // than snapping back to where the master was when it sent the packet.
        var nowTicks = Stopwatch.GetTimestamp();
        var lateSecs = Math.Max(0, (nowTicks - _anchorLocalStopwatch) / Stopwatch.Frequency);
        var targetBars = _anchorBars + _anchorSpeed * lateSecs * _anchorBpm / 240.0;

        // Self-check: compare the new anchor with where the previous trajectory says the timeline
        // should be at this same instant. A node following a wrong clock still looks busy, so the
        // measured correction is the only honest signal that it is actually in phase.
        var measuredCorrection = MeasureAnchorCorrection(nowTicks, targetBars);
        if (measuredCorrection > -0.5)
            _syncErrorMs = _syncErrorMs < -0.5 ? measuredCorrection : _syncErrorMs * 0.7 + measuredCorrection * 0.3;

        _lastSyncStopwatch = nowTicks;
        _lastSyncBars = targetBars;
        _lastSyncSpeed = _anchorSpeed;
        _lastSyncBpm = _anchorBpm;
        _lastAnchorTicks = Environment.TickCount64;

        playback.PlaybackSpeed = _anchorSpeed;
        playback.Bpm = _anchorBpm;
        ApplyIntegratedTime();

        _hasAnchor = false;
        TimeFollowSummary = _anchorSpeed != 0
                                ? $"Following master: {_lastSyncBars:0.00} bars @ {_anchorBpm:0} bpm"
                                : "Following master (paused)";
    }

    /// <summary>Milliseconds the timeline had to be corrected to match the new anchor; -1 when unknown.</summary>
    private double MeasureAnchorCorrection(double nowTicks, double targetBars)
    {
        if (_lastSyncStopwatch < 0 || _anchorBpm <= 0)
            return -1;

        if (Math.Abs(_lastSyncSpeed - _anchorSpeed) > 1e-9 || Math.Abs(_lastSyncBpm - _anchorBpm) > 1e-6)
            return -1; // Paused, resumed or changed tempo: the two trajectories are not comparable.

        var barsPerSec = Math.Abs(_anchorSpeed) * _anchorBpm / 240.0;

        // A paused node still has a phase; convert the bar offset at 1x so it reads in milliseconds too.
        var msPerBar = barsPerSec > 1e-9 ? 1000.0 / barsPerSec : 240.0 / _anchorBpm * 1000.0;
        var predictedBars = _lastSyncBars + _lastSyncSpeed * ((nowTicks - _lastSyncStopwatch) / Stopwatch.Frequency) * _lastSyncBpm / 240.0;
        return (targetBars - predictedBars) * msPerBar;
    }

    private double _lastSyncStopwatch = -1;
    private double _lastSyncBars;
    private double _lastSyncSpeed;
    private double _lastSyncBpm = 120;
    private double _syncErrorMs = -1;
    private long _lastAnchorTicks;

    /// <summary>Dead-reckons between anchors so the time cursor moves every frame, not in 10 Hz steps.</summary>
    private void ApplyIntegratedTime()
    {
        var playback = Playback.Current;
        if (playback == null || _lastSyncStopwatch < 0)
            return;

        // Stopwatch is monotonic: TimeOfDay would wrap at midnight and throw the time cursor away.
        var elapsedSecs = (Stopwatch.GetTimestamp() - _lastSyncStopwatch) / (double)Stopwatch.Frequency;
        playback.TimeInBars = _lastSyncBars + _lastSyncSpeed * elapsedSecs * _lastSyncBpm / 240.0;
    }

    private double _lastMasterSpeed;
    private double _playStartBars;
    // ---------------------------------------------------------------------------------------
    // Discovery
    // ---------------------------------------------------------------------------------------

    public void StartDiscovery()
    {
        _discoveryScanner ??= new LanDiscoveryScanner();
        _discoveredServers.Clear();

        // A master whose address is already known is also probed by unicast, so the scan still
        // finds it on networks that swallow broadcast packets.
        List<IPAddress>? directTargets = null;
        var lastServer = UserSettings.Config.BoygroupLastServer;
        if (!string.IsNullOrWhiteSpace(lastServer))
        {
            var resolved = ResolveHost(lastServer);
            if (resolved != null)
                directTargets = [resolved];
        }

        _discoveryScanner.Start(UserSettings.Config.BoygroupPort + LanDiscoveryResponder.DiscoveryPortOffset, directTargets, UserSettings.Config.BoygroupInterface);
    }

    public void StopDiscovery()
    {
        _discoveryScanner?.Stop();
        _discoveryScanner = null;
        _discoveredServers.Clear();
    }

    /// <summary>Moves scan results into the visible list; called from Update and the panel.</summary>
    public void DrainDiscovery()
    {
        if (_discoveryScanner == null)
            return;

        while (_discoveryScanner.Results.TryDequeue(out var result))
        {
            DiscoveredServer server;
            try
            {
                var announcement = result.Announcement;
                if (announcement["boygroup"] == null)
                    continue;

                server = new DiscoveredServer
                             {
                                 ServerName = CollabProtocol.GetString(announcement, "server", "boygroup server"),
                                 Address = result.Source.Address.ToString(),
                                 Port = announcement["port"]?.Value<int>() ?? UserSettings.Config.BoygroupPort,
                             };

                if (announcement["groups"] is JArray groups)
                {
                    foreach (var group in groups)
                    {
                        var name = group.Value<string>();
                        if (!string.IsNullOrEmpty(name))
                            server.Groups.Add(name);
                    }
                }
            }
            catch (Exception e)
            {
                // A malformed announcement (hostile or from another tool) must never reach the
                // frame loop: drop it and keep draining.
                Log.Debug($"Boygroup: dropping a malformed discovery announcement: {e.Message}");
                continue;
            }

            if (_discoveredServers.Any(d => d.Address == server.Address && d.Port == server.Port))
                continue;

            _discoveredServers.Add(server);
        }

        _discoveredServers.RemoveAll(d => d.Seen < DateTime.UtcNow - TimeSpan.FromSeconds(15));
    }

    private void RefreshDiscoveredServers() => DrainDiscovery();

    // ---------------------------------------------------------------------------------------
    // Shared plumbing
    // ---------------------------------------------------------------------------------------

    private void SendJson(CollabPeerConnection peer, object message) => _transport?.SendJson(peer, message);

    private void BroadcastToGroup(object message)
    {
        if (_transport == null)
            return;

        var payload = CollabProtocol.EncodeJson(message);
        foreach (var client in _clients)
        {
            if (client.Peer.IsAuthenticated && client.Group == GroupName)
                client.Peer.Write(CollabProtocol.FrameType.Json, payload);
        }
    }
}
