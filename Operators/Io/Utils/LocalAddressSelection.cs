#nullable enable
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace Io.Utils;

/// <summary>Uses a saved local IPv4 address, or OS routing when that address is unavailable.</summary>
internal sealed class LocalAddressSelection(Func<LocalAddressSelection.Adapter[]> enumerateAdapters)
{
    internal sealed class Adapter(IPAddress address, IPAddress subnetMask, string name)
    {
        internal IPAddress Address { get; } = address;
        internal IPAddress SubnetMask { get; } = subnetMask;
        internal string DisplayName { get; } = $"{name}: {address}";
    }

    internal LocalAddressSelection() : this(EnumerateAdapters) { }

    internal IPAddress? EffectiveAddress { get; private set; }
    internal IPAddress? SubnetMask { get; private set; }
    internal string? ErrorMessage { get; private set; }
    internal string DisplayValue { get; private set; } = string.Empty;
    internal string SendingMessage { get; private set; } = string.Empty;
    /// <summary>Whether the configured address was unavailable locally and the wildcard (Auto) is bound instead.</summary>
    internal bool IsFallback { get; private set; }

    /// <summary>Re-evaluates the configured address. Returns whether the effective bind address changed, so the caller should reconnect.</summary>
    internal bool Update(string? configuredAddress, bool forceRefresh = false)
    {
        configuredAddress ??= string.Empty;
        if (!forceRefresh && _configuredAddress == configuredAddress)
            return false;

        _configuredAddress = configuredAddress;
        var previousAddress = EffectiveAddress;
        var previousMask = SubnetMask;
        EffectiveAddress = null;
        SubnetMask = null;
        ErrorMessage = null;
        IsFallback = false;
        SelectAddress(configuredAddress);

        DisplayValue = IsFallback
                           ? $"{configuredAddress} (not found, bound 0.0.0.0)"
                           : IPAddress.Any.Equals(EffectiveAddress) ? AutoOption
                           : configuredAddress;
        SendingMessage = $"Sending. Bound to {EffectiveAddress}.";
        return !Equals(previousAddress, EffectiveAddress) || !Equals(previousMask, SubnetMask);
    }

    /// <summary>Dropdown choices: Auto first, then every adapter. Refreshes the adapter snapshot without touching the active binding.</summary>
    internal IEnumerable<string> GetOptions()
    {
        yield return AutoOption;
        Refresh();
        foreach (var adapter in _adapters)
            yield return adapter.DisplayName;
    }

    /// <summary>The address to store for a dropdown choice, or null when the choice is not one of the options.</summary>
    internal IPAddress? GetAddress(string displayName)
    {
        if (displayName == AutoOption)
            return IPAddress.Any;
        foreach (var adapter in _adapters)
        {
            if (adapter.DisplayName == displayName)
                return adapter.Address;
        }
        return null;
    }

    private bool Refresh()
    {
        // A failed refresh must not reuse a stale adapter list.
        try
        {
            _adapters = enumerateAdapters();
            return true;
        }
        catch (NetworkInformationException) { _adapters = Array.Empty<Adapter>(); }
        catch (SocketException) { _adapters = Array.Empty<Adapter>(); }
        return false;
    }

    private void SelectAddress(string configuredAddress)
    {
        if (string.IsNullOrWhiteSpace(configuredAddress))
        {
            ErrorMessage = "Select a Local IP Address to send from.";
        }
        else if (!IPAddress.TryParse(configuredAddress, out var configuredIp) || configuredIp.AddressFamily != AddressFamily.InterNetwork)
        {
            ErrorMessage = $"Local IP '{configuredAddress}' is not IPv4. Select a local adapter.";
        }
        else if (configuredIp.Equals(IPAddress.Any))
        {
            EffectiveAddress = IPAddress.Any;
        }
        else if (IPAddress.IsLoopback(configuredIp))
        {
            EffectiveAddress = configuredIp;
            SubnetMask = LoopbackMask;
        }
        else if (!IsUnicast(configuredIp))
        {
            ErrorMessage = $"Local IP '{configuredAddress}' is not a unicast IPv4 address.";
        }
        else if (!Refresh())
        {
            ErrorMessage = "Cannot read network adapters. Press Reconnect to retry.";
        }
        else
        {
            foreach (var adapter in _adapters)
            {
                if (!adapter.Address.Equals(configuredIp))
                    continue;
                EffectiveAddress = adapter.Address;
                SubnetMask = adapter.SubnetMask;
                return;
            }

            EffectiveAddress = IPAddress.Any;
            IsFallback = true;
        }
    }

    /** Routable unicast only: excludes the this-network (0/8) and multicast/reserved (224+) ranges. */
    private static bool IsUnicast(IPAddress address)
    {
        Span<byte> bytes = stackalloc byte[4];
        address.TryWriteBytes(bytes, out _);
        return bytes[0] > 0 && bytes[0] < 224;
    }

    private static Adapter[] EnumerateAdapters()
    {
        var adapters = new List<Adapter> { new(IPAddress.Loopback, LoopbackMask, "Localhost") };
        foreach (var networkInterface in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (networkInterface.OperationalStatus != OperationalStatus.Up || networkInterface.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                continue;
            try
            {
                foreach (var unicast in networkInterface.GetIPProperties().UnicastAddresses)
                {
                    if (unicast.Address.AddressFamily == AddressFamily.InterNetwork && IsUnicast(unicast.Address))
                        adapters.Add(new Adapter(unicast.Address, unicast.IPv4Mask, networkInterface.Name));
                }
            }
            catch (NetworkInformationException) { /* The adapter can disappear during enumeration. */ }
        }
        return adapters.ToArray();
    }

    private const string AutoOption = "Auto (OS routing)";
    private static readonly IPAddress LoopbackMask = IPAddress.Parse("255.0.0.0");
    private Adapter[] _adapters = Array.Empty<Adapter>();
    private string? _configuredAddress;
}
