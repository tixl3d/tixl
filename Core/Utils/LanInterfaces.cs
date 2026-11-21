#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace T3.Core.Utils;

/// <summary>
/// One selectable IPv4 adapter. Collaboration and boygrouping hosting, LAN discovery and failover
/// heartbeats bind to the adapter picked in their settings, so a multi-homed show machine cannot
/// answer on the wrong network.
/// </summary>
public sealed class LanInterface
{
    public LanInterface(string name, string description, IPAddress address, IPAddress? subnetMask,
                        IPAddress? broadcastAddress, bool supportsMulticast)
    {
        Name = name;
        Description = description;
        Address = address;
        SubnetMask = subnetMask;
        BroadcastAddress = broadcastAddress;
        SupportsMulticast = supportsMulticast;
    }

    /// <summary>Stable adapter name (persisted in the user settings).</summary>
    public string Name { get; }

    public string Description { get; }

    public IPAddress Address { get; }

    /// <summary>Subnet mask, or null when the adapter does not report one.</summary>
    public IPAddress? SubnetMask { get; }

    /// <summary>Subnet broadcast address, or null when the adapter does not report a mask.</summary>
    public IPAddress? BroadcastAddress { get; }

    public bool SupportsMulticast { get; }

    public string DisplayName => $"{Name} ({Address})";

    /// <summary>True when the address shares this adapter's subnet — the same masked network, so
    /// packets to it leave on this adapter without a route decision. False when the adapter reports
    /// no mask: with no mask there is no claim about what this adapter reaches.</summary>
    public bool SubnetContains(IPAddress address)
    {
        if (SubnetMask == null || address.AddressFamily != AddressFamily.InterNetwork)
            return false;

        var own = Address.GetAddressBytes();
        var other = address.GetAddressBytes();
        var mask = SubnetMask.GetAddressBytes();
        if (own.Length != 4 || other.Length != 4 || mask.Length != 4)
            return false;

        // Some Linux adapters report 0.0.0.0 as their mask — "unknown", not "everywhere".
        if (mask[0] == 0 && mask[1] == 0 && mask[2] == 0 && mask[3] == 0)
            return false;

        for (var i = 0; i < 4; i++)
        {
            if ((own[i] & mask[i]) != (other[i] & mask[i]))
                return false;
        }

        return true;
    }
}

public static class LanInterfaces
{
    /// <summary>
    /// Up, non-tunnel IPv4 adapters, wired first. Loopback is excluded: it cannot carry a session
    /// between machines, and the transports fall back to all interfaces when none is selected.
    /// </summary>
    public static List<LanInterface> GetUsable()
    {
        var result = new List<(int Priority, LanInterface Interface)>();
        try
        {
            foreach (var networkInterface in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (networkInterface.OperationalStatus != OperationalStatus.Up
                    || networkInterface.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)
                    continue;

                IPAddress? address = null;
                IPAddress? mask = null;
                try
                {
                    foreach (var unicast in networkInterface.GetIPProperties().UnicastAddresses)
                    {
                        if (unicast.Address.AddressFamily != AddressFamily.InterNetwork
                            || IPAddress.IsLoopback(unicast.Address))
                            continue;

                        address = unicast.Address;
                        mask = unicast.IPv4Mask;
                        break;
                    }
                }
                catch (NetworkInformationException)
                {
                    // The adapter disappeared between enumeration and inspection.
                    continue;
                }

                if (address == null)
                    continue;

                result.Add((InterfacePriority(networkInterface.NetworkInterfaceType),
                            new LanInterface(networkInterface.Name,
                                             networkInterface.Description,
                                             address,
                                             mask,
                                             ComputeBroadcastAddress(address, mask),
                                             networkInterface.SupportsMulticast)));
            }
        }
        catch (NetworkInformationException)
        {
            // An empty list means "automatic"; the caller binds all interfaces.
        }

        return result.OrderBy(entry => entry.Priority)
                     .ThenBy(entry => entry.Interface.Name, StringComparer.OrdinalIgnoreCase)
                     .Select(entry => entry.Interface)
                     .ToList();
    }

