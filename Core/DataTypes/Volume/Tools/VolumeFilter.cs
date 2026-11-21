#nullable enable
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading.Tasks;

namespace T3.Core.DataTypes.Volume.Tools;

/// <summary>
/// Voxel filtering (OpenVDB's tools/Filter.h): Gaussian and box smoothing, median, and offset,
/// applied to the active voxels of a float grid. Neighbor samples read through the tree so band
/// boundaries stay consistent; results build into a fresh grid (inputs are never mutated).
/// </summary>
public static class VolumeFilter
{
    public enum FilterType
    {
        Gaussian,
        Box,
        Median,
        Offset,
    }

    public static VdbGrid<float> Apply(VdbGrid<float> input, FilterType type, int radiusVoxels, float offset = 0f, int iterations = 1)
    {
        radiusVoxels = Math.Clamp(radiusVoxels, 0, 8);
        iterations = Math.Clamp(iterations, 1, 10);

        var current = input;
        for (var i = 0; i < iterations; i++)
        {
            current = type switch
            {
                FilterType.Gaussian => Convolve(current, GaussianKernel(radiusVoxels)),
                FilterType.Box => Convolve(current, BoxKernel(radiusVoxels)),
                FilterType.Median => Median(current, radiusVoxels),
                FilterType.Offset => OffsetValues(current, offset),
                _ => current,
            };
        }

        return current;
    }

    public static VdbGrid<float> Gaussian(VdbGrid<float> input, int radiusVoxels = 1, int iterations = 1)
        => Apply(input, FilterType.Gaussian, radiusVoxels, iterations: iterations);

    /// <summary>Separable binomial kernel (Pascal row), normalized to sum 1.</summary>
    private static float[] GaussianKernel(int radius)
    {
        radius = Math.Max(radius, 1);
        var kernel = new float[2 * radius + 1];
        var n = 2 * radius;
        long value = 1;
        var sum = 0f;
        for (var i = 0; i <= n; i++)
        {
            kernel[i] = value;
            sum += value;
            value = value * (n - i) / (i + 1);
        }

        for (var i = 0; i < kernel.Length; i++)
            kernel[i] /= sum;
        return kernel;
    }

    private static float[] BoxKernel(int radius)
    {
        radius = Math.Max(radius, 1);
        var kernel = new float[2 * radius + 1];
        Array.Fill(kernel, 1f / kernel.Length);
        return kernel;
    }

    /// <summary>
    /// Separable 3D convolution over the active voxels, parallel per leaf. Each leaf is convolved inside a dense
    /// copy of its neighbourhood, one axis at a time: 3 x (2r+1) reads per voxel instead of (2r+1)^3 tree lookups.
    /// </summary>
    private static VdbGrid<float> Convolve(VdbGrid<float> input, float[] kernel1D)
    {
        var radius = kernel1D.Length / 2;
        var result = new VdbGrid<float>(input.Background, input.Transform.VoxelSize, input.Name, input.GridClass)
        {
            Transform = input.Transform,
        };

        var (sources, targets) = MirrorLeaves(input, result);
        Parallel.For(0, sources.Count, VolumeParallelism.Options,
                     () => new ConvolveScratch(radius),
                     (leafIndex, _, scratch) =>
                     {
                         ConvolveLeaf(input.Tree, sources[leafIndex], targets[leafIndex], kernel1D, scratch);
                         return scratch;
                     },
                     _ => { });

        result.Tree.Prune();

        return result;
    }

    /** Per-thread state of <see cref="ConvolveLeaf"/>: the neighbourhood and the results of the first two passes, in its layout. */
    private sealed class ConvolveScratch
    {
        public ConvolveScratch(int radius)
        {
            Block = new VdbNeighbourhood(radius);
            AlongX = new float[Block.Values.Length];
            AlongY = new float[Block.Values.Length];
        }

        public readonly VdbNeighbourhood Block;
        public readonly float[] AlongX;
        public readonly float[] AlongY;
    }

