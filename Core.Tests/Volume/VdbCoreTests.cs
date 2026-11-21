#nullable enable
using System;
using System.Collections.Generic;
using System.Numerics;
using T3.Core.DataTypes.Volume;
using Xunit;

namespace Core.Tests.Volume;

/// <summary>Sphere mesh generator shared by the volume tests: UV sphere as triangle soup.</summary>
public static class TestMeshes
{
    public static (Vector3[] Positions, int[] Triangles) UvSphere(float radius, int segments = 24, int rings = 12, Vector3? center = null)
    {
        var c = center ?? Vector3.Zero;
        var positions = new List<Vector3>();

        positions.Add(c + Vector3.UnitY * radius); // top
        for (var r = 1; r < rings; r++)
        {
            var phi = MathF.PI * r / rings;
            for (var s = 0; s < segments; s++)
            {
                var theta = MathF.PI * 2 * s / segments;
                positions.Add(c + new Vector3(
                                          MathF.Sin(phi) * MathF.Cos(theta),
                                          MathF.Cos(phi),
                                          MathF.Sin(phi) * MathF.Sin(theta)) * radius);
            }
        }

        positions.Add(c - Vector3.UnitY * radius); // bottom

        var triangles = new List<int>();

        void AddTri(int a, int b, int cc)
        {
            // Outward winding (counter-clockwise seen from outside) - the pseudo-normal
            // sign computation in MeshToVolume requires a consistently oriented surface.
            triangles.Add(a);
            triangles.Add(cc);
            triangles.Add(b);
        }

        // Top cap
        for (var s = 0; s < segments; s++)
        {
            AddTri(0, 1 + s, 1 + (s + 1) % segments);
        }

        // Body rings
        for (var r = 0; r < rings - 2; r++)
        {
            var rowStart = 1 + r * segments;
            var nextStart = rowStart + segments;
            for (var s = 0; s < segments; s++)
            {
                var sNext = (s + 1) % segments;
                AddTri(rowStart + s, nextStart + s, nextStart + sNext);
                AddTri(rowStart + s, nextStart + sNext, rowStart + sNext);
            }
        }

        // Bottom cap
        var lastRowStart = 1 + (rings - 2) * segments;
        var bottom = positions.Count - 1;
        for (var s = 0; s < segments; s++)
        {
            AddTri(bottom, lastRowStart + (s + 1) % segments, lastRowStart + s);
        }

        return (positions.ToArray(), triangles.ToArray());
    }
}

public class VdbCoordTests
{
    [Fact]
    public void FloorDivHandlesNegatives()
    {
        Assert.Equal(-1, VdbCoord.FloorDiv(-1, 8));
        Assert.Equal(-1, VdbCoord.FloorDiv(-8, 8));
        Assert.Equal(-2, VdbCoord.FloorDiv(-9, 8));
        Assert.Equal(0, VdbCoord.FloorDiv(7, 8));
        Assert.Equal(1, VdbCoord.FloorDiv(8, 8));
    }

    [Fact]
    public void LeafOffsetIsBijective()
    {
        var seen = new HashSet<int>();
        for (var z = 0; z < 8; z++)
        {
            for (var y = 0; y < 8; y++)
            {
                for (var x = 0; x < 8; x++)
                {
                    var offset = VdbCoord.LeafOffset(x, y, z);
                    Assert.True(seen.Add(offset), $"duplicate offset {offset}");
                    var local = VdbCoord.LocalFromOffset(offset);
                    Assert.Equal(new VdbCoord(x, y, z), local);
                }
            }
        }

        Assert.Equal(512, seen.Count);
    }

    [Fact]
    public void RoundDownLeafSnapsNegativeCoords()
    {
        Assert.Equal(new VdbCoord(0, 0, 0), new VdbCoord(7, 7, 7).RoundDownLeaf());
        Assert.Equal(new VdbCoord(-8, 0, -8), new VdbCoord(-1, 7, -8).RoundDownLeaf());
    }
}

public class NodeMaskTests
{
    [Fact]
    public void SetAndCountBits()
    {
        var mask = new NodeMask(8);
        Assert.Equal(0, mask.CountOn());
        mask.SetOn(0);
        mask.SetOn(511);
        mask.SetOn(123);
        Assert.Equal(3, mask.CountOn());
        Assert.True(mask.IsOn(0));
        Assert.True(mask.IsOn(511));
        Assert.False(mask.IsOn(510));
        Assert.Equal(509, mask.CountOff());
    }

    [Fact]
    public void OnBitsAreAscending()
    {
        var mask = new NodeMask(2);
        mask.SetOn(70);
        mask.SetOn(3);
        mask.SetOn(64);
        var bits = new List<int>(mask.OnBits());
        Assert.Equal(new[] { 3, 64, 70 }, bits);
    }

    [Fact]
    public void SaveLoadRoundTrips()
    {
        var mask = new NodeMask(8);
        mask.SetOn(17);
        mask.SetOn(256);

        using var ms = new System.IO.MemoryStream();
        mask.Save(ms);
        Assert.Equal(64, ms.Length);

        ms.Position = 0;
        var loaded = NodeMask.Load(ms, 8);
        Assert.Equal(mask.CountOn(), loaded.CountOn());
        Assert.True(loaded.IsOn(17));
        Assert.True(loaded.IsOn(256));
    }
}

