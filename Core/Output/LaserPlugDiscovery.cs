#nullable enable
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using LaserCore.EtherDream.Net.Discovery;
using T3.Core.Logging;

namespace T3.Core.Output;

/// <summary>
/// Device discovery for laser plugs: Ether Dream DACs announce themselves via UDP broadcast, and
/// <see cref="DeviceDiscovery"/> collects them in a shared static table (one listener owns the port;
/// further instances fail softly). The Output Setup's plug card starts the listener when an
/// EtherDream plug is being edited and lists what it found, so binding at a venue is a dropdown
/// instead of a typed IP. Also enumerates this machine's IPv4 interfaces for the plug's interface
/// field. Both lists are cached — the UI reads them every frame, and their queries (LINQ over the
/// shared table, GetAllNetworkInterfaces) range from allocating to outright blocking.
/// </summary>
public static class LaserPlugDiscovery
{
    /// <summary>
    /// Starts the shared broadcast listener if it isn't already running (idempotent, never throws).
    /// DeviceDiscovery only receives inside its own query calls, so a background loop drains the
    /// socket — without it, broadcasts pile up past the kernel buffer and drop, and the shared
    /// table stays empty forever.
    /// </summary>
    public static void EnsureEtherDreamDiscovery()
    {
        DeviceDiscovery discovery;
        lock (_discoveryLock)
        {
            if (_discovery != null)
                return;

            // A failed start (port taken for a moment, hot reload) is retried later instead of
            // leaving the picker empty for the whole session.
            var now = Environment.TickCount64;
            if (now < _nextStartAttempt)
                return;

            _nextStartAttempt = now + StartRetryIntervalMs;
            try
            {
                _discovery = discovery = new DeviceDiscovery();
            }
            catch (Exception e)
            {
                // Address-in-use when another listener in the process already owns the port - its
                // shared DiscoveredDevices table still fills, so this is fine.
                Log.Debug($"Laser plug discovery not started ({e.Message}) - reusing any running discovery.");
                return;
            }
        }

        new Thread(() => DrainDiscovery(discovery))
            {
                IsBackground = true,
                Name = "EtherDream_Discovery",
            }
            .Start();
    }

    private static void DrainDiscovery(DeviceDiscovery discovery)
    {
        while (true)
        {
            try
            {
                discovery.GetAvailableDevices();
            }
            catch
            {
                // Disposed (editor shutdown) — nothing left to drain
                return;
            }

            Thread.Sleep(200);
        }
    }

    private const int StartRetryIntervalMs = 5000;
    private static readonly object _discoveryLock = new();
    private static DeviceDiscovery? _discovery;
    private static long _nextStartAttempt;

    /// <summary>Currently announced Ether Dream DACs as "(name, ip)" pairs, sorted by ip. Cached for a second.</summary>
    public static IReadOnlyList<(string Name, string Ip)> EtherDreamDacs
    {
        get
        {
            var now = Environment.TickCount64;
            if (_cachedDacs != null && now - _dacsTimestamp < 1000)
                return _cachedDacs;

            _dacsTimestamp = now;
            try
            {
                var devices = DeviceDiscovery.DiscoveredDevices;
                if (devices == null || devices.Count == 0)
                    return _cachedDacs = Array.Empty<(string, string)>();

                _cachedDacs = devices.Values
                                     .Select(d => (DeviceDiscovery.GetDeviceName(d), d.Ip))
                                     .OrderBy(pair => pair.Item2, StringComparer.OrdinalIgnoreCase)
                                     .ToArray();
            }
            catch
            {
                _cachedDacs = Array.Empty<(string, string)>();
            }

            return _cachedDacs;
        }
    }

    private static IReadOnlyList<(string Name, string Ip)>? _cachedDacs;
    private static long _dacsTimestamp = long.MinValue;

    /// <summary>
    /// The device an output op is currently streaming to, reported by the op: a connected DAC
    /// stops its discovery broadcasts, so without this the plug's picker would offer nothing
    /// exactly while a show is running. Set/cleared from the ops; read on the UI thread.
    /// </summary>
    public static (string Name, string Ip)? ConnectedDevice
    {
        get
        {
            var device = Volatile.Read(ref _connectedDevice);
            return device == null ? null : (device.Name, device.Ip);
        }
    }

    /// <summary>Published as one object: the name and address are written by stream threads and
    /// read on the UI thread, and a tuple of two references is not written atomically.</summary>
    private sealed record ConnectedDeviceInfo(string Name, string Ip);

    private static ConnectedDeviceInfo? _connectedDevice;

    public static void SetConnectedDevice(string name, string ip)
    {
        Volatile.Write(ref _connectedDevice, new ConnectedDeviceInfo(name, ip));
        _dacsTimestamp = long.MinValue; // the picker re-lists at once
    }

