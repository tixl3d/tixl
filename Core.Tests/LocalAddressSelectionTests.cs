using System.Net;
using System.Net.NetworkInformation;
using Io.Utils;
using Xunit;

namespace Core.Tests;

public class LocalAddressSelectionTests
{
    [Fact]
    public void AvailableAddressKeepsItsSubnetMaskOnMultihomedHosts()
    {
        var selection = Selection(Adapter("10.1.2.3", "255.255.0.0"), Adapter("10.1.2.4"), Adapter("192.168.1.2"));
        selection.Update("10.1.2.3");
        Assert.Equal(IPAddress.Parse("10.1.2.3"), selection.EffectiveAddress);
        Assert.Equal(IPAddress.Parse("255.255.0.0"), selection.SubnetMask);
        Assert.False(selection.IsFallback);
        Assert.Null(selection.ErrorMessage);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void UnavailableAddressUsesOsRoutingRegardlessOfSubnetMatches(int addressCount)
    {
        // Same-subnet adapters are present on purpose; selection must stay exact-match only.
        LocalAddressSelection.Adapter[] adapters = [Adapter("10.1.2.3"), Adapter("10.1.2.4")];
        var selection = Selection(adapters[..addressCount]);
        selection.Update("10.1.2.99");
        Assert.Equal(IPAddress.Any, selection.EffectiveAddress);
        Assert.Null(selection.SubnetMask);
        Assert.True(selection.IsFallback);
        Assert.Null(selection.ErrorMessage);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("not-an-ip")]
    [InlineData("::1")]
    [InlineData("239.255.0.1")]
    [InlineData("255.255.255.255")]
    public void InvalidSourceNeverFallsBackToOsRouting(string? saved)
    {
        var selection = Selection(Adapter("10.1.2.3"));
        selection.Update(saved);
        Assert.Null(selection.EffectiveAddress);
        Assert.False(selection.IsFallback);
        Assert.NotNull(selection.ErrorMessage);
    }

    [Theory]
    [InlineData("0.0.0.0")]
    [InlineData("127.0.0.1")]
    [InlineData("127.0.0.2")]
    public void ExplicitAutoAndLoopbackDoNotRequireAdapterEnumeration(string saved)
    {
        var selection = new LocalAddressSelection(() => throw new NetworkInformationException());
        selection.Update(saved);
        Assert.Equal(IPAddress.Parse(saved), selection.EffectiveAddress);
        Assert.False(selection.IsFallback);
        Assert.Null(selection.ErrorMessage);
    }

    [Fact]
    public void ReconnectCanRestoreTheSavedAddressAfterFallback()
    {
        LocalAddressSelection.Adapter[] snapshot = [];
        var selection = new LocalAddressSelection(() => snapshot);
        selection.Update("10.1.2.3");
        Assert.Equal(IPAddress.Any, selection.EffectiveAddress);
        snapshot = [Adapter("10.1.2.3")];
        Assert.False(selection.Update("10.1.2.3"));
        Assert.True(selection.IsFallback);

        Assert.True(selection.Update("10.1.2.3", true));
        Assert.Equal(IPAddress.Parse("10.1.2.3"), selection.EffectiveAddress);
        Assert.False(selection.IsFallback);
    }

    [Fact]
    public void DropdownRefreshDoesNotChangeTheActiveBinding()
    {
        // GetOptions refreshes the snapshot; only Update may change the binding.
        LocalAddressSelection.Adapter[] snapshot = [Adapter("10.1.2.3")];
        var selection = new LocalAddressSelection(() => snapshot);
        selection.Update("10.1.2.3");
        snapshot = [Adapter("192.168.1.2")];
        foreach (var option in selection.GetOptions())
            selection.GetAddress(option);
        Assert.Equal(IPAddress.Parse("10.1.2.3"), selection.EffectiveAddress);

        Assert.True(selection.Update("10.1.2.3", true));
        Assert.Equal(IPAddress.Any, selection.EffectiveAddress);
        Assert.Null(selection.SubnetMask);
        Assert.True(selection.IsFallback);
    }

    [Fact]
    public void EnumerationFailureDoesNotMasqueradeAsAnUnavailableAddress()
    {
        bool fail = false;
        var selection = new LocalAddressSelection(() => fail ? throw new NetworkInformationException() : [Adapter("10.1.2.3")]);
        selection.Update("10.1.2.3");
        fail = true;
        Assert.True(selection.Update("10.1.2.3", true));
        Assert.Null(selection.EffectiveAddress);
        Assert.False(selection.IsFallback);
        Assert.NotNull(selection.ErrorMessage);

        fail = false;
        Assert.True(selection.Update("10.1.2.3", true));
        Assert.Equal(IPAddress.Parse("10.1.2.3"), selection.EffectiveAddress);
        Assert.Null(selection.ErrorMessage);
    }

    [Fact]
    public void ChangingSourceReplacesFallbackWithTheExplicitSelection()
    {
        var selection = Selection(Adapter("10.1.2.3"));
        selection.Update("192.168.1.99");
        Assert.True(selection.IsFallback);
        Assert.True(selection.Update("10.1.2.3"));
        Assert.Equal(IPAddress.Parse("10.1.2.3"), selection.EffectiveAddress);
        Assert.False(selection.IsFallback);
    }

    private static LocalAddressSelection Selection(params LocalAddressSelection.Adapter[] adapters) => new(() => adapters);
    private static LocalAddressSelection.Adapter Adapter(string ip, string mask = "255.255.255.0") => new(IPAddress.Parse(ip), IPAddress.Parse(mask), ip);
}
