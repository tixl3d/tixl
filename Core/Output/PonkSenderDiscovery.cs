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
/// Passively listens on the PONK multicast group (239.255.10.24:5583) and records the senders
/// seen there (name + id from packet headers, source IP) - the dropdown under a PONK plug's
/// address field. PONK has no receiver announcement: a receive-only MadMapper cannot be
/// discovered, only peers that are themselves sending.
/// </summary>
public static class PonkSenderDiscovery
{
    public sealed record Sender(string Name, int SenderId, string Ip);

    private const string HeaderMagic = "PONK-UDP";
    private const int SenderNameOffset = 13;
    private const int SenderNameBytes = 32;

    private static readonly ConcurrentDictionary<string, Sender> SendersByIp = new();
    private static readonly ConcurrentDictionary<string, long> LastSeenByIp = new();
    private static UdpClient? _listener;
    private static int _started;

    /// <summary>Senders that go silent drop out of the dropdown instead of accumulating.</summary>
    private const int SenderLifetimeMs = 30_000;

    private const int PruneIntervalMs = 2000;

    public static IReadOnlyCollection<Sender> Senders => SendersByIp.Values.OrderBy(s => s.Name).ToArray();

    public static void ForgetSenders()
    {
        SendersByIp.Clear();
        LastSeenByIp.Clear();
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
            _listener.Client.Bind(new IPEndPoint(IPAddress.Any, 5583));
            try
            {
                _listener.JoinMulticastGroup(IPAddress.Parse("239.255.10.24"));
            }
            catch (SocketException)
            {
                _listener.JoinMulticastGroup(IPAddress.Parse("239.255.10.24"), IPAddress.Any);
            }

            _listener.Client.ReceiveTimeout = 2000;
            var thread = new Thread(ReceiveLoop)
                             {
                                 IsBackground = true,
                                 Name = "PONK_Sender_Discovery",
                             };
            thread.Start();
        }
        catch (Exception e)
        {
            Log.Debug($"PONK sender discovery not started ({e.Message})");
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
                    PruneStaleSenders();
                }

                var bytes = listener.Receive(ref remoteEndPoint);
                if (bytes.Length < SenderNameOffset + SenderNameBytes)
                    continue;

                if (Encoding.ASCII.GetString(bytes, 0, 8) != HeaderMagic)
                    continue;

                var senderId = bytes[9]
                               | (bytes[10] << 8)
                               | (bytes[11] << 16)
                               | (bytes[12] << 24);
                var name = Encoding.ASCII.GetString(bytes, SenderNameOffset, SenderNameBytes);
                var terminator = name.IndexOf('\0');
                if (terminator >= 0)
                    name = name[..terminator];

                var remoteIp = remoteEndPoint.Address;
                if (remoteIp.IsIPv4MappedToIPv6)
                    remoteIp = remoteIp.MapToIPv4();

                var key = remoteIp.ToString();
                SendersByIp[key] = new Sender(name, senderId, key);
                LastSeenByIp[key] = Environment.TickCount64;
            }
            catch (SocketException ex) when (ex.SocketErrorCode == SocketError.TimedOut)
            {
                // loop - the receive timeout just bounds the shutdown check; it is also the
                // heartbeat of the stale-sender sweep.
                PruneStaleSenders();
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

    private static void PruneStaleSenders()
    {
        var now = Environment.TickCount64;
        foreach (var pair in SendersByIp)
        {
            if (LastSeenByIp.TryGetValue(pair.Key, out var seen) && now - seen <= SenderLifetimeMs)
                continue;

            if (SendersByIp.TryRemove(pair.Key, out _))
                LastSeenByIp.TryRemove(pair.Key, out _);
        }
    }
}
