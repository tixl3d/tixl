#nullable enable
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using T3.Core.Show;
using Xunit;

namespace Core.Tests;

public class LocalAdapterResolverTests
{
    [Fact]
    public void EmptyAddressIsLeftAlone()
    {
        var resolver = new LocalAdapterResolver();
        resolver.Update("", null, 0);

        Assert.Equal("", resolver.EffectiveIp);
        Assert.Null(resolver.SubnetMask);
    }

    [Fact]
    public void LoopbackIsKept()
    {
        var resolver = new LocalAdapterResolver();
        resolver.Update("127.0.0.1", null, 0);

        Assert.Equal("127.0.0.1", resolver.EffectiveIp);
    }

    [Fact]
    public void OwnAddressIsKept()
    {
        var own = FirstOwnAddress();
        if (own == null)
            return;

        var resolver = new LocalAdapterResolver();
        resolver.Update(own.ToString(), null, 0);

        Assert.Equal(own.ToString(), resolver.EffectiveIp);
        Assert.NotNull(resolver.SubnetMask);
    }

    [Fact]
    public void AddressOfAnotherMachineFallsBackToAnOwnAdapter()
    {
        var own = FirstOwnAddress();
        if (own == null)
            return;

        // 203.0.113.0/24 is documentation space: no machine here holds it, so the pick must be one of ours.
        var resolver = new LocalAdapterResolver();
        resolver.Update("203.0.113.77", null, 0);

        Assert.NotEqual("203.0.113.77", resolver.EffectiveIp);
        Assert.True(OwnAddresses().Any(a => a.ToString() == resolver.EffectiveIp));
        Assert.NotNull(resolver.SubnetMask);
    }

    [Fact]
    public void NeighbourInTheSameSubnetResolvesToTheOwnAdapterOfThatSubnet()
    {
        var own = FirstOwnAddress();
        if (own == null)
            return;

        var bytes = own.GetAddressBytes();
        bytes[3] = (byte)(bytes[3] == 250 ? 249 : 250);
        var neighbour = new IPAddress(bytes);
        var resolver = new LocalAdapterResolver();
        resolver.Update(neighbour.ToString(), null, 0);

        Assert.Equal(own.ToString(), resolver.EffectiveIp);
    }

    private static IPAddress? FirstOwnAddress() => OwnAddresses().FirstOrDefault();

    // Only /24 or wider-prefix adapters make the neighbour test meaningful, so keep to ordinary LAN adapters.
    private static IPAddress[] OwnAddresses()
    {
        return NetworkInterface.GetAllNetworkInterfaces()
                               .Where(ni => ni.OperationalStatus == OperationalStatus.Up
                                            && ni.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                               .SelectMany(ni => ni.GetIPProperties().UnicastAddresses)
                               .Where(a => a.Address.AddressFamily == AddressFamily.InterNetwork
                                           && a.IPv4Mask.ToString() == "255.255.255.0")
                               .Select(a => a.Address)
                               .ToArray();
    }
}
