#nullable enable
using System;
using System.Collections.Generic;

namespace T3.Core.DataTypes.Volume;

/// <summary>
/// A typed sparse tree: root → level-2 nodes (32³ level-1 slots) → level-1 nodes (16³ leaf slots)
/// → 8³ leaves, matching OpenVDB's default Tree4&lt;5,4,3&gt; configuration and its 2^36 index space.
/// Provides the accessor primitives tools are built on: probe/add leaf, value get/set with
/// active-state, leaf iteration and pruning.
/// </summary>
public sealed class VdbTree<T>
{
    public readonly VdbRootNode<T> Root;

    public VdbTree(T background)
    {
        Root = new VdbRootNode<T>(background);
    }

    public T Background => Root.Background;

    private static VdbCoord RoundDown(VdbCoord c, int n)
        => new(VdbCoord.FloorDiv(c.X, n) * n, VdbCoord.FloorDiv(c.Y, n) * n, VdbCoord.FloorDiv(c.Z, n) * n);

    /// <summary>Returns the leaf containing <paramref name="coord"/>, or null when that region holds tiles or nothing.</summary>
    public VdbLeafNode<T>? ProbeLeaf(VdbCoord coord)
    {
        var rootOrigin = RoundDown(coord, VdbInternalNode2<T>.TotalDim);
        if (!Root.Children.TryGetValue(rootOrigin, out var node2))
            return null;

        var local = coord - node2.Origin;
        var node1 = node2.Children[VdbInternalNode2<T>.LocalSlot(local)];
        if (node1 == null)
            return null;

        return node1.Children[VdbInternalNode1<T>.LocalSlot(local)];
    }

    /// <summary>
    /// Returns the leaf containing <paramref name="coord"/>, allocating nodes along the way and
    /// splitting any tile that covers the coordinate into child nodes.
    /// </summary>
    public VdbLeafNode<T> AddLeaf(VdbCoord coord)
    {
        var rootOrigin = RoundDown(coord, VdbInternalNode2<T>.TotalDim);
        if (!Root.Children.TryGetValue(rootOrigin, out var node2))
        {
            node2 = new VdbInternalNode2<T>(rootOrigin);
            Root.Children[rootOrigin] = node2;
        }

        // A root tile covering this region is superseded by the child node: transfer its state
        // onto the node's whole slot table, then drop the tile.
        if (Root.Tiles.Remove(rootOrigin, out var rootTile))
        {
            node2.Values.AsSpan().Fill(rootTile.Value);
            if (rootTile.Active)
                node2.ValueMask.SetAll();
        }

        var slot2 = VdbInternalNode2<T>.LocalSlot(coord - node2.Origin);
        var node1 = node2.Children[slot2];

        if (node1 == null)
        {
            // The slot currently holds an N2 tile (or nothing): capture its state, then transfer
            // it onto the fresh level-1 node's 8³-tile slot table.
            var tile2Value = node2.Values[slot2];
            var tile2Active = node2.ValueMask.IsOn(slot2);
            node1 = node2.AddChild(coord);
            node1.Values.AsSpan().Fill(tile2Value);
            if (tile2Active)
                node1.ValueMask.SetAll();
            node2.ValueMask.SetOff(slot2);
            node2.Values[slot2] = default!;
        }

        var slot1 = VdbInternalNode1<T>.LocalSlot(coord - node1.Origin);
        var hadLeaf = node1.ChildMask.IsOn(slot1);
        var leaf = node1.AddLeaf(coord);
        if (!hadLeaf)
        {
            // A new leaf replaces an 8³ tile: inherit that tile's value and active state.
            var tile1Value = node1.Values[slot1];
            var tile1Active = node1.ValueMask.IsOn(slot1);
            if (tile1Active)
                leaf.Fill(tile1Value);
            else
                leaf.Deactivate(Root.Background); // inactive tiles read as background; stored tile values are ignored
            node1.ValueMask.SetOff(slot1);
            node1.Values[slot1] = default!;
        }

        return leaf;
    }

