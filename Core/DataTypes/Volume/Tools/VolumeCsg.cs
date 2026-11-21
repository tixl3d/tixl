#nullable enable
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading.Tasks;

namespace T3.Core.DataTypes.Volume.Tools;

/// <summary>
/// Constructive solid geometry on level sets (OpenVDB's csgUnion/Intersection/DifferenceCopy):
/// union = min, intersection = max, difference = max(a, -b). Both grids are resampled onto the
/// union of their allocated leaves, so no topology guessing is involved.
/// </summary>
public static class VolumeCsg
{
    public enum Operation
    {
        Union,
        Intersection,
        Difference,
    }

    public static VdbGrid<float> Combine(VdbGrid<float> a, VdbGrid<float> b, Operation operation)
    {
        var background = operation switch
        {
            Operation.Union => MathF.Min(a.Background, b.Background),
            Operation.Intersection => MathF.Max(a.Background, b.Background),
            Operation.Difference => MathF.Max(a.Background, -b.Background),
            _ => a.Background,
        };

        // Snap to the finer of the two voxel sizes so the result keeps the sharper surface.
        var voxelSize = MathF.Min(a.Transform.VoxelSize, b.Transform.VoxelSize);
        var sameSpace = a.Transform.VoxelSize == b.Transform.VoxelSize
                        && a.Transform.Translation == b.Transform.Translation;
        var transform = sameSpace
                            ? a.Transform
                            : new VdbTransform(voxelSize, a.Transform.Translation);
        var result = new VdbGrid<float>(background, voxelSize, a.Name,
                                        operation == Operation.Union ? VdbGridClass.LevelSet : a.GridClass)
        {
            Transform = transform,
        };

        var leafCoords = new SortedSet<VdbCoord>();
        if (sameSpace)
        {
            CollectLeafCoords(a, leafCoords);
            CollectLeafCoords(b, leafCoords);
        }
        else
        {
            // The result's index space differs from at least one grid's, so its leaf origins are not valid as they are.
            CollectLeafCoordsMapped(a, transform, leafCoords);
            CollectLeafCoordsMapped(b, transform, leafCoords);
        }

        Func<float, float, float> combine = operation switch
        {
            Operation.Union => static (va, vb) => MathF.Min(va, vb),
            Operation.Intersection => static (va, vb) => MathF.Max(va, vb),
            Operation.Difference => static (va, vb) => MathF.Max(va, -vb),
            _ => static (va, _) => va,
        };
        CombineInto(result, a, b, leafCoords, combine);

        result.Tree.Prune();
        return result;
    }

