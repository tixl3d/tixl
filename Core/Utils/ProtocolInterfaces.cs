#nullable enable
using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using T3.Core.IO;
using T3.Core.Logging;

namespace T3.Core.Utils;

/// <summary>
/// The persisted, per-machine interface bindings for the network protocols — edited on the Settings
/// window's Networking page. One adapter choice per protocol (UDP, TCP, OSC, ArtNet, sACN, ...) replaces
/// the per-operator local-address dropdowns: a multi-homed show machine keeps every protocol socket on
/// the show network without touching operators, and because the choice lives here instead of in
/// operator inputs, a composition shared by collaboration, boygrouping or failover never carries the
/// machine that edited it — every machine binds its own adapters.
/// </summary>
public sealed class ProtocolInterfaceData
{
    /// <summary>Protocol key → selection: <see cref="ProtocolInterfaces.AnyKey"/>,
    /// <see cref="ProtocolInterfaces.LoopbackKey"/> or a <see cref="LanInterface.Name"/>.</summary>
    public Dictionary<string, string> Interfaces = new();
}

internal sealed class ProtocolInterfaceSettings : Settings<ProtocolInterfaceData>
{
    public ProtocolInterfaceSettings() : base("protocolInterfaces.json", saveOnQuit: true)
    {
    }
}

public static class ProtocolInterfaces
{
    /// <summary>Selection meaning "bind any adapter" — what "0.0.0.0" meant in the operator inputs.</summary>
    public const string AnyKey = "";

    /// <summary>Selection meaning "loopback only" — 127.0.0.1, for protocols talking to the same machine.</summary>
    public const string LoopbackKey = "Loopback";

    /// <summary>Stable keys the operators reference. Order defines the settings-page row order.</summary>
    public static class Keys
    {
        public const string Udp = "Udp";
        public const string Tcp = "Tcp";
        public const string Osc = "Osc";
        public const string ArtNet = "ArtNet";
        public const string Sacn = "Sacn";
        public const string PosiStageNet = "PosiStageNet";
        public const string FreeD = "FreeD";
        public const string WebSocket = "WebSocket";
        public const string WebServer = "WebServer";
        public const string Visca = "Visca";
        public const string Onvif = "Onvif";
        public const string Ponk = "Ponk";
    }

    /// <summary>The protocols with a bindable local interface, in settings-page order.</summary>
    public static IReadOnlyList<(string Key, string Label)> Protocols { get; } =
        [
            (Keys.Udp, "UDP"),
            (Keys.Tcp, "TCP"),
            (Keys.Osc, "OSC"),
            (Keys.ArtNet, "ArtNet"),
            (Keys.Sacn, "sACN"),
            (Keys.PosiStageNet, "PosiStageNet"),
            (Keys.FreeD, "FreeD"),
            (Keys.WebSocket, "WebSocket"),
            (Keys.WebServer, "Web Server"),
            (Keys.Visca, "Visca"),
            (Keys.Onvif, "ONVIF"),
            (Keys.Ponk, "PONK"),
        ];

    /// <summary>The persisted selection for a protocol — never null. An unknown key reads as Automatic.</summary>
    public static string GetSelection(string protocolKey)
    {
        lock (_dataLock)
        {
            return ProtocolInterfaceSettings.Config.Interfaces.TryGetValue(protocolKey, out var selection) ? selection : AnyKey;        }
    }

    /// <summary>Persists the selection for a protocol and saves the settings file immediately, so a crash
    /// cannot undo what the user picked on the Networking page.</summary>
    public static void SetSelection(string protocolKey, string selection)
    {
        lock (_dataLock)
        {
            ProtocolInterfaceSettings.Config.Interfaces[protocolKey] = selection ?? AnyKey;
        }

        ProtocolInterfaceSettings.Save();
    }

    /// <summary>
    /// Address a protocol's sockets bind to, as the operator bind code expects it: "0.0.0.0" for any
    /// adapter, "127.0.0.1" for loopback, the selected adapter's current address otherwise. A selected
    /// adapter that is not connected falls back to "0.0.0.0" — the same "bind all" the Automatic choice
    /// gives — so a show keeps running on a machine whose adapter name changed. Cheap enough to call
    /// every frame: reads a dictionary and a cached adapter list.
    /// </summary>
    public static string ResolveAddressString(string protocolKey)
    {
        return ResolveAddressStringForSelection(GetSelection(protocolKey), CachedUsableInterfaces());
    }