    /// <summary>Value at a coordinate; inactive or unallocated regions read as their tile/background value.</summary>
    public T GetValue(VdbCoord coord)
    {
        var rootOrigin = RoundDown(coord, VdbInternalNode2<T>.TotalDim);
        if (Root.Tiles.TryGetValue(rootOrigin, out var rootTile))
            return rootTile.Value;

        if (!Root.Children.TryGetValue(rootOrigin, out var node2))
            return Root.Background;

        var slot2 = VdbInternalNode2<T>.LocalSlot(coord - node2.Origin);
        var node1 = node2.Children[slot2];
        if (node1 == null)
            return node2.ValueMask.IsOn(slot2) ? node2.Values[slot2] : Root.Background;

        var slot1 = VdbInternalNode1<T>.LocalSlot(coord - node1.Origin);
        if (node1.ChildMask.IsOn(slot1))
            return node1.Children[slot1]!.GetValue(coord);

        return node1.ValueMask.IsOn(slot1) ? node1.Values[slot1] : Root.Background;
    }

    public bool IsValueOn(VdbCoord coord)
    {
        var rootOrigin = RoundDown(coord, VdbInternalNode2<T>.TotalDim);
        if (Root.Tiles.TryGetValue(rootOrigin, out var rootTile))
            return rootTile.Active;

        if (!Root.Children.TryGetValue(rootOrigin, out var node2))
            return false;

        var slot2 = VdbInternalNode2<T>.LocalSlot(coord - node2.Origin);
        var node1 = node2.Children[slot2];
        if (node1 == null)
            return node2.ValueMask.IsOn(slot2);

        var slot1 = VdbInternalNode1<T>.LocalSlot(coord - node1.Origin);
        if (node1.ChildMask.IsOn(slot1))
            return node1.Children[slot1]!.IsValueOn(coord);

        return node1.ValueMask.IsOn(slot1);
    }

    public void SetValueOn(VdbCoord coord, T value)
    {
        var leaf = AddLeaf(coord);
        leaf.SetValueOn(coord, value);
    }

    public void SetValueOnly(VdbCoord coord, T value)
    {
        var leaf = AddLeaf(coord);
        leaf.SetValueOnly(coord, value);
    }

    public void SetValueOff(VdbCoord coord)
    {
        if (ProbeLeaf(coord) is { } leaf)
            leaf.SetValueOff(coord);
    }

    public IEnumerable<VdbLeafNode<T>> Leaves
    {
        get
        {
            foreach (var node2 in Root.Children.Values)
            {
                foreach (var node1 in node2.Children)
                {
                    if (node1 == null)
                        continue;

                    foreach (var leaf in node1.Children)
                    {
                        if (leaf != null)
                            yield return leaf;
                    }
                }
            }
        }
    }

    public int LeafCount
    {
        get
        {
            var count = 0;
            foreach (var node2 in Root.Children.Values)
            {
                foreach (var node1 in node2.Children)
                {
                    if (node1 != null)
                        count += node1.ChildMask.CountOn();
                }
            }

            return count;
        }
    }

    public long ActiveVoxelCount
    {
        get
        {
            long count = 0;
            foreach (var tile in Root.Tiles.Values)
            {
                if (tile.Active)
                    count += Cubed(VdbInternalNode2<T>.TotalDim);
            }

            foreach (var node2 in Root.Children.Values)
            {
                count += ActiveTileVoxels(node2.ValueMask, node2.ChildMask, VdbInternalNode1<T>.TotalDim);
                foreach (var node1 in node2.Children)
                {
                    if (node1 == null)
                        continue;

                    count += ActiveTileVoxels(node1.ValueMask, node1.ChildMask, VdbLeafNode<T>.Size);
                    foreach (var leaf in node1.Children)
                    {
                        if (leaf != null)
                            count += leaf.OnVoxelCount;
                    }
                }
            }

            return count;

            static long Cubed(int d) => (long)d * d * d;

            static long ActiveTileVoxels(NodeMask valueMask, NodeMask childMask, int voxelsPerSlot)
            {
                long sum = 0;
                foreach (var slot in valueMask.OnBits())
                {
                    if (!childMask.IsOn(slot))
                        sum += voxelsPerSlot;
                }

                return sum;
            }
        }
    }

