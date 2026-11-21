#nullable enable
using System.Numerics;
using T3.Core.DataTypes.Volume;
using Xunit;

namespace Core.Tests.Volume;

/// <summary>OpenVDB and NanoVDB index every node with x as the slowest and z as the fastest axis; .vdb and .nvdb files depend on it.</summary>
public class VdbIndexOrderTests
{
    [Fact]
    public void LeafOffsetHasZFastest()
    {
        Assert.Equal(1, new VdbCoord(0, 0, 1).LeafOffset());
        Assert.Equal(8, new VdbCoord(0, 1, 0).LeafOffset());
        Assert.Equal(64, new VdbCoord(1, 0, 0).LeafOffset());
        Assert.Equal((5 << 6) | (3 << 3) | 6, new VdbCoord(5, 3, 6).LeafOffset());
    }

    [Fact]
    public void LocalFromOffsetInvertsLeafOffset()
    {
        for (var offset = 0; offset < 512; offset++)
        {
            var local = VdbCoord.LocalFromOffset(offset);
            Assert.Equal(offset, local.LeafOffset());
        }
    }

    [Fact]
    public void InternalNodeSlotsHaveZFastest()
    {
        Assert.Equal(1, VdbInternalNode1<float>.LocalSlot(new VdbCoord(0, 0, 8)));
        Assert.Equal(16, VdbInternalNode1<float>.LocalSlot(new VdbCoord(0, 8, 0)));
        Assert.Equal(256, VdbInternalNode1<float>.LocalSlot(new VdbCoord(8, 0, 0)));

        Assert.Equal(1, VdbInternalNode2<float>.LocalSlot(new VdbCoord(0, 0, 128)));
        Assert.Equal(32, VdbInternalNode2<float>.LocalSlot(new VdbCoord(0, 128, 0)));
        Assert.Equal(1024, VdbInternalNode2<float>.LocalSlot(new VdbCoord(128, 0, 0)));
    }

    [Fact]
    public void ChildOriginInvertsSlot()
    {
        var upper = new VdbInternalNode2<float>(new VdbCoord(4096, -4096, 0));
        foreach (var local in new[] { new VdbCoord(0, 0, 0), new VdbCoord(128, 256, 384), new VdbCoord(3968, 0, 3840) })
        {
            var slot = VdbInternalNode2<float>.LocalSlot(local);
            Assert.Equal(upper.Origin + local, upper.ChildOrigin(slot));
        }

        var lower = new VdbInternalNode1<float>(new VdbCoord(-128, 0, 256));
        foreach (var local in new[] { new VdbCoord(0, 0, 0), new VdbCoord(8, 16, 24), new VdbCoord(120, 0, 112) })
        {
            var slot = VdbInternalNode1<float>.LocalSlot(local);
            Assert.Equal(lower.Origin + local, lower.LeafOrigin(slot));
        }
    }
}