    /// <summary>
    /// Fills <paramref name="result"/> with <paramref name="combine"/>(a, b) at every voxel of the given leaves, in the
    /// result's index space. A voxel is active where either input is.
    /// </summary>
    /// <remarks>
    /// A grid whose translation differs from the result's by whole voxels (a primitive with a Center offset that is a
    /// multiple of the voxel size) is read voxel by voxel at a shifted coordinate; only grids that are really offset by
    /// a fraction of a voxel, or have another voxel size, are sampled trilinearly. Leaves are combined in parallel into
    /// plain arrays (reads only), then added to the tree serially in sorted order because the tree is not thread-safe.
    /// </remarks>
    internal static void CombineInto(VdbGrid<float> result, VdbGrid<float> a, VdbGrid<float> b, SortedSet<VdbCoord> leafCoords,
                                     Func<float, float, float> combine)
    {
        var transform = result.Transform;
        var shiftA = IntegerShift(a.Transform, transform);
        var shiftB = IntegerShift(b.Transform, transform);
        var coords = new VdbCoord[leafCoords.Count];
        leafCoords.CopyTo(coords);
        var leafValues = new float[coords.Length][];
        var leafActive = new ulong[coords.Length][];
        Parallel.For(0, coords.Length, VolumeParallelism.Options,
                     () => (A: new VdbAccessor(a.Tree), B: new VdbAccessor(b.Tree)),
                     (leafIndex, _, accessors) =>
                     {
                         var coord = coords[leafIndex];
                         var values = new float[VdbLeafNode<float>.Size];
                         ulong[]? activeWords = null;
                         for (var i = 0; i < VdbLeafNode<float>.Size; i++)
                         {
                             var voxelCoord = coord + VdbCoord.LocalFromOffset(i);
                             var world = transform.IndexToWorld(voxelCoord);
                             var va = shiftA is { } sa
                                          ? accessors.A.GetValue(voxelCoord + sa, out var onA)
                                          : SampleWorld(a.Transform, accessors.A, world, out onA);
                             var vb = shiftB is { } sb
                                          ? accessors.B.GetValue(voxelCoord + sb, out var onB)
                                          : SampleWorld(b.Transform, accessors.B, world, out onB);

                             values[i] = combine(va, vb);
                             if (onA || onB)
                             {
                                 activeWords ??= new ulong[VdbLeafNode<float>.Size / 64];
                                 activeWords[i >> 6] |= 1ul << (i & 63);
                             }
                         }

                         // A leaf without an active voxel is left out.
                         if (activeWords != null)
                         {
                             leafValues[leafIndex] = values;
                             leafActive[leafIndex] = activeWords;
                         }

                         return accessors;
                     },
                     _ => { });

        for (var leafIndex = 0; leafIndex < coords.Length; leafIndex++)
        {
            if (leafActive[leafIndex] is not { } activeWords)
                continue;

            var leaf = result.Tree.AddLeaf(coords[leafIndex]);
            Array.Copy(leafValues[leafIndex], leaf.Values, VdbLeafNode<float>.Size);
            activeWords.AsSpan().CopyTo(leaf.ValueMask.Words);
        }
    }

    /// <summary>Trilinear value through the grid's own transform; <paramref name="isOn"/> is the nearest voxel's state.</summary>
    private static float SampleWorld(VdbTransform gridTransform, VdbAccessor accessor, Vector3 world, out bool isOn)
    {
        var index = gridTransform.WorldToIndexf(world);
        accessor.GetValue(new VdbCoord((int)MathF.Round(index.X), (int)MathF.Round(index.Y), (int)MathF.Round(index.Z)), out isOn);
        return accessor.SampleIndex(index);
    }

    /// <summary>
    /// The whole-voxel index offset from <paramref name="target"/>'s index space into <paramref name="source"/>'s, or null
    /// when the two have different voxel sizes or are offset by a fraction of a voxel.
    /// </summary>
    private static VdbCoord? IntegerShift(VdbTransform source, VdbTransform target)
    {
        if (source.VoxelSize != target.VoxelSize)
            return null;

        var shift = (target.Translation - source.Translation) / source.VoxelSize;
        var rounded = new Vector3(MathF.Round(shift.X), MathF.Round(shift.Y), MathF.Round(shift.Z));
        var error = Vector3.Abs(shift - rounded);
        const float tolerance = 1e-3f;
        if (error.X > tolerance || error.Y > tolerance || error.Z > tolerance)
            return null;

        return new VdbCoord((int)rounded.X, (int)rounded.Y, (int)rounded.Z);
    }

    private static void CollectLeafCoords(VdbGrid<float> grid, SortedSet<VdbCoord> into)
    {
        foreach (var leaf in grid.Tree.Leaves)
            into.Add(leaf.Origin);
    }