    /// <summary>Integer bounds covering all allocated leaves.</summary>
    public VdbCoordBBox IndexBBox()
    {
        var bbox = VdbCoordBBox.Empty;
        foreach (var leaf in Leaves)
            bbox.Expand(leaf.BBox);
        foreach (var coord in Root.Tiles.Keys)
        {
            var extent = VdbInternalNode2<T>.TotalDim;
            bbox.Expand(new VdbCoordBBox(coord, coord + new VdbCoord(extent - 1)));
        }

        return bbox;
    }

    /// <summary>Integer bounds of active values (voxels and tiles), OpenVDB's evalActiveVoxelBoundingBox.</summary>
    public VdbCoordBBox ActiveIndexBBox()
    {
        var bbox = VdbCoordBBox.Empty;
        foreach (var leaf in Leaves)
        {
            foreach (var offset in leaf.ValueMask.OnBits())
                bbox.Expand(leaf.Origin + VdbCoord.LocalFromOffset(offset));
        }

        foreach (var (coord, tile) in Root.Tiles)
        {
            if (!tile.Active)
                continue;
            var extent = VdbInternalNode2<T>.TotalDim;
            bbox.Expand(new VdbCoordBBox(coord, coord + new VdbCoord(extent - 1)));
        }

        foreach (var node2 in Root.Children.Values)
        {
            ExpandActiveTileBBox(bbox, node2.Origin, node2.ValueMask, node2.ChildMask, VdbInternalNode1<T>.TotalDim, bits: 5);
            foreach (var node1 in node2.Children)
            {
                if (node1 == null)
                    continue;
                ExpandActiveTileBBox(bbox, node1.Origin, node1.ValueMask, node1.ChildMask, VdbLeafNode<T>.Dim, bits: 4);
            }
        }

        return bbox;
    }

    private static void ExpandActiveTileBBox(VdbCoordBBox bbox, VdbCoord origin, NodeMask valueMask, NodeMask childMask, int span, int bits)
    {
        var mask = (1 << bits) - 1;
        foreach (var slot in valueMask.OnBits())
        {
            if (childMask.IsOn(slot))
                continue;
            var lx = slot >> (2 * bits);
            var ly = (slot >> bits) & mask;
            var lz = slot & mask;
            bbox.Expand(new VdbCoordBBox(origin + new VdbCoord(lx * span, ly * span, lz * span),
                                         origin + new VdbCoord((lx + 1) * span - 1, (ly + 1) * span - 1, (lz + 1) * span - 1)));
        }
    }