    private static void ConvolveLeaf(VdbTree<float> tree, VdbLeafNode<float> leaf, VdbLeafNode<float> target, float[] kernel, ConvolveScratch scratch)
    {
        if (leaf.ValueMask.IsEmpty())
            return;

        const int dim = VdbLeafNode<float>.Dim;
        var block = scratch.Block;
        block.Fill(tree, leaf.Origin);
        var radius = block.Margin;
        var values = block.Values;
        var alongX = scratch.AlongX;
        var alongY = scratch.AlongY;
        var strideX = block.Side * block.Side;
        var strideY = block.Side;

        // X pass over the leaf's x range and the full margin in y and z, which the later passes read.
        for (var x = 0; x < dim; x++)
        {
            for (var y = -radius; y < dim + radius; y++)
            {
                for (var z = -radius; z < dim + radius; z++)
                {
                    var center = block.Index(x, y, z);
                    var sum = 0f;
                    for (var d = -radius; d <= radius; d++)
                        sum += kernel[d + radius] * values[center + d * strideX];
                    alongX[center] = sum;
                }
            }
        }

        for (var x = 0; x < dim; x++)
        {
            for (var y = 0; y < dim; y++)
            {
                for (var z = -radius; z < dim + radius; z++)
                {
                    var center = block.Index(x, y, z);
                    var sum = 0f;
                    for (var d = -radius; d <= radius; d++)
                        sum += kernel[d + radius] * alongX[center + d * strideY];
                    alongY[center] = sum;
                }
            }
        }

        // The kernel sums to 1, so the weighted sum needs no normalisation. Inactive voxels keep their value.
        for (var x = 0; x < dim; x++)
        {
            for (var y = 0; y < dim; y++)
            {
                for (var z = 0; z < dim; z++)
                {
                    var offset = VdbCoord.LeafOffset(x, y, z);
                    if (!leaf.ValueMask.IsOn(offset))
                        continue;

                    var center = block.Index(x, y, z);
                    var sum = 0f;
                    for (var d = -radius; d <= radius; d++)
                        sum += kernel[d + radius] * alongY[center + d];
                    target.Values[offset] = sum;
                }
            }
        }
    }

    private static VdbGrid<float> Median(VdbGrid<float> input, int radius)
    {
        radius = Math.Clamp(radius, 1, 2);
        var result = new VdbGrid<float>(input.Background, input.Transform.VoxelSize, input.Name, input.GridClass)
        {
            Transform = input.Transform,
        };

        var (sources, targets) = MirrorLeaves(input, result);
        Parallel.For(0, sources.Count, VolumeParallelism.Options,
                     () => (Block: new VdbNeighbourhood(radius), Window: new float[(2 * radius + 1) * (2 * radius + 1) * (2 * radius + 1)]),
                     (leafIndex, _, scratch) =>
                     {
                         var leaf = sources[leafIndex];
                         var target = targets[leafIndex];
                         if (leaf.ValueMask.IsEmpty())
                             return scratch;

                         var block = scratch.Block;
                         block.Fill(input.Tree, leaf.Origin);
                         for (var i = 0; i < VdbLeafNode<float>.Size; i++)
                         {
                             if (!leaf.ValueMask.IsOn(i))
                                 continue;

                             var local = VdbCoord.LocalFromOffset(i);
                             var count = 0;
                             for (var dx = -radius; dx <= radius; dx++)
                             {
                                 for (var dy = -radius; dy <= radius; dy++)
                                 {
                                     var index = block.Index(local.X + dx, local.Y + dy, local.Z - radius);
                                     for (var dz = -radius; dz <= radius; dz++, index++)
                                         scratch.Window[count++] = block.Values[index];
                                 }
                             }

                             Array.Sort(scratch.Window, 0, count);
                             target.Values[i] = scratch.Window[count / 2];
                         }

                         return scratch;
                     },
                     _ => { });

        result.Tree.Prune();

        return result;
    }

    /// <summary>
    /// Copies the input's leaves (values and active mask) into the result serially, because the tree is
    /// not thread-safe, so the leaves can then be filtered in parallel. Inactive voxels keep their values:
    /// for a level set they carry the interior fill.
    /// </summary>
    private static (List<VdbLeafNode<float>> Sources, VdbLeafNode<float>[] Targets) MirrorLeaves(VdbGrid<float> input, VdbGrid<float> result)
    {
        var sources = new List<VdbLeafNode<float>>(input.Tree.Leaves);
        var targets = new VdbLeafNode<float>[sources.Count];
        for (var i = 0; i < sources.Count; i++)
        {
            var target = result.Tree.AddLeaf(sources[i].Origin);
            Array.Copy(sources[i].Values, target.Values, target.Values.Length);
            target.ValueMask.Words.AsSpan().CopyFrom(sources[i].ValueMask.Words);
            targets[i] = target;
        }

        return (sources, targets);
    }

