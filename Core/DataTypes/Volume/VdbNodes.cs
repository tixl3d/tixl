#nullable enable
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace T3.Core.DataTypes.Volume;

/// <summary>
/// The bottom tree level: a dense 8³ value buffer with an active-state bitmask,
/// mirroring OpenVDB's LeafNode (Log2Dim = 3, 512 voxels, 64-byte mask).
/// Inactive voxels still carry values; consumers must check the mask for semantic state.
/// </summary>
public sealed class VdbLeafNode<T>
{
    public const int Log2Dim = 3;
    public const int Dim = 8;
    public const int Size = 512;
    public const int TotalDim = 8;

    public VdbCoord Origin;
    public readonly NodeMask ValueMask = new(8);
    public readonly T[] Values = new T[Size];

    public VdbLeafNode(VdbCoord origin) => Origin = origin;

    /// <summary>World-space center of the voxel containing <paramref name="coord"/>.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public T GetValue(VdbCoord coord) => Values[coord.LeafOffset()];

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SetValueOnly(VdbCoord coord, T value) => Values[coord.LeafOffset()] = value;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SetValueOn(VdbCoord coord, T value)
    {
        var offset = coord.LeafOffset();
        Values[offset] = value;
        ValueMask.SetOn(offset);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SetValueOff(VdbCoord coord) => ValueMask.SetOff(coord.LeafOffset());

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool IsValueOn(VdbCoord coord) => ValueMask.IsOn(coord.LeafOffset());

    public int OnVoxelCount => ValueMask.CountOn();

    /// <summary>Sets all voxels to the same active value.</summary>
    public void Fill(T value)
    {
        Array.Fill(Values, value);
        ValueMask.SetAll();
    }

    public void Deactivate(T value)
    {
        Array.Fill(Values, value);
        ValueMask.Clear();
    }

    public IEnumerable<(int Offset, T Value)> ActiveValues()
    {
        foreach (var offset in ValueMask.OnBits())
            yield return (offset, Values[offset]);
    }

    public VdbCoordBBox BBox => new(Origin, Origin + new VdbCoord(Dim - 1));

    /// <summary>Local 8³ access for stencil iteration; index = x + y*8 + z*64.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public T ValueAt(int offset) => Values[offset];

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool IsActiveAt(int offset) => ValueMask.IsOn(offset);
}

/// <summary>
/// First internal level: tiles and 8³ leaves on a 16³ lattice (Log2Dim = 4, 4096 child slots),
/// mirroring OpenVDB's InternalNode&lt;LeafNode, 4&gt;. Tile state per slot: child mask selects
/// an allocated leaf, value mask marks an active tile, everything else is an inactive tile
/// holding Values[slot].
/// </summary>
public sealed class VdbInternalNode1<T>
{
    public const int Log2Dim = 4;
    public const int Dim = 16;
    public const int Size = 4096;
    public const int ChildWordCount = 64;
    /// <summary>Voxel-space extent of this node (16 leaves × 8 voxels).</summary>
    public const int TotalDim = Dim * VdbLeafNode<T>.Dim;

    public VdbCoord Origin;
    public readonly NodeMask ChildMask = new(ChildWordCount);
    public readonly NodeMask ValueMask = new(ChildWordCount);
    public readonly T[] Values = new T[Size];
    public readonly VdbLeafNode<T>?[] Children = new VdbLeafNode<T>[Size];

    public VdbInternalNode1(VdbCoord origin) => Origin = origin;

    public int ChildCount => ChildMask.CountOn();
    public int ActiveTileCount
    {
        get
        {
            var count = 0;
            foreach (var slot in ValueMask.OnBits())
            {
                if (!ChildMask.IsOn(slot))
                    count++;
            }

            return count;
        }
    }

    public VdbLeafNode<T> AddLeaf(VdbCoord coord)
    {
        var local = coord - Origin;
        var leaf = Children[LocalSlot(local)];
        if (leaf == null)
        {
            var slot = LocalSlot(local);
            leaf = new VdbLeafNode<T>(coord.RoundDownLeaf());
            Children[slot] = leaf;
            ChildMask.SetOn(slot);
        }

        return leaf;
    }

    /// <summary>Slot of the leaf covering a local (voxel-space) offset.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int LocalSlot(VdbCoord local)
    {
        var lx = VdbCoord.FloorDiv(local.X, VdbLeafNode<T>.Dim) & 15;
        var ly = VdbCoord.FloorDiv(local.Y, VdbLeafNode<T>.Dim) & 15;
        var lz = VdbCoord.FloorDiv(local.Z, VdbLeafNode<T>.Dim) & 15;
        return (lx << 8) + (ly << 4) + lz;
    }