    /// <summary>
    /// Drops fully-inactive leaves and collapses uniform leaves into parent tiles, then removes
    /// childless level-1/level-2 nodes. This is the standard post-CSG / post-filter cleanup.
    /// </summary>
    public void Prune(float tolerance = 0f)
    {
        foreach (var node2 in Root.Children.Values)
        {
            for (var slot2 = 0; slot2 < node2.Children.Length; slot2++)
            {
                var node1 = node2.Children[slot2];
                if (node1 == null)
                    continue;

                for (var slot1 = 0; slot1 < node1.Children.Length; slot1++)
                {
                    var leaf = node1.Children[slot1];
                    if (leaf == null)
                        continue;

                    if (leaf.OnVoxelCount == 0)
                    {
                        // Only collapse when the values really are the background. A leaf that
                        // holds non-background values without an active mask - the interior fill
                        // of a level set, stored as inactive -background - must survive, or the
                        // solid's interior would read as +background (outside) after pruning.
                        if (!TryCollapseValues(leaf.Values, tolerance, out var only)
                            || !EqualityComparer<T>.Default.Equals(only, Root.Background))
                        {
                            continue;
                        }

                        CollapseLeaf(node1, slot1, leaf, Root.Background, false, tolerance);
                    }
                    else if (leaf.ValueMask.IsFull() && TryCollapseValues(leaf.Values, tolerance, out var uniform))
                    {
                        CollapseLeaf(node1, slot1, leaf, uniform, true, tolerance);
                    }
                }

                // A level-1 node without leaves can go away entirely only when it carries no
                // active 8³ tiles either (those can't be represented at level-2 granularity).
                if (node1.ChildMask.IsEmpty() && node1.ValueMask.IsEmpty())
                {
                    node2.Children[slot2] = null;
                    node2.ChildMask.SetOff(slot2);
                    node2.Values[slot2] = default!;
                }
            }
        }

        List<VdbCoord>? stale = null;
        foreach (var (coord, node2) in Root.Children)
        {
            if (node2.ChildMask.IsEmpty() && node2.ValueMask.IsEmpty())
                (stale ??= new List<VdbCoord>()).Add(coord);
        }

        if (stale != null)
        {
            foreach (var coord in stale)
                Root.Children.Remove(coord);
        }
    }

    private static void CollapseLeaf(VdbInternalNode1<T> node1, int slot1, VdbLeafNode<T> leaf, T value, bool active, float tolerance)
    {
        // Only collapse into a tile when the value is uniform (within tolerance) - otherwise keep
        // the leaf as allocated data with its mask cleared/enabled.
        var uniform = active ? TryCollapseValues(leaf.Values, tolerance, out var v) : true;
        if (!uniform)
            return;

        node1.Children[slot1] = null;
        node1.ChildMask.SetOff(slot1);
        node1.Values[slot1] = value;
        node1.ValueMask.Set(slot1, active);
    }

    private static bool TryCollapseValues(T[] values, float tolerance, out T uniform)
    {
        uniform = values[0];
        if (typeof(T) == typeof(float))
        {
            if (tolerance <= 0f)
            {
                var first = (float)(object)uniform!;
                foreach (var v in values)
                {
                    if ((float)(object)v! != first)
                        return false;
                }
            }
            else
            {
                var first = (float)(object)uniform!;
                foreach (var v in values)
                {
                    if (MathF.Abs((float)(object)v! - first) > tolerance)
                        return false;
                }
            }
        }
        else
        {
            foreach (var v in values)
            {
                if (!EqualityComparer<T>.Default.Equals(v, uniform))
                    return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Applies a function to every stored value: root tiles, active internal tiles and all leaf
    /// voxels (active and inactive). Pruning collapses uniform leaf regions into internal tiles,
    /// so walking only <see cref="Leaves"/> would leave those regions unmodified.
    /// </summary>
    public void TransformValues(Func<T, T> transform)
    {
        foreach (var tile in Root.Tiles.Values)
            tile.Value = transform(tile.Value);

        foreach (var node2 in Root.Children.Values)
        {
            foreach (var slot in node2.ValueMask.OnBits())
            {
                if (!node2.ChildMask.IsOn(slot))
                    node2.Values[slot] = transform(node2.Values[slot]);
            }

            foreach (var node1 in node2.Children)
            {
                if (node1 == null)
                    continue;

                foreach (var slot in node1.ValueMask.OnBits())
                {
                    if (!node1.ChildMask.IsOn(slot))
                        node1.Values[slot] = transform(node1.Values[slot]);
                }

                foreach (var leaf in node1.Children)
                {
                    if (leaf == null)
                        continue;

                    for (var i = 0; i < VdbLeafNode<T>.Size; i++)
                        leaf.Values[i] = transform(leaf.Values[i]);
                }
            }
        }
    }

    /// <summary>Total leaf voxels allocated (active + inactive), for memory diagnostics.</summary>
    public long AllocatedVoxelCount
    {
        get
        {
            long count = 0;
            foreach (var _ in Leaves)
                count += VdbLeafNode<T>.Size;
            return count;
        }
    }
}