    /// <summary>Adds the leaves of <paramref name="grid"/> expressed in <paramref name="target"/>'s index space.</summary>
    internal static void CollectLeafCoordsMapped(VdbGrid<float> grid, VdbTransform target, SortedSet<VdbCoord> into)
    {
        const int dim = VdbLeafNode<float>.Dim;
        foreach (var leaf in grid.Tree.Leaves)
        {
            var origin = new Vector3(leaf.Origin.X, leaf.Origin.Y, leaf.Origin.Z);
            var boxMin = target.WorldToIndex(grid.Transform.IndexCornerToWorld(origin));
            var boxMax = target.WorldToIndex(grid.Transform.IndexCornerToWorld(origin + new Vector3(dim, dim, dim)));

            var x0 = VdbCoord.FloorDiv(Math.Min(boxMin.X, boxMax.X), dim) * dim;
            var y0 = VdbCoord.FloorDiv(Math.Min(boxMin.Y, boxMax.Y), dim) * dim;
            var z0 = VdbCoord.FloorDiv(Math.Min(boxMin.Z, boxMax.Z), dim) * dim;
            var x1 = VdbCoord.FloorDiv(Math.Max(boxMin.X, boxMax.X), dim) * dim;
            var y1 = VdbCoord.FloorDiv(Math.Max(boxMin.Y, boxMax.Y), dim) * dim;
            var z1 = VdbCoord.FloorDiv(Math.Max(boxMin.Z, boxMax.Z), dim) * dim;

            for (var z = z0; z <= z1; z += dim)
            for (var y = y0; y <= y1; y += dim)
            for (var x = x0; x <= x1; x += dim)
                into.Add(new VdbCoord(x, y, z));
        }
    }
}

/// <summary>
/// Morphological operations. On level sets, dilate/erode are implemented as surface offsets
/// (value + radius / value - radius, OpenVDB's levelset dilate/erode approach) followed by a
/// band collapse; open/close combine the two. On fog/other volumes, min/max stencil filters
/// run over the active voxels.
/// </summary>
public static class VolumeMorphology
{
    public enum Operation
    {
        Dilate,
        Erode,
        Open,
        Close,
    }

    public static VdbGrid<float> Apply(VdbGrid<float> input, Operation operation, float radiusWorld)
    {
        if (input.GridClass == VdbGridClass.LevelSet)
            return ApplyLevelSet(input, operation, radiusWorld);

        return ApplyStencil(input, operation, radiusWorld);
    }

    private static VdbGrid<float> ApplyLevelSet(VdbGrid<float> input, Operation operation, float radiusWorld)
    {
        // Offsetting a level set by -r moves the surface outward by r (dilate); +r erodes.
        // Level-set values are world-space distances, so the offset applies directly.
        var steps = operation switch
        {
            Operation.Dilate => new[] { -radiusWorld },
            Operation.Erode => new[] { +radiusWorld },
            Operation.Open => new[] { +radiusWorld, -radiusWorld },
            Operation.Close => new[] { -radiusWorld, +radiusWorld },
            _ => new[] { 0f },
        };

        var current = input;
        foreach (var step in steps)
        {
            var shifted = OffsetLevelSet(current, step);
            var collapsed = CollapseLevelSet(shifted);
            current = collapsed;
        }

        return current;
    }

    /// <summary>Adds a world-space offset to every voxel value (moves the surface outward for positive offsets).</summary>
    public static VdbGrid<float> OffsetLevelSet(VdbGrid<float> input, float offsetVoxels)
    {
        var result = (VdbGrid<float>)input.CloneGrid();
        // Includes the tiles that pruning collapsed uniform leaves into.
        result.Tree.TransformValues(v => v + offsetVoxels);
        return result;
    }