public class VdbTreeTests
{
    [Fact]
    public void SetValueThenReadAcrossLeafBorders()
    {
        var tree = new VdbTree<float>(1.0f);
        tree.SetValueOn(new VdbCoord(0, 0, 0), 5f);
        tree.SetValueOn(new VdbCoord(-1, -1, -1), -3f);
        tree.SetValueOn(new VdbCoord(7, 7, 7), 7f);
        tree.SetValueOn(new VdbCoord(8, 0, 0), 9f); // next leaf over
        tree.SetValueOn(new VdbCoord(-1000, 999, 1234), 2.5f);

        Assert.Equal(5f, tree.GetValue(new VdbCoord(0, 0, 0)));
        Assert.Equal(-3f, tree.GetValue(new VdbCoord(-1, -1, -1)));
        Assert.Equal(7f, tree.GetValue(new VdbCoord(7, 7, 7)));
        Assert.Equal(9f, tree.GetValue(new VdbCoord(8, 0, 0)));
        Assert.Equal(2.5f, tree.GetValue(new VdbCoord(-1000, 999, 1234)));
        Assert.Equal(1.0f, tree.GetValue(new VdbCoord(5000, 5000, 5000))); // background
        Assert.False(tree.IsValueOn(new VdbCoord(5000, 5000, 5000)));
    }

    [Fact]
    public void AddLeafInheritsTileState()
    {
        var tree = new VdbTree<float>(2f);
        tree.Root.Tiles[new VdbCoord(0, 0, 0)] = new VdbRootTile<float> { Value = -4f, Active = true };

        Assert.True(tree.IsValueOn(new VdbCoord(10, 10, 10))); // inside the root tile
        Assert.Equal(-4f, tree.GetValue(new VdbCoord(10, 10, 10)));

        // Writing into the tile densifies it: the new leaf keeps the tile's value/state.
        tree.SetValueOn(new VdbCoord(12, 3, 7), -1f);
        Assert.Equal(-1f, tree.GetValue(new VdbCoord(12, 3, 7)));
        Assert.Equal(-4f, tree.GetValue(new VdbCoord(3, 3, 3)));
        Assert.True(tree.IsValueOn(new VdbCoord(3, 3, 3)));
    }

    [Fact]
    public void ActiveVoxelCountAndLeaves()
    {
        var tree = new VdbTree<float>(0f);
        for (var x = 0; x < 20; x++)
        {
            for (var y = 0; y < 20; y++)
            {
                for (var z = 0; z < 20; z++)
                {
                    tree.SetValueOn(new VdbCoord(x, y, z), 1f);
                }
            }
        }

        Assert.Equal(8000, tree.ActiveVoxelCount);
        Assert.Equal(27, tree.LeafCount); // 20 voxels span 3 leaves per axis

        var bbox = tree.ActiveIndexBBox();
        Assert.Equal(new VdbCoord(0, 0, 0), bbox.Min);
        Assert.Equal(new VdbCoord(19, 19, 19), bbox.Max);
    }

    [Fact]
    public void PruneRemovesEmptyLeaves()
    {
        var tree = new VdbTree<float>(1f);
        tree.SetValueOn(new VdbCoord(0, 0, 0), 1f);
        tree.SetValueOff(new VdbCoord(0, 0, 0));
        Assert.Equal(1, tree.LeafCount);

        tree.Prune();
        Assert.Equal(0, tree.LeafCount);
        Assert.Equal(1f, tree.GetValue(new VdbCoord(0, 0, 0))); // reads background again
    }

    [Fact]
    public void CloneGridCopiesValues()
    {
        var grid = new VdbGrid<float>(1f, 0.25f, "test", VdbGridClass.LevelSet);
        grid.Tree.SetValueOn(new VdbCoord(3, 1, 9), -0.5f);

        var clone = (VdbGrid<float>)grid.CloneGrid();
        Assert.Equal(-0.5f, clone.Tree.GetValue(new VdbCoord(3, 1, 9)));
        Assert.Equal(0.25f, clone.Transform.VoxelSize);

        clone.Tree.SetValueOn(new VdbCoord(3, 1, 9), 42f);
        Assert.Equal(-0.5f, grid.Tree.GetValue(new VdbCoord(3, 1, 9))); // original untouched
    }

    [Fact]
    public void CloneGridKeepsTilesCollapsedByPrune()
    {
        var grid = new VdbGrid<float>(0f, 0.1f);
        var leaf = grid.Tree.AddLeaf(new VdbCoord(0, 0, 0));
        leaf.Fill(2f);
        grid.Tree.Prune();

        // The uniform leaf is now an active 8^3 tile in its parent, not a leaf.
        Assert.Equal(0, grid.Tree.LeafCount);
        Assert.Equal(2f, grid.Tree.GetValue(new VdbCoord(3, 3, 3)));

        var clone = (VdbGrid<float>)grid.CloneGrid();
        Assert.Equal(2f, clone.Tree.GetValue(new VdbCoord(3, 3, 3)));
        Assert.True(clone.Tree.IsValueOn(new VdbCoord(3, 3, 3)));
        Assert.Equal(grid.Tree.ActiveVoxelCount, clone.Tree.ActiveVoxelCount);
    }
}
