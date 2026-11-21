#nullable enable
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using T3.Editor.Collab;

namespace T3.Editor.Show;

/// <summary>
/// Transport for boygroup sessions, reusing the collab framing/auth primitives: a TCP server (the
/// show master) plus UDP discovery that announces the server's groups. Same threading contract as
/// the collab transports — socket threads only enqueue inbound messages; the manager consumes them
/// on the UI thread.
/// </summary>
internal abstract class BoygroupTransport : CollabTransport
{
}

/// <summary>The show master: accepts clients and announces groups via LAN discovery.</summary>
internal sealed class BoygroupServerTransport : BoygroupTransport
{
    /// <summary>Each accepted connection costs a frame buffer; a LAN peer must not be able to exhaust them.</summary>
    private const int MaxClients = 64;

    public override IReadOnlyList<CollabPeerConnection> Peers => _peerSnapshot;

    private readonly List<CollabPeerConnection> _peers = [];

    /** Copy of _peers republished on every change, so UI and network readers never see the mutable list. */
    private volatile CollabPeerConnection[] _peerSnapshot = [];

    private readonly TcpListener _listener;
    private readonly CancellationTokenSource _cancellation = new();
    private int _nextPeerId = 1;

    public LanDiscoveryResponder DiscoveryResponder { get; }

    public BoygroupServerTransport(int tcpPort, string[] groups, string? interfaceName = null)
    {
        // Pinned adapter (Settings → Network); empty keeps the all-interface bind.
        var bindAddress = T3.Core.Utils.LanInterfaces.ResolveAddress(interfaceName) ?? IPAddress.Any;
        _listener = new TcpListener(bindAddress, tcpPort);
        _listener.Start();
        try
        {
            DiscoveryResponder = new LanDiscoveryResponder(tcpPort,
                                                           () => new JObject
                                                                     {
                                                                         ["boygroup"] = true,
                                                                         ["server"] = Environment.MachineName,
                                                                         ["port"] = tcpPort,
                                                                         ["groups"] = new JArray(groups),
                                                                     },
                                                           bindAddress == IPAddress.Any ? null : bindAddress);
        }
        catch
        {
            // Release the listener, or a retry cannot bind the port again in this process.
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
        Log.Info($"Boygroup: server listening on port {tcpPort}");
    }

    public override void SendToPeer(CollabPeerConnection peer, CollabProtocol.FrameType type, ReadOnlySpan<byte> payload)
    {
        peer.Write(type, payload);
    }

    protected override void CleanupPeer(CollabPeerConnection peer)
    {
        lock (_peers)
        {
            _peers.Remove(peer);
            _peerSnapshot = _peers.ToArray();
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
            catch (SocketException)
            {
                await Task.Delay(500, CancellationToken.None);
                continue;
            }

            if (_peerSnapshot.Count(p => !p.SessionClosed) >= MaxClients)
            {
                Log.Warning("Boygroup: refused connection, client limit reached");
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
                // The client reset between accept and setup. One bad connection must not end
                // the accept loop and leave the show master unreachable.
                client.Close();
                continue;
            }

            lock (_peers)
            {
                _peers.Add(peer);
                _peerSnapshot = _peers.ToArray();
            }

            _ = Task.Run(() => ReadLoop(peer), CancellationToken.None);
        }
    }

    public override void Dispose()
    {
        _cancellation.Cancel();
        DiscoveryResponder?.Dispose();
        lock (_peers)
        {
            foreach (var peer in _peers)
                peer.Close();

            _peers.Clear();
            _peerSnapshot = [];
        }

        try
        {
            _listener.Stop();
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

/// <summary>A boygroup client's connection to the show master.</summary>
internal sealed class BoygroupClientTransport : BoygroupTransport
{
    public CollabPeerConnection ServerPeer => _serverPeer;

    private readonly CollabPeerConnection _serverPeer;
    private readonly TcpClient _client;
    private readonly CancellationTokenSource _cancellation = new();

    private BoygroupClientTransport(TcpClient client)
    {
        _client = client;
        // TcpClient overload: bounds UI-thread writes with a send timeout, like the collab client.
        _serverPeer = new CollabPeerConnection(client, 0);
    }

    public static BoygroupClientTransport Connect(IPEndPoint endPoint)
    {
        var client = CollabProtocol.ConnectWithTimeout(endPoint);
        CollabProtocol.ConfigureShowSocket(client);
        return new BoygroupClientTransport(client);
    }

    /// <summary>
    /// Starts the reader. Call it after subscribing to <see cref="CollabTransport.PeerDisconnected"/>,
    /// or a link that dies during the connect hand-off reports nothing and the session hangs.
    /// </summary>
    public void StartReading() => _ = Task.Run(() => ReadLoop(_serverPeer), CancellationToken.None);

    public override IReadOnlyList<CollabPeerConnection> Peers => [_serverPeer];

    public override void SendToPeer(CollabPeerConnection peer, CollabProtocol.FrameType type, ReadOnlySpan<byte> payload)
    {
        peer.Write(type, payload);
    }

    public override void Dispose()
    {
        _cancellation.Cancel();
        _serverPeer.Close();
        try
        {
            _client.Close();
        }
        catch
        {
            // Socket already dead.
        }
    }

    protected override void EchoHeartbeat(CollabPeerConnection peer, ReadOnlySpan<byte> timestamp)
    {
        peer.Write(CollabProtocol.FrameType.Heartbeat, timestamp);
    }
}
