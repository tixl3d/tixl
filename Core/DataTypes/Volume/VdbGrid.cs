#nullable enable
using System;
using System.Collections.Generic;
using System.Numerics;

namespace T3.Core.DataTypes.Volume;

/// <summary>Semantic classification mirrored from OpenVDB grid classes.</summary>
public enum VdbGridClass
{
    Unknown,
    LevelSet,
    FogVolume,
    Staggered,
}

/// <summary>Discriminated value type of a grid, used for file IO and UI display.</summary>
public enum VdbValueType
{
    Float,
    Vec3Float,
    Int32,
    Int64,
    Double,
    Bool,
    Mask,
}

/// <summary>
/// Type-erased view over a <see cref="VdbGrid{T}"/> so ops and file IO can handle
/// heterogeneous grids (scalar, vector, integer) without generics fan-out everywhere.
/// </summary>
public interface IVdbGrid
{
    /// <summary>Grid name as stored in .vdb metadata ("name" meta); "density", "vel", ...</summary>
    string Name { get; set; }

    /// <summary>Short value-type name, e.g. "FloatGrid", "Vec3SGrid".</summary>
    string TypeName { get; }

    VdbValueType ValueType { get; }

    VdbGridClass GridClass { get; set; }

    VdbTransform Transform { get; set; }

    long ActiveVoxelCount { get; }

    long AllocatedMemoryBytes { get; }

    VdbCoordBBox ActiveIndexBBox { get; }

    /// <summary>World-space bounding box of active values (voxel corners, not centers).</summary>
    BBox ActiveWorldBBox();

    IVdbGrid CloneGrid();

    void PruneGrid(float tolerance = 0f);
}

/// <summary>World-space axis aligned box.</summary>
public readonly struct BBox
{
    public readonly Vector3 Min;
    public readonly Vector3 Max;

    public BBox(Vector3 min, Vector3 max)
    {
        Min = min;
        Max = max;
    }

    public readonly bool IsEmpty => Min.X > Max.X || Min.Y > Max.Y || Min.Z > Max.Z;
    public Vector3 Center => (Min + Max) * 0.5f;
    public Vector3 Extents => Max - Min;
}

/// <summary>A typed sparse volumetric grid: tree + transform + metadata, OpenVDB's Grid&lt;T&gt;.</summary>
public sealed class VdbGrid<T> : IVdbGrid
{
    public VdbTree<T> Tree { get; }
    public string Name { get; set; }
    public VdbGridClass GridClass { get; set; }
    public VdbTransform Transform { get; set; }

    public VdbGrid(T background, float voxelSize = 0.1f, string name = "", VdbGridClass gridClass = VdbGridClass.Unknown)
    {
        Tree = new VdbTree<T>(background);
        Transform = new VdbTransform(voxelSize, Vector3.Zero);
        Name = name;
        GridClass = gridClass;
    }

    public VdbGrid(VdbTree<T> tree, VdbTransform transform, string name = "", VdbGridClass gridClass = VdbGridClass.Unknown)
    {
        Tree = tree;
        Transform = transform;
        Name = name;
        GridClass = gridClass;
    }

    public T Background => Tree.Background;

    public string TypeName => TypeNameOf();

    public VdbValueType ValueType => ValueTypeOf();

    public long ActiveVoxelCount => Tree.ActiveVoxelCount;

    public long AllocatedMemoryBytes
    {
        get
        {
            var bytesPerVoxel = System.Runtime.CompilerServices.Unsafe.SizeOf<T>();
            var count = Tree.LeafCount;
            var leafBytes = count * (64 + VdbLeafNode<T>.Size * bytesPerVoxel);
            var level1Count = 0;
            foreach (var node2 in Tree.Root.Children.Values)
                level1Count += node2.ChildMask.CountOn();
            var node1Bytes = level1Count * (64 + 2 * 64 * 8 + VdbInternalNode1<T>.Size * bytesPerVoxel + VdbInternalNode1<T>.Size * 8);
            return leafBytes + node1Bytes;
        }
    }

    public VdbCoordBBox ActiveIndexBBox => Tree.ActiveIndexBBox();

    public BBox ActiveWorldBBox()
    {
        var indexBBox = Tree.ActiveIndexBBox();
        if (indexBBox.IsEmpty)
            return new BBox(Vector3.Zero, Vector3.Zero);

        var min = Transform.IndexCornerToWorld(new Vector3(indexBBox.Min.X, indexBBox.Min.Y, indexBBox.Min.Z));
        var max = Transform.IndexCornerToWorld(new Vector3(indexBBox.Max.X + 1, indexBBox.Max.Y + 1, indexBBox.Max.Z + 1));
        return new BBox(Vector3.Min(min, max), Vector3.Max(min, max));
    }