    /// <summary>
    /// Tightens the active band around the zero crossing: voxels beyond the band become inactive,
    /// their values clipped to the signed background (sign preserved from the offset value), and
    /// leaves without band content are dropped. Required after offsets before marching cubes,
    /// and cheap insurance after CSG.
    /// </summary>
    public static VdbGrid<float> CollapseLevelSet(VdbGrid<float> input, int bandVoxels = 3)
    {
        var background = input.Background;
        var result = new VdbGrid<float>(background, input.Transform.VoxelSize, input.Name, input.GridClass)
        {
            Transform = input.Transform,
        };

        var valueRange = MathF.Abs(background);
        foreach (var leaf in input.Tree.Leaves)
        {
            var values = new float[VdbLeafNode<float>.Size];
            var hasContent = false;
            for (var i = 0; i < VdbLeafNode<float>.Size; i++)
            {
                var v = leaf.Values[i];
                if (MathF.Abs(v) <= valueRange)
                {
                    values[i] = v;
                    hasContent = true;
                }
                else
                {
                    // Beyond the band: park at the signed background so stray reads stay consistent.
                    values[i] = v < 0 ? -background : background;
                }
            }

            if (!hasContent)
            {
                // A leaf without band content still matters when it sits INSIDE the surface:
                // keep it as inactive -background so interior reads stay signed (dilate moves
                // the surface past the old band, and the interior sign must survive).
                var anyNegative = false;
                foreach (var v in values)
                {
                    if (v < 0)
                    {
                        anyNegative = true;
                        break;
                    }
                }

                if (anyNegative)
                {
                    var interiorLeaf = result.Tree.AddLeaf(leaf.Origin);
                    interiorLeaf.Deactivate(-background);
                }

                continue;
            }

            var newLeaf = result.Tree.AddLeaf(leaf.Origin);
            Array.Copy(values, newLeaf.Values, values.Length);
            for (var i = 0; i < values.Length; i++)
            {
                if (MathF.Abs(values[i]) <= valueRange)
                    newLeaf.ValueMask.SetOn(i);
                else if (values[i] < 0)
                    newLeaf.Values[i] = -background;
                else
                    newLeaf.Values[i] = background;
            }
        }

        return result;
    }

    private static VdbGrid<float> ApplyStencil(VdbGrid<float> input, Operation operation, float radiusWorld)
    {
        var radius = Math.Clamp((int)MathF.Round(radiusWorld / input.Transform.VoxelSize), 0, 16);
        if (radius == 0)
            return (VdbGrid<float>)input.CloneGrid();

        var passes = operation switch
        {
            Operation.Dilate => 1,
            Operation.Erode => 1,
            Operation.Open => 2,
            Operation.Close => 2,
            _ => 0,
        };

        var current = input;
        for (var p = 0; p < passes; p++)
        {
            var isMax = operation switch
            {
                Operation.Dilate => true,
                Operation.Erode => false,
                Operation.Open => p == 1, // erode then dilate
                Operation.Close => p == 0, // dilate then erode
                _ => true,
            };
            current = StencilFilter(current, isMax, radius);
        }

        return current;
    }

    private static VdbGrid<float> StencilFilter(VdbGrid<float> input, bool isMax, int radius)
    {
        var result = new VdbGrid<float>(input.Background, input.Transform.VoxelSize, input.Name, input.GridClass)
        {
            Transform = input.Transform,
        };

        foreach (var leaf in input.Tree.Leaves)
        {
            var values = new float[VdbLeafNode<float>.Size];
            var anyActive = false;
            for (var i = 0; i < VdbLeafNode<float>.Size; i++)
            {
                var coord = leaf.Origin + VdbCoord.LocalFromOffset(i);
                if (!leaf.ValueMask.IsOn(i))
                    continue;

                var extreme = input.Tree.GetValue(coord);
                for (var dz = -radius; dz <= radius; dz++)
                {
                    for (var dy = -radius; dy <= radius; dy++)
                    {
                        for (var dx = -radius; dx <= radius; dx++)
                        {
                            if (dx == 0 && dy == 0 && dz == 0)
                                continue;
                            var sample = input.Tree.GetValue(coord + new VdbCoord(dx, dy, dz));
                            if (isMax ? sample > extreme : sample < extreme)
                                extreme = sample;
                        }
                    }
                }

                values[i] = extreme;
                anyActive = true;
            }

            if (!anyActive)
                continue;

            var newLeaf = result.Tree.AddLeaf(leaf.Origin);
            Array.Copy(values, newLeaf.Values, values.Length);
            for (var i = 0; i < values.Length; i++)
            {
                if (MathF.Abs(values[i] - input.Background) > 1e-6f)
                    newLeaf.ValueMask.SetOn(i);
            }
        }

        return result;
    }
}