    public VdbCoordBBox BBox => new(Origin, Origin + new VdbCoord(TotalDim - 1));

    /// <summary>Origin of the leaf node covering local slot <paramref name="slot"/>.</summary>
    public VdbCoord LeafOrigin(int slot)
        => new(Origin.X + (slot >> 8) * 8, Origin.Y + ((slot >> 4) & 15) * 8, Origin.Z + (slot & 15) * 8);
}

/// <summary>
/// Second internal level: tiles and level-1 nodes on a 32³ lattice (Log2Dim = 5, 32768 slots),
/// mirroring OpenVDB's InternalNode&lt;InternalNode&lt;LeafNode,4&gt;, 5&gt;. Together with the root
/// this spans the same 2^36 index space as OpenVDB's default FloatTree.
/// </summary>
public sealed class VdbInternalNode2<T>
{
    public const int Log2Dim = 5;
    public const int Dim = 32;
    public const int Size = 32768;
    public const int ChildWordCount = 512;
    /// <summary>Voxel-space extent of this node (32 level-1 nodes × 128 voxels).</summary>
    public const int TotalDim = Dim * VdbInternalNode1<T>.TotalDim;

    public VdbCoord Origin;
    public readonly NodeMask ChildMask = new(ChildWordCount);
    public readonly NodeMask ValueMask = new(ChildWordCount);
    public readonly T[] Values = new T[Size];
    public readonly VdbInternalNode1<T>?[] Children = new VdbInternalNode1<T>[Size];

    public VdbInternalNode2(VdbCoord origin) => Origin = origin;

    public int ChildCount => ChildMask.CountOn();

    public VdbInternalNode1<T> AddChild(VdbCoord coord)
    {
        var local = coord - Origin;
        var slot = LocalSlot(local);
        var child = Children[slot];
        if (child == null)
        {
            child = new VdbInternalNode1<T>(ChildOrigin(slot));
            Children[slot] = child;
            ChildMask.SetOn(slot);
        }

        return child;
    }

    /// <summary>Slot of the level-1 node covering a local (voxel-space) offset.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int LocalSlot(VdbCoord local)
    {
        var span = VdbInternalNode1<T>.TotalDim;
        var lx = VdbCoord.FloorDiv(local.X, span) & 31;
        var ly = VdbCoord.FloorDiv(local.Y, span) & 31;
        var lz = VdbCoord.FloorDiv(local.Z, span) & 31;
        return (lx << 10) + (ly << 5) + lz;
    }

    public VdbCoordBBox BBox => new(Origin, Origin + new VdbCoord(TotalDim - 1));

    public VdbCoord ChildOrigin(int slot)
        => new(Origin.X + (slot >> 10) * VdbInternalNode1<T>.TotalDim,
               Origin.Y + ((slot >> 5) & 31) * VdbInternalNode1<T>.TotalDim,
               Origin.Z + (slot & 31) * VdbInternalNode1<T>.TotalDim);
}

/// <summary>One entry of the root node table: either an active/inactive tile or an allocated child.</summary>
public sealed class VdbRootTile<T>
{
    public T Value = default!;
    public bool Active;
}

/// <summary>
/// Top tree level: a sparse hash table of coordinates to level-2 children and root tiles,
/// mirroring OpenVDB's RootNode. Root tiles are large (2^15 leafs per axis), so they are
/// only created explicitly (e.g. by file IO or dense tools).
/// </summary>
public sealed class VdbRootNode<T>
{
    public T Background;
    public readonly Dictionary<VdbCoord, VdbInternalNode2<T>> Children = new();
    public readonly Dictionary<VdbCoord, VdbRootTile<T>> Tiles = new();

    public VdbRootNode(T background) => Background = background;

    public int ChildCount => Children.Count;
    public int TileCount => Tiles.Count;

    public void GetIndexBounds(out VdbCoordBBox bbox)
    {
        bbox = VdbCoordBBox.Empty;
        foreach (var (coord, child) in Children)
            bbox.Expand(child.BBox);
        foreach (var (coord, _) in Tiles)
        {
            var tileExtent = VdbInternalNode2<T>.TotalDim;
            bbox.Expand(new VdbCoordBBox(coord, coord + new VdbCoord(tileExtent - 1)));
        }
    }
}
