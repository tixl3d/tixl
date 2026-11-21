#nullable enable
using System.Net;
using T3.Core.Show;
using T3.Core.Utils;
using T3.Editor.Collab;

namespace T3.Editor.Show;

/// <summary>A machine this instance is currently linked to over the network.</summary>
internal readonly record struct NetworkPeer(string Source, IPAddress Address);

/// <summary>
/// Compares the adapter this machine is set to with the networks of the machines it is linked to in
/// collaboration, boygrouping and failover. The adapter name is a per-computer setting that never
/// travels with a project, so each machine has to be right on its own; every one of them sees the
/// others' addresses and can tell when its pick leaves them on a different network.
/// </summary>
internal static class NetworkPeerCheck
{
    internal enum Kind
    {
        /// <summary>The picked adapter's network does not contain the peer, another adapter's does.</summary>
        WrongAdapter,

        /// <summary>The saved adapter is not on this machine (a renamed or foreign adapter name).</summary>
        SavedAdapterMissing,

        /// <summary>No adapter is picked and this machine has several; the OS route decides.</summary>
        NotPinned,
    }

    internal readonly record struct Finding(Kind Kind, NetworkPeer Peer, LanInterface Suggested);

    /// <summary>Everything this instance is linked to right now. Not for per-frame use outside the settings window.</summary>
    public static void CollectPeers(List<NetworkPeer> peers)
    {
        peers.Clear();
        CollabManager.Instance.AddLinkedPeers(peers);
        BoygroupManager.Instance.AddLinkedPeers(peers);

        _failoverAddresses.Clear();
        FailoverArbiter.GetLivePeerAddresses(_failoverAddresses);
        foreach (var text in _failoverAddresses)
        {
            if (IPAddress.TryParse(text, out var address))
                peers.Add(new NetworkPeer("Failover peer", address));
        }
    }

    /// <summary>
    /// Peers that are on the network of an adapter other than the one in use. Peers beyond a router
    /// (no adapter shares their subnet) and loopback peers are not judged.
    /// </summary>
    public static void FindProblems(string savedAdapterName, IReadOnlyList<LanInterface> usable, List<NetworkPeer> peers, List<Finding> findings)
    {
        findings.Clear();
        LanInterface? picked = null;
        foreach (var adapter in usable)
        {
            if (string.Equals(adapter.Name, savedAdapterName, StringComparison.OrdinalIgnoreCase))
                picked = adapter;
        }

        var hasSavedName = !string.IsNullOrWhiteSpace(savedAdapterName);
        foreach (var peer in peers)
        {
            if (IPAddress.IsLoopback(peer.Address))
                continue;

            LanInterface? reaching = null;
            foreach (var adapter in usable)
            {
                if (!adapter.SubnetContains(peer.Address))
                    continue;

                reaching = adapter;
                break;
            }

            if (reaching == null)
                continue;

            if (picked != null)
            {
                if (picked != reaching && !picked.SubnetContains(peer.Address))
                    findings.Add(new Finding(Kind.WrongAdapter, peer, reaching));
            }
            else if (hasSavedName)
            {
                findings.Add(new Finding(Kind.SavedAdapterMissing, peer, reaching));
            }
            else if (usable.Count > 1)
            {
                findings.Add(new Finding(Kind.NotPinned, peer, reaching));
            }
        }
    }

    private static readonly List<string> _failoverAddresses = [];
}
