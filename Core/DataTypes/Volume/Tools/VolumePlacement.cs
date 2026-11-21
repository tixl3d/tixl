#nullable enable
using System;
using System.Numerics;

namespace T3.Core.DataTypes.Volume.Tools;

/// <summary>
/// Moves, scales and re-orients whole grids without resampling, for bringing files into TiXL's space: a Z-up cache
/// from Blender or EmberGen becomes Y-up, and a simulation in centimetres or voxel units becomes a scene-sized object.
/// </summary>
public static class VolumePlacement
{
    /// <summary>
    /// A copy of <paramref name="grid"/> scaled by <paramref name="scale"/>, optionally turned from Z-up to Y-up, then moved
    /// by <paramref name="translation"/>. Level-set values are distances, so they scale with the grid.
    /// </summary>
    public static VdbGrid<float> Apply(VdbGrid<float> grid, float scale, bool zUpToYUp, Vector3 translation)
    {
        var scalesValues = grid.GridClass == VdbGridClass.LevelSet;
        return Apply(grid, scale, zUpToYUp, translation, value => scalesValues ? value * scale : value);
    }

    /// <summary>Vectors (a velocity, say) turn with the grid and scale with it.</summary>
    public static VdbGrid<Vector3> Apply(VdbGrid<Vector3> grid, float scale, bool zUpToYUp, Vector3 translation)
    {
        return Apply(grid, scale, zUpToYUp, translation, value => (zUpToYUp ? ZUpToYUp(value) : value) * scale);
    }

    /// <summary>The Y-up direction of a Z-up one: Z becomes up, Y becomes towards the viewer.</summary>
    public static Vector3 ZUpToYUp(Vector3 value) => new(value.X, value.Z, -value.Y);

    private static VdbGrid<T> Apply<T>(VdbGrid<T> grid, float scale, bool zUpToYUp, Vector3 translation, Func<T, T> mapValue)
    {
        if (!(scale > 0) || !float.IsFinite(scale))
            throw new ArgumentOutOfRangeException(nameof(scale), scale, "the scale must be positive");

        var voxelSize = grid.Transform.VoxelSize;
        var origin = grid.Transform.Translation;

        if (!zUpToYUp)
        {
            var copy = (VdbGrid<T>)grid.CloneGrid();
            copy.Tree.Root.Background = mapValue(grid.Background);
            copy.Tree.TransformValues(mapValue);
            copy.Transform = new VdbTransform(voxelSize * scale, origin * scale + translation);
            return copy;
        }

        // Index (x, y, z) goes to (x, z, -y - 1). The extra -1 keeps every 8³ leaf on an 8³ leaf, so leaves are copied
        // whole; the translation takes the one voxel back: world z' = -(origin.y + y * voxelSize).
        var turnedOrigin = new Vector3(origin.X, origin.Z, -origin.Y + voxelSize);
        var result = new VdbGrid<T>(new VdbTree<T>(mapValue(grid.Background)),
                                    new VdbTransform(voxelSize * scale, turnedOrigin * scale + translation),
                                    grid.Name,
                                    grid.GridClass);

        foreach (var leaf in grid.Tree.Leaves)
        {
            var target = result.Tree.AddLeaf(TurnLeafOrigin(leaf.Origin));
            for (var offset = 0; offset < VdbLeafNode<T>.Size; offset++)
            {
                var local = VdbCoord.LocalFromOffset(offset);
                var turned = VdbCoord.LeafOffset(local.X, local.Z, 7 - local.Y);
                target.Values[turned] = mapValue(leaf.Values[offset]);
                if (leaf.ValueMask.IsOn(offset))
                    target.ValueMask.SetOn(turned);
            }
        }

        // Pruning stores uniform active regions as tiles above the leaves; they are written back out as leaves.
        foreach (var node2 in grid.Tree.Root.Children.Values)
        {
            foreach (var slot2 in node2.ValueMask.OnBits())
            {
                if (!node2.ChildMask.IsOn(slot2))
                    FillTurnedBlock(result.Tree, node2.Origin + SlotOffset(slot2, VdbInternalNode2<T>.Log2Dim, VdbInternalNode1<T>.TotalDim),
                                    VdbInternalNode1<T>.TotalDim, mapValue(node2.Values[slot2]));
            }

            foreach (var node1 in node2.Children)
            {
                if (node1 == null)
                    continue;

                foreach (var slot1 in node1.ValueMask.OnBits())
                {
                    if (!node1.ChildMask.IsOn(slot1))
                        FillTurnedBlock(result.Tree, node1.Origin + SlotOffset(slot1, VdbInternalNode1<T>.Log2Dim, VdbLeafNode<T>.Dim),
                                        VdbLeafNode<T>.Dim, mapValue(node1.Values[slot1]));
                }
            }
        }

        if (grid.Tree.Root.Tiles.Count > 0)
            throw new NotSupportedException("the grid has root-level tiles, which cannot be turned yet");

        result.Tree.Prune();
        return result;
    }

    private static VdbCoord TurnLeafOrigin(VdbCoord origin) => new(origin.X, origin.Z, -origin.Y - VdbLeafNode<float>.Dim);

    /// <summary>Writes an active cube of one value, whose source origin is <paramref name="origin"/>, as turned leaves.</summary>
    private static void FillTurnedBlock<T>(VdbTree<T> tree, VdbCoord origin, int size, T value)
    {
        const int dim = VdbLeafNode<float>.Dim;
        for (var x = 0; x < size; x += dim)
        {
            for (var y = 0; y < size; y += dim)
            {
                for (var z = 0; z < size; z += dim)
                {
                    var leaf = tree.AddLeaf(TurnLeafOrigin(origin + new VdbCoord(x, y, z)));
                    Array.Fill(leaf.Values, value);
                    leaf.ValueMask.SetAll();
                }
            }
        }
    }

    private static VdbCoord SlotOffset(int slot, int log2Dim, int childSize)
    {
        var mask = (1 << log2Dim) - 1;
        return new VdbCoord((slot >> (2 * log2Dim)) * childSize, ((slot >> log2Dim) & mask) * childSize, (slot & mask) * childSize);
    }
}
