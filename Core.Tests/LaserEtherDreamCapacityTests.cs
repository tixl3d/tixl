using T3.Core.Output;
using Xunit;

namespace Core.Tests;

/// <summary>
/// The EtherDream library paces every send against the buffer capacity it was constructed with
/// (1799 by default), so the stream must resolve the same number or a send blocks inside the
/// library's ping loop and the stall watchdog recycles a healthy connection. Newer DACs report a
/// larger buffer in their discovery broadcast; a connected DAC stops broadcasting, and then the
/// fill level in its own status is the only proof that the buffer is bigger than the assumption.
/// </summary>
public class LaserEtherDreamCapacityTests
{
    [Fact]
    public void UnknownDeviceKeepsTheLibraryDefault()
    {
        Assert.Equal((ushort)1799, LaserOutputStream.ResolveBufferCapacity(0));
        Assert.Equal((ushort)1799, LaserOutputStream.ResolveBufferCapacity(0, 0));
    }

    [Fact]
    public void AdvertisedCapacityWins()
    {
        Assert.Equal((ushort)2048, LaserOutputStream.ResolveBufferCapacity(2048));
    }

    [Fact]
    public void ObservedFullnessAboveTheAssumptionRaisesIt()
    {
        // The values a never-discovered, larger-buffer DAC reports: 2049..2055
        Assert.Equal((ushort)2055, LaserOutputStream.ResolveBufferCapacity(0, 2055));
        Assert.Equal((ushort)2055, LaserOutputStream.ResolveBufferCapacity(1799, 2055));
        Assert.Equal((ushort)2055, LaserOutputStream.ResolveBufferCapacity(2048, 2055));
    }

    [Fact]
    public void LowFillLevelNeverShrinksTheCapacity()
    {
        Assert.Equal((ushort)1799, LaserOutputStream.ResolveBufferCapacity(1799, 300));
        Assert.Equal((ushort)2048, LaserOutputStream.ResolveBufferCapacity(2048, 300));
    }
}
