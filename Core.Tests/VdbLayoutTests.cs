using System.IO;
using T3.Core.DataTypes.Volume;
using T3.Core.DataTypes.Volume.IO;
using Xunit;

namespace Core.Tests;

/// <summary>
/// Pins the voxel/child ordering of the sparse tree to OpenVDB's on-disk layout. OpenVDB indexes
/// both leaf voxels and internal-node child slots with X as the slowest-moving axis and Z as the
/// fastest (LeafNode::coordToOffset, InternalNode::coordToOffset), and .vdb value masks and buffers
/// are stored in that bit order. A reader that assumes the opposite order still parses every file
/// without error — it just transposes the model, which for grids that straddle a root-node boundary
/// turns into offsets of thousands of voxels. Round-trip tests alone cannot catch that, hence the
/// explicit layout assertions.
/// </summary>
public class VdbLayoutTests
{
    [Theory]
    [InlineData(1, 0, 0, 64)]
    [InlineData(0, 1, 0, 8)]
    [InlineData(0, 0, 1, 1)]
    [InlineData(2, 3, 4, 2 * 64 + 3 * 8 + 4)]
    [InlineData(7, 7, 7, 511)]
    public void LeafOffsetsFollowOpenVdbOrder(int x, int y, int z, int expected)
    {
        Assert.Equal(expected, VdbCoord.LeafOffset(x, y, z));

        var coord = new VdbCoord(x, y, z);
        Assert.Equal(expected, coord.LeafOffset());
        Assert.Equal(coord, VdbCoord.LocalFromOffset(expected));
    }

    [Fact]
    public void EveryLeafOffsetMapsBackToItsCoordinate()
    {
        for (var offset = 0; offset < VdbLeafNode<float>.Size; offset++)
        {
            var coord = VdbCoord.LocalFromOffset(offset);
            Assert.Equal(offset, coord.LeafOffset());
        }
    }

    [Fact]
    public void InternalNodeChildSlotsFollowOpenVdbOrder()
    {
        // Level-1 nodes: 16³ slots, slot = x*256 + y*16 + z.
        var leafSlotX = VdbInternalNode1<float>.LocalSlot(new VdbCoord(8, 0, 0));
        var leafSlotY = VdbInternalNode1<float>.LocalSlot(new VdbCoord(0, 8, 0));
        var leafSlotZ = VdbInternalNode1<float>.LocalSlot(new VdbCoord(0, 0, 8));
        Assert.Equal(256, leafSlotX);
        Assert.Equal(16, leafSlotY);
        Assert.Equal(1, leafSlotZ);

        var node1 = new VdbInternalNode1<float>(new VdbCoord(-16, 32, 0));
        Assert.Equal(new VdbCoord(-8, 32, 0), node1.LeafOrigin(leafSlotX));
        Assert.Equal(new VdbCoord(-16, 40, 0), node1.LeafOrigin(leafSlotY));
        Assert.Equal(new VdbCoord(-16, 32, 8), node1.LeafOrigin(leafSlotZ));

        // Level-2 nodes: 32³ slots, slot = x*1024 + y*32 + z.
        var nodeSlotX = VdbInternalNode2<float>.LocalSlot(new VdbCoord(128, 0, 0));
        var nodeSlotY = VdbInternalNode2<float>.LocalSlot(new VdbCoord(0, 256, 0));
        var nodeSlotZ = VdbInternalNode2<float>.LocalSlot(new VdbCoord(0, 0, 384));
        Assert.Equal(1024, nodeSlotX);
        Assert.Equal(64, nodeSlotY);   // 256 voxels = 2 × 128-voxel child spans
        Assert.Equal(3, nodeSlotZ);

        var node2 = new VdbInternalNode2<float>(new VdbCoord(-4096, 0, 4096));
        Assert.Equal(new VdbCoord(-3968, 0, 4096), node2.ChildOrigin(nodeSlotX));
        Assert.Equal(new VdbCoord(-4096, 256, 4096), node2.ChildOrigin(nodeSlotY));
        Assert.Equal(new VdbCoord(-4096, 0, 4480), node2.ChildOrigin(nodeSlotZ));
    }

    [Fact]
    public void SavedGridReloadsValuesAndMasksAtAsymmetricCoordinates()
    {
        var grid = new VdbGrid<float>(0.5f, 0.05f, "density", VdbGridClass.LevelSet)
        {
            Transform = new VdbTransform(0.05f, new System.Numerics.Vector3(1, 2, 3)),
        };

        // Coordinates chosen so that every local axis and every tree level has a distinct value;
        // a transposed layout would swap x and z and fail the assertions below.
        var samples = new (VdbCoord Coord, float Value)[]
        {
            (new VdbCoord(1, 0, 0), -0.11f),
            (new VdbCoord(0, 1, 0), -0.12f),
            (new VdbCoord(0, 0, 1), -0.13f),
            (new VdbCoord(7, 3, 5), -0.14f),
            (new VdbCoord(-9, 12, 33), -0.15f),
            (new VdbCoord(129, 1, 4), -0.16f),
        };

        foreach (var (coord, value) in samples)
            grid.Tree.SetValueOn(coord, value);

        using var stream = new MemoryStream();
        VdbFile.Save(new VdbVolume(grid), stream);
        stream.Position = 0;
        var loaded = VdbFile.Load(stream).GetFloatGrid();

        Assert.NotNull(loaded);
        Assert.Equal(grid.Background, loaded!.Background);
        Assert.Equal(grid.Transform.VoxelSize, loaded.Transform.VoxelSize);
        Assert.Equal(grid.Transform.Translation, loaded.Transform.Translation);

        foreach (var (coord, value) in samples)
        {
            Assert.True(loaded.Tree.IsValueOn(coord), $"voxel {coord} lost its active state");
            Assert.Equal(value, loaded.Tree.GetValue(coord));
        }

        // Neighborhood values were never written and must read as background.
        Assert.False(loaded.Tree.IsValueOn(new VdbCoord(2, 1, 1)));
        Assert.Equal(grid.Background, loaded.Tree.GetValue(new VdbCoord(2, 1, 1)));

        var leaf = loaded.Tree.ProbeLeaf(new VdbCoord(7, 3, 5));
        Assert.NotNull(leaf);
        Assert.Equal(new VdbCoord(0, 0, 0), leaf!.Origin);
        Assert.True(leaf.ValueMask.IsOn(VdbCoord.LeafOffset(7, 3, 5)));
    }
}
