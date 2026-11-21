#nullable enable
using System;
using System.Numerics;
using System.Threading.Tasks;

namespace T3.Core.DataTypes.Volume.Tools;

/// <summary>
/// A dense copy of one leaf and a margin of voxels around it, so stencil operations (filters, gradients, marching
/// cubes) read neighbours from an array instead of walking the tree for every sample. One instance per worker
/// thread; <see cref="Fill"/> overwrites it for the next leaf.
/// </summary>
internal sealed class VdbNeighbourhood
{
    public VdbNeighbourhood(int margin)
    {
        if (margin < 0 || margin > VdbLeafNode<float>.Dim)
            throw new ArgumentOutOfRangeException(nameof(margin), margin, "the margin must fit inside the neighbouring leaves");

        Margin = margin;
        Side = VdbLeafNode<float>.Dim + 2 * margin;
        Values = new float[Side * Side * Side];
        Active = new bool[Side * Side * Side];
    }

    public readonly int Margin;
    public readonly int Side;

    /// <summary>Values at local coordinates -Margin..7+Margin on every axis.</summary>
    public readonly float[] Values;

    /// <summary>Active flags, same layout as <see cref="Values"/>.</summary>
    public readonly bool[] Active;

    /// <summary>Array index of a coordinate relative to the leaf origin; X is the slowest axis, Z the fastest.</summary>
    public int Index(int x, int y, int z) => ((x + Margin) * Side + (y + Margin)) * Side + (z + Margin);

    public void Fill(VdbTree<float> tree, VdbCoord origin)
    {
        const int dim = VdbLeafNode<float>.Dim;
        var margin = Margin;
        for (var nx = -1; nx <= 1; nx++)
        {
            var (x0, x1) = Range(nx, margin);
            if (x0 > x1)
                continue;

            for (var ny = -1; ny <= 1; ny++)
            {
                var (y0, y1) = Range(ny, margin);
                if (y0 > y1)
                    continue;

                for (var nz = -1; nz <= 1; nz++)
                {
                    var (z0, z1) = Range(nz, margin);
                    if (z0 > z1)
                        continue;

                    var blockOrigin = origin + new VdbCoord(nx * dim, ny * dim, nz * dim);
                    var leaf = tree.ProbeLeaf(blockOrigin);
                    if (leaf != null)
                    {
                        for (var x = x0; x <= x1; x++)
                        {
                            for (var y = y0; y <= y1; y++)
                            {
                                var index = Index(x, y, z0);
                                var offset = VdbCoord.LeafOffset(x & 7, y & 7, z0 & 7);
                                for (var z = z0; z <= z1; z++, index++, offset++)
                                {
                                    Values[index] = leaf.Values[offset];
                                    Active[index] = leaf.ValueMask.IsOn(offset);
                                }
                            }
                        }

                        continue;
                    }

                    // Without a leaf the whole 8^3 block is one tile value or the background.
                    var value = tree.GetValue(blockOrigin);
                    var active = tree.IsValueOn(blockOrigin);
                    for (var x = x0; x <= x1; x++)
                    {
                        for (var y = y0; y <= y1; y++)
                        {
                            var index = Index(x, y, z0);
                            for (var z = z0; z <= z1; z++, index++)
                            {
                                Values[index] = value;
                                Active[index] = active;
                            }
                        }
                    }
                }
            }
        }
    }

    /// <summary>The local coordinates of the neighbouring block <paramref name="n"/> (-1, 0, 1) that fall into the margin.</summary>
    private static (int From, int To) Range(int n, int margin) => n switch
    {
        < 0 => (-margin, -1),
        0 => (0, VdbLeafNode<float>.Dim - 1),
        _ => (VdbLeafNode<float>.Dim, VdbLeafNode<float>.Dim - 1 + margin),
    };
}

/// <summary>
/// Random access into a float tree that remembers the last 8^3 block it touched, so coherent reads (a whole leaf
/// worth of neighbouring voxels) cost one tree walk per block instead of one per voxel. Not thread-safe: one
/// accessor per worker. The tree must not change while an accessor reads it.
/// </summary>
internal sealed class VdbAccessor(VdbTree<float> tree)
{
    public float GetValue(VdbCoord coord, out bool isOn)
    {
        var origin = new VdbCoord(coord.X & ~7, coord.Y & ~7, coord.Z & ~7);
        if (!_hasBlock || origin != _origin)
        {
            _origin = origin;
            _hasBlock = true;
            _leaf = tree.ProbeLeaf(origin);
            if (_leaf == null)
            {
                // Without a leaf the whole block is one tile value or the background.
                _blockValue = tree.GetValue(origin);
                _blockActive = tree.IsValueOn(origin);
            }
        }

        if (_leaf == null)
        {
            isOn = _blockActive;
            return _blockValue;
        }

        var offset = coord.LeafOffset();
        isOn = _leaf.ValueMask.IsOn(offset);
        return _leaf.Values[offset];
    }

    /// <summary>Trilinear value at a fractional index position, the same as <see cref="VdbSampler.SampleIndex"/>.</summary>
    public float SampleIndex(Vector3 index)
    {
        var x0 = (int)MathF.Floor(index.X);
        var y0 = (int)MathF.Floor(index.Y);
        var z0 = (int)MathF.Floor(index.Z);
        var tx = index.X - x0;
        var ty = index.Y - y0;
        var tz = index.Z - z0;

        var c000 = GetValue(new VdbCoord(x0, y0, z0), out _);
        var c100 = GetValue(new VdbCoord(x0 + 1, y0, z0), out _);
        var c010 = GetValue(new VdbCoord(x0, y0 + 1, z0), out _);
        var c110 = GetValue(new VdbCoord(x0 + 1, y0 + 1, z0), out _);
        var c001 = GetValue(new VdbCoord(x0, y0, z0 + 1), out _);
        var c101 = GetValue(new VdbCoord(x0 + 1, y0, z0 + 1), out _);
        var c011 = GetValue(new VdbCoord(x0, y0 + 1, z0 + 1), out _);
        var c111 = GetValue(new VdbCoord(x0 + 1, y0 + 1, z0 + 1), out _);

        var c00 = c000 + (c100 - c000) * tx;
        var c10 = c010 + (c110 - c010) * tx;
        var c01 = c001 + (c101 - c001) * tx;
        var c11 = c011 + (c111 - c011) * tx;

        var c0 = c00 + (c10 - c00) * ty;
        var c1 = c01 + (c11 - c01) * ty;

        return c0 + (c1 - c0) * tz;
    }

    private VdbCoord _origin;
    private bool _hasBlock;
    private VdbLeafNode<float>? _leaf;
    private float _blockValue;
    private bool _blockActive;
}

/// <summary>How the volume tools spread their work over the cores.</summary>
internal static class VolumeParallelism
{
    /// <summary>
    /// Volume operators compute on background threads while a frame renders. Leaving a couple of cores free keeps the
    /// render thread and the GPU driver from waiting for a core behind a long splat or filter.
    /// </summary>
    public static readonly ParallelOptions Options = new() { MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount - 2) };
}
