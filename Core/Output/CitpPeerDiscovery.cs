#nullable enable
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using T3.Core.Logging;

namespace T3.Core.Output;

/// <summary>
/// Listens for CITP PLoc announcements on 224.0.0.180:4809 and records the peers that report
/// themselves (Capture, Depence, media servers) - the dropdown under a CITP plug's address field.
/// One shared listener feeds every CITP stream: streams subscribe to <see cref="PeerAnnounced"/>
/// instead of running their own sockets.
/// </summary>
public static class CitpPeerDiscovery
{
    public sealed record Peer(string Name, string Type, string Ip, int TcpPort);

    private const uint CitpCookie = 0x43495450; // "CITP"
    private const uint PinFCookie = 0x50494E46; // "PINF"
    private const uint PLocCookie = 0x504C6F63; // "PLoc"

    private static readonly ConcurrentDictionary<string, Peer> PeersByKey = new();
    private static readonly ConcurrentDictionary<string, long> LastSeenByKey = new();
    private static UdpClient? _listener;
    private static int _started;

    /// <summary>Peers stop announcing when they leave; entries older than this are dropped, so a
    /// session that changed address does not linger in the dropdown (and a direct-connect target
    /// is never picked from a peer that is no longer there).</summary>
    private const int PeerLifetimeMs = 30_000;

    private const int PruneIntervalMs = 2000;

    /// <summary>Raised for every PLoc announcement seen - subscribers (CITP streams) decide
    /// whether the peer is interesting; the registry keeps everything for the dropdown.</summary>
    public static event Action<Peer>? PeerAnnounced;

    public static IReadOnlyCollection<Peer> Peers => PeersByKey.Values.OrderBy(p => p.Name).ToArray();

    public static void ForgetPeers()
    {
        PeersByKey.Clear();
        LastSeenByKey.Clear();
    }

    /// <summary>Starts the shared listener once; safe to call from anywhere, any number of times.</summary>
    public static void EnsureStarted()
    {
        if (Interlocked.Exchange(ref _started, 1) == 1)
            return;

        try
        {
            _listener = new UdpClient(AddressFamily.InterNetwork);
            _listener.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            _listener.Client.Bind(new IPEndPoint(IPAddress.Any, 4809));
            try
            {
                _listener.JoinMulticastGroup(IPAddress.Parse("224.0.0.180"));
            }
            catch (SocketException)
            {
                _listener.JoinMulticastGroup(IPAddress.Parse("224.0.0.180"), IPAddress.Any);
            }

            _listener.Client.ReceiveTimeout = 2000;
            var thread = new Thread(ReceiveLoop)
                             {
                                 IsBackground = true,
                                 Name = "CITP_Peer_Discovery",
                             };
            thread.Start();
        }
        catch (Exception e)
        {
            Log.Debug($"CITP peer discovery not started ({e.Message})");
            _listener?.Dispose();
            _listener = null;
            _started = 0;
        }
    }

    private static void ReceiveLoop()
    {
        var listener = _listener!;
        var remoteEndPoint = new IPEndPoint(IPAddress.Any, 0);
        var lastPrune = Environment.TickCount64;
        while (true)
        {
            try
            {
                // Steady traffic keeps the receive from ever timing out, so the sweep cannot hang off the timeout alone.
                if (Environment.TickCount64 - lastPrune > PruneIntervalMs)
                {
                    lastPrune = Environment.TickCount64;
                    PruneStalePeers();
                }

                var bytes = listener.Receive(ref remoteEndPoint);
                if (bytes.Length < 16 || ReadBigEndianUint(bytes, 0) != CitpCookie)
                    continue;

                var remoteIp = remoteEndPoint.Address;
                if (remoteIp.IsIPv4MappedToIPv6)
                    remoteIp = remoteIp.MapToIPv4();

                if (!TryParsePloc(bytes, out var name, out var type, out var port))
                    continue;

                var peer = new Peer(name, type, remoteIp.ToString(), port);
                var key = $"{peer.Ip}:{peer.TcpPort}";
                PeersByKey[key] = peer;
                LastSeenByKey[key] = Environment.TickCount64;
                RaisePeerAnnounced(peer);
            }
            catch (SocketException ex) when (ex.SocketErrorCode == SocketError.TimedOut)
            {
                // loop - the receive timeout just bounds the shutdown check; it is also the
                // heartbeat of the stale-peer sweep.
                PruneStalePeers();
            }
            catch (ObjectDisposedException)
            {
                return;
            }
            catch
            {
                Thread.Sleep(100);
            }
        }
    }

    /// <summary>Drops peers whose last announcement is older than <see cref="PeerLifetimeMs"/> —
    /// a Capture session that shut down or changed address disappears from the dropdown instead
    /// of accumulating until restart.</summary>
    private static void PruneStalePeers()
    {
        var now = Environment.TickCount64;
        foreach (var pair in PeersByKey)
        {
            if (LastSeenByKey.TryGetValue(pair.Key, out var seen) && now - seen <= PeerLifetimeMs)
                continue;

            if (PeersByKey.TryRemove(pair.Key, out _))
                LastSeenByKey.TryRemove(pair.Key, out _);
        }
    }

    /// <summary>Notifies every subscriber independently - one throwing subscriber (a stream
    /// mid-teardown) must not silence the announcement for the rest.</summary>
    private static void RaisePeerAnnounced(Peer peer)
    {
        var handlers = PeerAnnounced?.GetInvocationList();
        if (handlers == null)
            return;

        foreach (var handler in handlers)
        {
            try
            {
                ((Action<Peer>)handler)(peer);
            }
            catch (Exception e)
            {
                Log.Debug($"CITP peer discovery subscriber failed: {e.Message}");
            }
        }
    }

    /// <summary>Parses a PLoc layer: u16 TCP port, then two UCS1 strings (type, name).</summary>
    public static bool TryParsePloc(byte[] bytes, out string name, out string type, out int tcpPort)
    {
        name = type = string.Empty;
        tcpPort = 0;

        var pLocOffset = -1;
        for (var i = 12; i <= Math.Min(bytes.Length - 4, 24); i += 4)
        {
            if (ReadBigEndianUint(bytes, i) != PLocCookie)
                continue;

            pLocOffset = i;
            break;
        }

        // A PINF layer may wrap the PLoc: scan forward from it as well.
        for (var i = 12; pLocOffset == -1 && i <= bytes.Length - 8; i++)
        {
            if (ReadBigEndianUint(bytes, i) == PLocCookie)
                pLocOffset = i;
        }

        if (pLocOffset == -1 || pLocOffset + 5 >= bytes.Length)
            return false;

        // Little-endian per CITP spec
        tcpPort = bytes[pLocOffset + 4] | (bytes[pLocOffset + 5] << 8);
        if (tcpPort == 0)
            return false;

        var offset = pLocOffset + 6;
        type = ReadUcs1String(bytes, ref offset);
        name = ReadUcs1String(bytes, ref offset);
        return true;
    }

    private static string ReadUcs1String(byte[] bytes, ref int offset)
    {
        var start = offset;
        while (offset < bytes.Length && bytes[offset] != 0)
            offset++;

        var text = offset > start ? Encoding.ASCII.GetString(bytes, start, offset - start) : string.Empty;
        if (offset < bytes.Length)
            offset++;
        return text;
    }

    private static uint ReadBigEndianUint(byte[] buffer, int offset)
    {
        return ((uint)buffer[offset] << 24)
               | ((uint)buffer[offset + 1] << 16)
               | ((uint)buffer[offset + 2] << 8)
               | buffer[offset + 3];
    }
}
