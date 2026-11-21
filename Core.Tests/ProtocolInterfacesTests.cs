#nullable enable
using System;
using System.Collections.Generic;
using System.Net;
using T3.Core.Utils;
using Xunit;

namespace Core.Tests;

/// <summary>
/// Resolution rules for the per-protocol interface bindings picked on the Networking settings page.
/// The pure resolver is tested against synthetic adapters, so no real network enumeration is involved.
/// </summary>
public class ProtocolInterfacesTests
{
    private static readonly LanInterface Ethernet = new("Ethernet 2",
                                                        "Realtek PCIe GbE",
                                                        IPAddress.Parse("10.0.0.69"),
                                                        IPAddress.Parse("255.255.255.0"),
                                                        IPAddress.Parse("10.0.0.255"),
                                                        true);

    private static readonly LanInterface Wifi = new("Wi-Fi",
                                                    "Intel AX210",
                                                    IPAddress.Parse("192.168.1.42"),
                                                    IPAddress.Parse("255.255.255.0"),
                                                    IPAddress.Parse("192.168.1.255"),
                                                    true);

    private static readonly List<LanInterface> Adapters = [Ethernet, Wifi];

    [Fact]
    public void AnySelectionResolvesToBindAllAddress()
    {
        Assert.Equal("0.0.0.0", ProtocolInterfaces.ResolveAddressStringForSelection(ProtocolInterfaces.AnyKey, Adapters));
    }

    [Fact]
    public void LoopbackSelectionResolvesToLoopbackAddress()
    {
        Assert.Equal("127.0.0.1", ProtocolInterfaces.ResolveAddressStringForSelection(ProtocolInterfaces.LoopbackKey, Adapters));
    }

    [Fact]
    public void AdapterSelectionResolvesToAdapterAddressCaseInsensitive()
    {
        Assert.Equal("10.0.0.69", ProtocolInterfaces.ResolveAddressStringForSelection("ethernet 2", Adapters));
        Assert.Equal("192.168.1.42", ProtocolInterfaces.ResolveAddressStringForSelection("Wi-Fi", Adapters));
    }

    [Fact]
    public void MissingAdapterFallsBackToBindAll()
    {
        Assert.Equal("0.0.0.0", ProtocolInterfaces.ResolveAddressStringForSelection("Ethernet 5", Adapters));
        Assert.Equal("0.0.0.0", ProtocolInterfaces.ResolveAddressStringForSelection("Wi-Fi", []));
    }

    [Fact]
    public void NullSelectionReadsAsAny()
    {
        Assert.Equal("0.0.0.0", ProtocolInterfaces.ResolveAddressStringForSelection(null, Adapters));
    }

    [Fact]
    public void IPAddressVariantReturnsNullForAnyAndAdapterForNamedSelections()
    {
        Assert.Null(ProtocolInterfaces.ResolveAddressForSelection(ProtocolInterfaces.AnyKey, Adapters));
        Assert.Equal(IPAddress.Loopback, ProtocolInterfaces.ResolveAddressForSelection(ProtocolInterfaces.LoopbackKey, Adapters));
        Assert.Equal(IPAddress.Parse("10.0.0.69"), ProtocolInterfaces.ResolveAddressForSelection("Ethernet 2", Adapters));
        Assert.Null(ProtocolInterfaces.ResolveAddressForSelection("Gone", Adapters));
    }
}
