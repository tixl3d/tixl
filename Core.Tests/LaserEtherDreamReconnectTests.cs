using T3.Core.Output;
using Xunit;

namespace Core.Tests;

/// <summary>
/// Closing an Ether Dream session mid-write leaves the DAC holding a stale full buffer until it is
/// power-cycled, so a settings edit on the plug card must not make the driver rebuild the stream
/// unless the device address itself changed.
/// </summary>
public class LaserEtherDreamReconnectTests
{
    private static LaserPlug UnreachableDac() => new()
                                                     {
                                                         Kind = LaserPlug.Kinds.EtherDream,
                                                         Name = "DAC",
                                                         Address = "127.0.0.1",
                                                     };

    [Fact]
    public void InterfaceAndDeviceSettingEditsKeepTheConnection()
    {
        var plug = UnreachableDac();
        var stream = LaserOutputStream.Create(plug);
        Assert.NotNull(stream);
        try
        {
            plug.LocalAddress = "10.0.0.69";
            Assert.True(stream.MatchesPlug(plug));

            plug.EtherDreamSettings.MaxBrightness = 0.25f;
            plug.EtherDreamSettings.SwapColorChannels = true;
            plug.EtherDreamSettings.ScanRate = 20000;
            Assert.True(stream.MatchesPlug(plug));
            Assert.Null(stream.DescribeMismatch(plug));
        }
        finally
        {
            stream.Dispose();
        }
    }

    [Fact]
    public void ChangingTheDeviceAddressRebuildsTheStream()
    {
        var plug = UnreachableDac();
        var stream = LaserOutputStream.Create(plug);
        Assert.NotNull(stream);
        try
        {
            plug.Address = "127.0.0.2";
            Assert.False(stream.MatchesPlug(plug));
            Assert.Contains("address", stream.DescribeMismatch(plug));
        }
        finally
        {
            stream.Dispose();
        }
    }

    [Fact]
    public void CitpLevelEditsKeepTheSession()
    {
        var plug = new LaserPlug { Kind = LaserPlug.Kinds.Citp, Name = "CITP", Address = "127.0.0.1", Port = 1 };
        var stream = LaserOutputStream.Create(plug);
        Assert.NotNull(stream);
        try
        {
            plug.CitpSettings.MaxBrightness = 0.5f;
            plug.CitpSettings.SwapColorChannels = true;
            plug.CitpSettings.Keepalive = false;
            Assert.True(stream.MatchesPlug(plug));

            plug.Port = 2;
            Assert.False(stream.MatchesPlug(plug));
        }
        finally
        {
            stream.Dispose();
        }
    }
}