    /// <summary>
    /// The same address, or null when the protocol should bind all adapters and let the OS route. Call
    /// sites that can tell "bind everything" apart from a literal address (for example the DMX outputs,
    /// which pick an adapter from the target's subnet when none is pinned) use this overload.
    /// </summary>
    public static string? ResolveOptionalAddress(string protocolKey)
    {
        return ResolveAddressForSelection(GetSelection(protocolKey), CachedUsableInterfaces())?.ToString();
    }

    /// <summary>Address a protocol's sockets bind to, or <see cref="IPAddress.Any"/> for "bind all".</summary>
    public static IPAddress ResolveAddress(string protocolKey)
    {
        return ResolveAddressForSelection(GetSelection(protocolKey), CachedUsableInterfaces()) ?? IPAddress.Any;
    }

    /// <summary>Resolves one selection against an adapter list. Internal and allocation-free so the
    /// resolution rules can be tested without touching the machine's real adapters.</summary>
    internal static string ResolveAddressStringForSelection(string? selection, IReadOnlyList<LanInterface> adapters)
    {
        var address = ResolveAddressForSelection(selection, adapters);
        return address?.ToString() ?? AnyAddress;
    }

    internal static IPAddress? ResolveAddressForSelection(string? selection, IReadOnlyList<LanInterface> adapters)
    {
        if (string.IsNullOrEmpty(selection) || selection == AnyKey)
            return null;

        if (selection == LoopbackKey)
            return IPAddress.Loopback;

        for (var i = 0; i < adapters.Count; i++)
        {
            if (string.Equals(adapters[i].Name, selection, StringComparison.OrdinalIgnoreCase))
                return adapters[i].Address;
        }

        // The adapter is gone (unplugged, renamed, or the settings were copied from another machine):
        // binding nothing would silence the protocol, so fall back to all interfaces.
        return null;
    }

    private const string AnyAddress = "0.0.0.0";

    /// <summary>
    /// The usable adapters, refreshed in the background every few seconds. Enumerating the OS adapters
    /// can stall for hundreds of milliseconds, which must never land on the render thread — the first
    /// call enumerates inline (editor start-up, where the stall is invisible) and later refreshes run
    /// on a worker thread while callers keep using the previous list.
    /// </summary>
    private static IReadOnlyList<LanInterface> CachedUsableInterfaces()
    {
        var now = Environment.TickCount64;
        if (_adaptersRefreshedTicks >= 0 && now - Volatile.Read(ref _adaptersRefreshedTicks) <= AdapterRefreshIntervalMs)
            return _adapters;

        if (Interlocked.Exchange(ref _refreshInProgress, 1) != 0)
            return _adapters;

        if (_adaptersRefreshedTicks < 0)
        {
            // Cold start: fill the cache before the first bind instead of letting sockets come up on
            // "any" and restart once the worker catches up.
            try
            {
                _adapters = LanInterfaces.GetUsable();
            }
            catch (Exception e)
            {
                Log.Warning($"Could not enumerate network adapters: {e.Message}");
                _adapters = [];
            }

            _adaptersRefreshedTicks = now;
            Interlocked.Exchange(ref _refreshInProgress, 0);
            return _adapters;
        }

        ThreadPool.QueueUserWorkItem(_ =>
                         {
                             try
                             {
                                 var adapters = LanInterfaces.GetUsable();
                                 _adapters = adapters;
                                 _adaptersRefreshedTicks = Environment.TickCount64;
                             }
                             catch (Exception e)
                             {
                                 Log.Warning($"Could not refresh network adapters: {e.Message}");
                             }
                             finally
                             {
                                 Interlocked.Exchange(ref _refreshInProgress, 0);
                             }
                         });

        return _adapters;
    }

    private const int AdapterRefreshIntervalMs = 5000;
    private static IReadOnlyList<LanInterface> _adapters = [];
    private static long _adaptersRefreshedTicks = -1;
    private static int _refreshInProgress;
    private static readonly object _dataLock = new();
    private static readonly ProtocolInterfaceSettings _settings = new();
}