    private static VdbGrid<float> OffsetValues(VdbGrid<float> input, float offset)
    {
        var result = (VdbGrid<float>)input.CloneGrid();
        // Includes the tiles that pruning collapsed uniform leaves into.
        result.Tree.TransformValues(v => v + offset);
        return result;
    }
}

/// <summary>
/// Vector calculus on grids (OpenVDB's tools/PointAdvect + math operators): gradient, Laplacian,
/// curl, divergence, magnitude, normalize - all via central differences on tree accessors.
/// </summary>
public static class VolumeCalculus
{
    public static VdbGrid<Vector3> Gradient(VdbGrid<float> input, float scale = 1f)
    {
        var result = NewVecGrid(input);
        foreach (var leaf in input.Tree.Leaves)
        {
            var values = new Vector3[VdbLeafNode<Vector3>.Size];
            var anyActive = false;
            for (var i = 0; i < VdbLeafNode<float>.Size; i++)
            {
                if (!leaf.ValueMask.IsOn(i))
                    continue;

                var coord = leaf.Origin + VdbCoord.LocalFromOffset(i);
                var gx = Sample(coord + new VdbCoord(1, 0, 0)) - Sample(coord - new VdbCoord(1, 0, 0));
                var gy = Sample(coord + new VdbCoord(0, 1, 0)) - Sample(coord - new VdbCoord(0, 1, 0));
                var gz = Sample(coord + new VdbCoord(0, 0, 1)) - Sample(coord - new VdbCoord(0, 0, 1));
                values[i] = new Vector3(gx, gy, gz) * (0.5f * scale);
                anyActive = true;
            }

            if (anyActive)
                Commit(result, leaf.Origin, values);
        }

        return result;

        float Sample(VdbCoord c) => input.Tree.GetValue(c);
    }

    public static VdbGrid<float> Laplacian(VdbGrid<float> input, float scale = 1f)
    {
        var result = NewFloatGrid(input);
        foreach (var leaf in input.Tree.Leaves)
        {
            var values = new float[VdbLeafNode<float>.Size];
            var anyActive = false;
            for (var i = 0; i < VdbLeafNode<float>.Size; i++)
            {
                if (!leaf.ValueMask.IsOn(i))
                    continue;

                var coord = leaf.Origin + VdbCoord.LocalFromOffset(i);
                var center = Sample(coord);
                var sum = Sample(coord + new VdbCoord(1, 0, 0)) + Sample(coord - new VdbCoord(1, 0, 0))
                          + Sample(coord + new VdbCoord(0, 1, 0)) + Sample(coord - new VdbCoord(0, 1, 0))
                          + Sample(coord + new VdbCoord(0, 0, 1)) + Sample(coord - new VdbCoord(0, 0, 1))
                          - 6f * center;
                values[i] = sum * scale;
                anyActive = true;
            }

            if (anyActive)
                Commit(result, leaf.Origin, values);
        }

        return result;

        float Sample(VdbCoord c) => input.Tree.GetValue(c);
    }

    public static VdbGrid<Vector3> Curl(VdbGrid<Vector3> input, float scale = 1f)
    {
        var result = NewVecGrid(input);
        foreach (var leaf in input.Tree.Leaves)
        {
            var values = new Vector3[VdbLeafNode<Vector3>.Size];
            var anyActive = false;
            for (var i = 0; i < VdbLeafNode<Vector3>.Size; i++)
            {
                if (!leaf.ValueMask.IsOn(i))
                    continue;

                var coord = leaf.Origin + VdbCoord.LocalFromOffset(i);
                // curl = (dWy/dz - dVz/dy, dVx/dz - dVz/dx, dVy/dx - dVx/dy)
                var dWyDz = SampleY(coord + new VdbCoord(0, 0, 1)) - SampleY(coord - new VdbCoord(0, 0, 1));
                var dVzDy = SampleZ(coord + new VdbCoord(0, 1, 0)) - SampleZ(coord - new VdbCoord(0, 1, 0));
                var dVxDz = SampleX(coord + new VdbCoord(0, 0, 1)) - SampleX(coord - new VdbCoord(0, 0, 1));
                var dVzDx = SampleZ(coord + new VdbCoord(1, 0, 0)) - SampleZ(coord - new VdbCoord(1, 0, 0));
                var dVyDx = SampleY(coord + new VdbCoord(1, 0, 0)) - SampleY(coord - new VdbCoord(1, 0, 0));
                var dVxDy = SampleX(coord + new VdbCoord(0, 1, 0)) - SampleX(coord - new VdbCoord(0, 1, 0));

                var curl = new Vector3(dWyDz - dVzDy, dVxDz - dVzDx, dVyDx - dVxDy) * (0.5f * scale);
                values[i] = curl;
                anyActive = true;
            }

            if (anyActive)
                Commit(result, leaf.Origin, values);
        }

        return result;

        float SampleX(VdbCoord c) => input.Tree.GetValue(c).X;
        float SampleY(VdbCoord c) => input.Tree.GetValue(c).Y;
        float SampleZ(VdbCoord c) => input.Tree.GetValue(c).Z;
    }