    /// <summary>Resolves a persisted adapter name; null when it is empty or the adapter is gone.</summary>
    public static LanInterface? Find(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return null;

        return GetUsable().FirstOrDefault(i => string.Equals(i.Name, name, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Address to bind to, or null to bind all interfaces.</summary>
    public static IPAddress? ResolveAddress(string? name) => Find(name)?.Address;

    /// <summary>Directed broadcast for the selected adapter, or null to use the global broadcast.</summary>
    public static IPAddress? ResolveBroadcast(string? name) => Find(name)?.BroadcastAddress;

    /// <summary>True when the address text parses to an address any adapter on this machine currently
    /// holds — up or down, so a deliberate pin survives an unplugged cable while a foreign address
    /// (what a project folder cloned from another computer carries) is recognized as foreign.</summary>
    public static bool IsLocalAddress(string? addressText)
    {
        return !string.IsNullOrWhiteSpace(addressText)
               && IPAddress.TryParse(addressText, out var parsed)
               && IsLocalAddress(parsed);
    }

    public static bool IsLocalAddress(IPAddress address)
    {
        try
        {
            foreach (var networkInterface in NetworkInterface.GetAllNetworkInterfaces())
            {
                foreach (var unicast in networkInterface.GetIPProperties().UnicastAddresses)
                {
                    if (Equals(unicast.Address, address))
                        return true;
                }
            }
        }
        catch (NetworkInformationException)
        {
            // Enumeration failed: claim nothing.
        }

        return false;
    }

    /// <summary>The usable adapter whose subnet contains the address — the NIC that reaches it without
    /// a route decision. Null when no usable adapter claims that subnet.</summary>
    public static LanInterface? FindInSubnetOf(IPAddress target)
    {
        return GetUsable().FirstOrDefault(adapter => adapter.SubnetContains(target));
    }

    /// <summary>
    /// Repairs one persisted "packets leave on this address" value after the project moved to another
    /// computer: an address this machine does not own re-homes to the usable adapter that reaches the
    /// target device (same subnet), or falls back to any interface when neither a parsable target nor
    /// a matching adapter pins it down. An empty value, anything unparsable, and an address this
    /// machine itself holds — even on a down adapter — stay untouched.
    /// </summary>
    public static string ReconcileLocalAddress(string? localAddress, string? targetAddress)
    {
        if (string.IsNullOrWhiteSpace(localAddress) || !IPAddress.TryParse(localAddress, out var parsedLocal))
            return localAddress ?? string.Empty;

        if (IsLocalAddress(parsedLocal))
            return localAddress;

        if (IPAddress.TryParse(targetAddress, out var target)
            && FindInSubnetOf(target) is { } reaching)
        {
            return reaching.Address.ToString();
        }

        return string.Empty;
    }

    /// <summary>Wired adapters sort first; that is what a show machine wants at the top of the list.</summary>
    private static int InterfacePriority(NetworkInterfaceType type)
    {
        return type switch
                   {
                       NetworkInterfaceType.Ethernet or NetworkInterfaceType.GigabitEthernet => 0,
                       NetworkInterfaceType.FastEthernetT or NetworkInterfaceType.FastEthernetFx => 1,
                       NetworkInterfaceType.Wireless80211 => 2,
                       _ => 3,
                   };
    }

    private static IPAddress? ComputeBroadcastAddress(IPAddress address, IPAddress? mask)
    {
        var addressBytes = address.GetAddressBytes();
        var maskBytes = mask?.GetAddressBytes();
        if (addressBytes.Length != 4 || maskBytes == null || maskBytes.Length != 4)
            return null;

        var broadcast = new byte[4];
        for (var i = 0; i < 4; i++)
            broadcast[i] = (byte)(addressBytes[i] | (maskBytes[i] ^ 0xFF));

        return new IPAddress(broadcast);
    }
}