    public static void ClearConnectedDevice(string ip)
    {
        var device = Volatile.Read(ref _connectedDevice);
        if (device != null && device.Ip == ip
            && ReferenceEquals(Interlocked.CompareExchange(ref _connectedDevice, null, device), device))
        {
            _dacsTimestamp = long.MinValue;
        }
    }

    /// <summary>
    /// This machine's IPv4 addresses with their interface names ("ethernet: 10.0.0.69"). The enumeration
    /// queries the network stack and can block for tens to hundreds of milliseconds, so it never runs
    /// on the caller's thread: the first list is built right here (a few milliseconds) so a picker
    /// opens populated, later refreshes run in the background every ten seconds.
    /// </summary>
    public static IReadOnlyList<(string Name, string Ip)> InterfaceAddresses
    {
        get
        {
            var now = Environment.TickCount64;
            if (now - _interfacesTimestamp >= 10000 && Interlocked.Exchange(ref _interfaceRefreshRunning, 1) == 0)
            {
                _interfacesTimestamp = now;
                if (Volatile.Read(ref _everRefreshed) == 0)
                    RefreshInterfaceAddresses();
                else
                    _ = Task.Run(RefreshInterfaceAddresses);
            }

            return _cachedInterfaces;
        }
    }

    private static void RefreshInterfaceAddresses()
    {
        try
        {
            var result = new List<(string, string)>(BasicInterfaces);
            NetworkInterface[] nics;
            try
            {
                nics = NetworkInterface.GetAllNetworkInterfaces();
            }
            catch (Exception e)
            {
                // The whole enumeration can fail (a NIC vanishing mid-call, WSL/Hyper-V quirks);
                // keeping the basics is better than an empty picker, and the retry in 10 s may work.
                Log.Debug($"Laser plug discovery: interface enumeration failed - {e.Message}");
                return;
            }

            foreach (var nic in nics)
            {
                try
                {
                    if (nic.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                        continue;

                    // Windows reports Unknown instead of Up for many working adapters (drivers
                    // that don't provide media-connect state, virtual Hyper-V/WSL NICs) - only
                    // Down/LowerLayerDown are really out.
                    if (nic.OperationalStatus is OperationalStatus.Down or OperationalStatus.LowerLayerDown)
                        continue;

                    foreach (var address in nic.GetIPProperties().UnicastAddresses)
                    {
                        if (address.Address.AddressFamily != AddressFamily.InterNetwork)
                            continue;

                        result.Add((nic.Name, address.Address.ToString()));
                    }
                }
                catch
                {
                    // A single NIC failing mid-read must not hide all the others
                }
            }

            if (result.Count > BasicInterfaces.Length)
                _cachedInterfaces = result;
        }
        finally
        {
            Volatile.Write(ref _everRefreshed, 1);
            Volatile.Write(ref _interfaceRefreshRunning, 0);
        }
    }

    /** Always offered: bind-all and loopback (local simulators/receivers). */
    private static readonly (string Name, string Ip)[] BasicInterfaces = [("any interface", "0.0.0.0"), ("localhost", "127.0.0.1")];

    private static int _interfaceRefreshRunning;
    private static int _everRefreshed;
    private static volatile IReadOnlyList<(string Name, string Ip)> _cachedInterfaces = BasicInterfaces;
    // Half of long.MinValue: the delta `now - this` must not overflow (TickCount64 - long.MinValue
    // wraps negative), or the refresh never fires and the picker keeps the two basic entries forever.
    private static long _interfacesTimestamp = long.MinValue / 2;

    #region ShowNet discovery
    /// <summary>
    /// Starts the shared showNET probe listener if it isn't already running (idempotent, never
    /// throws). The device answers the 30-byte broadcast probe on UDP 41856 — a port only one
    /// process can own, so a failed start (another TiXL instance or the vendor AdminTool is
    /// listening) is retried later instead of leaving the picker empty for the whole session.
    /// </summary>
    public static void EnsureShowNetDiscovery()
    {
        ShowNetListener listener;
        lock (_showNetLock)
        {
            if (_showNetListener != null)
                return;

            var now = Environment.TickCount64;
            if (now < _showNetNextStartAttempt)
                return;

            _showNetNextStartAttempt = now + StartRetryIntervalMs;
            try
            {
                listener = new ShowNetListener();
            }
            catch (Exception e)
            {
                Log.Debug($"showNET plug discovery not started ({e.Message}) - retrying in {StartRetryIntervalMs / 1000} s");
                return;
            }

            _showNetListener = listener;
        }

        new Thread(() => DrainShowNet(listener))
            {
                IsBackground = true,
                Name = "showNET_Discovery",
            }
            .Start();
    }

    private static void DrainShowNet(ShowNetListener listener)
    {
        while (true)
        {
            try
            {
                listener.Poll();
            }
            catch (SocketException)
            {
                // One stray ICMP-driven reset must not end discovery for the session. Fall
                // through to the sleep below: a persistent failure (network down, repeated
                // WSAECONNRESET) must not turn this loop into a hot spin.
            }
            catch
            {
                // Disposed (editor shutdown) — nothing left to drain
                return;
            }

            Thread.Sleep(200);
        }
    }

    /// <summary>Currently announced showNET lasers as "(name, ip)" pairs, sorted by ip. Cached for a second.</summary>
    public static IReadOnlyList<(string Name, string Ip)> ShowNetLasers
    {
        get
        {
            var now = Environment.TickCount64;
            if (_showNetCachedLasers != null && now - _showNetLasersTimestamp < 1000)
                return _showNetCachedLasers;

            _showNetLasersTimestamp = now;
            var listener = _showNetListener;
            if (listener == null)
                return _showNetCachedLasers = Array.Empty<(string, string)>();

            try
            {
                return _showNetCachedLasers = listener.Snapshot();
            }
            catch
            {
                return _showNetCachedLasers = Array.Empty<(string, string)>();
            }
        }
    }

    private static readonly object _showNetLock = new();
    private static ShowNetListener? _showNetListener;
    private static long _showNetNextStartAttempt;
    private static IReadOnlyList<(string Name, string Ip)>? _showNetCachedLasers;
    private static long _showNetLasersTimestamp = long.MinValue / 2;

    /// <summary>
    /// Owns the UDP 41856 probe socket: broadcasts the 30-byte probe to 255.255.255.255:41855 from
    /// that port, and collects the plaintext replies (device identity XOR 0xD49A3433).
    /// </summary>
    private sealed class ShowNetListener : IDisposable
    {
        public ShowNetListener()
        {
            _socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp)
                          {
                              EnableBroadcast = true,
                          };
            _socket.Bind(new IPEndPoint(IPAddress.Any, ShowNetProtocol.DiscoveryPort));
        }

        private const int ProbeIntervalMs = 4000;
        private const int PruneAfterMs = 12000;

        private readonly Socket _socket;
        private readonly ConcurrentDictionary<string, ShowNetLaserInfo> _lasers = new(StringComparer.OrdinalIgnoreCase);
        private long _lastProbeTicks;

        /// <summary>The listener thread's poll: probe when due, drain replies, prune stale entries.</summary>
        public void Poll()
        {
            var now = Environment.TickCount64;
            if (now - _lastProbeTicks >= ProbeIntervalMs)
            {
                _lastProbeTicks = now;
                BroadcastProbe();
            }

            DrainReplies();
            Prune(now);
        }

        private void BroadcastProbe()
        {
            var probe = ShowNetProtocol.DiscoveryProbe;
            var broadcast = new IPEndPoint(IPAddress.Broadcast, ShowNetProtocol.LaserPort);
            try
            {
                _socket.SendTo(probe, broadcast);
            }
            catch (SocketException)
            {
                // Network unreachable mid-poll - the next poll retries.
            }
        }

        private void DrainReplies()
        {
            // One buffer for the drain: the single listener thread parses each datagram before
            // receiving the next.
            var buffer = new byte[1600];
            while (_socket.Available > 0)
            {
                EndPoint remote = new IPEndPoint(IPAddress.Any, 0);
                var length = _socket.ReceiveFrom(buffer, ref remote);
                if (length < 104)
                    continue;

                var address = ((IPEndPoint)remote).Address;
                var info = ShowNetProtocol.TryParseDiscoveryReply(address, buffer, length);
                if (info != null)
                {
                    info.Timestamp = Environment.TickCount64;
                    _lasers[address.ToString()] = info;
                }
            }
        }

        /// <summary>The picker's view: (display name, ip) pairs, sorted by ip.</summary>
        public List<(string Name, string Ip)> Snapshot()
        {
            return _lasers.Values
                          .Select(laser => (laser.DisplayName, laser.Address.ToString()))
                          .OrderBy(pair => pair.Item2, StringComparer.OrdinalIgnoreCase)
                          .ToList();
        }

        private void Prune(long now)
        {
            // The ConcurrentDictionary is already thread-safe - no additional lock, and the
            // enumerator tolerates removals during the walk.
            foreach (var pair in _lasers)
            {
                if (now - pair.Value.Timestamp > PruneAfterMs)
                    _lasers.TryRemove(pair.Key, out _);
            }
        }

        public void Dispose()
        {
            try { _socket.Close(); }
            catch { /* already dead */ }
        }
    }
    #endregion
}