    public static VdbGrid<float> Divergence(VdbGrid<Vector3> input, float scale = 1f)
    {
        var result = NewFloatGrid(input);
        foreach (var leaf in input.Tree.Leaves)
        {
            var values = new float[VdbLeafNode<float>.Size];
            var anyActive = false;
            for (var i = 0; i < VdbLeafNode<Vector3>.Size; i++)
            {
                if (!leaf.ValueMask.IsOn(i))
                    continue;

                var coord = leaf.Origin + VdbCoord.LocalFromOffset(i);
                var dx = Sample(coord + new VdbCoord(1, 0, 0)).X - Sample(coord - new VdbCoord(1, 0, 0)).X;
                var dy = Sample(coord + new VdbCoord(0, 1, 0)).Y - Sample(coord - new VdbCoord(0, 1, 0)).Y;
                var dz = Sample(coord + new VdbCoord(0, 0, 1)).Z - Sample(coord - new VdbCoord(0, 0, 1)).Z;
                values[i] = (dx + dy + dz) * (0.5f * scale);
                anyActive = true;
            }

            if (anyActive)
                Commit(result, leaf.Origin, values);
        }

        return result;

        Vector3 Sample(VdbCoord c) => input.Tree.GetValue(c);
    }

    /// <summary>Magnitude of a vector grid as a scalar grid.</summary>
    public static VdbGrid<float> Magnitude(VdbGrid<Vector3> input)
    {
        var result = NewFloatGrid(input);
        foreach (var leaf in input.Tree.Leaves)
        {
            var values = new float[VdbLeafNode<float>.Size];
            var anyActive = false;
            for (var i = 0; i < VdbLeafNode<Vector3>.Size; i++)
            {
                if (!leaf.ValueMask.IsOn(i))
                    continue;
                values[i] = leaf.Values[i].Length();
                anyActive = true;
            }

            if (anyActive)
                Commit(result, leaf.Origin, values);
        }

        return result;
    }

    /// <summary>Normalizes active vectors in place-ish (returns a copy).</summary>
    public static VdbGrid<Vector3> Normalize(VdbGrid<Vector3> input)
    {
        var result = NewVecGrid(input);
        foreach (var leaf in input.Tree.Leaves)
        {
            var values = new Vector3[VdbLeafNode<Vector3>.Size];
            var anyActive = false;
            for (var i = 0; i < VdbLeafNode<Vector3>.Size; i++)
            {
                if (!leaf.ValueMask.IsOn(i))
                    continue;
                var v = leaf.Values[i];
                var length = v.Length();
                values[i] = length > 1e-12f ? v / length : Vector3.Zero;
                anyActive = true;
            }

            if (anyActive)
                Commit(result, leaf.Origin, values);
        }

        return result;
    }

    private static VdbGrid<float> NewFloatGrid(IVdbGrid source)
        => new(0f, source.Transform.VoxelSize, source.Name, source.GridClass) { Transform = source.Transform };

    private static VdbGrid<Vector3> NewVecGrid(IVdbGrid source)
        => new(Vector3.Zero, source.Transform.VoxelSize, source.Name, source.GridClass) { Transform = source.Transform };

    private static void Commit(VdbGrid<float> target, VdbCoord origin, float[] values)
    {
        var leaf = target.Tree.AddLeaf(origin);
        Array.Copy(values, leaf.Values, values.Length);
        leaf.ValueMask.SetAll();
    }

    private static void Commit(VdbGrid<Vector3> target, VdbCoord origin, Vector3[] values)
    {
        var leaf = target.Tree.AddLeaf(origin);
        Array.Copy(values, leaf.Values, values.Length);
        leaf.ValueMask.SetAll();
    }
}