    public IVdbGrid CloneGrid()
    {
        var copy = new VdbGrid<T>(Tree.Background, Transform.VoxelSize, Name, GridClass)
        {
            Transform = Transform
        };

        // Deep-copy the whole structure, including the tile values that pruning stores on the
        // internal nodes. Iterating only leaves would silently drop every collapsed uniform
        // region, so a cloned (e.g. offset) volume would read as background there.
        foreach (var (coord, tile) in Tree.Root.Tiles)
            copy.Tree.Root.Tiles[coord] = new VdbRootTile<T> { Value = tile.Value, Active = tile.Active };

        foreach (var (coord, node2) in Tree.Root.Children)
        {
            var new2 = new VdbInternalNode2<T>(node2.Origin);
            new2.ChildMask.FillFrom(node2.ChildMask);
            new2.ValueMask.FillFrom(node2.ValueMask);
            Array.Copy(node2.Values, new2.Values, node2.Values.Length);
            copy.Tree.Root.Children[coord] = new2;

            for (var slot2 = 0; slot2 < node2.Children.Length; slot2++)
            {
                var node1 = node2.Children[slot2];
                if (node1 == null)
                    continue;

                var new1 = new VdbInternalNode1<T>(node1.Origin);
                new1.ChildMask.FillFrom(node1.ChildMask);
                new1.ValueMask.FillFrom(node1.ValueMask);
                Array.Copy(node1.Values, new1.Values, node1.Values.Length);
                new2.Children[slot2] = new1;

                for (var slot1 = 0; slot1 < node1.Children.Length; slot1++)
                {
                    var leaf = node1.Children[slot1];
                    if (leaf == null)
                        continue;

                    var newLeaf = new VdbLeafNode<T>(leaf.Origin);
                    Array.Copy(leaf.Values, newLeaf.Values, VdbLeafNode<T>.Size);
                    newLeaf.ValueMask.FillFrom(leaf.ValueMask);
                    new1.Children[slot1] = newLeaf;
                }
            }
        }

        return copy;
    }

    public void PruneGrid(float tolerance = 0f) => Tree.Prune(tolerance);

    private static string TypeNameOf()
    {
        if (typeof(T) == typeof(float))
            return "FloatGrid";
        if (typeof(T) == typeof(Vector3))
            return "Vec3SGrid";
        if (typeof(T) == typeof(int))
            return "Int32Grid";
        if (typeof(T) == typeof(long))
            return "Int64Grid";
        if (typeof(T) == typeof(double))
            return "DoubleGrid";
        return "FloatGrid";
    }

    private static VdbValueType ValueTypeOf()
    {
        if (typeof(T) == typeof(float))
            return VdbValueType.Float;
        if (typeof(T) == typeof(Vector3))
            return VdbValueType.Vec3Float;
        if (typeof(T) == typeof(int))
            return VdbValueType.Int32;
        if (typeof(T) == typeof(long))
            return VdbValueType.Int64;
        if (typeof(T) == typeof(double))
            return VdbValueType.Double;
        return VdbValueType.Float;
    }
}

/// <summary>
/// A set of named grids travelling through the graph as one value - the .vdb analog.
/// Most ops operate on the first float grid (see <see cref="GetFloatGrid"/>); vector grids
/// (velocity, color) ride along in the set.
/// </summary>
public sealed class VdbVolume
{
    public List<IVdbGrid> Grids { get; } = new();

    /// <summary>
    /// Grids the file contained but this reader cannot represent, as "name (gridType)". Point caches
    /// are the common case; without this a load that found only point data would look like an empty
    /// file rather than an unsupported one.
    /// </summary>
    public List<string> SkippedGrids { get; } = new();

    public VdbVolume()
    {
    }

    public VdbVolume(params IVdbGrid[] grids) => Grids.AddRange(grids);

    public VdbVolume(IEnumerable<IVdbGrid> grids) => Grids.AddRange(grids);

    public IVdbGrid? GetGrid(string name)
    {
        foreach (var grid in Grids)
        {
            if (string.Equals(grid.Name, name, StringComparison.OrdinalIgnoreCase))
                return grid;
        }

        return null;
    }

    /// <summary>First float grid in the set, or null when the volume holds none.</summary>
    public VdbGrid<float>? GetFloatGrid()
    {
        foreach (var grid in Grids)
        {
            if (grid is VdbGrid<float> floatGrid)
                return floatGrid;
        }

        return null;
    }

    public VdbGrid<Vector3>? GetVectorGrid(string? name = null)
    {
        foreach (var grid in Grids)
        {
            if (grid is VdbGrid<Vector3> vecGrid && (name == null || string.Equals(grid.Name, name, StringComparison.OrdinalIgnoreCase)))
                return vecGrid;
        }

        return null;
    }

    public VdbVolume Clone()
    {
        var copy = new VdbVolume();
        foreach (var grid in Grids)
            copy.Grids.Add(grid.CloneGrid());
        return copy;
    }
}

public static class SpanExtensions
{
    public static void CopyFrom(this Span<ulong> target, ReadOnlySpan<ulong> source)
    {
        source.CopyTo(target);
    }
}
